using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2OpeningHandoffProofNumericContracts
{
    internal const int GroundingFrames = 120;
    internal const int MovementFrames = 120;
    internal const int NeutralReleaseFrames = 4;
    internal const int Sha256HexLength = 64;
}

internal static class Fo2OpeningHandoffProof
{
    internal static async Task Run(Fo2CharacterStartHost host, string proofRoot)
    {
        string? pressedAction = null;
        try
        {
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 2 opening handoff proof requires a rendering display driver.");
            var output = Path.GetFullPath(proofRoot);
            if (Directory.Exists(output))
                throw new InvalidOperationException(
                    $"Fallout 2 opening proof output already exists: {output}");
            Directory.CreateDirectory(output);
            if (host.RestoredFromSave || host.Runtime is not null ||
                Fo2CharacterStartSaveState.Exists(host.SavePath))
                throw new InvalidOperationException(
                    "Fallout 2 opening proof requires a clean new-game save boundary.");
            var contract = host.CharacterStart.OpeningTail ??
                throw new InvalidOperationException(
                    "Fallout 2 opening proof requires the v2 owned Elder-tail cache.");

            host.Picker.Select(0);
            host.Picker.ChooseCurrent();
            var handoff = host.OpeningHandoff ?? throw new InvalidOperationException(
                "Fallout 2 opening handoff did not start.");
            await (host.OpeningHandoffTask ?? throw new InvalidOperationException(
                "Fallout 2 opening handoff task is unavailable."));
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 opening handoff did not prepare Arroyo.");
            var worldAudit = handoff.World3DAudit ?? throw new InvalidOperationException(
                "Fallout 2 opening handoff did not run its global/frustum 3D audit.");
            var sourceClosure = handoff.SourceClosure ?? throw new InvalidOperationException(
                "Fallout 2 opening handoff did not run its source-closure ledger.");
            var player = runtime.Player;
            for (var frame = 0;
                 frame < Fo2OpeningHandoffProofNumericContracts.GroundingFrames &&
                    !player.IsOnFloor();
                 frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            for (var frame = 0;
                 frame < Fo2OpeningHandoffProofNumericContracts.NeutralReleaseFrames;
                 frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);

            var movement = SelectFirstAction(runtime, player);
            var startTile = player.CurrentTile;
            Input.ActionPress(movement.Binding.Action);
            pressedAction = movement.Binding.Action;
            var movementFrames = 0;
            for (; movementFrames < Fo2OpeningHandoffProofNumericContracts.MovementFrames;
                 movementFrames++)
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
                if (player.CurrentTile != startTile)
                    break;
            }
            await host.ToSignal(
                RenderingServer.Singleton,
                RenderingServer.SignalName.FramePostDraw);
            var firstActionFrame = Capture(
                host,
                output,
                "04-live-arroyo-first-action.png");
            Input.ActionRelease(movement.Binding.Action);
            pressedAction = null;

            var expectedFrames = Enumerable.Range(
                contract.PlaybackStartFrame,
                contract.TerminalFrame - contract.PlaybackStartFrame + 1).ToArray();
            var exactSourceSequence = handoff.PresentedSourceFrames.SequenceEqual(expectedFrames);
            var exactCameraSeam = handoff.PreparedCameraTransform.IsEqualApprox(
                handoff.RevealedCameraTransform);
            var firstActionPassed = player.CurrentTile == movement.ExpectedTile &&
                player.CurrentTile != startTile && player.ControlsEnabled;
            var evidence = new[]
            {
                Evidence("raw-owned-terminal", handoff.RawTerminalFramePath),
                Evidence("rendered-source-fade-terminal", handoff.RenderedTerminalFramePath),
                Evidence("movie-end-black", handoff.BlackSeamFramePath),
                Evidence("adapted-live-arroyo-before-control", handoff.LiveRevealFramePath),
                Evidence("live-arroyo-first-action", firstActionFrame),
            };
            var passed = handoff.Completed && !handoff.SkipRequested &&
                !handoff.SkipTerminalStateApplied && handoff.TerminalBlackPresented &&
                handoff.ControlReleased && exactSourceSequence && exactCameraSeam &&
                handoff.FinalPresentedSourceFrame == contract.TerminalFrame &&
                Mathf.IsEqualApprox(
                    handoff.FinalSourceFadeFraction,
                    contract.TerminalFadeFraction) &&
                player.IsOnFloor() && firstActionPassed &&
                worldAudit.VisibleSpriteCards == 0 &&
                worldAudit.InFrustumSpriteCards == 0 &&
                sourceClosure.UnaccountedSourceObjects == 0 &&
                sourceClosure.FirstBeatRuntimeClosurePassed &&
                evidence.All(row =>
                    row.Sha256.Length == Fo2OpeningHandoffProofNumericContracts.Sha256HexLength &&
                    row.Bytes > 0);
            var reportPath = Path.Combine(output, "fo2-opening-handoff-proof.json");
            WriteReport(reportPath, new
            {
                schema = "opennv-fo2-opening-handoff-proof/v1",
                status = passed
                    ? "pass-owned-elder-full-source-sequence-black-adapted-live-action"
                    : "fail-fo2-opening-handoff",
                source = new
                {
                    contract.MovieLogicalPath,
                    contract.MovieSha256,
                    contract.MovieBytes,
                    contract.FadeConfigLogicalPath,
                    contract.FadeConfigSha256,
                    contract.FadeConfigBytes,
                    contract.PlaybackStartFrame,
                    contract.TailStartFrame,
                    contract.TerminalFrame,
                    contract.TerminalFrameRepeatedFrom,
                    contract.TerminalFramePngSha256,
                    frameRate = new
                    {
                        numerator = contract.FrameRateNumerator,
                        denominator = contract.FrameRateDenominator,
                    },
                    nominalMovieDuration = new
                    {
                        numerator = contract.AudioDurationNumerator,
                        denominator = contract.AudioDurationDenominator,
                    },
                    fade = new
                    {
                        contract.FadeStartFrame,
                        contract.FadeEndFrame,
                        contract.FadeSteps,
                        finalPresentedStep = contract.TerminalFadeStep,
                        finalPresentedFraction = handoff.FinalSourceFadeFraction,
                        contract.MovieEndForcesBlack,
                    },
                    exactSourceSequence,
                    presentedFrames = handoff.PresentedSourceFrames,
                    audio = new
                    {
                        contract.AudioSha256,
                        contract.AudioBytes,
                        contract.AudioSampleRate,
                        contract.AudioChannels,
                        contract.AudioSampleBytes,
                        contract.AudioSampleFrames,
                    },
                },
                seam = new
                {
                    contract.HandoffPresentation,
                    contract.ParityStatus,
                    authoredMovieFromFirstFrame = true,
                    authoredFadeSchedule = true,
                    authoredMovieEndBlack = true,
                    liveRevealAdapted = true,
                    pixelMatched = false,
                    handoff.TerminalBlackPresented,
                    exactCameraSeam,
                    preparedCamera = Transform(handoff.PreparedCameraTransform),
                    revealedCamera = Transform(handoff.RevealedCameraTransform),
                },
                live = new
                {
                    mapIndex = player.CurrentMapIndex,
                    elevation = player.CurrentElevation,
                    arrivalTile = contract.HandoffArrivalTile,
                    arrivalRotation = contract.HandoffArrivalRotation,
                    controlReleased = player.ControlsEnabled,
                    grounded = player.IsOnFloor(),
                    cameraComposition = new
                    {
                        runtime.Profile.CameraCompositionMode,
                        sourceFramePixels = new[]
                        {
                            runtime.Profile.CameraSourceFramePixels.X,
                            runtime.Profile.CameraSourceFramePixels.Y,
                        },
                        sourceHudCropHeightPixels =
                            runtime.Profile.CameraSourceHudCropHeightPixels,
                        player.CameraSourcePixelScale,
                        player.CameraVisibleSourceFrameHeightPixels,
                        player.CameraSourceFrameCropPixels,
                        player.CameraWorldViewportHeightPixels,
                        player.CameraSizeMeters,
                        authority =
                            "owned classic frame/HUD crop plus source floor/FRM pixel projection",
                    },
                    firstAction = new
                    {
                        movement.Binding.Action,
                        startTile,
                        expectedTile = movement.ExpectedTile,
                        endTile = player.CurrentTile,
                        movementFrames,
                        passed = firstActionPassed,
                    },
                    classicHud = new
                    {
                        runtime.Hud.Mode,
                        runtime.Hud.OwnedFallout2ClassicInterface,
                        runtime.Hud.SourcePixelLayout,
                        runtime.Hud.FirstMovementBeatStateComplete,
                        runtime.Hud.State.CharacterId,
                        runtime.Hud.State.HitPoints,
                        runtime.Hud.State.MaximumHitPoints,
                        runtime.Hud.State.ArmorClass,
                        runtime.Hud.State.ActionPoints,
                        runtime.Hud.State.MaximumActionPoints,
                        runtime.Hud.State.Authority,
                        runtime.Hud.RetailBehaviorParity,
                    },
                },
                world3dAudit = new
                {
                    scope = "entire-live-scene-and-active-gameplay-camera-frustum",
                    worldAudit.SourceSpriteNodes,
                    worldAudit.VisibleSpriteCards,
                    worldAudit.InFrustumSpriteCards,
                    worldAudit.ClosedReliefSourceObjects,
                    worldAudit.Critters3D,
                    worldAudit.Doors3D,
                    worldAudit.Torches3D,
                    worldAudit.OtherPropsAndStonePosts3D,
                    worldAudit.SourceTorchAssemblies,
                    worldAudit.SourceTorchFrmPixelProps,
                    worldAudit.SourceMapLightRecords,
                    worldAudit.SourceMapLights,
                    worldAudit.SourceTorchMotivatedMapLights,
                    worldAudit.SourceTorchPostLayeredAssemblies,
                    worldAudit.InFrustumTorchAssemblies,
                    worldAudit.InFrustumTorchFrmPixelProps,
                    worldAudit.InFrustumTorchAssembliesWithMissingSourcePixels,
                    worldAudit.InvalidSourceMapLights,
                    passed = worldAudit.VisibleSpriteCards == 0 &&
                        worldAudit.InFrustumSpriteCards == 0 &&
                        worldAudit.SourceTorchPostLayeredAssemblies > 0 &&
                        worldAudit.InFrustumTorchAssembliesWithMissingSourcePixels == 0 &&
                        worldAudit.InvalidSourceMapLights == 0,
                },
                sourceClosure = new
                {
                    sourceClosure.SourceTopLevelObjects,
                    sourceClosure.CaveShell3DSourceObjects,
                    sourceClosure.ClosedRelief3DSourceObjects,
                    sourceClosure.ConvertedTo3DSourceObjects,
                    sourceClosure.IntentionallyHiddenSourceNonvisualBlocks,
                    sourceClosure.IntentionallyHiddenSourceExitMarkers,
                    sourceClosure.IntentionallyHiddenBySourceState,
                    sourceClosure.ClassifiedSourceObjects,
                    sourceClosure.UnaccountedSourceObjects,
                    sourceClosure.ScriptBackedSourceObjects,
                    sourceClosure.ImplementedSourceScripts,
                    sourceClosure.BehaviorIncompleteSourceObjects,
                    sourceClosure.AdmittedFirstActionTiles,
                    sourceClosure.AdmittedScriptBackedSourceObjects,
                    sourceClosure.AdmittedExitMarkerSourceObjects,
                    sourceClosure.AdmittedInactiveExitMarkers,
                    sourceClosure.OutOfBeatSourceBoundaryExitMarkerInactive,
                    sourceClosure.AdmittedBehaviorIncompleteSourceObjects,
                    sourceClosure.OutOfBeatDeferredBehaviorSourceObjects,
                    sourceClosure.FirstBeatRuntimeClosurePassed,
                    unaccounted = sourceClosure.UnaccountedSourceObjects,
                },
                evidence,
                windowsAppControlUsed = false,
                foregroundInputInjected = false,
                godotActionDrive = true,
                fullIntroReplay = false,
                retailParity = false,
            });
            GD.Print(
                passed
                    ? $"OPENNV_FO2_OPENING_HANDOFF_PASS report={reportPath} action={movement.Binding.Action} tile={player.CurrentTile}"
                    : $"OPENNV_FO2_OPENING_HANDOFF_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_OPENING_HANDOFF_FAIL {exception}");
            host.GetTree().Quit(1);
        }
        finally
        {
            if (pressedAction is not null)
                Input.ActionRelease(pressedAction);
        }
    }

