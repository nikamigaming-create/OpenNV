using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private async Task RenderEvents(FalloutPluginStack records, FalloutReferenceWorld world)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidDataException("Render callbacks require an actual rendered fixture.");
        var events = new FalloutScriptEvents();
        FalloutReferenceScripts Executor(FalloutReferenceWorld references) => new(records, references, new(records),
            new((_, _) => false, _ => throw new InvalidDataException("Unexpected render effect."), Events: events));
        var executor = Executor(world);
        var instance = world.Get(Key(0x901));
        executor.ExecuteProgram(records.GetEffective(Key(0x901)), records.GetEffective(Key(0x500)),
            FalloutGameModeProgram.Read("begin GameMode\nSetJohnnyOnRenderUpdateEventHandler 1 NativeRender\nend"), 0);
        var root = new Node3D { ProcessMode = ProcessModeEnum.Always };
        var preDraw = false;
        long draws = 0;
        void Before() { preDraw = true; ++draws; }
        void After() => preDraw = false;
        RenderingServer.FramePreDraw += Before;
        RenderingServer.FramePostDraw += After;
        FalloutUserFunctionInvoker invoke = (script, caller, arguments, seconds) =>
        {
            Require(preDraw && caller is null && arguments.Count == 0, "Native render callback ran outside pre-draw or injected a caller/arguments.");
            return executor.InvokeFunction(script, caller, arguments, seconds);
        };
        var adapter = new RuntimeNativeScriptEvents(events, () => invoke);
        using var cold = new FalloutReferenceWorld(records);
        try
        {
            AddChild(root);
            root.AddChild(new Camera3D { Current = true });
            // Additional viewports must not multiply an ordinary global event.
            for (var i = 0; i < 2; ++i)
            {
                var viewport = new SubViewport { Size = new(32, 32), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                root.AddChild(viewport);
                viewport.AddChild(new Camera3D { Current = true });
            }
            root.AddChild(adapter);
            async Task Draws(int count = 3)
            {
                for (var frame = 0; frame < count; ++frame)
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            await Draws();
            Require(instance.Read(13) == 0, "An inactive native session executed render callbacks.");
            adapter.Active = true;
            var beforeDraws = draws;
            await Draws();
            Require(instance.Read(13) == draws - beforeDraws && instance.Read(14) == 0 && instance.Read(15) > 0,
                "Native render phase did not execute source expressions exactly once per drawn frame.");
            var before = instance.Read(13);
            beforeDraws = draws;
            GetTree().Paused = true;
            await Draws();
            Require(instance.Read(13) == before + draws - beforeDraws, "Paused source menus suppressed their render callback.");
            GetTree().Paused = false;
            adapter.Active = false;
            before = instance.Read(13);
            await Draws();
            Require(instance.Read(13) == before, "An inactive world retained a render invocation.");
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
            cold.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
            executor = Executor(cold);
            adapter.Active = true;
            beforeDraws = draws;
            await Draws();
            Require(instance.Read(13) == before && cold.Get(Key(0x901)).Read(13) == before + draws - beforeDraws,
                "Native render dispatch retained the retired world after cold restoration.");
            adapter.Free();
            var coldBefore = cold.Get(Key(0x901)).Read(13);
            await Draws();
            Require(cold.Get(Key(0x901)).Read(13) == coldBefore, "Freed native adapter retained its draw subscription.");
            GD.Print("OPENNV_NATIVE_RENDER_EVENTS_PASS phase=pre-draw sourceExpressions=true oncePerFrame=true extraViewports=2 paused=true inactive=true coldOwner=rebound retired=false recording=false fixture=true parity=unverified");
        }
        finally
        {
            GetTree().Paused = false;
            RenderingServer.FramePreDraw -= Before;
            RenderingServer.FramePostDraw -= After;
            root.Free();
        }
    }
}
