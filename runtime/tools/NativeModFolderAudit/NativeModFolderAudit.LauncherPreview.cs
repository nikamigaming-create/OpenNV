using Godot;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeModFolderAudit
{
    private async Task RenderLauncherPreview(string[] args)
    {
        if (args.Length is < 2 or > 3)
            throw new ArgumentException("Expected --launcher-preview, private output folder and optional owned New Vegas installation.");
        if (DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("A launcher visual check requires the native renderer.");
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var profiles = GodotLauncherProfileStore.Open(Path.Combine(output, "profiles.private.json"), Path.Combine(output, "saves"));
        if (args.Length == 3) profiles.Save("newvegas", args[2]);
        var launcher = new NativeGodotLauncher();
        launcher.Configure(profiles);
        AddChild(launcher);
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.IsEmpty() || image.SavePng(Path.Combine(output, "launcher.png")) != Error.Ok)
                throw new IOException("The native launcher visual check produced no image.");
            GD.Print("OPENNV_NATIVE_LAUNCHER_PREVIEW_READY controls=actual-native-launcher gameplay=unverified");
        }
        finally
        {
            launcher.QueueFree();
        }
    }
}
