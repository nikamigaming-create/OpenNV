using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeRenderedMenuAudit
{
    private async Task ScriptUi(string root)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Source UI pixels need an actual renderer.");
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        SubViewport? view = null;
        RuntimeNativeUiClock? clock = null;
        var timeScale = Engine.TimeScale;
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            var ui = FalloutUiComponentStore.Open(records);
            view = new SubViewport { Size = new(512, 288), Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(view);
            var hud = new NativeOwnedGameplayHud(records, "E", () => null, () => true, scriptUi: ui);
            view.AddChild(hud);
            clock = new RuntimeNativeUiClock(() => ui); AddChild(clock);
            async Task Draws(int count = 3)
            {
                for (var frame = 0; frame < count; ++frame)
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (hud.Error is not null) throw new InvalidOperationException(hud.Error);
            }
            // Move an existing source reticle through its script trait. The
            // fixture does not invent art, save pixels or alter gameplay state.
            const string path = "HUDMainMenu/ReticleCenter/x";
            ui.SetFloatGradual(path, 200);
            await Draws();
            byte[] first;
            using (var frame = view.GetTexture().GetImage()) first = frame.GetData();
            Engine.TimeScale = 0;
            GetTree().Paused = true;
            ui.SetFloatGradual(path, 200, 500, 0.15);
            for (var iteration = 0; iteration < 60 && ui.GetFloat(path) < 500; ++iteration) await Draws(1);
            await Draws();
            if (ui.GetFloat(path) != 500) throw new InvalidOperationException("Paused source UI interpolation did not finish.");
            using var final = view.GetTexture().GetImage();
            var last = final.GetData();
            if (first.Length == 0 || first.Length != last.Length || first.SequenceEqual(last))
                throw new InvalidOperationException("The native source tile renderer did not draw the animated script trait.");
            GD.Print("OPENNV_NATIVE_SCRIPT_UI_PIXELS_PASS ownedReticle=true floatBridge=true changedPixels=true paused=true timeMult=zero recording=false retainedFrames=0 fixture=true retailParity=unverified");
        }
        finally
        {
            GetTree().Paused = false;
            Engine.TimeScale = timeScale;
            clock?.Free(); view?.Free();
            RuntimeLiveContentSource.Clear();
        }
    }
}
