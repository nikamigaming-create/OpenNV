using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class QuestGraphAuditContracts
{
    internal static void Run()
    {
        // Reuse the existing instrumented all-arm owner: actual callbacks,
        // state reads/writes and effects remain absent during inspection.
        ScriptInspectionContracts.Run();
        var directory = Path.Combine(Path.GetTempPath(), "opennv-quest-denominator-" + Guid.NewGuid().ToString("N"));
        var input = Path.Combine(directory, "inputs"); Directory.CreateDirectory(input);
        try
        {
            var source = Path.Combine(input, "Source.esm"); var winner = Path.Combine(input, "Winner.esp");
            File.WriteAllBytes(source, SourceFixture()); File.WriteAllBytes(winner, WinnerFixture());
            var beforeSource = SHA256.HashData(File.ReadAllBytes(source));
            var beforeWinner = SHA256.HashData(File.ReadAllBytes(winner));
            using (var records = FalloutPluginStack.Load(input, ["Source.esm", "Winner.esp"]))
            {
                var output = Path.Combine(directory, "complete");
                Require(QuestGraphAudit.Run(records, "authored-quest-denominator-fixture", output) == 1,
                    "Orphan/duplicate/parse/uninspected source rows produced a passing audit.");
                using var summary = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "summary.json")));
                var root = summary.RootElement;
                Require(root.GetProperty("schema").GetString() == "opennv-private-quest-graph-audit/v2" &&
                    root.GetProperty("programs").GetInt32() == 12 && root.GetProperty("sourceBodies").GetInt32() == 10 &&
                    root.GetProperty("sourcePrograms").GetInt32() == 9 && root.GetProperty("orphanPrograms").GetInt32() == 3 &&
                    root.GetProperty("deletedProgramRows").GetInt32() == 1 && root.GetProperty("compiledProgramRows").GetInt32() == 4 &&
                    root.GetProperty("emptyCompiledRows").GetInt32() == 1 && root.GetProperty("compiledOnlyPrograms").GetInt32() == 1,
                    "Winning/deleted/empty/orphan/compiled-only program bodies lost their original denominator.");
                var invariants = root.GetProperty("invariants");
                Require(invariants.GetProperty("allRawProgramFieldsAccountedFor").GetBoolean() &&
                    invariants.GetProperty("allSourceBodiesHaveParseDisposition").GetBoolean() &&
                    invariants.GetProperty("allSourceProgramsAccountedFor").GetBoolean() &&
                    invariants.GetProperty("discoveredProgramFieldIdentities").GetInt32() == 23 &&
                    invariants.GetProperty("accountedProgramFieldIdentities").GetInt32() == 23 &&
                    invariants.GetProperty("worldInstancesAfterInspection").GetInt32() == 0,
                    "Original source fields were duplicated/omitted or inspection admitted a runtime reference.");
                Require(root.GetProperty("compiledExecutionOwners").ValueKind == JsonValueKind.Null &&
                    !root.GetProperty("readiness").GetProperty("auditedLanePassed").GetBoolean() &&
                    !root.GetProperty("readiness").GetProperty("completeRuntimeReadiness").GetBoolean() &&
                    root.GetProperty("uninspectedCommands").GetInt32() > 0 && root.GetProperty("parsedSourceFailures").GetInt32() == 1,
                    "Unknown/uninspected command effects, binary execution or source failures became success.");
                var programs = ReadRows(output, "programs.jsonl");
                var deleted = programs.Single(row => row.GetProperty("source").GetString() == "Source.esm:000108");
                Require(deleted.GetProperty("deleted").GetBoolean() && deleted.GetProperty("winner").GetString() == "Winner.esp" &&
                    deleted.GetProperty("commands").EnumerateArray().Any(value => value.GetString() == "unknowndeletedwinner") &&
                    !programs.Any(row => row.GetProperty("commands").EnumerateArray().Any(value => value.GetString() == "unknownobsolete")),
                    "Deleted original winner was dropped or replaced with the superseded source body.");
                Require(programs.Single(row => row.GetProperty("source").GetString() == "Source.esm:000102").GetProperty("parsed").ValueKind == JsonValueKind.Null &&
                    programs.Single(row => row.GetProperty("source").GetString() == "Source.esm:000101").GetProperty("parsed").ValueKind == JsonValueKind.Null,
                    "Absent SCTX in compiled-only/empty-header rows was awarded a source parser pass.");
                var fields = ReadRows(output, "program-fields.jsonl");
                Require(fields.Count == 23 && fields.Select(row => row.GetProperty("source").GetString() + "/" + row.GetProperty("fieldOrdinal").GetInt32()).Distinct().Count() == 23 &&
                    fields.Count(row => row.GetProperty("disposition").GetString() == "orphan-source-field-without-SCHR") == 5,
                    "Raw original program fields did not receive exact once-only header/orphan dispositions.");
                var sourceBodies = ReadRows(output, "source-bodies.jsonl");
                Require(sourceBodies.Count == 10 && sourceBodies.Count(row => !row.GetProperty("parsed").GetBoolean()) == 1 &&
                    sourceBodies.Count(row => row.GetProperty("source").GetString()!.StartsWith("Source.esm:000105/", StringComparison.Ordinal)) == 2,
                    "Repeated/orphan/failed source bodies were collapsed into a single parser result.");
                var conditions = ReadRows(output, "conditions.jsonl");
                Require(root.GetProperty("conditions").GetInt32() == 1 && root.GetProperty("parsedConditions").GetInt32() == 0 &&
                    conditions.Count == 1 && conditions[0].GetProperty("fieldOrdinal").GetInt32() == 2 &&
                    conditions[0].GetProperty("disposition").GetString() == "condition-decode-failed",
                    "A failed original condition field disappeared from the source ledger.");
                var statements = ReadRows(output, "statements.jsonl");
                Require(statements.Any(row => row.GetProperty("statement").GetProperty("Operation").GetString() == "unknowninactive") &&
                    statements.Any(row => row.GetProperty("statement").GetProperty("Operation").GetString() == "unknownloop") &&
                    statements.Any(row => row.GetProperty("statement").GetProperty("Operation").GetString() == "unknownrepeatedtwo") &&
                    statements.All(row => row.GetProperty("effects").GetString() == "not-executed-not-certified"),
                    "Inactive, loop or repeated source arms were discarded or executed to manufacture coverage.");
                Reject(() => QuestGraphAudit.Run(records, "fixture", output), "fresh");
                var rejected = Path.Combine(input, "must-not-be-created");
                Reject(() => QuestGraphAudit.Run(records, "fixture", rejected), "outside owned input");
                Require(!Directory.Exists(rejected), "Refused owned output admission still created a source directory.");
            }
            Require(beforeSource.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))) &&
                beforeWinner.SequenceEqual(SHA256.HashData(File.ReadAllBytes(winner))), "Source inventory changed an original input.");

            var controlRoot = Path.Combine(directory, "control-inputs"); Directory.CreateDirectory(controlRoot);
            File.WriteAllBytes(Path.Combine(controlRoot, "Source.esm"), Join(Header(), Record("INFO", 0x200, 0,
                Field("SCHR", ScriptHeader()), Field("SCTX", Text("return")))));
            using (var records = FalloutPluginStack.Load(controlRoot, ["Source.esm"]))
            {
                var output = Path.Combine(directory, "unverified-control");
                Require(QuestGraphAudit.Run(records, "authored-control", output) == 1,
                    "A parsed source body with unmeasured effects/execution was awarded readiness.");
                using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "summary.json")));
                Require(report.RootElement.GetProperty("failures").GetInt32() == 0 &&
                    report.RootElement.GetProperty("parsedSourceBodies").GetInt32() == 1 &&
                    !report.RootElement.GetProperty("readiness").GetProperty("auditedLanePassed").GetBoolean(),
                    "Source parse success was confused with structural failure or actual execution readiness.");
            }
            var failedRoot = Path.Combine(directory, "failed-inputs"); Directory.CreateDirectory(failedRoot);
            File.WriteAllBytes(Path.Combine(failedRoot, "Source.esm"), Join(Header(), Record("FUTR", 0x300,
                FalloutPluginRecord.CompressedFlag, new byte[] { 0, 0, 0, 0 })));
            using (var records = FalloutPluginStack.Load(failedRoot, ["Source.esm"]))
            {
                var output = Path.Combine(directory, "unread-record");
                Require(QuestGraphAudit.Run(records, "authored-unread-record", output) == 1, "Unread source record passed.");
                using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "summary.json")));
                Require(report.RootElement.GetProperty("selection").GetProperty("unreadWinningRecords").GetInt32() == 1 &&
                    !report.RootElement.GetProperty("selection").GetProperty("originalProgramDenominatorKnown").GetBoolean() &&
                    !report.RootElement.GetProperty("invariants").GetProperty("allSourceProgramsAccountedFor").GetBoolean() &&
                    ReadRows(output, "records.jsonl").Single().GetProperty("disposition").GetString() == "unread-winning-record-program-denominator-unknown",
                    "Unread bytes disappeared or claimed a complete program denominator.");
            }
            CompressedEmptyControl(directory);
            StrictCompressedFraming(directory);
            StrictBsaCompressedFraming(directory);
            SourceBsaEncodingAdmission(directory);
            StrictHuffmanReaderAdmission(directory);
            SourceCallDenominator(directory);
            Console.WriteLine("OPENNV_QUEST_GRAPH_DENOMINATOR_CONTRACT_PASS deletedWinner=true orphanBodies=true duplicateBodies=true emptyCompiled=true inactiveArms=true rawFieldsOnce=true unknownExecution=true failuresRetained=true sourceUnchanged=true");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static List<JsonElement> ReadRows(string directory, string name) => File.ReadLines(Path.Combine(directory, name))
        .Select(line => { using var value = JsonDocument.Parse(line); return value.RootElement.Clone(); }).ToList();
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action, string expected)
    {
        try { action(); }
        catch (IOException error) when (error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidDataException("Expected quest audit refusal: " + expected);
    }
}
