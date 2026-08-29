using System.Security.Cryptography;
using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal static class PipBoyVisualAcceptance
{
    internal static async Task Run(
        RuntimeCoordinator host,
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options,
        RuntimeConfiguration configuration)
    {
        var input = configuration.Player.DesktopInput;
        try
        {
            if (loaded.Player.UsesXr)
                throw new InvalidOperationException("Flat Pip-Boy visual proof loaded an XR player.");
            await host.WaitForLoadingScreenDismissal();
            await FlatControlsAcceptance.WaitPhysicsFrames(host, input.Acceptance.SettleFrames);
            await FlatControlsAcceptance.PulseKeyBinding(
                host,
                input.PipBoy,
                input.Acceptance.SettleFrames);
            if (!loaded.Session.HasPipBoy || !loaded.Session.IsPipBoyOpen)
                throw new InvalidOperationException(
                    $"Configured Tab input did not open the Pip-Boy: " +
                    $"available={loaded.Session.HasPipBoy} open={loaded.Session.IsPipBoyOpen}.");

            for (var frame = 0; frame < input.Acceptance.RenderedFramesBeforeScreenshot; frame++)
                await host.ToSignal(
                    RenderingServer.Singleton,
                    RenderingServer.SignalName.FramePostDraw);
            var screenshot = Path.GetFullPath(
                RuntimeCoordinator.RequireOption(options, "pipboy-screenshot"));
            Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
            var heldImage = host.GetViewport().GetTexture().GetImage();
            if (heldImage.IsEmpty() || heldImage.SavePng(screenshot) != Error.Ok)
                throw new InvalidOperationException(
                    $"Could not save the open Pip-Boy screenshot: {screenshot}");
            using var stream = File.OpenRead(screenshot);
            var heldSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var snapshot = loaded.Session.BuildUiSnapshot();

            await FlatControlsAcceptance.PulseKeyBinding(
                host,
                input.Cancel,
                input.Acceptance.SettleFrames);
            if (loaded.Session.IsPipBoyOpen)
                throw new InvalidOperationException("Configured Escape input did not close the Pip-Boy.");
            for (var frame = 0; frame < input.Acceptance.RenderedFramesBeforeScreenshot; frame++)
                await host.ToSignal(
                    RenderingServer.Singleton,
                    RenderingServer.SignalName.FramePostDraw);
            var loweredScreenshot = Path.Combine(
                Path.GetDirectoryName(screenshot)!,
                Path.GetFileNameWithoutExtension(screenshot) + "-lowered.png");
            var loweredImage = host.GetViewport().GetTexture().GetImage();
            if (loweredImage.IsEmpty() || loweredImage.SavePng(loweredScreenshot) != Error.Ok)
                throw new InvalidOperationException(
                    $"Could not save the lowered Pip-Boy screenshot: {loweredScreenshot}");
            using var loweredStream = File.OpenRead(loweredScreenshot);
            var loweredSha256 = Convert.ToHexString(SHA256.HashData(loweredStream))
                .ToLowerInvariant();

            RuntimeCoordinator.WriteReport(
                RuntimeCoordinator.RequireOption(options, "report"),
                new
                {
                    schema = "opennv-pipboy-visual-acceptance/v1",
                    status = "pass",
                    scene = scenePath,
                    inputTransport = "godot-input-map-plus-parse-input-event",
                    openBinding = input.PipBoy.PhysicalKey,
                    closeBinding = input.Cancel.PhysicalKey,
                    opened = true,
                    closed = true,
                    inventoryEntries = snapshot.Inventory.Count,
                    quests = snapshot.Quests.Count,
                    objectives = snapshot.Objectives.Count,
                    screenshot = new
                    {
                        path = screenshot,
                        width = heldImage.GetWidth(),
                        height = heldImage.GetHeight(),
                        sha256 = heldSha256,
                    },
                    loweredScreenshot = new
                    {
                        path = loweredScreenshot,
                        width = loweredImage.GetWidth(),
                        height = loweredImage.GetHeight(),
                        sha256 = loweredSha256,
                    },
                    windowsAppControlUsed = false,
                    foregroundInputInjected = false,
                });
            GD.Print(
                $"OPENNV_PIPBOY_VISUAL_PASS inventory={snapshot.Inventory.Count} " +
                $"screenshot={screenshot}");
        }
        finally
        {
            Input.ParseInputEvent(DesktopInputMap.CreateEvent(input.PipBoy, false));
            Input.ParseInputEvent(DesktopInputMap.CreateEvent(input.Cancel, false));
            loaded.Session.ClosePipBoy();
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }
}
