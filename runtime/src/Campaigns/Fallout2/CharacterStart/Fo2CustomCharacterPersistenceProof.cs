using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;

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
            editor.SetFaceShape(expected.Appearance.FaceShapeId);
            editor.SetHairStyle(expected.Appearance.HairStyleId);
            editor.SetSkinTone(expected.Appearance.SkinToneId);
            editor.SetHairColor(expected.Appearance.HairColorId);
            editor.SetEyeColor(expected.Appearance.EyeColorId);
            editor.SetBrowStyle(expected.Appearance.BrowStyleId);
            editor.SetNoseStyle(expected.Appearance.NoseStyleId);
            editor.SetMouthStyle(expected.Appearance.MouthStyleId);
            foreach (var role in BodyRoles)
                editor.SetBodyProportion(role, expected.Body.Value(role));
            editor.ToggleBodyControls();
            var preview = editor.LivePreview;
            preview._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.WheelUp,
                Pressed = true,
            });
            var zoomAfterWheel = preview.Zoom;
            preview._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            });
            preview._GuiInput(new InputEventMouseMotion
            {
                Relative = new Vector2(36.0f, 0.0f),
            });
            preview._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
            });
            var orbitAfterDrag = preview.OrbitYawRadians;
            preview._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Middle,
                Pressed = true,
            });
            var viewerInteractionPassed = zoomAfterWheel < 1.0f &&
                MathF.Abs(orbitAfterDrag) > 0.1f &&
                Mathf.IsEqualApprox(preview.Zoom, 1.0f) &&
                Mathf.IsZeroApprox(preview.OrbitYawRadians);
            if (!editor.CanConfirm || editor.AllocatedSpecial != 40)
                throw new InvalidOperationException(
                    "Fallout 2 custom editor rejected an exact bounded allocation.");
            await WaitForDraws(host, DrawFrames);
            var editorFrame = Capture(
                host,
                output,
                $"custom-{sex.ToLowerInvariant()}-editor.png");
            editor.ToggleClassicProjection();
            await WaitForDraws(host, DrawFrames);
            var classicProjectionFrame = Capture(
                host,
                output,
                $"custom-{sex.ToLowerInvariant()}-classic-projection.png");
            var classicProjectionPassed = editor.ClassicProjectionVisible &&
                preview.ClassicPortraitProjection && !editor.Live3DVisible &&
                classicProjectionFrame.Sha256 != editorFrame.Sha256;
            editor.ToggleClassicProjection();
            await WaitForDraws(host, DrawFrames);
            var projectionRoundTripPassed = editor.Live3DVisible &&
                !editor.ClassicProjectionVisible &&
                !preview.ClassicPortraitProjection;
            editor.Confirm();
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 custom character did not enter Arroyo.");
            for (var frame = 0; frame < GroundingFrames && !runtime.Player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            await WaitForDraws(host, DrawFrames);
            var worldFrame = await CaptureGameplayCharacter(
                host,
                runtime.Player.VillageHumanoid ?? throw new InvalidOperationException(
                    "Fallout 2 custom character entered Arroyo without its humanoid."),
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
                editor.Live3DVisible && editor.BodyControlsVisible &&
                !editor.AppearanceControlsVisible && viewerInteractionPassed &&
                classicProjectionPassed && projectionRoundTripPassed &&
                Mathf.IsEqualApprox(
                    preview.CompositionRightOffset,
                    Fo2PremadeHumanoidPreview.EditorColumnCompositionRightOffset) &&
                selection.Appearance.BodyProportions == expected.Body &&
                runtime.Player.VillageHumanoid?.Proportions == expected.Body &&
                runtime.Player.VillageHumanoid?.Appearance == expected.Appearance &&
                runtime.Player.VillageHumanoid?.AppliedFaceGeometryControlCount ==
                    ExpectedFaceControlCount(expected) &&
                selection.Appearance.CustomFaceEdited &&
                selection.Appearance.CustomPortraitGenerated &&
                File.Exists(selection.Appearance.GeneratedPortraitPath) &&
                saved.MapIndex == Fo2ArroyoCavesPresentationCatalog.MapIndex &&
                saved.Elevation == Fo2ArroyoCavesPresentationCatalog.Elevation &&
                saved.ArrivalTile == 28707 && saved.CurrentTile == 28707 &&
                runtime.Player.IsOnFloor() &&
                runtime.Player.VillageHumanoid is { Visible: true, UsesOwnedDonor: true } &&
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
                        visibleOwned3DHumanoid =
                        runtime.Player.VillageHumanoid?.Visible == true,
                        sourceFrmReliefHidden = !runtime.Player.Presentation.Visible,
                        appearance = runtime.Player.VillageHumanoid?.Appearance,
                        nativeFaceGenControls = runtime.Player.VillageHumanoid?
                            .AppliedFaceGeometryControlCount,
                    },
                    frames = new[] { editorFrame, classicProjectionFrame, worldFrame },
                    viewer = new
                    {
                        dragOrbit = true,
                        wheelZoom = true,
                        middleClickReset = true,
                        zoomAfterWheel,
                        orbitAfterDrag,
                        resetZoom = preview.Zoom,
                        resetOrbit = preview.OrbitYawRadians,
                        compositionRightOffset = preview.CompositionRightOffset,
                        appearanceControlsHiddenInBodyMode =
                            !editor.AppearanceControlsVisible,
                        classicProjection = new
                        {
                            source = "current-data-bound-3d-character",
                            shader = "frozen-stylized-live-character-projection",
                            substitutedModel = false,
                            classicProjectionPassed,
                            projectionRoundTripPassed,
                            frame = classicProjectionFrame,
                        },
                    },
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
                selection.Appearance.BodyProportions == expected.Body &&
                runtime.Player.VillageHumanoid?.Proportions == expected.Body &&
                runtime.Player.VillageHumanoid?.Appearance == expected.Appearance &&
                runtime.Player.VillageHumanoid?.AppliedFaceGeometryControlCount ==
                    ExpectedFaceControlCount(expected) &&
                selection.Appearance.CustomFaceEdited &&
                selection.Appearance.CustomPortraitGenerated &&
                File.Exists(selection.Appearance.GeneratedPortraitPath) &&
                runtime.Player.VillageHumanoid is { Visible: true, UsesOwnedDonor: true } &&
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
                        visibleOwned3DHumanoid =
                            runtime.Player.VillageHumanoid?.Visible == true,
                        sourceFrmReliefHidden = !runtime.Player.Presentation.Visible,
                        appearance = runtime.Player.VillageHumanoid?.Appearance,
                        nativeFaceGenControls = runtime.Player.VillageHumanoid?
                            .AppliedFaceGeometryControlCount,
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
            Fo2CharacterSelection.ModifyMode,
            Fo2CharacterBodyProfile.ForSex("Male") with
            {
                Height = 1.08f,
                Chest = 1.20f,
                Waist = 0.90f,
            },
            new Fo2HumanoidAppearance(
                Fo2ProceduralPortrait.AngularFace,
                Fo2ProceduralPortrait.SweptHair,
                Fo2ProceduralPortrait.LightSkin,
                Fo2ProceduralPortrait.AuburnHairColor,
                Fo2ProceduralPortrait.BlueEyeColor,
                Fo2ProceduralPortrait.HeavyBrow,
                Fo2ProceduralPortrait.BroadNose,
                Fo2ProceduralPortrait.WideMouth)),
        "Female" => new ExpectedCharacter(
            "Female", "Mara", 18, [4, 7, 5, 6, 8, 5, 5], 2, false,
            Fo2CharacterSelection.CreateMode,
            Fo2CharacterBodyProfile.ForSex("Female") with
            {
                Height = 0.94f,
                Shoulders = 0.92f,
                Thighs = 1.08f,
            },
            new Fo2HumanoidAppearance(
                Fo2ProceduralPortrait.RoundFace,
                Fo2ProceduralPortrait.LongHair,
                Fo2ProceduralPortrait.DeepSkin,
                Fo2ProceduralPortrait.BlackHairColor,
                Fo2ProceduralPortrait.GreenEyeColor,
                Fo2ProceduralPortrait.ArchedBrow,
                Fo2ProceduralPortrait.NarrowNose,
                Fo2ProceduralPortrait.SmallMouth)),
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
        Fo2HumanoidAppearance.FromContract(selection.Appearance) == expected.Appearance &&
        (expected.Modify
            ? selection.Profile.TaggedSkills.SequenceEqual(selection.Source.Profile.TaggedSkills) &&
              selection.Profile.Traits.SequenceEqual(selection.Source.Profile.Traits)
            : selection.Profile.TaggedSkills.Count == 0 && selection.Profile.Traits.Count == 0);

    private static int ExpectedFaceControlCount(ExpectedCharacter expected) =>
        Fo2ProceduralAppearanceCatalog.Load().NativeFaceGenControls(
            expected.Appearance.FaceShapeId,
            expected.Appearance.BrowStyleId,
            expected.Appearance.NoseStyleId,
            expected.Appearance.MouthStyleId).Count;

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

    private static async Task<FrameEvidence> CaptureGameplayCharacter(
        Fo2CharacterStartHost host,
        Temple.Fo2HumanoidVisual humanoid,
        string output,
        string filename)
    {
        var bounds = humanoid.PresentationBounds;
        var aspect = ExpectedWidth / (float)ExpectedHeight;
        var size = MathF.Max(bounds.Size.Y, bounds.Size.X / aspect) * 1.24f;
        if (!bounds.Position.IsFinite() || !bounds.Size.IsFinite() ||
            !float.IsFinite(size) || size <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 2 gameplay humanoid framing bounds are invalid.");
        var target = bounds.GetCenter();
        var proofViewport = new SubViewport
        {
            Name = "FO2_CUSTOM_CHARACTER_GAMEPLAY_PROOF_VIEWPORT",
            Size = new Vector2I(ExpectedWidth, ExpectedHeight),
            OwnWorld3D = true,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        host.AddChild(proofViewport);
        proofViewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("07100b"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.74f,
            },
        });
        proofViewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-26.0f, -30.0f, 0.0f),
            LightEnergy = 1.15f,
            ShadowEnabled = false,
        });
        var proofCamera = new Camera3D
        {
            Name = "FO2_CUSTOM_CHARACTER_GAMEPLAY_PROOF_CAMERA",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = size,
            Near = 0.01f,
            Far = 20.0f,
        };
        var originalParent = humanoid.GetParent();
        var originalTransform = humanoid.GlobalTransform;
        humanoid.Reparent(proofViewport, true);
        proofViewport.AddChild(proofCamera);
        proofCamera.GlobalPosition = target + Vector3.Forward *
            MathF.Max(1.25f, bounds.Size.Z + 0.4f);
        proofCamera.LookAt(target, Vector3.Up);
        proofCamera.Current = true;
        try
        {
            await WaitForDraws(host, DrawFrames);
            if (proofViewport.GetCamera3D() != proofCamera)
                throw new InvalidOperationException(
                    "Fallout 2 gameplay actor proof camera did not remain current.");
            return Capture(proofViewport, output, filename);
        }
        finally
        {
            humanoid.Reparent(originalParent, true);
            humanoid.GlobalTransform = originalTransform;
            proofViewport.QueueFree();
        }
    }

    private static FrameEvidence Capture(Node host, string output, string filename) =>
        Capture(host.GetViewport(), output, filename);

    private static FrameEvidence Capture(Viewport viewport, string output, string filename)
    {
        var path = Path.Combine(output, filename);
        var image = viewport.GetTexture().GetImage();
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
        string Mode,
        CharacterBodyProportions Body,
        Fo2HumanoidAppearance Appearance);

    private static readonly string[] BodyRoles =
    [
        "height", "chest", "shoulders", "waist", "arms", "thighs", "calves",
    ];

    private sealed record FrameEvidence(
        string Path,
        long Bytes,
        int Width,
        int Height,
        string Sha256);
}
