using System.Security.Cryptography;
using Godot;


using OpenNV.Runtime.Diagnostics.Acceptance;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Ui;

internal static class PipBoyVisualAcceptance
{
    private const int MovieProofHeldFrames = 150;
    private const int MovieProofLoweredFrames = 150;

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
            if (!string.IsNullOrWhiteSpace(Engine.GetWriteMoviePath()))
                for (var frame = 0; frame < MovieProofHeldFrames; frame++)
                    await host.ToSignal(
                        RenderingServer.Singleton,
                        RenderingServer.SignalName.FramePostDraw);
            var screenshot = Path.GetFullPath(
                RuntimeCoordinator.RequireOption(options, "pipboy-screenshot"));
            Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
            var heldImage = host.GetViewport().GetTexture().GetImage();
            heldImage.Convert(Image.Format.Rgba8);
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
            if (!string.IsNullOrWhiteSpace(Engine.GetWriteMoviePath()))
                for (var frame = 0; frame < MovieProofLoweredFrames; frame++)
                    await host.ToSignal(
                        RenderingServer.Singleton,
                        RenderingServer.SignalName.FramePostDraw);
            var loweredScreenshot = Path.Combine(
                Path.GetDirectoryName(screenshot)!,
                Path.GetFileNameWithoutExtension(screenshot) + "-lowered.png");
            var loweredImage = host.GetViewport().GetTexture().GetImage();
            loweredImage.Convert(Image.Format.Rgba8);
            if (loweredImage.IsEmpty() || loweredImage.SavePng(loweredScreenshot) != Error.Ok)
                throw new InvalidOperationException(
                    $"Could not save the lowered Pip-Boy screenshot: {loweredScreenshot}");
            using var loweredStream = File.OpenRead(loweredScreenshot);
            var loweredSha256 = Convert.ToHexString(SHA256.HashData(loweredStream))
                .ToLowerInvariant();
            var heldPixels = heldImage.GetData();
            var loweredPixels = loweredImage.GetData();
            if (heldImage.GetSize() != loweredImage.GetSize() ||
                heldPixels.Length != loweredPixels.Length)
                throw new InvalidOperationException(
                    "Held and lowered Pip-Boy frames have different dimensions.");
            var changedPixels = 0;
            for (var offset = 0; offset < heldPixels.Length; offset += 4)
            {
                if (heldPixels[offset] != loweredPixels[offset] ||
                    heldPixels[offset + 1] != loweredPixels[offset + 1] ||
                    heldPixels[offset + 2] != loweredPixels[offset + 2])
                    changedPixels++;
            }
            if (changedPixels == 0)
                throw new InvalidOperationException(
                    "Held and lowered Pip-Boy captures contain no changed pixels.");
            var changedPixelFraction = (double)changedPixels /
                (heldImage.GetWidth() * heldImage.GetHeight());
            var vitalsColdRestorePassed = loaded.Session.PersistAndVerifyVitalsColdRestore();
            var missingAuthoritativeVitals = new[]
            {
                ("level", snapshot.Level.HasValue),
                ("hitPoints", snapshot.HitPoints.HasValue && snapshot.MaximumHitPoints.HasValue),
                ("actionPoints", snapshot.ActionPoints.HasValue &&
                    snapshot.MaximumActionPoints.HasValue),
                ("experience", snapshot.ExperiencePoints.HasValue &&
                    snapshot.NextLevelExperiencePoints.HasValue),
            }
                .Where(value => !value.Item2)
                .Select(value => value.Item1)
                .ToArray();
            if (missingAuthoritativeVitals.Length == 0 && !vitalsColdRestorePassed)
                throw new InvalidOperationException(
                    "Authoritative gameplay vitals did not survive a cold save restore.");

            RuntimeCoordinator.WriteReport(
                RuntimeCoordinator.RequireOption(options, "report"),
                new
                {
                    schema = "opennv-pipboy-visual-acceptance/v1",
                    status = "input-pass-visual-inspection-required",
                    inputStatus = "pass",
                    visualStatus = "inspection-required",
                    scene = scenePath,
                    inputTransport = "godot-input-map-plus-parse-input-event",
                    openBinding = input.PipBoy.PhysicalKey,
                    closeBinding = input.Cancel.PhysicalKey,
                    opened = true,
                    closed = true,
                    inventoryEntries = snapshot.Inventory.Count,
                    quests = snapshot.Quests.Count,
                    objectives = snapshot.Objectives.Count,
                    authoritativeVitals = new
                    {
                        complete = missingAuthoritativeVitals.Length == 0,
                        missing = missingAuthoritativeVitals,
                        level = snapshot.Level,
                        hitPoints = snapshot.HitPoints,
                        maximumHitPoints = snapshot.MaximumHitPoints,
                        actionPoints = snapshot.ActionPoints,
                        maximumActionPoints = snapshot.MaximumActionPoints,
                        experiencePoints = snapshot.ExperiencePoints,
                        nextLevelExperiencePoints = snapshot.NextLevelExperiencePoints,
                        persisted = vitalsColdRestorePassed,
                        coldRestorePassed = vitalsColdRestorePassed,
                    },
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
                    heldLoweredDifference = new
                    {
                        changedPixels,
                        changedPixelFraction,
                    },
                    windowsAppControlUsed = false,
                    foregroundInputInjected = false,
                });
            GD.Print(
                $"OPENNV_PIPBOY_INPUT_PASS_VISUAL_REVIEW_REQUIRED " +
                $"inventory={snapshot.Inventory.Count} changedPixels={changedPixels} " +
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
