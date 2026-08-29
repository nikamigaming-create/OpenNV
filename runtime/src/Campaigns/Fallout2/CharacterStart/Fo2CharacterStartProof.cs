using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2CharacterStartProof
{
    private const int DrawFrames = 4;
    private const int GroundingFrames = 120;
    private const int ExpectedWidth = 1280;
    private const int ExpectedHeight = 720;

    internal static async Task Run(Fo2CharacterStartHost host, string proofRoot)
    {
        try
        {
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 2 character-start proof requires a rendering display driver.");
            var output = Path.GetFullPath(proofRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout 2 character-start proof: {output}");
            Directory.CreateDirectory(output);
            await WaitForDraws(host, DrawFrames);
            var initial = Capture(host, output, "character-start-narg.png");

            host.Picker.Select(2);
            var selected = host.Picker.Selected;
            await WaitForDraws(host, DrawFrames);
            var selectedFrame = Capture(host, output, "character-start-chitsa.png");
            host.Picker.ChooseCurrent();
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 character selection did not hand off to Arroyo.");
            for (var frame = 0; frame < GroundingFrames && !runtime.Player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            await WaitForDraws(host, DrawFrames);
            var world = Capture(host, output, "character-start-chitsa-arroyo.png");

            var profile = selected.Profile;
            var presentation = runtime.SelectedPlayerPresentation;
            var passed = selected.Id == "diplomat" &&
                profile.Name == "Chitsa" && profile.Sex == "Female" && profile.Age == 19 &&
                profile.Special.SequenceEqual([4, 5, 4, 10, 7, 6, 4]) &&
                profile.TaggedSkills.SequenceEqual(["Speech", "Barter", "First Aid"]) &&
                profile.Traits.SequenceEqual(["One Hander", "Night Person"]) &&
                presentation.Fid == Fo2CharacterStartCatalog.FemaleFid &&
                presentation.LogicalPath == Fo2CharacterStartCatalog.FemaleLogicalPath &&
                runtime.Player.Presentation.Visible &&
                runtime.Player.Presentation.Texture is not null &&
                runtime.Player.CurrentTile == 28707 &&
                runtime.Player.IsOnFloor() &&
                initial.Sha256 != selectedFrame.Sha256 &&
                selectedFrame.Sha256 != world.Sha256;
            var report = new
            {
                schema = "opennv-fo2-character-start-runtime-proof/v1",
                status = passed
                    ? "pass-owned-premade-selection-to-arroyo-arrival-no-save"
                    : "fail-character-start-runtime-gate",
                campaign = "Fallout2",
                source = new
                {
                    profileId = host.CharacterStart.SourceProfileId,
                    cache = host.CharacterStart.ManifestPath,
                    cacheManifestSha256 = host.CharacterStart.ManifestSha256,
                    recipeSha256 = host.CharacterStart.RecipeSha256,
                    verifiedResources = host.CharacterStart.VerifiedResources,
                    pickerFrm = host.CharacterStart.Picker.LogicalPath,
                    pickerSourceSha256 = host.CharacterStart.Picker.SourceSha256,
                },
                roster = host.CharacterStart.Characters.Select(row => new
                {
                    row.Id,
                    row.Role,
                    row.Profile.Name,
                    row.Profile.Sex,
                    row.Profile.Age,
                    special = row.Profile.Special,
                    tags = row.Profile.TaggedSkills,
                    traits = row.Profile.Traits,
                    row.GcdSha256,
                    row.BioSha256,
                    panelSourceSha256 = row.Panel.SourceSha256,
                }).ToArray(),
                selected = new
                {
                    selected.Id,
                    selected.Role,
                    profile.Name,
                    profile.Sex,
                    profile.Age,
                    special = profile.Special,
                    tags = profile.TaggedSkills,
                    traits = profile.Traits,
                    selected.GcdSha256,
                    presentation.Fid,
                    presentation.LogicalPath,
                    presentation.SourceSha256,
                    sourceDirections = presentation.Directions.Count,
                    animationPlayback = false,
                },
                handoff = new
                {
                    mapIndex = host.Scene?.MapIndex,
                    elevation = host.Scene?.Elevation,
                    arrivalTile = runtime.Player.ArrivalTile,
                    currentTile = runtime.Player.CurrentTile,
                    position = new[]
                    {
                        runtime.Player.Position.X,
                        runtime.Player.Position.Y,
                        runtime.Player.Position.Z,
                    },
                    grounded = runtime.Player.IsOnFloor(),
                    visibleCharacter = runtime.Player.Presentation.Visible,
                    exactMapArrivalPreserved = runtime.Player.ArrivalTile == 28707,
                },
                frames = new[] { initial, selectedFrame, world },
                promotion = new
                {
                    transported = true,
                    rendered = true,
                    ownedPremadeRosterSelectable = passed,
                    selectedStateAppliedToPlayer = passed,
                    immediateArroyoHandoff = passed,
                    humanKeyboardAndMouseEntryAvailable = true,
                    modifyRoute = false,
                    customCharacterRoute = false,
                    playerStatePersistent = false,
                    interactive = false,
                    playableCampaign = false,
                    launcherPlayable = false,
                    retailParityReviewed = false,
                },
                proofSelectionMode = "direct-runtime-selection-no-host-input",
                windowsAppControlUsed = false,
                foregroundInputInjected = false,
            };
            File.WriteAllText(
                Path.Combine(output, "fo2-character-start-proof.json"),
                JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            if (passed)
                GD.Print(
                    $"OPENNV_FO2_CHARACTER_START_PASS name={profile.Name} " +
                    $"sex={profile.Sex} tile={runtime.Player.CurrentTile} output={output}");
            else
                GD.PushError($"OPENNV_FO2_CHARACTER_START_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_CHARACTER_START_FAIL {exception}");
            host.GetTree().Quit(1);
        }
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
                "Fallout 2 character-start viewport dimensions drifted.");
        if (image.SavePng(path) != Error.Ok)
            throw new InvalidOperationException(
                $"Could not save Fallout 2 character-start frame: {path}");
        using var stream = File.OpenRead(path);
        return new FrameEvidence(
            path,
            stream.Length,
            image.GetWidth(),
            image.GetHeight(),
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private sealed record FrameEvidence(
        string Path,
        long Bytes,
        int Width,
        int Height,
        string Sha256);
}
