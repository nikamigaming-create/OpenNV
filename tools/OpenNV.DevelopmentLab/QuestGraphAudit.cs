using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

// Read-only coverage through the existing runtime readers and expression owner.
internal static class QuestGraphAudit
{
    internal static int Run(FalloutPluginStack records, RuntimeLiveContentSource content, string output)
    {
        var directory = Path.GetFullPath(output);
        if (Directory.Exists(directory)) throw new IOException("Use a fresh private audit directory.");
        Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions { WriteIndented = true };
        using var rows = new StreamWriter(Path.Combine(directory, "programs.jsonl"));
        using var questRows = new StreamWriter(Path.Combine(directory, "quests.jsonl"));
        using var conditionRows = new StreamWriter(Path.Combine(directory, "conditions.jsonl"));
        using var statementRows = new StreamWriter(Path.Combine(directory, "statements.jsonl"));
        using var world = new FalloutReferenceWorld(records);
        var inspector = new FalloutReferenceScripts(records, world, new(records),
            new((_, _) => throw new InvalidOperationException("Static audit queried furniture."),
                _ => throw new InvalidOperationException("Static audit applied gameplay.")));
        var failures = new List<object>();
        var commandOwners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var transitions = new List<object>();
        var signatures = new HashSet<string>(["QUST", "SCPT", "INFO", "PACK", "TERM"]);
        var counts = new Dictionary<string, int>();
        var conditionCounts = new Dictionary<uint, int>();
        var programs = 0; var parsedPrograms = 0; var sourcePrograms = 0;
        var compiledPrograms = 0; var compiledOnlyPrograms = 0;
        var references = 0; var resolvedReferences = 0;
        var localReferences = 0; var parsedSourceFailures = 0;
        var conditions = 0; var parsedConditions = 0;
        var stageCount = 0; var objectiveCount = 0;
        var statements = 0; var predicates = 0; var expressionStatements = 0;
        var unboundStatements = 0; var uninspectedCommands = 0; var loopPredicates = 0;
        var authoredStages = new Dictionary<FalloutFormKey, HashSet<short>>();
        void Failure(string lane, FalloutPluginRecord record, string scope, Exception error) =>
            failures.Add(new { lane, source = record.FormKey.ToString(), record.Signature, scope, error = error.Message });

        // Discover every embedded script/condition owner from the winning bytes,
        // including patrol results and future record signatures.
        foreach (var record in records.EffectiveRecords())
        {
            try
            {
                if (record.ReadSubrecords().Any(field => field.Signature is "SCHR" or "SCDA" or "SCTX" or "CTDA"))
                    signatures.Add(record.Signature);
            }
            catch (Exception error) { Failure("record-discovery", record, "record", error); }
        }

        foreach (var quest in records.EffectiveRecords("QUST"))
        {
            try
            {
                var stages = quest.ReadSubrecords().Where(field => field.Signature == "INDX").Select(field =>
                {
                    if (field.Data.Length != 2) throw new InvalidDataException("Stage index is not Int16.");
                    return BinaryPrimitives.ReadInt16LittleEndian(field.Data.Span);
                }).ToArray();
                authoredStages.Add(quest.FormKey, stages.ToHashSet());
                if (stages.Any(stage => stage < 0) || stages.Distinct().Count() != stages.Length)
                    Failure("stage-identity", quest, "stages", new InvalidDataException("Negative or repeated authored stage."));
                stageCount += stages.Length;
            }
            catch (Exception error) { Failure("stage-reader", quest, "stages", error); }
        }
        foreach (var signature in signatures.Order())
        {
            var selected = records.EffectiveRecords(signature);
            counts[signature] = selected.Count;
            foreach (var record in selected)
            {
                FalloutPluginSubrecord[] fields;
                try { fields = record.ReadSubrecords().ToArray(); }
                catch (Exception error) { Failure("record-reader", record, "record", error); continue; }
                foreach (var field in fields.Where(field => field.Signature == "CTDA"))
                {
                    ++conditions;
                    try
                    {
                        var condition = FalloutCondition.Read(record, field.Data.Span);
                        conditionCounts[condition.Function] = conditionCounts.GetValueOrDefault(condition.Function) + 1;
                        conditionRows.WriteLine(JsonSerializer.Serialize(new { source = record.FormKey.ToString(),
                            record.Signature, condition.Function, condition.RunOn, condition.Flags,
                            condition.Comparison, condition.Argument1, condition.Argument2, condition.Reference,
                            semanticEvaluation = "requires-bound-runtime-state" }));
                        ++parsedConditions;
                    }
                    catch (Exception error) { Failure("condition-reader", record, "CTDA", error); }
                }
                if (signature == "SCPT")
                {
                    try { _ = FalloutScriptLocals.ReadDeclarations(record); }
                    catch (Exception error) { Failure("local-declarations", record, "SCPT", error); }
                }
                if (signature == "QUST")
                {
                    var objectives = fields.Count(field => field.Signature == "QOBJ");
                    objectiveCount += objectives;
                    string? attachment = null;
                    try { attachment = FalloutScriptLocals.AttachedScript(records, record)?.FormKey.ToString(); }
                    catch (Exception error) { Failure("quest-script-link", record, "SCRI", error); }
                    questRows.WriteLine(JsonSerializer.Serialize(new { quest = record.FormKey.ToString(),
                        winner = record.Plugin.Name, sourceSha256 = Convert.ToHexString(SHA256.HashData(record.ReadData())),
                        stages = authoredStages.GetValueOrDefault(record.FormKey), objectives, attachment,
                        resultEntries = fields.Count(field => field.Signature == "QSDT"),
                        stateExecution = "not-executed-by-static-audit" }));
                }
                var scope = "root"; short? stage = null; var entry = 0;
                for (var index = 0; index < fields.Length; ++index)
                {
                    var field = fields[index];
                    if (field.Signature == "INDX" && field.Data.Length == 2)
                    { stage = BinaryPrimitives.ReadInt16LittleEndian(field.Data.Span); entry = 0; }
                    if (field.Signature == "QSDT") { scope = $"stage:{stage}/entry:{entry++}"; }
                    if (field.Signature is "POBA" or "POEA" or "POCA" or "NEXT") scope = field.Signature;
                    if (field.Signature != "SCHR") continue;
                    ++programs;
                    var end = index + 1;
                    while (end < fields.Length && fields[end].Signature != "SCHR") ++end;
                    var body = fields[index..end];
                    var compiled = body.Where(value => value.Signature == "SCDA").ToArray();
                    var sources = body.Where(value => value.Signature == "SCTX").ToArray();
                    var compiledBytes = compiled.Sum(value => value.Data.Length);
                    var bodyReferences = body.Where(value => value.Signature is "SCRO" or "SCRV").ToArray();
                    var scopeIdentity = record.FormKey + "/" + scope + "/" + index;
                    if (compiledBytes > 0) ++compiledPrograms;
                    if (sources.Length > 0) ++sourcePrograms;
                    if (compiledBytes > 0 && sources.Length == 0) ++compiledOnlyPrograms;
                    var bindings = new Dictionary<string, FalloutFormKey>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        if (field.Data.Length != 20) throw new InvalidDataException("SCHR is not 20 bytes.");
                        var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span[8..]);
                        var declaredRefs = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span[4..]);
                        if (compiled.Length > 1 || sources.Length > 1 || declaredSize != compiledBytes || declaredRefs != bodyReferences.Length)
                            throw new InvalidDataException("Compiled body/reference extent or source multiplicity differs from SCHR.");
                    }
                    catch (Exception error) { Failure("compiled-extents", record, scopeIdentity, error); }
                    foreach (var reference in bodyReferences)
                    {
                        ++references;
                        try
                        {
                            if (reference.Data.Length != 4) throw new InvalidDataException("Script reference is not UInt32.");
                            var raw = BinaryPrimitives.ReadUInt32LittleEndian(reference.Data.Span);
                            if (reference.Signature == "SCRV") { ++localReferences; continue; }
                            if (raw == 0) { ++resolvedReferences; continue; }
                            var key = record.Plugin.AdjustFormId(raw);
                            if (records.RuntimeFormId(key) == 0x14) { bindings["player"] = key; bindings["playerref"] = key; ++resolvedReferences; continue; }
                            var target = records.GetEffective(key);
                            foreach (var editor in target.ReadSubrecords().Where(value => value.Signature == "EDID"))
                            {
                                var name = FalloutDialogueTopic.Text(editor.Data.Span);
                                if (bindings.TryGetValue(name, out var previous) && previous != key)
                                    throw new InvalidDataException("Compiled reference names are ambiguous.");
                                bindings[name] = key;
                            }
                            ++resolvedReferences;
                        }
                        catch (Exception error) { Failure("compiled-reference-link", record, scopeIdentity, error); }
                    }
                    string[] commands = [];
                    var parsed = sources.Length == 0;
                    if (sources.Length == 1)
                    {
                        try
                        {
                            var text = FalloutDialogueTopic.ScriptText(sources[0].Data.Span);
                            var events = signature == "SCPT" ? FalloutGameModeProgram.ReadEvents(text) :
                                [new FalloutScriptEventProgram("Result", null, FalloutGameModeProgram.Read("begin GameMode\n" + text + "\nend"))];
                            var localScript = signature == "SCPT" ? record : FalloutScriptLocals.AttachedScript(records, record);
                            var locals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            if (localScript is not null)
                            {
                                try { locals.UnionWith(FalloutScriptLocals.ReadDeclarations(localScript).Keys); }
                                catch (Exception error)
                                {
                                    // Standalone declaration failures were already recorded. They
                                    // cannot suppress inspection of the successfully parsed source.
                                    if (signature != "SCPT") Failure("inspection-declarations", record, scopeIdentity, error);
                                }
                            }
                            var declarationBindings = new FalloutScriptBindings(records, record, record, body);
                            void InspectValue(string name)
                            {
                                if (name.Equals("this", StringComparison.OrdinalIgnoreCase) || locals.Contains(name) ||
                                    FalloutScriptBindings.IsPlayer(name) && declarationBindings.HasPlayerReference ||
                                    declarationBindings.TryForm(name) is not null || declarationBindings.HasVariable(name)) return;
                                throw new NotSupportedException($"Operand {name} needs a declared value/function or runtime caller context.");
                            }
                            for (var eventIndex = 0; eventIndex < events.Count; ++eventIndex)
                            {
                                var block = events[eventIndex];
                                var coverage = inspector.Inspect(record, record, body, block.Program, InspectValue);
                                foreach (var statement in coverage)
                                {
                                    ++statements;
                                    if (statement.PredicateOutcomes != 0) ++predicates;
                                    if (statement.Kind == "while") ++loopPredicates;
                                    if (statement.Ownership == "expression-declarations-and-signatures") ++expressionStatements;
                                    if (statement.Error is not null) ++unboundStatements;
                                    if (statement.Ownership == "statement-dispatch-not-inspected") ++uninspectedCommands;
                                    statementRows.WriteLine(JsonSerializer.Serialize(new { source = scopeIdentity,
                                        eventIndex, block.Event, block.Filter, statement,
                                        callerContext = "metadata-only-no-reference-admission",
                                        reachability = "all-authored-arms-inspected-state-feasibility-unverified" }));
                                }
                            }
                            commands = events.SelectMany(value => value.Program.CommandNames).Distinct().Order().ToArray();
                            foreach (var command in commands)
                            {
                                if (!commandOwners.TryGetValue(command, out var owners)) commandOwners.Add(command, owners = []);
                                owners.Add(scopeIdentity);
                            }
                            // Include every textual SetStage edge, independent of branch outcome.
                            foreach (var line in text.Split('\n'))
                            {
                                var uncommented = FalloutGameModeProgram.StripComment(line).Trim();
                                if (uncommented.Length == 0) continue;
                                string[] tokens;
                                try { tokens = FalloutGameModeProgram.Tokens(uncommented); }
                                catch { continue; } // The source-parser failure retains this body separately.
                                if (tokens.Length < 3 || !tokens[0].Equals("setstage", StringComparison.OrdinalIgnoreCase)) continue;
                                var bound = bindings.TryGetValue(tokens[1], out var destination);
                                var literal = short.TryParse(tokens[2], out var destinationStage);
                                var exists = bound && literal && authoredStages.TryGetValue(destination, out var targetStages) && targetStages.Contains(destinationStage);
                                transitions.Add(new { source = scopeIdentity, destination = bound ? destination.ToString() : tokens[1],
                                    stage = literal ? (object)destinationStage : tokens[2], bound, literal, authoredTargetStage = exists,
                                    branchReachability = "state-dependent-unverified" });
                            }
                            parsed = true; ++parsedPrograms;
                        }
                        catch (Exception error) { ++parsedSourceFailures; Failure("source-parser", record, scopeIdentity, error); }
                    }
                    else if (sources.Length > 1)
                    { ++parsedSourceFailures; Failure("source-parser", record, scopeIdentity, new InvalidDataException("Repeated source bodies.")); }
                    rows.WriteLine(JsonSerializer.Serialize(new { source = record.FormKey.ToString(), signature, scopeIdentity,
                        winner = record.Plugin.Name, compiledBytes, sourceBodies = sources.Length, references = bodyReferences.Length,
                        parsed, commands, compiledAuthority = compiledBytes > 0 ? "unowned-SCDA-execution" : "no-compiled-instructions",
                        commandSemantics = "not-certified-by-parsing", bytesSha256 = Convert.ToHexString(SHA256.HashData(
                            compiled.SelectMany(value => value.Data.ToArray()).ToArray())) }));
                }
            }
        }
        var report = new { schema = "opennv-private-quest-graph-audit/v1", content.SaveCompatibilityId,
            plugins = records.Plugins.Select(value => new { name = value.Plugin.Name, value.Sha256 }), records = counts,
            stageCount, objectiveCount, programs, sourcePrograms, parsedPrograms, compiledPrograms, compiledOnlyPrograms,
            compiledExecutionOwners = 0, references, resolvedFormReferences = resolvedReferences, localReferences,
            localReferenceSemantics = "not-certified-by-extent-validation", conditions, parsedConditions,
            conditionFunctions = conditionCounts.OrderByDescending(value => value.Value).Select(value => new { function = value.Key, uses = value.Value }),
            commands = commandOwners.OrderByDescending(value => value.Value.Count).Select(value => new { name = value.Key, owners = value.Value.Count }),
            transitions = transitions.Count, failures = failures.Count,
            statements, predicates, predicateOutcomes = predicates * 2L, loopPredicates, expressionStatements,
            unboundStatements, uninspectedCommands,
            stateDomain = new { booleanValuations = "2^" + predicates,
                scope = "independent authored predicate truth assignments; correlations and feasibility unverified",
                execution = "not-enumerated-or-executed", numericAndLoopState = "unbounded-not-exhaustively-certified" },
            invariants = new { programRows = programs, allSourceProgramsAccountedFor = parsedPrograms + parsedSourceFailures == sourcePrograms,
                unresolvedFormReferences = references - resolvedReferences - localReferences, undecodedConditions = conditions - parsedConditions,
                worldInstancesAfterInspection = world.InstanceCount },
            boundary = "Every winning record is inspected to discover standalone/embedded scripts and condition owners. Full payload/BSA accounting is in the canonical corpus audit. This static audit never executes scripts or certifies local-reference semantics, state-dependent branches, native plugin ABI, event timing, geometry, audio, pixels or gameplay." };
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(report, options));
        File.WriteAllText(Path.Combine(directory, "failures.json"), JsonSerializer.Serialize(failures, options));
        File.WriteAllText(Path.Combine(directory, "stage-transitions.json"), JsonSerializer.Serialize(transitions, options));
        Console.WriteLine(JsonSerializer.Serialize(report));
        return failures.Count == 0 ? 0 : 1;
    }
}
