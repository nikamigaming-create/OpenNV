using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;


namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal static class Fo2ArrivalVisualProof
{
    private const int GroundingFrames = 120;
    private const int SettleFrames = 4;
    private const int ExpectedWidth = 1280;
    private const int ExpectedHeight = 720;
    private const int ExpectedSourceMapLights = 33;
    private const int ExpectedTorchMotivatedMapLights = 22;

    internal static async Task Run(Fo2CharacterStartHost host, string proofRoot)
    {
        try
        {
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 2 arrival visual proof requires a rendering display driver.");
            var output = Path.GetFullPath(proofRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout 2 arrival visual proof: {output}");
            Directory.CreateDirectory(output);
            var contract = host.CharacterStart.OpeningTail ??
                throw new InvalidOperationException(
                    "Fallout 2 arrival visual proof requires the owned Elder-tail contract.");

            host.Picker.Select(0);
            host.Picker.ChooseCurrent();
            var handoff = host.OpeningHandoff ?? throw new InvalidOperationException(
                "Fallout 2 arrival visual proof did not start its source handoff.");
            handoff.RequestSkip();
            await (host.OpeningHandoffTask ?? throw new InvalidOperationException(
                "Fallout 2 arrival visual proof has no handoff task."));

            var scene = host.Scene ?? throw new InvalidOperationException(
                "Fallout 2 arrival visual proof has no Arroyo scene.");
            var runtime = host.Runtime ?? throw new InvalidOperationException(
                "Fallout 2 arrival visual proof has no player runtime.");
            var player = runtime.Player;
            for (var frame = 0; frame < GroundingFrames && !player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            for (var frame = 0; frame < SettleFrames; frame++)
                await host.ToSignal(
                    RenderingServer.Singleton,
                    RenderingServer.SignalName.FramePostDraw);

            var world = handoff.World3DAudit ?? throw new InvalidOperationException(
                "Fallout 2 arrival visual proof has no 3D audit.");
            var closure = handoff.SourceClosure ?? throw new InvalidOperationException(
                "Fallout 2 arrival visual proof has no source-closure ledger.");
            if (!player.IsOnFloor() ||
                player.CurrentTile != host.Arroyo.ArrivalTile ||
                !runtime.Hud.VisibleInViewport ||
                !runtime.Hud.FirstMovementBeatStateComplete ||
                !runtime.Hud.OwnedFallout2ClassicInterface ||
                world.VisibleSpriteCards != 0 ||
                world.InFrustumSpriteCards != 0 ||
                world.InvalidSourceMapLights != 0 ||
                scene.Molded3D.SourceMapLightRecords != ExpectedSourceMapLights ||
                scene.Molded3D.SourceMapLights != ExpectedSourceMapLights ||
                scene.Molded3D.SourceTorchMotivatedMapLights !=
                    ExpectedTorchMotivatedMapLights ||
                closure.UnaccountedSourceObjects != 0 ||
                !closure.FirstBeatRuntimeClosurePassed ||
                !handoff.SkipRequested ||
                !handoff.SkipTerminalStateApplied ||
                handoff.FinalPresentedSourceFrame != contract.TerminalFrame ||
                !Mathf.IsEqualApprox(
                    handoff.FinalSourceFadeFraction,
                    contract.TerminalFadeFraction) ||
                !handoff.TerminalBlackPresented ||
                !handoff.Completed ||
                !handoff.ControlReleased ||
                !handoff.PreparedCameraTransform.IsEqualApprox(
                    handoff.RevealedCameraTransform))
                throw new InvalidOperationException(
                    "Fallout 2 arrival visual proof pre-capture contract failed.");

            var path = Path.Combine(output, "fo2-arroyo-arrival-r9.png");
            var image = host.GetViewport().GetTexture().GetImage();
            if (image.IsEmpty() || image.GetWidth() != ExpectedWidth ||
                image.GetHeight() != ExpectedHeight || image.SavePng(path) != Error.Ok)
                throw new InvalidOperationException(
                    "Fallout 2 arrival visual proof could not write its one native frame.");
            var bytes = File.ReadAllBytes(path);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var reportPath = Path.Combine(output, "fo2-arroyo-arrival-r9.json");
            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schema = "opennv-fo2-arroyo-arrival-visual-proof/v1",
                        status = "captured-for-human-review-presentation-unaccepted",
                        frame = new
                        {
                            path,
                            bytes = bytes.Length,
                            width = image.GetWidth(),
                            height = image.GetHeight(),
                            sha256,
                        },
                        source = new
                        {
                            mapIndex = scene.MapIndex,
                            elevation = scene.Elevation,
                            tile = player.CurrentTile,
                            scene.MapSha256,
                            scene.WalkMaskSha256,
                            sourceMapLights = scene.Molded3D.SourceMapLights,
                            torchMotivatedMapLights =
                                scene.Molded3D.SourceTorchMotivatedMapLights,
                            wallNormalTextureSha256 =
                                scene.Molded3D.SourceWallNormalTextureSha256,
                            floorNormalTextureSha256 =
                                scene.Molded3D.SourceFloorNormalTextureSha256,
                        },
                        projection = new
                        {
                            mode = runtime.Profile.CameraCompositionMode,
                            player.CameraSizeMeters,
                            player.CameraSourcePixelScale,
                            player.CameraVisibleSourceFrameHeightPixels,
                            player.CameraSourceFrameCropPixels,
                            player.CameraWorldViewportHeightPixels,
                            exactHandoffTransform = true,
                        },
                        openingSkip = new
                        {
                            handoff.SkipRequested,
                            handoff.SkipTerminalStateApplied,
                            terminalSourceFrame = handoff.FinalPresentedSourceFrame,
                            terminalSourceFadeFraction =
                                handoff.FinalSourceFadeFraction,
                            movieEndBlack = handoff.TerminalBlackPresented,
                            controlReleased = handoff.ControlReleased,
                        },
                        hud = new
                        {
                            visible = runtime.Hud.VisibleInViewport,
                            ownedFallout2ClassicInterface =
                                runtime.Hud.OwnedFallout2ClassicInterface,
                            sourcePixelLayout = runtime.Hud.SourcePixelLayout,
                            runtime.Hud.OwnedSourceAssetCount,
                            retailBehaviorParity = runtime.Hud.RetailBehaviorParity,
                        },
                        world3d = new
                        {
                            world.VisibleSpriteCards,
                            world.InFrustumSpriteCards,
                            closure.UnaccountedSourceObjects,
                            closure.FirstBeatRuntimeClosurePassed,
                        },
                        promotion = new
                        {
                            presentationAccepted = false,
                            pairReady = false,
                            fo1QualityParity = false,
                            humanReviewRequired = true,
                        },
                    },
                    new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            GD.Print(
                $"OPENNV_FO2_ARROYO_R9_CAPTURE path={path} sha256={sha256} " +
                $"hud={runtime.Hud.VisibleInViewport} " +
                $"camera={player.CameraSizeMeters} mapLights=" +
                $"{scene.Molded3D.SourceMapLights}/{ExpectedSourceMapLights}");
            host.GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_ARROYO_R9_CAPTURE_FAIL {exception}");
            host.GetTree().Quit(1);
        }
    }
}
