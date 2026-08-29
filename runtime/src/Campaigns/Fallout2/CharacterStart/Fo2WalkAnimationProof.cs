using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2WalkAnimationProof
{
    private const int GroundingFrames = 120;
    private const int MaximumStepFrames = 120;
    private const int SettleFrames = 4;
    private const int ExpectedWidth = 1280;
    private const int ExpectedHeight = 720;

    internal static async Task RunWrite(
        Fo2CharacterStartHost host,
        string proofRoot,
        string requestedSex)
    {
        string? pressedAction = null;
        try
        {
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 2 walk-animation write proof requires a rendering display driver.");
            var sex = ParseSex(requestedSex);
            var output = PrepareOutput(proofRoot, false);
            if (host.RestoredFromSave || host.Runtime is not null ||
                Fo2CharacterStartSaveState.Exists(host.SavePath))
                throw new InvalidOperationException(
                    "Fallout 2 walk-animation write proof requires an empty save boundary.");

            host.Picker.Select(sex == "Male" ? 0 : 2);
            host.Picker.ChooseCurrent();
            var selected = host.SelectedCharacter ?? throw new InvalidOperationException(
                "Fallout 2 walk-animation proof did not retain its character selection.");
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 walk-animation proof did not enter Arroyo.");
            var player = runtime.Player;
            for (var frame = 0; frame < GroundingFrames && !player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (!player.IsOnFloor() || selected.Profile.Sex != sex)
                throw new InvalidOperationException(
                    "Fallout 2 walk-animation proof did not ground the requested Chosen One.");

            var first = await Step(
                host,
                player,
                runtime.Profile.MoveBackward,
                Vector3.Back,
                output,
                $"{sex.ToLowerInvariant()}-walk-first.png",
                action => pressedAction = action);
            var second = await Step(
                host,
                player,
                runtime.Profile.MoveForward,
                Vector3.Forward,
                output,
                $"{sex.ToLowerInvariant()}-walk-second.png",
                action => pressedAction = action);
            var saved = host.PersistCurrentState();
            var presentation = player.Presentation;
            var source = runtime.SelectedPlayerPresentation;
            var passed = first.Passed && second.Passed &&
                first.Direction != second.Direction &&
                first.StartTile == second.EndTile &&
                first.EndTile == second.StartTile &&
                selected.Profile.Sex == sex &&
                source.Walk.Code == "AB" &&
                source.Walk.FramesPerSecond ==
                    Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerSecond &&
                source.Walk.Directions.Count == Fo1HexMath.DirectionCount &&
                source.Walk.Directions.Values.All(frames =>
                    frames.Count == Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection) &&
                !presentation.IsWalking && presentation.AnimationCode == "AA" &&
                presentation.AnimationFrame == Fo2ArroyoPlayerPresentationCatalog.IdleFrame &&
                saved.Character == selected && saved.CurrentTile == player.CurrentTile &&
                saved.Rotation == presentation.Direction &&
                saved.Position.IsEqualApprox(player.Position) &&
                saved.Sha256.Length == 64;
            var reportPath = Path.Combine(
                output,
                $"walk-{sex.ToLowerInvariant()}-write-proof.json");
            WriteReport(reportPath, new
            {
                schema = "opennv-fo2-walk-animation-write-proof/v1",
                status = passed
                    ? "pass-owned-pro-linked-aa-ab-two-direction-save"
                    : "fail-fo2-owned-walk-animation-write",
                sex,
                selected = new
                {
                    selected.Id,
                    selected.Profile.Name,
                    selected.Profile.Sex,
                    selected.GcdSha256,
                },
                presentation = PresentationReport(source, presentation),
                steps = new[] { first, second },
                idleResumed = !presentation.IsWalking &&
                    presentation.AnimationCode == "AA" && presentation.AnimationFrame == 0,
                save = new
                {
                    saved.Path,
                    saved.Sha256,
                    saved.CurrentTile,
                    saved.Rotation,
                    position = Vector(saved.Position),
                },
                windowsAppControlUsed = false,
                foregroundInputInjected = false,
                godotActionDrive = true,
                retailParity = false,
            });
            GD.Print(
                passed
                    ? $"OPENNV_FO2_WALK_WRITE_PASS sex={sex} directions={first.Direction},{second.Direction} save={saved.Path}"
                    : $"OPENNV_FO2_WALK_WRITE_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_WALK_WRITE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
        finally
        {
            if (pressedAction is not null)
                Input.ActionRelease(pressedAction);
        }
    }

    internal static async Task RunRestore(
        Fo2CharacterStartHost host,
        string proofRoot,
        string requestedSex)
    {
        try
        {
            var sex = ParseSex(requestedSex);
            var output = PrepareOutput(proofRoot, true);
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 walk-animation cold restore did not enter Arroyo.");
            var selected = host.SelectedCharacter ?? throw new InvalidOperationException(
                "Fallout 2 walk-animation cold restore did not retain its character.");
            var saved = host.CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 walk-animation cold restore did not retain its save contract.");
            var player = runtime.Player;
            var presentation = player.Presentation;
            var exactInitialPosition = player.Position.IsEqualApprox(saved.Position);
            var exactInitialTile = player.CurrentTile == saved.CurrentTile;
            var exactInitialDirection = presentation.Direction == saved.Rotation;
            var idleAtRestore = !presentation.IsWalking &&
                presentation.AnimationCode == "AA" && presentation.AnimationFrame == 0;
            for (var frame = 0; frame < GroundingFrames && !player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            var source = runtime.SelectedPlayerPresentation;
            var passed = host.RestoredFromSave && selected == saved.Character &&
                selected.Profile.Sex == sex && exactInitialPosition && exactInitialTile &&
                exactInitialDirection && idleAtRestore && player.IsOnFloor() &&
                source.Walk.Code == "AB" &&
                source.Walk.Directions.Count == Fo1HexMath.DirectionCount &&
                source.Walk.Directions.Values.All(frames =>
                    frames.Count == Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection) &&
                saved.Sha256.Length == 64;
            WriteReport(
                Path.Combine(output, $"walk-{sex.ToLowerInvariant()}-restore-proof.json"),
                new
                {
                    schema = "opennv-fo2-walk-animation-restore-proof/v1",
                    status = passed
                        ? "pass-owned-pro-linked-aa-ab-cold-restore"
                        : "fail-fo2-owned-walk-animation-restore",
                    sex,
                    selected = new
                    {
                        selected.Id,
                        selected.Profile.Name,
                        selected.Profile.Sex,
                        selected.GcdSha256,
                    },
                    presentation = PresentationReport(source, presentation),
                    restore = new
                    {
                        coldProcess = true,
                        exactInitialPosition,
                        exactInitialTile,
                        exactInitialDirection,
                        idleAtRestore,
                        grounded = player.IsOnFloor(),
                    },
                    save = new { saved.Path, saved.Sha256 },
                    windowsAppControlUsed = false,
                    foregroundInputInjected = false,
                    retailParity = false,
                });
            GD.Print(
                passed
                    ? $"OPENNV_FO2_WALK_RESTORE_PASS sex={sex} tile={saved.CurrentTile} save={saved.Path}"
                    : $"OPENNV_FO2_WALK_RESTORE_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_WALK_RESTORE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
    }

    private static async Task<StepEvidence> Step(
        Fo2CharacterStartHost host,
        Fo2ArroyoCavesPlayerBody player,
        Fo2ArroyoInputBinding binding,
        Vector3 desired,
        string output,
        string filename,
        Action<string?> setPressedAction)
    {
        var startTile = player.CurrentTile;
        var direction = Fo2ArroyoCavesPlayerBody.DirectionForMovement(startTile, desired);
        var expectedTile = Fo1HexMath.TileInDirection(startTile, direction);
        if (!player.CanOccupy(expectedTile))
            throw new InvalidOperationException(
                $"Fallout 2 walk proof direction {direction} is not source-walkable from {startTile}.");
        var presentation = player.Presentation;
        var startingAdvances = presentation.WalkFrameAdvances;
        var sawWalking = false;
        var directionStayedExact = true;
        var frames = 0;
        Input.ActionPress(binding.Action);
        setPressedAction(binding.Action);
        for (; frames < MaximumStepFrames; frames++)
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            sawWalking |= presentation.IsWalking;
            if (presentation.IsWalking && presentation.Direction != direction)
                directionStayedExact = false;
            if (player.CurrentTile != startTile &&
                presentation.WalkFrameAdvances - startingAdvances >= 2)
                break;
        }
        await host.ToSignal(
            RenderingServer.Singleton,
            RenderingServer.SignalName.FramePostDraw);
        var frame = Capture(host, output, filename);
        var walkingAtCapture = presentation.IsWalking &&
            presentation.AnimationCode == "AB" && presentation.Direction == direction;
        var capturedAnimationFrame = presentation.AnimationFrame;
        var capturedLogicalPath = presentation.CurrentFrame.LogicalPath;
        var capturedSourceSha256 = presentation.CurrentFrame.SourceSha256;
        var capturedPngSha256 = presentation.CurrentFrame.PngSha256;
        Input.ActionRelease(binding.Action);
        setPressedAction(null);
        for (var settle = 0; settle < SettleFrames; settle++)
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        var advances = presentation.WalkFrameAdvances - startingAdvances;
        var endTile = player.CurrentTile;
        var idleResumed = !presentation.IsWalking &&
            presentation.AnimationCode == "AA" && presentation.AnimationFrame == 0;
        return new StepEvidence(
            binding.Action,
            startTile,
            expectedTile,
            endTile,
            direction,
            frames,
            advances,
            sawWalking,
            walkingAtCapture,
            directionStayedExact,
            idleResumed,
            capturedAnimationFrame,
            capturedLogicalPath,
            capturedSourceSha256,
            capturedPngSha256,
            endTile == expectedTile && advances >= 2 && sawWalking &&
                walkingAtCapture && directionStayedExact && idleResumed,
            frame);
    }

    private static object PresentationReport(
        Fo2ArroyoPlayerPresentationSource source,
        Fo2ArroyoPlayerPresentation presentation) => new
        {
            source.Fid,
            source.PrototypePid,
            source.PrototypeLogicalPath,
            source.PrototypeSha256,
            idleLogicalPath = source.LogicalPath,
            idleSourceSha256 = source.SourceSha256,
            walkCode = source.Walk.Code,
            walkLogicalPath = source.Walk.LogicalPath,
            walkSourceSha256 = source.Walk.SourceSha256,
            walkFps = source.Walk.FramesPerSecond,
            walkFramesPerDirection = source.Walk.Directions.Values.First().Count,
            walkDirections = source.Walk.Directions.Count,
            presentation.WalkFrameAdvances,
            presentation.CompletedWalkCycles,
            presentation.AnimationCode,
            presentation.AnimationFrame,
            presentation.Direction,
        };

    private static FrameEvidence Capture(Node host, string output, string filename)
    {
        var path = Path.Combine(output, filename);
        var image = host.GetViewport().GetTexture().GetImage();
        if (image.IsEmpty() || image.GetWidth() != ExpectedWidth ||
            image.GetHeight() != ExpectedHeight)
            throw new InvalidOperationException(
                "Fallout 2 walk-animation proof viewport dimensions drifted.");
        if (image.SavePng(path) != Error.Ok)
            throw new InvalidOperationException(
                "Could not save Fallout 2 walk-animation proof frame.");
        using var stream = File.OpenRead(path);
        return new FrameEvidence(
            path,
            stream.Length,
            image.GetWidth(),
            image.GetHeight(),
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static string ParseSex(string value) => value switch
    {
        "Male" => value,
        "Female" => value,
        _ => throw new ArgumentException(
            "Fallout 2 walk-animation sex must be Male or Female.",
            nameof(value)),
    };

    private static string PrepareOutput(string proofRoot, bool requireExisting)
    {
        var output = Path.GetFullPath(proofRoot);
        if (File.Exists(output) || requireExisting != Directory.Exists(output))
            throw new InvalidOperationException(
                requireExisting
                    ? $"Fallout 2 walk restore proof output is unavailable: {output}"
                    : $"Refusing to overwrite Fallout 2 walk proof: {output}");
        if (!requireExisting)
            Directory.CreateDirectory(output);
        return output;
    }

    private static void WriteReport(string path, object report) => File.WriteAllText(
        path,
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
            System.Environment.NewLine);

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

    private sealed record StepEvidence(
        string Action,
        int StartTile,
        int ExpectedTile,
        int EndTile,
        int Direction,
        int PhysicsFrames,
        int FrameAdvances,
        bool SawWalking,
        bool WalkingAtCapture,
        bool DirectionStayedExact,
        bool IdleResumed,
        int CapturedAnimationFrame,
        string CapturedLogicalPath,
        string CapturedSourceSha256,
        string CapturedPngSha256,
        bool Passed,
        FrameEvidence Frame);

    private sealed record FrameEvidence(
        string Path,
        long Bytes,
        int Width,
        int Height,
        string Sha256);
}
