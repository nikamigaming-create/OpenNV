using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class QuestScriptClockOwnedProbe
{
    internal static void Run(string mod, string selected, string game, string questEditorId,
        short initialStage, short expectedStage, string[] dependencies)
    {
        var installation = new FalloutModStackSelection([new(mod, selected, dependencies)]).Resolve(game);
        using var source = installation.OpenSource();
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var quest = FalloutDialogueTopic.Find(records, "QUST", questEditorId);
        var script = records.GetEffective(FalloutDialogueTopic.RequiredForm(quest, "SCRI"));
        var bindings = new FalloutScriptBindings(records, quest, script, script.ReadSubrecords());
        var timer = bindings.Variable("timer");
        var graph = FalloutOpeningPlayerControlResolver.Resolve(records, [questEditorId]);
        var entry = graph.Stage(questEditorId, initialStage);
        var delay = FalloutInstallationSettings.Read(source).Number("MAIN", "fQuestScriptDelayTime");
        var interval = FalloutQuestScriptInitialization.ProcessingDelay(quest);
        Require(interval > 0 && interval < 1f / 30,
            "The selected owned timer is not a fast-interval source quest.");
        var sourceHash = Convert.ToHexString(SHA256.HashData(script.ReadData()));
        foreach (var frameSeconds in new[] { 1f / 30, 1f / 60, 1f / 90 })
            RunTimer(frameSeconds, false);
        RunTimer(1f / 30, true);

        void RunTimer(float frameSeconds, bool hitch)
        {
            var state = new FalloutQuestState(records);
            state.EnterStage(quest.FormKey, initialStage);
            state.SetRunning(quest.FormKey, true);
            var writes = FalloutStageQuestVariableProgram.Read(records, entry).Prepare(state, null);
            foreach (var write in writes) state.SetVariable(write.Owner, write.Index, write.Value);
            var duration = state.Variable(timer.Owner, timer.Index);
            Require(double.IsFinite(duration) && duration > frameSeconds * 10,
                "The selected source stage has no measurable positive timer.");
            FalloutQuestScripts Scripts(FalloutQuestState owner) => new(records, owner,
                new HashSet<FalloutFormKey> { quest.FormKey }, new FalloutPlayerInventory(), defaultProcessingDelay: delay);
            var scripts = Scripts(state);
            var reached = 0;
            var host = Host(state, () => ++reached);
            // Prime the initial due invocation in this isolated source stage.
            // Subsequent caller frames execute the winning GameMode.
            scripts.AdvanceClaimed(quest.FormKey, 0, host);
            var coldState = new FalloutQuestState(records);
            var coldScripts = Scripts(coldState);
            var coldReached = 0;
            FalloutQuestScriptHost? coldHost = null;
            var saved = false;
            var elapsed = 0d;
            var frames = 0;
            while (reached == 0 && frames < 2048)
            {
                var seconds = hitch && frames == 2 ? 0.75f : frameSeconds;
                elapsed += seconds;
                var before = Selected(scripts);
                scripts.AdvanceClaimed(quest.FormKey, seconds, host);
                var after = Selected(scripts);
                Require(after.Executions - before.Executions <= 1 && after.Clock!.Invocations - before.Clock!.Invocations <= 1,
                    "A caller frame invented quest catch-up invocations.");
                if (coldHost is not null)
                {
                    coldScripts.AdvanceClaimed(quest.FormKey, seconds, coldHost);
                    Require(JsonSerializer.Serialize(state.Capture()) == JsonSerializer.Serialize(coldState.Capture()) &&
                        Selected(coldScripts).Clock!.HasSameBits(after.Clock!) && coldReached == reached,
                        "The owned timer changed values, due frame, result or clock bits after cold restore.");
                }
                if (!saved && after.Clock!.Remaining <= 0 && frames >= 8)
                {
                    var snapshot = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(scripts.Capture()))!;
                    coldState.Restore(JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(state.Capture()))!);
                    coldScripts.Restore(snapshot);
                    coldHost = Host(coldState, () => ++coldReached);
                    Require(Selected(coldScripts).Clock!.HasSameBits(after.Clock!),
                        "Owned cold restoration changed the overdue phase before advancement.");
                    var timerBeforePause = state.Variable(timer.Owner, timer.Index);
                    scripts.ExecuteClaimedMenu(quest.FormKey, 1084, host);
                    coldScripts.ExecuteClaimedMenu(quest.FormKey, 1084, coldHost);
                    Require(timerBeforePause == state.Variable(timer.Owner, timer.Index) && reached == 0 && coldReached == 0,
                        "The owned modal source executed the GameMode timer or a result.");
                    Require(Selected(scripts).Clock!.HasSameBits(after.Clock!) &&
                        Selected(coldScripts).Clock!.HasSameBits(after.Clock!),
                        "A claimed source menu invocation changed the gameplay clock.");
                    saved = true;
                }
                ++frames;
            }
            Require(reached == 1 && coldReached == 1 && saved &&
                elapsed >= duration && elapsed <= duration + 2 * frameSeconds + 0.00001 &&
                state.Stage(quest.FormKey) == expectedStage,
                "The owned source timer lost gameplay time or changed its following-invocation result boundary.");
            for (var frame = 0; frame < 12; frame++)
            {
                scripts.AdvanceClaimed(quest.FormKey, frameSeconds, host);
                coldScripts.AdvanceClaimed(quest.FormKey, frameSeconds, coldHost!);
            }
            Require(reached == 1 && coldReached == 1 &&
                Selected(scripts).Clock!.HasSameBits(Selected(coldScripts).Clock!),
                "Later owned recurrence duplicated the source result or diverged cold.");
            Console.WriteLine($"OPENNV_OWNED_QUEST_CLOCK_PASS quest={quest.FormKey} script={script.FormKey} " +
                $"sourceSha256={sourceHash} interval={interval:R} timer={duration:R} frameSeconds={frameSeconds:R} " +
                $"hitch={hitch} elapsed={elapsed:R} frames={frames} sourceResultOnce=true coldOverdue=true modal=true " +
                "boundary=isolated-winning-gamemode-and-stage-variable-fixture cameraDeparture=unverified retailTiming=unverified recording=false");
        }

        FalloutQuestScriptHost Host(FalloutQuestState owner, Action reached) => new((target, stage) =>
        {
            Require(target == quest.FormKey && stage == expectedStage,
                "The owned timer selected an unexpected result target/stage.");
            return () =>
            {
                Require(owner.Variable(timer.Owner, timer.Index) <= 0,
                    "The source result preceded the completed timer write.");
                owner.EnterStage(target, stage);
                reached();
            };
        }, name => throw new NotSupportedException($"Owned timer fixture reached an unrelated actor value {name}."));

        FalloutQuestScriptSnapshot Selected(FalloutQuestScripts scripts) =>
            scripts.Capture().Instances.Single(instance => instance.Quest == quest.FormKey);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
