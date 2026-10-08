using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

// Read-only coverage through the existing runtime readers and expression owner.
internal static partial class QuestGraphAudit
{
    internal static int Run(FalloutPluginStack records, RuntimeLiveContentSource content, string output)
    {
        RequireOutputOutsideInputs(output, content.ContentRoots);
        return Run(records, content.SaveCompatibilityId, output);
    }

    internal static int Run(FalloutPluginStack records, string sourceCompatibilityId, string output)
    {
        RequireOutputOutsideInputs(output, records.Plugins.Select(value => Path.GetDirectoryName(value.Plugin.Path)!));
        var directory = Path.GetFullPath(output);
        if (Directory.Exists(directory)) throw new IOException("Use a fresh private audit directory.");
        Directory.CreateDirectory(directory);
        var options = new JsonSerializerOptions { WriteIndented = true };
        using var rows = new StreamWriter(Path.Combine(directory, "programs.jsonl"));
        using var fieldRows = new StreamWriter(Path.Combine(directory, "program-fields.jsonl"));
        using var sourceRows = new StreamWriter(Path.Combine(directory, "source-bodies.jsonl"));
        using var recordRows = new StreamWriter(Path.Combine(directory, "records.jsonl"));
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
        var sourceBodies = 0; var parsedSourceBodies = 0; var inspectionFailureBodies = 0;
        var compiledProgramRows = 0; var emptyCompiledRows = 0; var orphanPrograms = 0; var deletedProgramRows = 0;
        var unreadRecords = 0;
        var discoveredFields = new Dictionary<string, long>(StringComparer.Ordinal);
        var accountedFields = new Dictionary<string, long>(StringComparer.Ordinal);
        var winners = WinningSourceRecords(records);
        var discoveredFieldIdentities = new HashSet<string>(StringComparer.Ordinal);
        var accountedFieldIdentities = new HashSet<string>(StringComparer.Ordinal);
        var compiledPrograms = 0; var compiledOnlyPrograms = 0;
        var references = 0; var resolvedReferences = 0;
        var localReferences = 0; var parsedSourceFailures = 0;
        var conditions = 0; var parsedConditions = 0;
        var stageCount = 0; var objectiveCount = 0;
        var statements = 0; var predicates = 0; var expressionStatements = 0;
        var unboundStatements = 0; var uninspectedCommands = 0; var loopPredicates = 0;
        var authoredStages = new Dictionary<FalloutFormKey, HashSet<short>>();
        void Failure(string lane, FalloutPluginRecord record, string scope, Exception error) =>
            failures.Add(new { lane, source = record.FormKey.ToString(), record.Signature, deleted = record.IsDeleted, scope, error = error.Message });

        // Discover every embedded script/condition owner from the winning bytes,
        // including patrol results and future record signatures.
        foreach (var record in winners)
        {
            try
            {
                var discovered = record.ReadSubrecords().ToArray();
                for (var ordinal = 0; ordinal < discovered.Length; ++ordinal)
                    if (IsProgramField(discovered[ordinal].Signature))
                    {
                        var field = discovered[ordinal];
                        discoveredFields[field.Signature] = discoveredFields.GetValueOrDefault(field.Signature) + 1;
                        discoveredFieldIdentities.Add(record.FormKey + "/field:" + ordinal);
                    }
                if (discovered.Any(field => IsProgramField(field.Signature) || field.Signature == "CTDA"))
                    signatures.Add(record.Signature);
                recordRows.WriteLine(JsonSerializer.Serialize(new { source = record.FormKey.ToString(), record.Signature,
                    winner = record.Plugin.Name, record.Flags, deleted = record.IsDeleted, record.HeaderOffset,
                    sourceSha256 = Convert.ToHexString(SHA256.HashData(record.ReadData())),
                    programFields = discovered.Count(field => IsProgramField(field.Signature)),
                    disposition = record.IsDeleted ? "deleted-winner-source-retained-runtime-inactive" : "winner-runtime-eligibility-unverified" }));
            }
            catch (Exception error)
            {
                ++unreadRecords; signatures.Add(record.Signature); Failure("record-discovery", record, "record", error);
                recordRows.WriteLine(JsonSerializer.Serialize(new { source = record.FormKey.ToString(), record.Signature,
                    winner = record.Plugin.Name, record.Flags, deleted = record.IsDeleted, record.HeaderOffset,
                    disposition = "unread-winning-record-program-denominator-unknown", error = error.Message }));
            }
        }

        foreach (var quest in winners.Where(record => record.Signature == "QUST"))
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
            var selected = winners.Where(record => record.Signature == signature).ToArray();
            counts[signature] = selected.Length;
            foreach (var record in selected)
            {
                FalloutPluginSubrecord[] fields;
                try { fields = record.ReadSubrecords().ToArray(); }
                catch (Exception error) { Failure("record-reader", record, "record", error); continue; }
                foreach (var conditionField in fields.Select((field, ordinal) => (Field: field, Ordinal: ordinal))
                    .Where(value => value.Field.Signature == "CTDA"))
                {
                    var field = conditionField.Field;
                    ++conditions;
                    try
                    {
                        var condition = FalloutCondition.Read(record, field.Data.Span);
                        conditionCounts[condition.Function] = conditionCounts.GetValueOrDefault(condition.Function) + 1;
                        conditionRows.WriteLine(JsonSerializer.Serialize(new { source = record.FormKey.ToString(),
                            record.Signature, fieldOrdinal = conditionField.Ordinal, deleted = record.IsDeleted, condition.Function, condition.RunOn, condition.Flags,
                            condition.Comparison, condition.Argument1, condition.Argument2, condition.Reference,
                            semanticEvaluation = "requires-bound-runtime-state" }));
                        ++parsedConditions;
                    }
                    catch (Exception error)
                    {
                        Failure("condition-reader", record, "CTDA/" + conditionField.Ordinal, error);
                        conditionRows.WriteLine(JsonSerializer.Serialize(new { source = record.FormKey.ToString(), record.Signature,
                            fieldOrdinal = conditionField.Ordinal, deleted = record.IsDeleted,
                            disposition = "condition-decode-failed", error = error.Message }));
                    }
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
                        winner = record.Plugin.Name, deleted = record.IsDeleted, sourceSha256 = Convert.ToHexString(SHA256.HashData(record.ReadData())),
                        stages = authoredStages.GetValueOrDefault(record.FormKey), objectives, attachment,
                        resultEntries = fields.Count(field => field.Signature == "QSDT"),
                        stateExecution = "not-executed-by-static-audit" }));
                }
                foreach (var program in ProgramScopes(fields))
                {
                    ++programs;
                    var body = fields[program.FieldStart..program.FieldEnd];
                    var compiled = body.Where(value => value.Signature == "SCDA").ToArray();
                    var sources = body.Select((field, offset) => (Field: field, Index: program.FieldStart + offset))
                        .Where(value => value.Field.Signature == "SCTX").ToArray();
                    var compiledBytes = compiled.Sum(value => (long)value.Data.Length);
                    var bodyReferences = body.Where(value => value.Signature is "SCRO" or "SCRV").ToArray();
                    var scopeIdentity = record.FormKey + "/" + program.Scope + "/field:" + program.FieldStart;
                    if (compiled.Length > 0) ++compiledProgramRows;
                    if (compiled.Length > 0 && compiledBytes == 0) ++emptyCompiledRows;
                    if (!program.HasHeader) ++orphanPrograms;
                    if (record.IsDeleted) ++deletedProgramRows;
                    for (var ordinal = program.FieldStart; ordinal < program.FieldEnd; ++ordinal)
                    {
                        var rawField = fields[ordinal]; if (!IsProgramField(rawField.Signature)) continue;
                        accountedFields[rawField.Signature] = accountedFields.GetValueOrDefault(rawField.Signature) + 1;
                        if (!accountedFieldIdentities.Add(record.FormKey + "/field:" + ordinal))
                            Failure("program-field-duplicate-owner", record, scopeIdentity, new InvalidDataException("Source field belongs to more than one row."));
                        fieldRows.WriteLine(JsonSerializer.Serialize(new { source = record.FormKey.ToString(), record.Signature,
                            fieldOrdinal = ordinal, fieldSignature = rawField.Signature, bytes = rawField.Data.Length,
                            sha256 = Convert.ToHexString(SHA256.HashData(rawField.Data.Span)), scopeIdentity,
                            headerFieldOrdinal = program.HasHeader ? (int?)program.FieldStart : null,
                            disposition = program.HasHeader ? "header-associated-source-field" : "orphan-source-field-without-SCHR",
                            deleted = record.IsDeleted, canonicalExecutionScope = "not-admitted-by-static-grouping" }));
                    }
                    if (compiledBytes > 0) ++compiledPrograms;
                    if (sources.Length > 0) ++sourcePrograms;
                    if (compiledBytes > 0 && sources.Length == 0) ++compiledOnlyPrograms;
                    var bindings = new Dictionary<string, FalloutFormKey>(StringComparer.OrdinalIgnoreCase);
                    string? extentError = null;
                    try
                    {
                        if (!program.HasHeader) throw new InvalidDataException("Program fields have no original SCHR owner in their source scope.");
                        var field = fields[program.FieldStart];
                        if (field.Data.Length != 20) throw new InvalidDataException("SCHR is not 20 bytes.");
                        var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span[8..]);
                        var declaredRefs = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span[4..]);
                        if (compiled.Length > 1 || sources.Length > 1 || declaredSize != compiledBytes || declaredRefs != bodyReferences.Length)
                            throw new InvalidDataException("Compiled body/reference extent or source multiplicity differs from SCHR.");
                    }
                    catch (Exception error)
                    {
                        extentError = error.Message;
                        Failure(program.HasHeader ? "compiled-extents" : "orphan-program-owner", record, scopeIdentity, error);
                    }
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
                            if (!records.TryGetWinner(key, out var target))
                                throw new KeyNotFoundException("Source script form reference has no winner: " + key);
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
                    var commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var parsedBodies = 0;
                    foreach (var source in sources)
                    {
                        ++sourceBodies;
                        var sourceIdentity = scopeIdentity + "/SCTX:" + source.Index;
                        var bodyParsed = false; string? sourceError = null;
                        try
                        {
                            var text = FalloutDialogueTopic.ScriptText(source.Field.Data.Span);
                            var events = signature == "SCPT" ? FalloutGameModeProgram.ReadEvents(text) :
                                [new FalloutScriptEventProgram("Result", null, FalloutGameModeProgram.Read("begin GameMode\n" + text + "\nend"))];
                            bodyParsed = true; ++parsedBodies; ++parsedSourceBodies;
                            foreach (var command in events.SelectMany(value => value.Program.CommandNames).Distinct())
                            {
                                commands.Add(command);
                                if (!commandOwners.TryGetValue(command, out var owners)) commandOwners.Add(command, owners = []);
                                owners.Add(sourceIdentity);
                            }
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
                                    statementRows.WriteLine(JsonSerializer.Serialize(new { source = sourceIdentity,
                                        deleted = record.IsDeleted, effects = "not-executed-not-certified",
                                        eventIndex, block.Event, block.Filter, statement,
                                        callerContext = "metadata-only-no-reference-admission",
                                        reachability = "all-authored-arms-inspected-state-feasibility-unverified" }));
                                }
                            }
                            // Include every textual SetStage edge, independent of branch outcome.
                            foreach (var line in text.Split('\n'))
                            {
                                var uncommented = FalloutGameModeProgram.StripComment(line).Trim();
                                if (uncommented.Length == 0) continue;
                                string[] tokens;
                                try { tokens = FalloutGameModeProgram.Tokens(uncommented); }
                                catch (Exception error) { Failure("transition-token-inspection", record, sourceIdentity, error); continue; }
                                if (tokens.Length < 3 || !tokens[0].Equals("setstage", StringComparison.OrdinalIgnoreCase)) continue;
                                var bound = bindings.TryGetValue(tokens[1], out var destination);
                                var literal = short.TryParse(tokens[2], out var destinationStage);
                                var exists = bound && literal && authoredStages.TryGetValue(destination, out var targetStages) && targetStages.Contains(destinationStage);
                                transitions.Add(new { source = sourceIdentity, destination = bound ? destination.ToString() : tokens[1],
                                    stage = literal ? (object)destinationStage : tokens[2], bound, literal, authoredTargetStage = exists,
                                    branchReachability = "state-dependent-unverified" });
                            }
                        }
                        catch (Exception error)
                        {
                            sourceError = error.Message;
                            if (!bodyParsed) { ++parsedSourceFailures; Failure("source-parser", record, sourceIdentity, error); }
                            else { ++inspectionFailureBodies; Failure("source-inspection", record, sourceIdentity, error); }
                        }
                        sourceRows.WriteLine(JsonSerializer.Serialize(new { source = sourceIdentity, fieldOrdinal = source.Index,
                            deleted = record.IsDeleted, bytes = source.Field.Data.Length,
                            sha256 = Convert.ToHexString(SHA256.HashData(source.Field.Data.Span)), parsed = bodyParsed, error = sourceError,
                            ownerDisposition = program.HasHeader ? "header-associated-diagnostic-source" : "orphan-diagnostic-source-no-execution-owner",
                            commandEffects = "not-executed-not-certified",
                            statementDenominator = !bodyParsed || sourceError is not null ? "unknown-parser-or-inspection-failure" : "all-parsed-authored-arms-inspected",
                            commandInventory = bodyParsed ? "parsed-direct-statement-command-names-not-effects" : "unknown-source-parser-failure",
                            expressionFunctionNameCoverage = "unknown-not-inventoried",
                            statementArgumentCallCoverage = "unknown-legacy-statement-arguments-not-inspected" }));
                    }
                    if (sources.Length > 0 && parsedBodies == sources.Length) ++parsedPrograms;
                    rows.WriteLine(JsonSerializer.Serialize(new { source = record.FormKey.ToString(), signature, scopeIdentity,
                        winner = record.Plugin.Name, deleted = record.IsDeleted, program.FieldStart, program.FieldEnd,
                        headerFieldOrdinal = program.HasHeader ? (int?)program.FieldStart : null,
                        ownerDisposition = program.HasHeader ? "declared-header-static-extent-only" : "orphan-program-owner-refused",
                        scopeAdmission = "header-and-known-delimiter-association-only-future-scopes-unverified",
                        extentDisposition = extentError is null ? "declared-extents-validated-no-execution-claim" : "program-owner-or-extents-refused", extentError,
                        compiledBytes, compiledBodies = compiled.Length, sourceBodies = sources.Length, references = bodyReferences.Length,
                        parsed = sources.Length > 0 ? (bool?)(parsedBodies == sources.Length) : null, commands = commands.Order().ToArray(),
                        commandInventoryUnit = "distinct-direct-statement-command-names-no-expression-call-count",
                        compiledDisposition = compiled.Length == 0 ? "absent" : compiledBytes == 0 ? "present-empty" : "present-instructions-uninspected",
                        sourceDisposition = sources.Length == 0 ? "absent" : sources.All(source => source.Field.Data.IsEmpty) ? "present-empty" : "present-diagnostic-source",
                        compiledAuthority = "unknown-not-inspected-or-executed-by-static-audit",
                        commandSemantics = "not-certified-by-parsing", bytesSha256 = Convert.ToHexString(SHA256.HashData(
                            compiled.SelectMany(value => value.Data.ToArray()).ToArray())) }));
                }
            }
        }
        var allFieldsAccounted = discoveredFieldIdentities.SetEquals(accountedFieldIdentities) &&
            discoveredFields.Count == accountedFields.Count &&
            discoveredFields.All(pair => accountedFields.GetValueOrDefault(pair.Key) == pair.Value);
        if (!allFieldsAccounted) failures.Add(new { lane = "program-field-denominator", error = "Raw winning source fields and grouped field rows differ." });
        if (sourceBodies != parsedSourceBodies + parsedSourceFailures)
            failures.Add(new { lane = "source-body-denominator", error = "Every source body needs its own parse disposition." });
        var unverifiedBehavior = compiledProgramRows > 0 || sourcePrograms > 0 || conditions > 0;
        var report = new { schema = "opennv-private-quest-graph-audit/v2", SaveCompatibilityId = sourceCompatibilityId,
            auditBuild = typeof(QuestGraphAudit).Module.ModuleVersionId, runtimeBuild = typeof(FalloutReferenceScripts).Module.ModuleVersionId,
            selection = new { winningRecords = winners.Count, deletedWinners = winners.Count(record => record.IsDeleted),
                runtimeEffectiveRecords = records.EffectiveRecordCount, unreadWinningRecords = unreadRecords,
                originalProgramDenominatorKnown = unreadRecords == 0 },
            plugins = records.Plugins.Select(value => new { name = value.Plugin.Name, value.Sha256 }), records = counts,
            stageCount, objectiveCount, programs, sourcePrograms, parsedPrograms, compiledPrograms, compiledOnlyPrograms,
            sourceBodies, parsedSourceBodies, parsedSourceFailures, inspectionFailureBodies,
            compiledProgramRows, emptyCompiledRows, orphanPrograms, deletedProgramRows,
            compiledExecutionOwners = (int?)null, compiledExecutionEvidence = "unknown-not-probed-no-runtime-owner-count-claim",
            references, resolvedFormReferences = resolvedReferences, localReferences,
            formReferenceAdmission = "source-winner-link-only-no-runtime-admission",
            localReferenceSemantics = "not-certified-by-extent-validation", conditions, parsedConditions,
            conditionFunctions = conditionCounts.OrderByDescending(value => value.Value).Select(value => new { function = value.Key, uses = value.Value }),
            commands = commandOwners.OrderByDescending(value => value.Value.Count).Select(value => new { name = value.Key, owners = value.Value.Count }),
            transitions = transitions.Count, failures = failures.Count,
            statements, predicates, predicateOutcomes = predicates * 2L, loopPredicates, expressionStatements,
            unboundStatements, uninspectedCommands,
            unknownStatementDenominatorBodies = parsedSourceFailures + inspectionFailureBodies,
            commandNameCoverage = unreadRecords == 0 && parsedSourceFailures == 0 ? "all-parsed-direct-statement-command-names" : "unknown-in-unread-or-parser-failed-bodies",
            sourceCallInventory = new
            {
                scope = "diagnostic-SCTX-only-independent-of-compiled-instructions",
                directStatementNameUnit = "distinct-name-per-source-body-owner-not-occurrence-counts",
                expressionFunctionNameCoverage = sourceBodies == 0 && unreadRecords == 0 ? "absent-from-readable-source-bodies" : "unknown-not-inventoried",
                expressionFunctionCallCount = sourceBodies == 0 && unreadRecords == 0 ? 0L : (long?)null,
                statementArgumentCallCoverage = sourceBodies == 0 && unreadRecords == 0 ? "absent-from-readable-source-bodies" : "unknown-legacy-statement-arguments-not-inspected",
                statementArgumentCallCount = sourceBodies == 0 && unreadRecords == 0 ? 0L : (long?)null,
                boundary = "Deferred signature inspection stops at a refused operand and does not inventory expression calls or legacy command arguments. A parsed body, inspected statement or bound signature is not a complete call denominator, invocation or effect."
            },
            compiledInstructionCoverage = "unknown-byte-program-not-decoded-by-this-audit",
            stateDomain = new { booleanValuations = "2^" + predicates,
                scope = "independent authored predicate truth assignments; correlations and feasibility unverified",
                execution = "not-enumerated-or-executed", numericAndLoopState = "unbounded-not-exhaustively-certified" },
            invariants = new { programRows = programs, discoveredProgramFields = discoveredFields, accountedProgramFields = accountedFields,
                allRawProgramFieldsAccountedFor = allFieldsAccounted,
                discoveredProgramFieldIdentities = discoveredFieldIdentities.Count, accountedProgramFieldIdentities = accountedFieldIdentities.Count,
                allSourceBodiesHaveParseDisposition = parsedSourceBodies + parsedSourceFailures == sourceBodies,
                allSourceProgramsAccountedFor = unreadRecords == 0 && allFieldsAccounted && parsedSourceBodies + parsedSourceFailures == sourceBodies,
                unresolvedFormReferences = references - resolvedReferences - localReferences, undecodedConditions = conditions - parsedConditions,
                worldInstancesAfterInspection = world.InstanceCount },
            readiness = new { structuralFailures = failures.Count, sourceExecution = "not-probed", compiledExecution = "not-probed",
                commandEffects = "not-probed", conditionState = "not-probed", completeRuntimeReadiness = false,
                auditedLanePassed = failures.Count == 0 && !unverifiedBehavior },
            boundary = "Every winning and deleted source record is inspected to discover standalone/embedded scripts and condition owners. Full payload/BSA accounting is in the canonical corpus audit. This static audit never executes scripts or certifies local-reference semantics, state-dependent branches, native plugin ABI, event timing, geometry, audio, pixels or gameplay." };
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(report, options));
        File.WriteAllText(Path.Combine(directory, "failures.json"), JsonSerializer.Serialize(failures, options));
        File.WriteAllText(Path.Combine(directory, "stage-transitions.json"), JsonSerializer.Serialize(transitions, options));
        Console.WriteLine(JsonSerializer.Serialize(report));
        return failures.Count == 0 && !unverifiedBehavior ? 0 : 1;
    }
}
