using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedCompiledQuestRecurrenceProbe
{
    internal static void Run(string installation, string questToken, string output, string[] options)
    {
        if (RuntimeLiveContentSource.Current is not null)
            throw new InvalidOperationException("Owned recurrence audit cannot replace an active content owner.");
        var split = questToken.LastIndexOf(':');
        if (split < 1 || !uint.TryParse(questToken.AsSpan(split + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) || id > 0xffffff)
            throw new ArgumentException("Owned recurrence quest is OriginalPlugin.esm:hex-object-id.");
        var questKey = new FalloutFormKey(questToken[..split], id);
        var selection = DevelopmentLabSource.ParseCommand(["compiled-quest-recurrence", installation, "records", .. options]);
        using var content = DevelopmentLabSource.Open(installation, selection.Selection);
        var path = OwnedSpeechCompletionProbe.OutputPath(output, installation, content.ContentRoots[0], content.ContentRoots.Skip(1).ToArray());
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quest = records.GetEffective(questKey);
        var script = FalloutScriptLocals.AttachedScript(records, quest) ?? throw new InvalidDataException("Selected original quest has no attached SCPT.");
        var program = FalloutQuestScriptAuthority.ReadCompiled(script) ?? throw new InvalidDataException("Selected original SCPT has no compiled authority.");
        var sourceHash = Hash(script); var questHash = Hash(quest);
        object? before = null, after = null; string? failure = null; var resumed = false; var unchanged = false;
        try
        {
            FalloutQuestScriptAuthority.RequireRecurringProgram(program);
            if (!program.Events.Any(block => block.Event == 0 && program.EventInstructions(block).Count() >= 2))
                throw new NotSupportedException("Selected original quest needs a genuine GameMode instruction suffix for the owned cold proof.");
            var claims = records.EffectiveRecords("QUST").Select(value => value.FormKey).ToHashSet();
            var delay = FalloutInstallationSettings.Read(content).Number("MAIN", "fQuestScriptDelayTime");
            var quests = new FalloutQuestState(records); quests.SetRunning(questKey, true);
            using var world = new FalloutReferenceWorld(records);
            var scheduler = new FalloutQuestScripts(records, quests, claims, new(), defaultProcessingDelay: delay, references: world);
            var globals = FalloutGlobalState.Read(records);
            FalloutQuestStages? stages = null;
            var executor = Executor(world, quests, globals, (key, stage) => stages!.Enter(key, stage));
            stages = new(records, quests, executor.StageSteps, condition =>
                FalloutPlatformConditions.Evaluate(condition) ?? quests.Evaluate(condition));
            var instructions = 0;
            var host = Host(executor, _ => instructions++ < 2);
            // The original full initialization denominator, phase and interval
            // determine the due time. No subset-selected ordinal is substituted.
            var initial = scheduler.Capture().Instances.Single(value => value.Quest == questKey);
            scheduler.AdvanceClaimed(questKey, Math.Max(0, initial.Remaining), host);
            var saved = Copy(scheduler.Capture());
            var actual = saved.Instances.Single(value => value.Quest == questKey);
            if (actual.Compiled?.Pending?.LastSlice is not { Disposition: "suspended", Invocation: > 0 } first ||
                first.Cursor.CommittedInstructions == 0 || first.Cursor.Completed || world.ScriptManualSaves.EnteredInvocations != 0)
                throw new InvalidDataException("Original quest did not reach a real suspended instruction boundary/lease.");
            before = new { clock = actual.Clock, actual.Compiled, world = world.Capture(), quests = quests.Capture(), stages = stages.State };
            // An independently pending stage or ForceSave request has its own
            // capture boundary; it cannot be silently absorbed by the cursor.
            var stageState = Copy(stages.CaptureResults()); var referenceState = Copy(world.Capture());
            var questState = Copy(quests.Capture()); var globalState = Copy(globals.Capture());
            using var coldWorld = new FalloutReferenceWorld(records); coldWorld.Restore(referenceState);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(questState);
            var coldGlobals = FalloutGlobalState.Read(records); coldGlobals.Restore(globalState);
            var cold = new FalloutQuestScripts(records, coldQuests, claims, new(), defaultProcessingDelay: delay, references: coldWorld);
            cold.Restore(saved);
            if (JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(cold.Capture()) ||
                JsonSerializer.Serialize(questState) != JsonSerializer.Serialize(coldQuests.Capture()))
                throw new InvalidDataException("Owned cold admission changed clocks, variables or committed execution prefix.");
            FalloutQuestStages? coldStages = null;
            var coldExecutor = Executor(coldWorld, coldQuests, coldGlobals, (key, stage) => coldStages!.Enter(key, stage));
            coldStages = new(records, coldQuests, coldExecutor.StageSteps, condition =>
                FalloutPlatformConditions.Evaluate(condition) ?? coldQuests.Evaluate(condition));
            coldStages.RestoreResults(stageState);
            cold.AdvanceClaimed(questKey, 0, Host(coldExecutor));
            var finished = cold.Capture().Instances.Single(value => value.Quest == questKey);
            if (finished.Compiled?.Pending is not null || finished.Executions != actual.Executions + 1 ||
                finished.Clock!.Invocations != actual.Clock!.Invocations + 1 ||
                !finished.Compiled!.LastCompletedEvents.Any(receipt => receipt.PrefixBefore >= first.Cursor.CommittedInstructions &&
                    receipt.EventOrdinal == first.EventOrdinal && receipt.Disposition == "completed"))
                throw new InvalidDataException("Original suffix did not complete exactly one due shared-clock invocation without prefix replay.");
            after = new { clock = finished.Clock, finished.Compiled, world = coldWorld.Capture(), quests = coldQuests.Capture(), stages = coldStages.CaptureResults() };
            resumed = true;

            FalloutReferenceScripts Executor(FalloutReferenceWorld selectedWorld, FalloutQuestState selectedQuests,
                FalloutGlobalState selectedGlobals, Action<FalloutFormKey, short> stageOwner) => new(records, selectedWorld, selectedQuests,
                    new((_, _) => throw new NotSupportedException("Owned recurrence furniture query requires the actual native actor."), effect =>
                    {
                        if (effect.Kind == FalloutReferenceEffectKind.SetStage) stageOwner(effect.Target!.Value, effect.Stage);
                        else if (effect.Kind != FalloutReferenceEffectKind.ReferenceEnable)
                            throw new NotSupportedException("Owned recurrence effect requires the actual native product owner: " + effect.Kind);
                    }, Globals: selectedGlobals,
                    Command: (_, _, command, _) => throw new NotSupportedException("Owned recurrence command requires the actual product host: " + command)));
            static FalloutQuestScriptHost Host(FalloutReferenceScripts selected, Func<bool, bool>? gate = null) => new(
                (_, _) => throw new InvalidDataException("Owned SCDA called diagnostic SetStage."),
                _ => throw new NotSupportedException("Owned scalar actor query requires player state."),
                (_, _, _, _) => throw new InvalidDataException("Owned SCDA called a void/source host."),
                ExecuteCompiledProgram: selected.ExecuteProgram, CanContinueCompiled: gate);
        }
        catch (Exception error) { failure = error.Message; }
        unchanged = sourceHash == Hash(script) && questHash == Hash(quest);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, new
            {
                schema = "opennv-owned-compiled-quest-recurrence/v1", capturedUtc = DateTime.UtcNow,
                runtimeMvid = typeof(FalloutQuestScripts).Module.ModuleVersionId, content.SaveCompatibilityId,
                quest = questKey.ToString(), script = script.FormKey.ToString(), questSha256 = questHash, scriptSha256 = sourceHash,
                program.Scope.ScopeSha256, program.ProgramSha256, decoder = FalloutCompiledScriptProgram.DecoderVersion,
                authoredSctxPresent = program.Scope.Any(field => field.Signature == "SCTX"), before, after, resumed, failure, sourceUnchanged = unchanged,
                nativeProcessesInputSavesOrCapture = false,
                boundary = "Original byte-program/shared-state cold suffix only; independent original menu filters, native effects, campaigns, speaker output and retail timing remain unverified."
            }, new JsonSerializerOptions { WriteIndented = true });
        if (failure is not null || !resumed || !unchanged)
            throw new InvalidDataException("OPENNV_OWNED_COMPILED_QUEST_RECURRENCE_FAIL " + failure);
        Console.WriteLine("OPENNV_OWNED_COMPILED_QUEST_RECURRENCE_PASS originalScope=true actualLeases=true coldSuffix=true sourceUnchanged=true ordinaryCampaign=false recording=false");
    }
    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
