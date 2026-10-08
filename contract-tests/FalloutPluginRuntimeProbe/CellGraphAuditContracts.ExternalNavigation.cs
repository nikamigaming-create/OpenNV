using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAuditContracts
{
    private static void ExternalNavigationTargetDeclarations()
    {
        const string overrides = "TargetOverride.esp", links = "NavmLinks.esp";
        var directory = Path.Combine(Path.GetTempPath(), "opennv-source-navm-targets-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(directory, "Game"); var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(data);
        try
        {
            var archive = Path.Combine(data, "FalloutNV.bsa"); ModContentContracts.WriteArchive(archive, "unused authored fixture member");
            var ini = Path.Combine(directory, "archives.ini"); File.WriteAllText(ini, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
            var configuration = Path.Combine(directory, "runtime.json");
            File.WriteAllText(configuration, "{\"schema\":\"opennv-runtime-configuration/v1\",\"world\":{\"gameUnitsToMeters\":0.1}}");
            foreach (var mode in new[] { "valid-winning-disabled", "valid-target-flag8", "out-of-bounds", "unused-out-of-bounds",
                "source-flag8-out-of-bounds", "unsupported", "malformed", "missing", "deleted", "wrong-signature" })
            {
                var fixture = ExternalNavigationFixture(mode);
                File.WriteAllBytes(Path.Combine(data, Plugin), fixture.Base);
                File.WriteAllBytes(Path.Combine(data, overrides), fixture.Overrides);
                File.WriteAllBytes(Path.Combine(data, links), fixture.Links);
                var before = new[] { Path.Combine(data, Plugin), Path.Combine(data, overrides), Path.Combine(data, links), archive, ini, configuration }
                    .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
                using var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                    activePlugins: [Plugin, overrides, links], archiveIniPath: ini);
                using var records = FalloutPluginStack.Load(source.PluginSources);
                var valid = mode.StartsWith("valid", StringComparison.Ordinal);
                var decoded = valid || mode.EndsWith("out-of-bounds", StringComparison.Ordinal);
                var bounds = valid ? "in-bounds" : decoded ? "out-of-bounds" : "target-" +
                    (mode is "unsupported" or "malformed" ? "layout-refused" : mode == "wrong-signature" ? "wrong-signature" : mode);
                var lane = decoded ? "navm-external-target-bounds" : mode is "unsupported" or "malformed" ?
                    "navm-external-target-layout" : "navm-external-owner";
                int? expectedTriangles = decoded ? valid ? 2 : 1 : null;
                bool? expectedExcluded = valid ? mode == "valid-target-flag8" : null;
                var cache = new CellGraphAudit.SourceNavigationTargets(records);
                var identities = new[] { Key(0x810), Key(0x812), new FalloutFormKey(links, 0x810) };
                foreach (var identity in identities)
                {
                    var record = records.GetEffective(identity); var mesh = FalloutNavigationMesh.Read(record);
                    var failures = new List<object>(); var rows = cache.Inspect(record, mesh, failures);
                    var target = records.TryGetWinner(Key(0x900), out var winning) ? winning : null;
                    Require(rows.Length == 2 && rows.Select(row => row.SourceEntry).SequenceEqual([0, 1]) &&
                        rows.Select(row => row.Type).SequenceEqual([0x12345678U, 0x87654321U]) &&
                        rows.All(row => row.Target == Key(0x900).ToString() && row.Triangle == 1 && row.Bounds == bounds &&
                            row.TargetTriangles == expectedTriangles &&
                            row.Winner == (mode == "missing" ? null : overrides) &&
                            row.InitiallyDisabled == (mode is not ("missing" or "deleted" or "wrong-signature")) &&
                            row.TargetTriangleExcludedByFlags == expectedExcluded &&
                            row.NativeAdmission.StartsWith("unverified", StringComparison.Ordinal) &&
                            row.TypeSemantics.StartsWith("uninspected", StringComparison.Ordinal)) &&
                        failures.Count == (valid ? 0 : 2),
                        "External entry bounds lost original fields, winning/disabled target ownership, failure multiplicity or unknown native state.");
                    if (decoded)
                        Require(rows.All(row => row.Sha256 == Convert.ToHexString(SHA256.HashData(target!.ReadData()))),
                            "Bounds were derived from a losing master or a same-object-ID foreign target instead of exact winning bytes.");
                    if (!valid)
                        Require(failures.All(failure => System.Text.Json.JsonSerializer.SerializeToElement(failure)
                            .GetProperty("lane").GetString() == lane), "External owner, target layout and target bounds refusals were conflated.");
                }
                Require(cache.TargetQueries == 1 && cache.TargetEntries == 1 &&
                    FalloutNavigationMesh.Read(records.GetEffective(new(links, 0x900))).Triangles.Length == 1,
                    "Repeated valid/refused targets reparsed the same winning mesh or selected a foreign decoy.");
                using (var other = FalloutPluginStack.Load(source.PluginSources))
                {
                    var foreignRecord = other.GetEffective(Key(0x810)); var foreignMesh = FalloutNavigationMesh.Read(foreignRecord);
                    Reject(() => cache.Inspect(foreignRecord, foreignMesh, []), "another source-stack record owner");
                    Require(cache.TargetQueries == 1 && cache.TargetEntries == 1,
                        "A foreign stack record changed source-target reuse before owner admission.");
                }
                var output = Path.Combine(directory, mode); Directory.CreateDirectory(output);
                var options = CellGraphAudit.ParseOptions([game, output, "--runtime-config", configuration]);
                Require(!CellGraphAudit.RunComponent(source, records, options, .1f, before[configuration]),
                    "Source target bounds fabricated current-state/native navigation readiness.");
                using var report = Read(Path.Combine(output, "component.private.json"));
                var graph = report.RootElement.GetProperty("sourceGraph");
                foreach (var identity in identities)
                {
                    var row = graph.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("identity").GetString() == identity.ToString());
                    var semantics = row.GetProperty("sourceSemantics");
                    var inspected = semantics.GetProperty("externalTargetDeclarations").EnumerateArray().ToArray();
                    Require(row.GetProperty("initiallyDisabled").GetBoolean() && row.GetProperty("failures").GetArrayLength() == (valid ? 0 : 2) &&
                        inspected.Length == 2 && inspected.All(entry => entry.GetProperty("bounds").GetString() == bounds) &&
                        semantics.GetProperty("externalTargetTriangleBoundsUninspected").GetArrayLength() == (decoded ? 0 : 2) &&
                        semantics.GetProperty("adjacencyTrianglesExcludedByFlags").GetArrayLength() == (mode == "source-flag8-out-of-bounds" ? 1 : 0),
                        "The complete source report omitted an NVEX entry, cleared a repeated target refusal or claimed excluded adjacency inspection.");
                }
                Require(report.RootElement.GetProperty("coverage").GetProperty("sourceAccountingPassed").GetBoolean() == valid &&
                    !report.RootElement.GetProperty("coverage").GetProperty("readinessPassed").GetBoolean(),
                    "Exact external target failures were cleared or source accounting became runtime readiness.");
                using var cell = Read(Path.Combine(output, "cell-FalloutNV-esm-000800.private.json"));
                Require(cell.RootElement.GetProperty("navMeshes").EnumerateArray().Select(row => row.GetProperty("form").GetString())
                    .SequenceEqual([new FalloutFormKey(links, 0x900).ToString()]),
                    "Source target declaration inspection activated an initially disabled mesh or replaced the enabled decoy's original identity.");
                Require(before.All(pair => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) == pair.Value),
                    "External NAVM source inspection changed an authored source/input file.");
            }
            Console.WriteLine("OPENNV_CELL_GRAPH_EXTERNAL_NAVM_TARGETS_PASS actualRecordReader=true exactWinningTarget=true masterAdjustment=true foreignObjectIdDecoy=true disabledTargetsInspectedNotActivated=true directedNoInverse=true everyNvexEntry=true targetBoundsRefused=true missingDeletedWrongTypeRefused=true unsupportedMalformedRefused=true refusalReuse=true foreignStackRefused=true flag8AdjacencyUninspected=true sourceReadOnly=true readinessUnverified=true");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static (byte[] Base, byte[] Overrides, byte[] Links) ExternalNavigationFixture(string mode)
    {
        byte[] Header(bool master)
        {
            var value = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(value, 1.34f);
            return master ? Record("TES4", 0, 0, Field("HEDR", value), Field("MAST", Text(Plugin)), Field("DATA", new byte[8])) :
                Record("TES4", 0, 0, Field("HEDR", value));
        }
        byte[] Mesh(uint form, int count, bool external = false, ushort flags = 0, uint version = 11, bool malformed = false, uint sourceFlags = 0x800)
        {
            var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, 0x800);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 3); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)count);
            if (external) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 2);
            float[] points = [0, 0, 0, 10, 0, 0, 0, 10, 0]; var vertices = new byte[36];
            for (var index = 0; index < points.Length; index++) BinaryPrimitives.WriteSingleLittleEndian(vertices.AsSpan(index * 4), points[index]);
            var triangles = new byte[(malformed ? 1 : count) * 16];
            for (var index = 0; index < triangles.Length / 16; index++)
            {
                var row = triangles.AsSpan(index * 16, 16); BinaryPrimitives.WriteUInt16LittleEndian(row[2..], 1);
                BinaryPrimitives.WriteUInt16LittleEndian(row[4..], 2);
                for (var offset = 6; offset <= 10; offset += 2) BinaryPrimitives.WriteInt16LittleEndian(row[offset..], -1);
                BinaryPrimitives.WriteUInt16LittleEndian(row[12..], flags);
                if (external && mode != "unused-out-of-bounds")
                {
                    BinaryPrimitives.WriteInt16LittleEndian(row[6..], 0); BinaryPrimitives.WriteInt16LittleEndian(row[8..], 1);
                    BinaryPrimitives.WriteUInt16LittleEndian(row[12..], (ushort)(flags | 3));
                }
            }
            var fields = new List<byte[]> { Field("NVER", BitConverter.GetBytes(version)), Field("DATA", data), Field("NVVX", vertices), Field("NVTR", triangles) };
            if (external)
            {
                var edges = new byte[20];
                for (var index = 0; index < 2; index++)
                {
                    var row = edges.AsSpan(index * 10, 10);
                    BinaryPrimitives.WriteUInt32LittleEndian(row, index == 0 ? 0x12345678U : 0x87654321U);
                    BinaryPrimitives.WriteUInt32LittleEndian(row[4..], 0x900); BinaryPrimitives.WriteUInt16LittleEndian(row[8..], 1);
                }
                fields.Add(Field("NVEX", edges));
            }
            return Record("NAVM", form, sourceFlags, fields.ToArray());
        }
        var sourceTriangleFlags = mode == "source-flag8-out-of-bounds" ? (ushort)8 : (ushort)0;
        var basis = Join(Header(false), Cell(0x800, "IndependentDirectedTargets"), Group(0x800,
            Mesh(0x810, 1, external: true, flags: sourceTriangleFlags), Mesh(0x812, 1, external: true, flags: sourceTriangleFlags),
            mode == "missing" ? [] : mode == "wrong-signature" ?
                Record("STAT", 0x900, 0, Field("EDID", Text("OriginalWrongTarget"))) : Mesh(0x900, 1)));
        var targetCount = mode.StartsWith("valid", StringComparison.Ordinal) || mode is "unsupported" or "malformed" ? 2 : 1;
        var target = mode == "missing" ? Array.Empty<byte>() : mode == "deleted" ? Record("NAVM", 0x900, 0x20) :
            mode == "wrong-signature" ? Record("STAT", 0x900, 0, Field("EDID", Text("ActualWrongTarget"))) :
            Mesh(0x900, targetCount, flags: mode == "valid-target-flag8" ? (ushort)8 : (ushort)0,
                version: mode == "unsupported" ? 99U : 11U, malformed: mode == "malformed");
        return (basis, Join(Header(true), Group(0x800, target)), Join(Header(true), Group(0x800,
            Mesh(0x01000810, 1, external: true, flags: sourceTriangleFlags), Mesh(0x01000900, 1, sourceFlags: 0))));
    }
}
