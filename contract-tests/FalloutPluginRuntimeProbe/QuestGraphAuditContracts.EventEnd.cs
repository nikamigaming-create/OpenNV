using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class QuestGraphAuditContracts
{
    private static void SourceEventEndLabels(string directory)
    {
        const string statements = "if 0\nUnknownInactive\nelse\nUnknownAlternative\nendif\n" +
            "while 0\nUnknownLoop\nloop\n";
        var positive = new (string Name, string Source)[]
        {
            ("AuthoredLabeledEnd", "scn AuthoredLabeledEnd\nBegin OnAdd\n" + statements + "EnD oNaDd ; closing label\n"),
            ("AuthoredBareEnd", "scn AuthoredBareEnd\nBegin OnAdd\n" + statements + "End\n"),
            ("AuthoredFilteredEnd", "scn AuthoredFilteredEnd\r\nBegin MenuMode 1003\r\nreturn\r\nEnd MenuMode\r\n"),
            ("AuthoredFunctionEnd", "scn AuthoredFunctionEnd\nBegin Function { }\nreturn\nEnd Function\n"),
            ("AuthoredMultipleEnds", "scn AuthoredMultipleEnds\nBegin OnLoad\nreturn\nEnd OnLoad\nBegin OnAdd\nreturn\nEnd\n"),
            ("AuthoredFutureEnd", "scn AuthoredFutureEnd\nBegin FutureDiagnosticEvent\nUnknownDiagnosticOnly\nEnd FutureDiagnosticEvent\n"),
        };
        var positiveGame = WriteEventEndInputs(directory, "matching-event-end-inputs", positive);
        var positivePath = Path.Combine(positiveGame, "Data", "FalloutNV.esm");
        var positiveHash = SHA256.HashData(File.ReadAllBytes(positivePath));
        using (var source = OpenEventEndInputs(positiveGame))
        using (var records = FalloutPluginStack.Load(source.PluginSources))
        {
            var blocks = positive.Select((entry, index) =>
            {
                var record = records.GetEffective(new("FalloutNV.esm", (uint)(0xb00 + index)));
                var field = record.ReadSubrecords().Single(value => value.Signature == "SCTX");
                Require(field.Data.Span.SequenceEqual(Text(entry.Source)), "Actual original source field changed before parsing.");
                return FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(field.Data.Span));
            }).ToArray();
            Require(blocks[0].Single().Event == "OnAdd" && blocks[0].Single().Filter is null &&
                blocks[0].Single().Program.ProgramSha256 == blocks[1].Single().Program.ProgramSha256 &&
                blocks[0].Single().Program.CommandNames.SequenceEqual(["unknowninactive", "unknownalternative", "unknownloop"]),
                "The matching label selected another event, changed accepted statement identity or dropped inactive/loop commands.");
            Require(blocks[2].Single().Event == "MenuMode" && blocks[2].Single().Filter == "1003" &&
                blocks[3].Single().Event == "Function" && blocks[3].Single().Parameters is { Count: 0 } &&
                blocks[4].Select(block => block.Event).SequenceEqual(["OnLoad", "OnAdd"]) &&
                blocks[5].Single().Event == "FutureDiagnosticEvent",
                "An End label changed original filter/parameter/multiple-block metadata or fabricated event admission.");
            Require(!FalloutGameModeProgram.WasRejectedByParser(positive[0].Source, FalloutGameModeProgram.ParserVersion),
                "Matching End metadata invented permission to fill a missing current-version saved owner.");

            var questDirectory = Path.Combine(directory, "matching-event-end-quest");
            Require(QuestGraphAudit.Run(records, source, questDirectory) == 1,
                "Diagnostic label parsing awarded unmeasured source/compiled execution readiness.");
            using var quest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(questDirectory, "summary.json")));
            var state = quest.RootElement;
            Require(state.GetProperty("failures").GetInt32() == 0 &&
                state.GetProperty("programs").GetInt32() == positive.Length &&
                state.GetProperty("sourceBodies").GetInt32() == positive.Length &&
                state.GetProperty("parsedSourceBodies").GetInt32() == positive.Length &&
                state.GetProperty("parsedSourceFailures").GetInt32() == 0 &&
                state.GetProperty("compiledProgramRows").GetInt32() == positive.Length &&
                state.GetProperty("emptyCompiledRows").GetInt32() == positive.Length - 1 &&
                state.GetProperty("uninspectedCommands").GetInt32() > 0,
                "Full-reader diagnostic grammar lost a source/compiled field or retained the unsupported bare-End restriction.");
            Require(state.GetProperty("compiledExecutionOwners").ValueKind == JsonValueKind.Null &&
                state.GetProperty("invariants").GetProperty("allRawProgramFieldsAccountedFor").GetBoolean() &&
                state.GetProperty("invariants").GetProperty("worldInstancesAfterInspection").GetInt32() == 0 &&
                !state.GetProperty("readiness").GetProperty("completeRuntimeReadiness").GetBoolean(),
                "Diagnostic event labels acquired binary execution, native references or runtime acceptance.");
            var fields = ReadRows(questDirectory, "program-fields.jsonl");
            Require(fields.Count == positive.Length * 3 &&
                fields.Where(row => row.GetProperty("fieldSignature").GetString() == "SCTX")
                    .Select(row => row.GetProperty("sha256").GetString()!)
                    .Order(StringComparer.Ordinal).SequenceEqual(positive.Select(entry => Convert.ToHexString(SHA256.HashData(Text(entry.Source))))
                        .Order(StringComparer.Ordinal)),
                "Matching-label parser coverage lost exact winning SCTX bytes or duplicated raw fields.");
            Require(ReadRows(questDirectory, "source-bodies.jsonl").All(row =>
                row.GetProperty("parsed").GetBoolean() && row.GetProperty("error").ValueKind == JsonValueKind.Null &&
                row.GetProperty("commandEffects").GetString() == "not-executed-not-certified"),
                "Matching-label source bodies were skipped or promoted from parsing to effects.");

            var corpusDirectory = Path.Combine(directory, "matching-event-end-corpus");
            Require(CorpusInventory.Run(records, source, corpusDirectory) == 0,
                "Actual byte/layout/parser corpus owner still refused authored matching End labels.");
            using var corpus = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(corpusDirectory, "summary.json")));
            var inventory = corpus.RootElement.GetProperty("recordInventory");
            Require(inventory.GetProperty("sourceBodies").GetInt64() == positive.Length &&
                inventory.GetProperty("parsedBodies").GetInt64() == positive.Length &&
                corpus.RootElement.GetProperty("sourceReadOutcome").GetString() == "accounted" &&
                !corpus.RootElement.GetProperty("runtimeReady").GetBoolean(),
                "Corpus matching-label completion lost bodies or claimed event invocation/runtime support.");
        }
        Require(positiveHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(positivePath))),
            "Matching-label full-reader inspection mutated its authored input.");

        var negative = new (string Name, string Source)[]
        {
            ("AuthoredMismatch", "Begin OnAdd\nreturn\nEnd OnLoad\n"),
            ("AuthoredExtraEndFilter", "Begin MenuMode 1003\nreturn\nEnd MenuMode 1003\n"),
            ("AuthoredOrphanEnd", "End OnAdd\n"),
            ("AuthoredRepeatedEnd", "Begin OnAdd\nreturn\nEnd OnAdd\nEnd OnAdd\n"),
            ("AuthoredOpenBranch", "Begin OnAdd\nif 0\nreturn\nEnd OnAdd\n"),
            ("AuthoredOpenLoop", "Begin OnAdd\nwhile 0\nreturn\nEnd OnAdd\n"),
            ("AuthoredMissingEnd", "Begin OnAdd\nreturn\n"),
            ("AuthoredQuotedEnd", "Begin OnAdd\nreturn\nEnd \"OnAdd\"\n"),
            ("AuthoredNumericEnd", "Begin OnAdd\nreturn\nEnd 1\n"),
        };
        var negativeGame = WriteEventEndInputs(directory, "refused-event-end-inputs", negative);
        var negativePath = Path.Combine(negativeGame, "Data", "FalloutNV.esm");
        var negativeHash = SHA256.HashData(File.ReadAllBytes(negativePath));
        using (var source = OpenEventEndInputs(negativeGame))
        using (var records = FalloutPluginStack.Load(source.PluginSources))
        {
            var questDirectory = Path.Combine(directory, "refused-event-end-quest");
            Require(QuestGraphAudit.Run(records, source, questDirectory) == 1, "Malformed End boundaries became quest acceptance.");
            using var quest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(questDirectory, "summary.json")));
            var state = quest.RootElement;
            Require(state.GetProperty("sourceBodies").GetInt32() == negative.Length &&
                state.GetProperty("parsedSourceBodies").GetInt32() == 0 &&
                state.GetProperty("parsedSourceFailures").GetInt32() == negative.Length &&
                state.GetProperty("failures").GetInt32() == negative.Length &&
                state.GetProperty("invariants").GetProperty("allSourceBodiesHaveParseDisposition").GetBoolean() &&
                state.GetProperty("invariants").GetProperty("allRawProgramFieldsAccountedFor").GetBoolean(),
                "A malformed closing scope vanished or retained a successful parser disposition.");
            Require(ReadRows(questDirectory, "source-bodies.jsonl").All(row =>
                !row.GetProperty("parsed").GetBoolean() && row.GetProperty("error").ValueKind == JsonValueKind.String) &&
                ReadRows(questDirectory, "program-fields.jsonl").Count == negative.Length * 3,
                "Malformed event labels dropped their actual failure/source field rows.");
            var corpusDirectory = Path.Combine(directory, "refused-event-end-corpus");
            Require(CorpusInventory.Run(records, source, corpusDirectory) == 1, "Malformed closing scope became corpus source completion.");
            using var corpus = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(corpusDirectory, "summary.json")));
            Require(corpus.RootElement.GetProperty("recordPayloadByteReads").GetInt64() == negative.Length &&
                corpus.RootElement.GetProperty("recordLayoutReads").GetInt64() == negative.Length &&
                corpus.RootElement.GetProperty("failedInstances").GetInt64() == negative.Length &&
                corpus.RootElement.GetProperty("recordInventory").GetProperty("sourceBodies").GetInt64() == negative.Length &&
                corpus.RootElement.GetProperty("recordInventory").GetProperty("parsedBodies").GetInt64() == 0 &&
                corpus.RootElement.GetProperty("sourceReadOutcome").GetString() == "failed",
                "Corpus byte/layout evidence silently skipped malformed diagnostic source scopes.");
        }
        Require(negativeHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(negativePath))),
            "Refused-label full-reader inspection mutated its authored input.");
        Console.WriteLine("OPENNV_EVENT_END_LABEL_CONTRACT_PASS matchingLabels=true bareIdentity=true casing=true filters=true parameters=true multipleBlocks=true negativeScopes=9 fullReader=true sourceUnchanged=true noEventInvocation=true compiledExecution=uninspected runtimeReady=false");
    }

    private static string WriteEventEndInputs(string directory, string name, IReadOnlyList<(string Name, string Source)> cases)
    {
        var game = Path.Combine(directory, name);
        var data = Path.Combine(game, "Data"); Directory.CreateDirectory(data);
        var scripts = cases.Select((entry, index) =>
        {
            // Independent authored bytes. The static audit never decodes or
            // executes this opaque nonempty compiled control or the empty ones.
            byte[] compiled = index == 0 ? [0xce, 0xf1] : [];
            return Record("SCPT", (uint)(0xb00 + index), 0, Field("EDID", Text(entry.Name)),
                Field("SCHR", ScriptHeader((uint)compiled.Length)), Field("SCDA", compiled), Field("SCTX", Text(entry.Source)));
        }).ToArray();
        File.WriteAllBytes(Path.Combine(data, "FalloutNV.esm"), Join(Header(), Join(scripts)));
        ModContentContracts.WriteArchive(Path.Combine(data, "FalloutNV.bsa"), "authored event-end resource sibling");
        File.WriteAllText(Path.Combine(game, "archives.ini"), "[Archive]\nsArchiveList=FalloutNV.bsa\n");
        return game;
    }

    private static RuntimeLiveContentSource OpenEventEndInputs(string game) => RuntimeLiveContentSource.Open(game,
        RuntimeLiveContentSource.FalloutNewVegasGame, activePlugins: ["FalloutNV.esm"], archiveIniPath: Path.Combine(game, "archives.ini"));
}
