using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

public partial class NativePlayerIdleModelAudit : Node
{
    public override void _Ready()
    {
        RuntimeNativePlayerActor? body = null, fresh = null;
        RuntimeLiveContentSource? content = null;
        var code = 1;
        try
        {
            if (DisplayServer.GetName() != "headless" || OS.GetCmdlineUserArgs() is not [var dataRoot])
                throw new InvalidOperationException("Player model audit requires headless Godot and one owned FO3 Data root.");
            RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
            content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var runtime = FalloutAdvancementRuntimeSource.Open(records);
            var main = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
            _ = FalloutExecutableStringTable.ReadPlayerReferenceHud(content.FalloutExecutablePath, main);
            // This is the real source-body factory. The audit exercises its
            // native lifetime and pose consumers, without claiming a campaign,
            // an input device, physical gameplay or complete cold Continue.
            var appearance = FalloutNpcAppearanceResolver.Resolve(records, records.RuntimeFormKey(7), equippedArmor: []);
            body = new(records, content, appearance, null, true, .0142875f, Colors.White);
            AddChild(body);
            try { _ = body.ReadIdleAnimationContext(null!); throw new InvalidDataException("Native presentation fabricated an unloaded AnimData context."); }
            catch (ArgumentNullException) { }
            var radius = body.ReadSourcePoseBoundRadius();
            if (radius is not { } bits || !float.IsFinite(BitConverter.UInt32BitsToSingle(bits)) || BitConverter.UInt32BitsToSingle(bits) <= 0)
                throw new InvalidDataException("Actual first-person model lost its posed source spheres.");
            body.Advance(.1, new Vector3(1, 0, 0), true, false);
            _ = body.ReadSourcePoseBoundRadius();
            fresh = new(records, content, appearance, null, true, .0142875f, Colors.White);
            AddChild(fresh);
            if (fresh.SourceSceneFactory == body.SourceSceneFactory)
                throw new InvalidDataException("Fresh Player assembly replayed a previous model factory.");
            RemoveChild(fresh);
            ExpectBoundary(() => { _ = fresh.ReadSourcePoseBoundRadius(); }, "Player bound lost its actual assembled skeleton and current pose.");
            GD.Print($"OPENNV_NATIVE_PLAYER_IDLE_MODEL_PASS sourceFactory=true currentPoseBound=true radiusBits={bits:x8} " +
                "missingAnimDataRefused=true animationPredicates=REQUIRE_ACTUAL_LOADED_CONTEXT retiredModelRefused=true freshFactories=true " +
                "headless=true gameLaunched=false framesRecorded=false gameplay=UNEXECUTED coldContinue=UNEXECUTED");
            code = 0;
        }
        catch (Exception error) { GD.PushError("OPENNV_NATIVE_PLAYER_IDLE_MODEL_FAIL " + error); }
        finally
        {
            try
            {
                if (GodotObject.IsInstanceValid(body)) body!.Free();
                if (GodotObject.IsInstanceValid(fresh)) fresh!.Free();
                content?.Dispose();
            }
            catch (Exception cleanup) { code = 1; GD.PushError("OPENNV_NATIVE_PLAYER_IDLE_MODEL_CLEANUP " + cleanup); }
            GetTree().Quit(code);
        }
    }
    private static void ExpectBoundary(Action read, string expected)
    {
        try { read(); }
        catch (NotSupportedException error) when (error.Message == expected) { return; }
        throw new InvalidDataException("Player model did not retain its exact missing or retired animation owner.");
    }
}
