using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativeRenderedMenuAudit
{
    private async Task HudControls(string baseRoot, string? mod = null, string? root = null, string[]? dependencies = null)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("HUD visibility requires an actual native renderer.");
        if (mod is null) RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        else
        {
            var selection = new FalloutModStackSelection([new(mod, root!, dependencies ?? [])]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                selection.ContentRoots.Skip(1).ToArray(), selection.ActivePlugins, selection.Settings);
        }
        SubViewport? view = null;
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            var controls = FalloutPlayerControlState.AllEnabled;
            var vitals = new GameplayVitals(1, 80, 100, 30, 100, 0, 200);
            view = new SubViewport
            {
                Size = new(512, 288),
                Disable3D = true,
                TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always
            };
            AddChild(view);
            var targetFails = false;
            var hud = new NativeOwnedGameplayHud(records, "E", () => targetFails
                    ? throw new NotSupportedException("Selected source interaction is unbound.")
                    : new("E", "CONTROL FIXTURE", null),
                () => true, () => vitals, controls: () => controls);
            view.AddChild(hud);
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            async Task<byte[]> Pixels()
            {
                for (var frame = 0; frame < 3; ++frame) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (hud.Error is not null) throw new InvalidDataException(hud.Error);
                using var image = view.GetTexture().GetImage();
                if (image.IsEmpty() || image.GetFormat() != Image.Format.Rgba8) throw new InvalidDataException("Native HUD has no RGBA pixels.");
                return image.GetData();
            }
            byte[] Bottom(byte[] pixels) => pixels[(512 * 201 * 4)..];
            static bool Painted(byte[] pixels) => Enumerable.Range(0, pixels.Length / 4).Any(index => pixels[index * 4 + 3] != 0);
            var baseline = await Pixels();
            if (!Painted(Bottom(baseline))) throw new InvalidDataException("Enabled source meters did not draw.");
            targetFails = true;
            var failedTarget = await Pixels();
            var failedState = JsonSerializer.SerializeToElement(hud.State, json);
            if (hud.TargetError is null || failedState.GetProperty("rolloverVisible").GetBoolean() ||
                !Bottom(baseline).AsSpan().SequenceEqual(Bottom(failedTarget)))
                throw new InvalidDataException("Source target failure hid vitals, advertised activation or lost its diagnostic.");
            vitals = vitals with { HitPoints = 60 };
            var damagedTarget = await Pixels();
            if (Bottom(failedTarget).AsSpan().SequenceEqual(Bottom(damagedTarget)))
                throw new InvalidDataException("HP stopped updating while the aimed source interaction failed.");
            vitals = vitals with { HitPoints = 80 };
            targetFails = false;
            var recoveredTarget = await Pixels();
            if (!baseline.SequenceEqual(recoveredTarget) || hud.TargetError is not null)
                throw new InvalidDataException("A later valid target did not restore ordinary HUD interaction.");
            controls = new FalloutPlayerControlCommand(false, [true]).Apply(controls);
            var disabled = await Pixels();
            var state = JsonSerializer.SerializeToElement(hud.State, json);
            if (Painted(disabled) || state.GetProperty("hitPointsVisible").GetBoolean() ||
                state.GetProperty("actionPointsVisible").GetBoolean() || state.GetProperty("reticleVisible").GetBoolean() ||
                state.GetProperty("rolloverVisible").GetBoolean() || !controls.RolloverText)
                throw new InvalidDataException("Movement disable retained vitals/reticle or advertised unavailable activation.");
            controls = new FalloutPlayerControlCommand(true, [true]).Apply(controls);
            var movementEnabled = await Pixels();
            if (!baseline.SequenceEqual(movementEnabled)) throw new InvalidDataException("Movement enable did not restore source HUD pixels.");
            controls = new FalloutPlayerControlCommand(false, [false, false, false, false, false, true]).Apply(controls);
            var rollover = await Pixels();
            if (baseline.AsSpan().SequenceEqual(rollover) || !Bottom(baseline).AsSpan().SequenceEqual(Bottom(rollover)))
                throw new InvalidDataException("Rollover disable removed vitals or retained source target text.");
            controls = new FalloutPlayerControlCommand(true, []).Apply(controls);
            var allEnabled = await Pixels();
            if (!baseline.SequenceEqual(allEnabled)) throw new InvalidDataException("Source enable failed to restore the complete HUD.");
            controls = new FalloutPlayerControlCommand(false, [false, true]).Apply(controls);
            var pipBoyDisabled = await Pixels();
            if (!baseline.SequenceEqual(pipBoyDisabled)) throw new InvalidDataException("Unrelated Pip-Boy disable hid the HUD.");
            controls = new FalloutPlayerControlCommand(false, [true, false, false, false, false, true]).Apply(controls);
            if (Painted(await Pixels())) throw new InvalidDataException("Disabled movement and rollover retained HUD pixels.");
            GD.Print("OPENNV_NATIVE_HUD_CONTROLS_PASS ownedXmlFontAtlas=true failedTargetIsolated=true failedTargetVitalsUpdate=true validTargetRecovers=true movementHidesHpApReticle=true unavailableActivationHidden=true rolloverIndependent=true unrelatedControlsPreserved=true enabledPixelsRestored=true recording=false retainedFrames=0 fixture=true parity=unverified");
        }
        finally { view?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
