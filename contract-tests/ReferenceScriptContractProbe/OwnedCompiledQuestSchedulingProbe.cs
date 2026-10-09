using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class OwnedCompiledQuestSchedulingProbe
{
    internal static void Run(string installation, string output, string[] options)
    {
        if (RuntimeLiveContentSource.Current is not null)
            throw new InvalidOperationException("Owned quest authority audit cannot replace an active content owner.");
        var selection = DevelopmentLabSource.ParseCommand(["compiled-quest-authority", installation, "records", .. options]);
        using var content = DevelopmentLabSource.Open(installation, selection.Selection);
        var path = OwnedSpeechCompletionProbe.OutputPath(output, installation, content.ContentRoots[0], content.ContentRoots.Skip(1).ToArray());
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var rows = new List<object>(); var failures = new List<object>();
        var quests = new FalloutQuestState(records);
        var claims = records.EffectiveRecords("QUST").Select(record => record.FormKey).ToHashSet();
        var settings = FalloutInstallationSettings.Read(content);
        var delay = settings.Number("MAIN", "fQuestScriptDelayTime");
        var scheduler = new FalloutQuestScripts(records, quests, claims, new(), defaultProcessingDelay: delay);
        var initialQuests = JsonSerializer.Serialize(quests.Capture());
        var initialScripts = JsonSerializer.Serialize(scheduler.Capture());
        var callbacks = 0;
        var host = new FalloutQuestScriptHost((_, _) => throw new InvalidDataException("Unowned compiled quest reached a stage host."),
            _ => throw new InvalidDataException("Unowned compiled quest queried an actor value."), (_, _, _, _) => ++callbacks);
        var sources = new List<(FalloutPluginRecord Record, string Hash)>();
        foreach (var quest in records.EffectiveRecords("QUST"))
        {
            try
            {
                if (!quest.ReadSubrecords().Any(field => field.Signature == "SCRI")) continue;
                var script = FalloutScriptLocals.AttachedScript(records, quest);
                if (script is null) continue;
                if (FalloutQuestScriptAuthority.ReadCompiled(script) is not { } program) continue;
                sources.Add((quest, Hash(quest))); sources.Add((script, Hash(script)));
                var admitted = scheduler.CompiledSelections.SingleOrDefault(value => value.Quest == quest.FormKey) ??
                    throw new InvalidDataException("Winning compiled quest did not retain its canonical program selection.");
                if (admitted.Program.ProgramSha256 != program.ProgramSha256 || admitted.Program.Scope.ScopeSha256 != program.Scope.ScopeSha256)
                    throw new InvalidDataException("The scheduler selected another original compiled scope.");
                Refusal(() => FalloutQuestScriptAuthority.RequireSourceExecution(script), FalloutQuestScriptAuthority.SchedulingRefusal);
                try
                {
                    FalloutQuestScriptAuthority.RequireRecurringProgram(program);
                    var refusalQuests = new FalloutQuestState(records); refusalQuests.SetRunning(quest.FormKey, true);
                    var refusal = new FalloutQuestScripts(records, refusalQuests, claims, new(), defaultProcessingDelay: delay);
                    Refusal(() => refusal.ExecuteClaimedMenu(quest.FormKey, 1001, host), "no shared ExecuteProgram/result authority");
                }
                catch (NotSupportedException error) when (error.Message.StartsWith("Compiled quest event", StringComparison.Ordinal) ||
                    error.Message.StartsWith("Filtered compiled", StringComparison.Ordinal))
                {
                    Refusal(() => scheduler.RequireQuestExecution(quest.FormKey), error.Message);
                }
                // Use a separate shared owner for each actual admission below;
                // this broad inventory has no result/effect completion claim.
                rows.Add(new
                {
                    quest = quest.FormKey.ToString(), script = script.FormKey.ToString(),
                    authoredSCTXPresent = script.ReadSubrecords().Any(field => field.Signature == "SCTX"),
                    originalQuestSha256 = Hash(quest), originalScriptSha256 = Hash(script),
                    selected = admitted.Observation
                });
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
            { failures.Add(new { quest = quest.FormKey.ToString(), error = error.Message }); }
        }
        if (rows.Count == 0) failures.Add(new { quest = "source graph", error = "No original compiled quest program was observed." });
        var unchanged = initialQuests == JsonSerializer.Serialize(quests.Capture()) && initialScripts == JsonSerializer.Serialize(scheduler.Capture()) &&
            callbacks == 0 && scheduler.ScriptManualSaves.EnteredInvocations == 0 && sources.All(value => Hash(value.Record) == value.Hash);
        if (!unchanged) failures.Add(new { quest = "audit ownership", error = "Refused compiled scheduling changed state, callbacks, source identity or invocation ownership." });
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, new
            {
                schema = "opennv-owned-compiled-quest-authority/v1", capturedUtc = DateTime.UtcNow,
                runtimeMvid = typeof(FalloutQuestScripts).Module.ModuleVersionId,
                content.SaveCompatibilityId, game = content.Game, stack = content.StackId,
                orderedPlugins = content.PluginSources.Select(source => source.Name).ToArray(),
                originalCompiledPrograms = rows, retainedFailures = failures,
                noInstructionsOrClocksCompleted = unchanged, sourceTextExecuted = false,
                nativeLaunched = false, inputSaveOrCaptureChanged = false,
                boundary = "Original byte-program selection and exact scheduling refusal only; no compiled quest recurrence, legacy cursor conversion, campaign or native-plugin execution acceptance."
            }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"OPENNV_OWNED_COMPILED_QUEST_AUTHORITY_RESULT originalPrograms={rows.Count} failed={failures.Count} " +
            $"sourceUnchanged={unchanged} scheduling=unowned noHostFallback=true native=false");
        if (failures.Count != 0) throw new InvalidDataException("The owned compiled quest authority audit retained an issue inventory.");
    }

    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();

    private static void Refusal(Action action, string expected)
    {
        try { action(); }
        catch (NotSupportedException error) when (error.Message.Contains(expected, StringComparison.Ordinal)) { return; }
        throw new InvalidDataException("Original compiled quest lost its exact authority refusal: " + expected);
    }
}
