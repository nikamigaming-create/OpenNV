using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

public partial class NativePlayerReferenceHudAudit : Node
{
    public override void _Ready()
    {
        NativeOwnedGameplayHud? hud = null;
        FalloutStandalonePlayerSceneState? scene = null;
        RuntimeLiveContentSource? content = null;
        var code = 1;
        try
        {
            if (DisplayServer.GetName() != "headless" || OS.GetCmdlineUserArgs() is not [var dataRoot])
                throw new InvalidOperationException("Owned HUD projection check requires headless Godot and one selected Data root.");
            RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
            content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var runtime = FalloutAdvancementRuntimeSource.Open(records);
            var main = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
            var ui = FalloutUiComponentStore.Open(records, content);
            scene = new(FalloutStandalonePlayerSceneSource.Read(main), content.StackId, Guid.NewGuid());
            hud = new(records, "E", () => null, () => true,
                // This marks the unrelated vitals projection as present. The
                // audit never executes it or treats authored vitals as a source
                // Player. It tests only the actual reference-frame UI binding.
                () => throw new NotSupportedException("Unrelated vitals fixture must remain unexecuted."), scriptUi: ui);
            hud.SetProcess(false); AddChild(hud);
            hud.BindStandaloneReferenceHud(scene);
            var state = hud.ReferenceHud(scene); var invocation = Guid.NewGuid();
            state.Update(invocation, 1, null, 0);
            var retained = hud.ReferenceFrameProjection(scene);
            if (retained.Color != 0x40000000 || retained.Bracket != 1 || retained.Meter != 1 || retained.Name != 1)
                throw new InvalidDataException("Native HUD projection lost its actual C# constructor/retained source traits.");
            ui.WriteComponentFloat(state.Branch, "user0", 1); // Explicit fixture for the independent color writer.
            state.Update(invocation, 2, null, 0);
            var hidden = hud.ReferenceFrameProjection(scene);
            if (hidden.Color != 0x3f800000 || hidden.Bracket != 0 || hidden.Meter != 0 || hidden.Name != 0)
                throw new InvalidDataException("Native HUD tree failed to consume the actual C# hide stores.");
            GD.Print("OPENNV_NATIVE_PLAYER_REFERENCE_HUD_PASS ownedSource=true actualNativeMenu=true sameCSharpTraits=true sourceArtAndFonts=true headless=true gameLaunched=false framesRecorded=false fixture=vitals-input-and-color-writer campaign=UNEXECUTED finalPixels=UNVERIFIED");
            code = 0;
        }
        catch (Exception error) { GD.PushError("OPENNV_NATIVE_PLAYER_REFERENCE_HUD_FAIL " + error); }
        finally
        {
            try { if (GodotObject.IsInstanceValid(hud)) hud!.Free(); }
            catch (Exception cleanup) { code = 1; GD.PushError("OPENNV_NATIVE_PLAYER_REFERENCE_HUD_CLEANUP " + cleanup); }
            try { scene?.Retire(); content?.Dispose(); }
            catch (Exception cleanup) { code = 1; GD.PushError("OPENNV_NATIVE_PLAYER_REFERENCE_HUD_CLEANUP " + cleanup); }
            GetTree().Quit(code);
        }
    }
}
