using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class QuestGraphAuditContracts
{
    private static void SourceCallDenominator(string directory)
    {
        InspectionCallBoundary();
        var game = Path.Combine(directory, "source-call-inputs");
        var data = Path.Combine(game, "Data"); Directory.CreateDirectory(data);
        var script = "scn AuthoredCallDenominator\nfloat counter\nbegin GameMode\n" +
            "if 0 && GetSecondsPassed > 0\nset counter to ((GetSecondsPassed + GetSecondsPassed))\n" +
            "elseif GetSecondsPassed > 0\nlet counter := GetSecondsPassed + (GetSecondsPassed)\n" +
            "else\neval GetSecondsPassed\nendif\n" +
            "while GetSecondsPassed\neval (GetSecondsPassed + GetSecondsPassed)\nbreak\nloop\n" +
            "set counter to ( GetSecondsPassed + )\nif UnboundSourceName > 0\neval GetSecondsPassed\nendif\n" +
            "OpaqueStatement (GetSecondsPassed)\nend";
        var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, 1);
        var original = Path.Combine(data, "FalloutNV.esm");
        File.WriteAllBytes(original, Join(Header(), Record("SCPT", 0x900, 0,
            Field("EDID", Text("AuthoredCallDenominator")), Field("SCHR", ScriptHeader()),
            Field("SLSD", local), Field("SCVR", Text("counter")), Field("SCTX", Text(script)))));
        ModContentContracts.WriteArchive(Path.Combine(data, "FalloutNV.bsa"), "authored resource sibling");
        var ini = Path.Combine(game, "archives.ini"); File.WriteAllText(ini, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
        var before = SHA256.HashData(File.ReadAllBytes(original));
        using (var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: ["FalloutNV.esm"], archiveIniPath: ini))
        using (var records = FalloutPluginStack.Load(source.PluginSources))
        {
            var questReport = Path.Combine(directory, "source-call-quest-report");
            Require(QuestGraphAudit.Run(records, source, questReport) == 1,
                "Malformed/unbound expression and uninspected statement calls became quest acceptance.");
            using var quest = JsonDocument.Parse(File.ReadAllText(Path.Combine(questReport, "summary.json")));
            var state = quest.RootElement;
            Require(state.GetProperty("commandNameCoverage").GetString() == "all-parsed-direct-statement-command-names" &&
                state.GetProperty("commands").EnumerateArray().Single().GetProperty("name").GetString() == "opaquestatement",
                "Direct statement name coverage included expression functions or continued claiming all source command names.");
            RequireUnknownSourceCalls(state.GetProperty("sourceCallInventory"));
            Require(state.GetProperty("unboundStatements").GetInt32() >= 2 &&
                state.GetProperty("uninspectedCommands").GetInt32() == 1 &&
                state.GetProperty("compiledExecutionOwners").ValueKind == JsonValueKind.Null &&
                state.GetProperty("invariants").GetProperty("worldInstancesAfterInspection").GetInt32() == 0 &&
                !state.GetProperty("readiness").GetProperty("completeRuntimeReadiness").GetBoolean(),
                "A refused expression, skipped statement argument or metadata-only signature acquired execution/runtime ownership.");
            var body = ReadRows(questReport, "source-bodies.jsonl").Single();
            Require(body.GetProperty("parsed").GetBoolean() &&
                body.GetProperty("expressionFunctionNameCoverage").GetString() == "unknown-not-inventoried" &&
                body.GetProperty("statementArgumentCallCoverage").GetString() == "unknown-legacy-statement-arguments-not-inspected",
                "A parsed source body manufactured a complete nested function/argument call denominator.");
            var statements = ReadRows(questReport, "statements.jsonl");
            Require(new[] { "if", "elseif", "while", "set", "let", "eval", "opaquestatement" }.All(operation =>
                statements.Any(row => row.GetProperty("statement").GetProperty("Operation").GetString() == operation)) &&
                statements.All(row => row.GetProperty("effects").GetString() == "not-executed-not-certified"),
                "Inactive, nested, loop, assignment or opaque statement source arms disappeared or were executed.");

            var corpusReport = Path.Combine(directory, "source-call-corpus-report");
            Require(CorpusInventory.Run(records, source, corpusReport) == 0,
                "Call-inventory uncertainty changed genuine byte/layout/event parser completion into another lane.");
            using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(corpusReport, "summary.json")));
            var inventory = corpus.RootElement.GetProperty("recordInventory");
            Require(inventory.GetProperty("commandNameCoverage").GetString() == "all-parsed-direct-statement-command-names" &&
                inventory.GetProperty("scriptCommands").EnumerateArray().Single().GetProperty("name").GetString() == "opaquestatement" &&
                corpus.RootElement.GetProperty("sourceReadOutcome").GetString() == "accounted" &&
                !corpus.RootElement.GetProperty("runtimeReady").GetBoolean(),
                "Corpus direct-name inventory or byte completion implied expression/compiled/runtime acceptance.");
            RequireUnknownSourceCalls(inventory.GetProperty("sourceCallInventory"));
        }
        Require(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(original))), "Call denominator review changed original authored source bytes.");
        SourceCallAbsence(directory, unread: false);
        SourceCallAbsence(directory, unread: true);
        Console.WriteLine("OPENNV_SOURCE_CALL_DENOMINATOR_CONTRACT_PASS statements=direct-names-only expressionCalls=unknown statementArgumentCalls=unknown everyArm=retained malformed=retained unbound=retained noInvocation=true noStateRead=true noStateWrite=true source=unchanged compiledExecution=uninspected runtimeReady=false");
    }

    private static void SourceCallAbsence(string directory, bool unread)
    {
        var game = Path.Combine(directory, unread ? "unread-call-inputs" : "absent-call-inputs");
        var data = Path.Combine(game, "Data"); Directory.CreateDirectory(data);
        var original = Path.Combine(data, "FalloutNV.esm");
        var record = unread ? Record("FUTR", 0x910, FalloutPluginRecord.CompressedFlag, new byte[4]) :
            Record("INFO", 0x910, 0, Field("SCHR", ScriptHeader(2)), Field("SCDA", [0xee, 0xff]));
        File.WriteAllBytes(original, Join(Header(), record));
        ModContentContracts.WriteArchive(Path.Combine(data, "FalloutNV.bsa"), "authored source sibling");
        var ini = Path.Combine(game, "archives.ini"); File.WriteAllText(ini, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
        var before = SHA256.HashData(File.ReadAllBytes(original));
        using (var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: ["FalloutNV.esm"], archiveIniPath: ini))
        using (var records = FalloutPluginStack.Load(source.PluginSources))
        {
            var questReport = Path.Combine(directory, unread ? "unread-call-quest-report" : "absent-call-quest-report");
            Require(QuestGraphAudit.Run(records, source, questReport) == 1,
                "Unread original bytes or unknown compiled-only execution became quest acceptance.");
            using var quest = JsonDocument.Parse(File.ReadAllText(Path.Combine(questReport, "summary.json")));
            var questCalls = quest.RootElement.GetProperty("sourceCallInventory");
            var corpusReport = Path.Combine(directory, unread ? "unread-call-corpus-report" : "absent-call-corpus-report");
            Require(CorpusInventory.Run(records, source, corpusReport) == (unread ? 1 : 0),
                "Unread bytes passed or source-call absence changed valid compiled-only byte accounting.");
            using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(corpusReport, "summary.json")));
            var corpusCalls = corpus.RootElement.GetProperty("recordInventory").GetProperty("sourceCallInventory");
            if (unread)
            {
                RequireUnknownSourceCalls(questCalls);
                RequireUnknownSourceCalls(corpusCalls);
            }
            else
            {
                foreach (var calls in new[] { questCalls, corpusCalls })
                    Require(calls.GetProperty("expressionFunctionNameCoverage").GetString() == "absent-from-readable-source-bodies" &&
                        calls.GetProperty("expressionFunctionCallCount").GetInt64() == 0 &&
                        calls.GetProperty("statementArgumentCallCount").GetInt64() == 0,
                        "Actual diagnostic source absence was confused with unknown unread or compiled instruction calls.");
            }
            Require(quest.RootElement.GetProperty("compiledExecutionOwners").ValueKind == JsonValueKind.Null &&
                !quest.RootElement.GetProperty("readiness").GetProperty("completeRuntimeReadiness").GetBoolean() &&
                !corpus.RootElement.GetProperty("runtimeReady").GetBoolean(),
                "A diagnostic source-call absence/unknown disposition admitted compiled execution or runtime readiness.");
        }
        Require(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(original))), "Absent/unread call inventory changed original source bytes.");
    }

    private static void RequireUnknownSourceCalls(JsonElement inventory)
    {
        Require(inventory.GetProperty("expressionFunctionNameCoverage").GetString() == "unknown-not-inventoried" &&
            inventory.GetProperty("expressionFunctionCallCount").ValueKind == JsonValueKind.Null &&
            inventory.GetProperty("statementArgumentCallCoverage").GetString() == "unknown-legacy-statement-arguments-not-inspected" &&
            inventory.GetProperty("statementArgumentCallCount").ValueKind == JsonValueKind.Null,
            "An uninspected function/argument call denominator was replaced with zero or a guessed count.");
    }

    private static void InspectionCallBoundary()
    {
        var calls = 0; var reads = 0; var writes = 0;
        var resolutions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FalloutScriptFunction? Function(string name)
        {
            FalloutScriptArgumentKind[]? signature = name is "Tick" or "OnlyInSkippedSuffix" or "OnlyInLegacyArguments" ?
                Array.Empty<FalloutScriptArgumentKind>() : name == "Nested" ? [FalloutScriptArgumentKind.Number] : null;
            if (signature is null) return null;
            resolutions.Add(name);
            return new(signature, _ => { ++calls; throw new InvalidOperationException("Inspection invoked an expression function."); });
        }
        var values = new FalloutScriptValueContext(_ => { ++reads; throw new InvalidOperationException("Inspection read source state."); },
            (_, _) => { ++writes; throw new InvalidOperationException("Inspection wrote source state."); });
        var context = new FalloutScriptInspectionContext(values, Function,
            (_, _) => throw new NotSupportedException("An unknown user function has no fixture signature."));
        var program = FalloutGameModeProgram.Read("begin GameMode\n" +
            "if 0 && Nested (Nested (Tick))\nset counter to Nested (Tick)\n" +
            "elseif Tick\nlet counter := Nested (Tick)\nelse\neval Nested (Tick)\nendif\n" +
            "while Tick\neval Nested (Tick)\nbreak\nloop\n" +
            "set counter to (Tick + )\nif MissingPrefix && OnlyInSkippedSuffix\nreturn\nendif\n" +
            "OpaqueStatement (OnlyInLegacyArguments)\nend");
        var rows = program.Inspect(context, name =>
        {
            if (name != "counter") throw new NotSupportedException("Unbound fixture source name: " + name);
        });
        Require(resolutions.SetEquals(["Tick", "Nested"]) && rows.Count(row => row.Error is not null) == 2 &&
            rows.Single(row => row.Operation == "opaquestatement").Ownership == "statement-dispatch-not-inspected" &&
            program.CommandNames.SequenceEqual(["opaquestatement"]) && calls == 0 && reads == 0 && writes == 0,
            "Signature inspection invoked source state, treated all call names as direct commands, or pretended to traverse refused/skipped syntax.");
    }
}
