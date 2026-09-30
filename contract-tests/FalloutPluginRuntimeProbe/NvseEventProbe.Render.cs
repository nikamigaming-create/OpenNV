using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class NvseEventProbe
{
    private static void RenderCallbacks(FalloutPluginStack records, FalloutReferenceScripts executor,
        FalloutQuestState quests, FalloutScriptEvents events, FalloutPluginRecord quest, FalloutPluginRecord definition)
    {
        void Run(string body) => executor.ExecuteProgram(quest, definition,
            FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
        var original = quests.Variable(quest.FormKey, 4);
        Run("SetJohnnyOnRenderUpdateEventHandler 1 RenderTick\nSetOnRenderUpdateEventHandler 1 RenderTick 0");
        events.Render(0.125, executor.InvokeFunction);
        Require(quests.Variable(quest.FormKey, 4) == original + 1 && quests.Variable(quest.FormKey, 5) == 0.125,
            "Render registration duplicated execution, invented a caller or lost its frame time/integer expressions.");
        Run("SetOnRenderUpdateEventHandler 0 RenderTick");
        events.Render(0.1, executor.InvokeFunction);
        Require(quests.Variable(quest.FormKey, 4) == original + 1, "Render removal left a live callback.");
        Run("SetOnRenderUpdateEventHandler 0 RenderUnsupported");
        foreach (var body in new[] { "SetOnRenderUpdateEventHandler 1 RenderUnsupported", "SetOnRenderUpdateEventHandler 1 RenderParameter",
            "SetOnRenderUpdateEventHandler 1 ProbeQuest", "SetOnRenderUpdateEventHandler 1 RenderTick 1",
            "SetOnRenderUpdateEventHandler 1 RenderTick 0 4" }) Reject(() => Run(body));
        events.Render(0.1, executor.InvokeFunction);
        Require(quests.Variable(quest.FormKey, 4) == original + 1, "Rejected registration mutated the callback set.");

        var failedBefore = quests.Variable(quest.FormKey, 8);
        Run("SetOnRenderUpdateEventHandler 1 Fault");
        events.Render(0.1, executor.InvokeFunction);
        Run("SetOnRenderUpdateEventHandler 1 Fault");
        events.Render(0.1, executor.InvokeFunction);
        Require(quests.Variable(quest.FormKey, 8) == failedBefore + 1 &&
            JsonSerializer.Serialize(events.State).Contains("MissingCommand", StringComparison.Ordinal),
            "Render failure lost its prefix/error or duplicate registration retried the fault.");
        Run("SetOnRenderUpdateEventHandler 0 Fault\nSetOnRenderUpdateEventHandler 1 Fault");
        events.Render(0.1, executor.InvokeFunction);
        Require(quests.Variable(quest.FormKey, 8) == failedBefore + 2, "Explicit render removal/re-registration could not resume a handler.");
        Run("SetOnRenderUpdateEventHandler 0 Fault\nSetOnRenderUpdateEventHandler 1 RenderTick");
        var saved = JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(quests.Capture()))!;
        var cold = new FalloutQuestState(records); cold.Restore(saved);
        using var coldWorld = new FalloutReferenceWorld(records);
        var coldExecutor = Executor(records, coldWorld, cold, events);
        events.LoadGame(); events.EnterMainMenu();
        events.Render(0.25, coldExecutor.InvokeFunction);
        Require(quests.Variable(quest.FormKey, 4) == original + 1 && cold.Variable(quest.FormKey, 4) == original + 2,
            "Render lifetime reset on load/menu or retained a retired executor.");
        Run("SetOnRenderUpdateEventHandler 0 RenderTick");
        RenderMutation();
        Console.WriteLine("OPENNV_NVSE_RENDER_EVENTS_PASS nullCaller=true idempotent=true removal=true failures=visible mutation=true coldOwner=rebound fixture=true parity=unverified");
    }

    private static void RenderMutation()
    {
        var events = new FalloutScriptEvents();
        FalloutFormKey Form(uint id) => new("Events.esm", id);
        var order = new List<uint>();
        events.SetRender(Form(0x260), true); events.SetRender(Form(0x261), true);
        double Invoke(FalloutFormKey script, FalloutFormKey? caller, IReadOnlyList<double> arguments, double seconds)
        {
            Require(caller is null && arguments.Count == 0, "Render dispatch injected a caller or arguments.");
            order.Add(script.ObjectId);
            events.SetRender(Form(0x260), false); events.SetRender(Form(0x261), false);
            events.SetRender(Form(0x262), true);
            return 0;
        }
        events.Render(0.1, Invoke); events.Render(0.1, Invoke);
        Require(order.SequenceEqual([0x260u, 0x262u]), "Render mutation invoked removed/new handlers in the same frame.");
        events.SetRender(Form(0x263), true);
        var ready = true;
        events.Render(0.1, (script, caller, arguments, seconds) => { order.Add(script.ObjectId); ready = false; return 0; }, () => ready);
        Require(order.SequenceEqual([0x260u, 0x262u, 0x262u]), "Render dispatch entered a retired owner after an earlier callback.");
    }
}
