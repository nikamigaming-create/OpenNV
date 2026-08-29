using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2CustomCharacterPersistenceProof
{
    private const int DrawFrames = 4;
    private const int GroundingFrames = 120;
    private const int ExpectedWidth = 1280;
    private const int ExpectedHeight = 720;

    internal static async Task RunWrite(
        Fo2CharacterStartHost host,
        string proofRoot,
        string sex)
    {
        try
        {
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 2 custom-character write proof requires a rendering display driver.");
            var output = PrepareOutput(proofRoot, false);
            var expected = Expected(sex);
            if (host.RestoredFromSave || host.Runtime is not null ||
                Fo2CharacterStartSaveState.Exists(host.SavePath))
                throw new InvalidOperationException(
                    "Fallout 2 custom-character write proof requires an empty save boundary.");
            host.Picker.Select(expected.SourceIndex);
            var cancelledEditor = host.Picker.OpenCustom(expected.Modify);
            cancelledEditor.Cancel();
            if (host.Picker.CustomEditor is not null || host.Runtime is not null ||
                Fo2CharacterStartSaveState.Exists(host.SavePath))
                throw new InvalidOperationException(
                    "Fallout 2 custom-character cancel path changed authoritative state.");
            var editor = host.Picker.OpenCustom(expected.Modify);
            editor.SetCharacterName(expected.Name);
            editor.SetSex(expected.Sex);
            editor.SetAge(expected.Age);
            editor.SetSpecial(expected.Special);
            if (!editor.CanConfirm || editor.AllocatedSpecial != 40)
                throw new InvalidOperationException(
                    "Fallout 2 custom editor rejected an exact bounded allocation.");
            await WaitForDraws(host, DrawFrames);
            var editorFrame = Capture(
                host,
                output,
                $"custom-{sex.ToLowerInvariant()}-editor.png");
            editor.Confirm();
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 custom character did not enter Arroyo.");
            for (var frame = 0; frame < GroundingFrames && !runtime.Player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            await WaitForDraws(host, DrawFrames);
            var worldFrame = Capture(
                host,
                output,
                $"custom-{sex.ToLowerInvariant()}-arroyo.png");
            var selection = host.SelectedCharacter ?? throw new InvalidOperationException(
                "Fallout 2 custom character handoff lost its selected state.");
            var saved = host.PersistCurrentState();
            var expectedFid = sex == "Female"
                ? Fo2CharacterStartCatalog.FemaleFid
                : Fo2ArroyoPlayerPresentationCatalog.ExpectedFid;
            var expectedLogicalPath = sex == "Female"
                ? Fo2CharacterStartCatalog.FemaleLogicalPath
                : Fo2ArroyoPlayerPresentationCatalog.ExpectedLogicalPath;
            var passed = Matches(selection, expected) && saved.Character == selection &&
                selection.Appearance.CustomFaceEdited &&
                selection.Appearance.CustomPortraitGenerated &&
                File.Exists(selection.Appearance.GeneratedPortraitPath) &&
                saved.MapIndex == Fo2ArroyoCavesPresentationCatalog.MapIndex &&
                saved.Elevation == Fo2ArroyoCavesPresentationCatalog.Elevation &&
                saved.ArrivalTile == 28707 && saved.CurrentTile == 28707 &&
                runtime.Player.IsOnFloor() && runtime.Player.Presentation.Visible &&
                runtime.SelectedPlayerPresentation.Fid == expectedFid &&
                runtime.SelectedPlayerPresentation.LogicalPath == expectedLogicalPath &&
                editorFrame.Sha256 != worldFrame.Sha256 &&
                File.Exists(saved.Path) && saved.Sha256.Length == 64;
            WriteReport(
                Path.Combine(output, $"custom-{sex.ToLowerInvariant()}-write-proof.json"),
                new
                {
                    schema = "opennv-fo2-custom-character-write-proof/v1",
                    status = passed
                        ? $"pass-{selection.Mode}-map3-atomic-save"
                        : "fail-fo2-custom-character-write",
                    selected = CharacterReport(selection),
                    sourceUi = new
                    {
                        picker = host.CharacterStart.Picker.LogicalPath,
                        pickerSha256 = host.CharacterStart.Picker.SourceSha256,
                        panel = selection.Source.Panel.LogicalPath,
                        panelSha256 = selection.Source.Panel.SourceSha256,
                    },
                    world = WorldReport(saved),
                    presentation = new
                    {
                        runtime.SelectedPlayerPresentation.Fid,
                        runtime.SelectedPlayerPresentation.LogicalPath,
                        visible = runtime.Player.Presentation.Visible,
                    },
                    frames = new[] { editorFrame, worldFrame },
                    save = new { path = saved.Path, sha256 = saved.Sha256, schema = Fo2CharacterStartSaveState.Schema },
                    exactBounds = new { nameMaximum = 11, ageMinimum = 16, ageMaximum = 35, specialMinimum = 1, specialMaximum = 10, specialTotal = 40 },
                    tagsAndTraits = expected.Modify ? "source-unchanged" : "unselected",
                    cancelPathPreservedState = true,
                    coldRestoreRequired = true,
                    windowsAppControlUsed = false,
                    foregroundInputInjected = false,
                });
            GD.Print(
                passed
                    ? $"OPENNV_FO2_CUSTOM_WRITE_PASS mode={selection.Mode} sex={sex} save={saved.Path}"
                    : $"OPENNV_FO2_CUSTOM_WRITE_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_CUSTOM_WRITE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
    }

    internal static async Task RunRestore(
        Fo2CharacterStartHost host,
        string proofRoot,
        string sex)
    {
        try
        {
            var output = PrepareOutput(proofRoot, true);
            var expected = Expected(sex);
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 custom-character cold restore did not enter Arroyo.");
            var saved = host.CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 custom-character cold restore did not retain its save.");
            var selection = host.SelectedCharacter ?? throw new InvalidOperationException(
                "Fallout 2 custom-character cold restore lost its character state.");
            var exactInitialPosition = runtime.Player.Position.IsEqualApprox(saved.Position);
            var exactInitialTile = runtime.Player.CurrentTile == saved.CurrentTile;
            var exactInitialRotation = runtime.Player.Presentation.Direction == saved.Rotation;
            for (var frame = 0; frame < GroundingFrames && !runtime.Player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            var expectedFid = sex == "Female"
                ? Fo2CharacterStartCatalog.FemaleFid
                : Fo2ArroyoPlayerPresentationCatalog.ExpectedFid;
            var passed = host.RestoredFromSave && Matches(selection, expected) &&
                selection == saved.Character && exactInitialPosition && exactInitialTile &&
                exactInitialRotation && runtime.Player.IsOnFloor() &&
                selection.Appearance.CustomFaceEdited &&
                selection.Appearance.CustomPortraitGenerated &&
                File.Exists(selection.Appearance.GeneratedPortraitPath) &&
                runtime.Player.Presentation.Visible &&
                runtime.SelectedPlayerPresentation.Fid == expectedFid &&
                saved.Sha256.Length == 64;
            WriteReport(
                Path.Combine(output, $"custom-{sex.ToLowerInvariant()}-restore-proof.json"),
                new
                {
                    schema = "opennv-fo2-custom-character-restore-proof/v1",
                    status = passed
                        ? $"pass-{selection.Mode}-map3-cold-restore"
                        : "fail-fo2-custom-character-restore",
                    selected = CharacterReport(selection),
                    world = WorldReport(saved),
                    restore = new
                    {
                        coldProcess = true,
                        exactInitialPosition,
                        exactInitialTile,
                        exactInitialRotation,
                        grounded = runtime.Player.IsOnFloor(),
                        visibleSexCorrectOwnedFrm = runtime.Player.Presentation.Visible,
                    },
                    save = new { path = saved.Path, sha256 = saved.Sha256, schema = Fo2CharacterStartSaveState.Schema },
                    windowsAppControlUsed = false,
                    foregroundInputInjected = false,
                });
            GD.Print(
                passed
                    ? $"OPENNV_FO2_CUSTOM_RESTORE_PASS mode={selection.Mode} sex={sex} save={saved.Path}"
                    : $"OPENNV_FO2_CUSTOM_RESTORE_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_CUSTOM_RESTORE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
    }

    private static ExpectedCharacter Expected(string sex) => sex switch
    {
        "Male" => new ExpectedCharacter(
            "Male", "Korin", 26, [7, 6, 7, 4, 5, 6, 5], 0, true,
            Fo2CharacterSelection.ModifyMode),
        "Female" => new ExpectedCharacter(
            "Female", "Mara", 18, [4, 7, 5, 6, 8, 5, 5], 2, false,
            Fo2CharacterSelection.CreateMode),
        _ => throw new InvalidOperationException(
            $"Unsupported Fallout 2 custom-character proof sex: {sex}"),
    };

    private static bool Matches(
        Fo2CharacterSelection selection,
        ExpectedCharacter expected) =>
        selection.Mode == expected.Mode &&
        selection.Source.Id == (expected.SourceIndex == 0 ? "combat" : "diplomat") &&
        selection.Profile.Name == expected.Name && selection.Profile.Sex == expected.Sex &&
        selection.Profile.Age == expected.Age &&
        selection.Profile.Special.SequenceEqual(expected.Special) &&
        (expected.Modify
            ? selection.Profile.TaggedSkills.SequenceEqual(selection.Source.Profile.TaggedSkills) &&
              selection.Profile.Traits.SequenceEqual(selection.Source.Profile.Traits)
            : selection.Profile.TaggedSkills.Count == 0 && selection.Profile.Traits.Count == 0);

    private static object CharacterReport(Fo2CharacterSelection character) => new
    {
        character.Mode,
        character.Id,
        sourceId = character.Source.Id,
        sourceRole = character.Source.Role,
        character.Profile.Name,
        character.Profile.Sex,
        character.Profile.Age,
        special = character.Profile.Special,
        taggedSkills = character.Profile.TaggedSkills,
        traits = character.Profile.Traits,
        character.GcdSha256,
        character.BioSha256,
        appearance = character.Appearance,
    };

    private static object WorldReport(Fo2CharacterStartSaveState saved) => new
    {
        saved.MapIndex,
        saved.Elevation,
        saved.ArrivalTile,
        saved.CurrentTile,
        saved.Rotation,
        position = new[] { saved.Position.X, saved.Position.Y, saved.Position.Z },
        saved.MapSha256,
        saved.WalkMaskSha256,
    };

    private static string PrepareOutput(string proofRoot, bool requireExisting)
    {
        var output = Path.GetFullPath(proofRoot);
        if (File.Exists(output) || requireExisting != Directory.Exists(output))
            throw new InvalidOperationException(
                requireExisting
                    ? $"Fallout 2 custom restore proof output is unavailable: {output}"
                    : $"Refusing to overwrite Fallout 2 custom proof: {output}");
        if (!requireExisting)
            Directory.CreateDirectory(output);
        return output;
    }

    private static async Task WaitForDraws(Node host, int count)
    {
        for (var frame = 0; frame < count; frame++)
            await host.ToSignal(
                RenderingServer.Singleton,
                RenderingServer.SignalName.FramePostDraw);
    }

    private static FrameEvidence Capture(Node host, string output, string filename)
    {
        var path = Path.Combine(output, filename);
        var image = host.GetViewport().GetTexture().GetImage();
        if (image.IsEmpty() || image.GetWidth() != ExpectedWidth ||
            image.GetHeight() != ExpectedHeight)
            throw new InvalidOperationException(
                "Fallout 2 custom-character viewport dimensions drifted.");
        if (image.SavePng(path) != Error.Ok)
            throw new InvalidOperationException(
                $"Could not save Fallout 2 custom-character frame: {path}");
        using var stream = File.OpenRead(path);
        return new FrameEvidence(
            path,
            stream.Length,
            image.GetWidth(),
            image.GetHeight(),
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static void WriteReport(string path, object report) => File.WriteAllText(
        path,
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
            System.Environment.NewLine);

    private sealed record ExpectedCharacter(
        string Sex,
        string Name,
        int Age,
        IReadOnlyList<int> Special,
        int SourceIndex,
        bool Modify,
        string Mode);

    private sealed record FrameEvidence(
        string Path,
        long Bytes,
        int Width,
        int Height,
        string Sha256);
}