    private static (Fo2ArroyoInputBinding Binding, int ExpectedTile) SelectFirstAction(
        Fo2ArroyoCavesPlayerRuntimeCoverage runtime,
        Fo2ArroyoCavesPlayerBody player)
    {
        var candidates = new[]
        {
            (runtime.Profile.MoveBackward, Vector3.Back),
            (runtime.Profile.MoveForward, Vector3.Forward),
            (runtime.Profile.MoveRight, Vector3.Right),
            (runtime.Profile.MoveLeft, Vector3.Left),
        };
        foreach (var (binding, desired) in candidates)
        {
            var direction = Fo2ArroyoCavesPlayerBody.DirectionForMovement(
                player.CurrentTile,
                desired);
            var tile = Fo1HexMath.TileInDirection(player.CurrentTile, direction);
            if (player.CanOccupy(tile))
                return (binding, tile);
        }
        throw new InvalidOperationException(
            "Fallout 2 opening arrival has no configured source-walkable first action.");
    }

    private static string Capture(
        Fo2CharacterStartHost host,
        string output,
        string filename)
    {
        var path = Path.Combine(output, filename);
        var image = host.GetViewport().GetTexture().GetImage();
        if (image.IsEmpty() || image.SavePng(path) != Error.Ok)
            throw new InvalidOperationException(
                $"Fallout 2 first-action frame could not be written: {path}");
        return path;
    }

    private static object Transform(Transform3D transform) => new
    {
        origin = Vector(transform.Origin),
        basisX = Vector(transform.Basis.X),
        basisY = Vector(transform.Basis.Y),
        basisZ = Vector(transform.Basis.Z),
    };

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

    private static EvidenceRow Evidence(string beat, string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new EvidenceRow(
            beat,
            path,
            bytes.LongLength,
            Fo2TemplePresentationCatalog.Sha256(bytes));
    }

    private static void WriteReport(string path, object value) =>
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                value,
                new JsonSerializerOptions { WriteIndented = true }));

    private sealed record EvidenceRow(
        string Beat,
        string Path,
        long Bytes,
        string Sha256);
}
