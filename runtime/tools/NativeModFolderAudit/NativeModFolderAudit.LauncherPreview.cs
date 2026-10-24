using Godot;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeModFolderAudit
{
    private async Task RenderLauncherPreview(string[] args)
    {
        if (args.Length is < 2 or > 4 || args.Length == 4 && args[3] != "--device-input")
            throw new ArgumentException("Expected --launcher-preview, private output folder, optional owned New Vegas installation and optional --device-input.");
        if (DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("A launcher visual check requires the native renderer.");
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var profiles = GodotLauncherProfileStore.Open(Path.Combine(output, "profiles.private.json"), Path.Combine(output, "saves"));
        if (args.Length >= 3) profiles.Save("newvegas", args[2]);
        var launcher = new NativeGodotLauncher();
        launcher.Configure(profiles);
        AddChild(launcher);
        try
        {
            // The device samples a live UI viewport through a second 3D viewport.
            // Capture the settled composition, rather than its first empty target.
            for (var frame = 0; frame < 4; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            using var image = GetViewport().GetTexture().GetImage();
            if (image.IsEmpty() || image.SavePng(Path.Combine(output, "launcher.png")) != Error.Ok)
                throw new IOException("The native launcher visual check produced no image.");
            GD.Print("OPENNV_NATIVE_LAUNCHER_PREVIEW_READY controls=actual-native-launcher gameplay=unverified");
            if (args.Length == 4) await CheckLauncherDeviceInput(launcher, output);
        }
        finally
        {
            launcher.QueueFree();
        }
    }
}
