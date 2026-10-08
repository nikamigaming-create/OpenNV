using System.Diagnostics;
using OpenNV.Runtime.Content;

internal static partial class CorpusInventory
{
    private sealed record RecordResult(long Winners, long Effective, long Deleted, long Payloads, long Layouts, object Details);

    private static RecordResult ReadRecords(FalloutPluginStack records, string directory, Failures failures, Stopwatch watch)
    {
        var layouts = new Dictionary<(string Record, string Field), Layout>();
        var events = new Dictionary<string, HashSet<FalloutFormKey>>(StringComparer.OrdinalIgnoreCase);
        var commands = new Dictionary<string, HashSet<FalloutFormKey>>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        // The runtime owns winning precedence. Unioning registered identities supplies
        // its complete denominator without changing the active-only runtime index.
        var keys = records.Plugins.SelectMany(plugin => plugin.Plugin.Records).Where(record => record.Signature != "TES4")
            .Select(record => record.FormKey).Distinct(FalloutFormKeyComparer.Instance).OrderBy(records.RuntimeFormId).ToArray();
        if (keys.Length != records.WinnerRecordCount) throw new InvalidDataException("Registered identity union differs from the runtime winner denominator.");
        long visited = 0, effective = 0, deleted = 0, payloads = 0, layoutReads = 0, scripts = 0, bodies = 0, parsedBodies = 0;
        using var rows = new StreamWriter(Path.Combine(directory, "winning-records.jsonl"), append: false);
        foreach (var key in keys)
        {
            if (!records.TryGetWinner(key, out var record)) throw new InvalidDataException("Registered identity has no runtime winner: " + key);
            var disposition = record.IsDeleted ? "deleted" : "effective";
            if (record.IsDeleted) ++deleted; else ++effective;
            counts[record.Signature] = counts.GetValueOrDefault(record.Signature) + 1;
            var owner = new { identity = key.ToString(), record.Signature, winningPlugin = record.Plugin.Name, disposition };
            string? payloadSha256 = null;
            int? payloadBytes = null;
            var byteOutcome = "failed";
            var layoutOutcome = "uninspected";
            var localOutcome = "not-a-script";
            var sourceBodyCount = 0;
            var parsedSourceBodies = 0;
            try
            {
                var payload = record.ReadData();
                payloadBytes = payload.Length; payloadSha256 = Hash(payload); byteOutcome = "read"; ++payloads;
                try
                {
                    var fields = record.ReadSubrecords().ToArray();
                    foreach (var field in fields)
                    {
                        var fieldKey = (record.Signature, field.Signature);
                        if (!layouts.TryGetValue(fieldKey, out var layout)) layouts.Add(fieldKey, layout = new());
                        layout.Add(field.Data.Length);
                    }
                    layoutOutcome = "read"; ++layoutReads;
                    if (record.Signature == "SCPT")
                    {
                        ++scripts;
                        try { _ = FalloutScriptLocals.Read(record); localOutcome = "read"; }
                        catch (Exception error) when (SourceFailure(error))
                        { localOutcome = "failed"; failures.Add("script-locals", record.Plugin.Path, error, owner); }
                    }
                    foreach (var body in fields.Where(field => field.Signature == "SCTX"))
                    {
                        ++bodies; ++sourceBodyCount;
                        try
                        {
                            var source = FalloutDialogueTopic.ScriptText(body.Data.Span);
                            var programs = record.Signature == "SCPT" ? FalloutGameModeProgram.ReadEvents(source) :
                                [new FalloutScriptEventProgram("Result", null, FalloutGameModeProgram.Read("begin GameMode\n" + source + "\nend"))];
                            foreach (var program in programs)
                            {
                                if (!events.TryGetValue(program.Event, out var owners)) events.Add(program.Event, owners = []);
                                owners.Add(record.FormKey);
                                foreach (var command in program.Program.CommandNames)
                                {
                                    if (!commands.TryGetValue(command, out var callers)) commands.Add(command, callers = []);
                                    callers.Add(record.FormKey);
                                }
                            }
                            ++parsedBodies; ++parsedSourceBodies;
                        }
                        catch (Exception error) when (SourceFailure(error))
                        { failures.Add("script-parser", record.Plugin.Path, error, new { owner, bodyIndex = sourceBodyCount - 1 }); }
                    }
                }
                catch (Exception error) when (SourceFailure(error))
                { layoutOutcome = "failed"; failures.Add("record-layout", record.Plugin.Path, error, owner); }
            }
            catch (Exception error) when (SourceFailure(error))
            { failures.Add("record-payload-bytes", record.Plugin.Path, error, owner); }
            WriteRow(rows, new
            {
                owner, runtimeFormId = records.RuntimeFormId(key), record.RawFormId, record.Flags, record.FormVersion,
                storedExtent = new { record.HeaderOffset, record.DataOffset, record.StoredSize, record.IsCompressed },
                byteOutcome, payloadBytes, payloadSha256, layoutOutcome, localOutcome, sourceBodyCount, parsedSourceBodies,
                semanticAcceptance = "uninspected", compiledExecution = "uninspected", runtimeAcceptance = "uninspected"
            });
            if (++visited % 100000 == 0) Console.Error.WriteLine($"CORPUS records={visited}/{keys.Length} seconds={watch.Elapsed.TotalSeconds:F1}");
        }
        if (effective != records.EffectiveRecordCount) throw new InvalidDataException("Corpus effective disposition differs from the runtime index.");
        WriteReport(directory, "record-layouts.json", layouts.OrderBy(pair => pair.Key.Record, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Field, StringComparer.Ordinal)
            .Select(pair => new { record = pair.Key.Record, field = pair.Key.Field, layout = pair.Value.Report() }).ToArray());
        return new(visited, effective, deleted, payloads, layoutReads, new
        {
            recordTypes = counts, scripts, sourceBodies = bodies, parsedBodies,
            scriptEvents = events.OrderByDescending(pair => pair.Value.Count).Select(pair => new { name = pair.Key, owners = pair.Value.Count }).ToArray(),
            scriptCommands = commands.OrderByDescending(pair => pair.Value.Count).Select(pair => new { name = pair.Key, owners = pair.Value.Count }).ToArray(),
            commandNameCoverage = layoutReads == visited && parsedBodies == bodies ? "all-parsed-direct-statement-command-names" : "unknown-in-unread-layout-or-parser-failed-bodies",
            sourceCallInventory = new
            {
                scope = "diagnostic-SCTX-only-independent-of-compiled-instructions",
                directStatementNameUnit = "distinct-name-per-winning-record-owner-not-occurrence-counts",
                expressionFunctionNameCoverage = bodies == 0 && layoutReads == visited ? "absent-from-readable-source-bodies" : "unknown-not-inventoried",
                expressionFunctionCallCount = bodies == 0 && layoutReads == visited ? 0L : (long?)null,
                statementArgumentCallCoverage = bodies == 0 && layoutReads == visited ? "absent-from-readable-source-bodies" : "unknown-legacy-statement-arguments-not-inspected",
                statementArgumentCallCount = bodies == 0 && layoutReads == visited ? 0L : (long?)null,
                boundary = "CommandNames inventories direct statement names only. Expression functions and legacy command argument calls are not inventoried or executed; parser/layout completion does not admit them."
            },
            denominator = "All registered non-TES4 identities, resolved through TryGetWinner including deletion; plugin headers remain container bytes.",
            subrecordSemantics = "uninspected", compiledAuthority = "uninspected", branchExecution = "uninspected"
        });
    }
}
