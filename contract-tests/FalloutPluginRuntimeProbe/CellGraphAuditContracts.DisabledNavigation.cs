using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAuditContracts
{
    private static void DisabledNavigationDeclarations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-disabled-navm-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(directory, "Game"); var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(data);
        try
        {
            var plugin = Path.Combine(data, Plugin);
            var archive = Path.Combine(data, "FalloutNV.bsa"); ModContentContracts.WriteArchive(archive, "unused authored fixture member");
            var ini = Path.Combine(directory, "archives.ini"); File.WriteAllText(ini, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
            var configuration = Path.Combine(directory, "runtime.json");
            File.WriteAllText(configuration, "{\"schema\":\"opennv-runtime-configuration/v1\",\"world\":{\"gameUnitsToMeters\":0.1}}");
            var cases = new[]
            {
                (Name: "disabled-out-of-range", Mode: 1, Disabled: true, Error: true),
                (Name: "disabled-nonreciprocal", Mode: 2, Disabled: true, Error: true),
                (Name: "disabled-wrong-shared-edge", Mode: 3, Disabled: true, Error: true),
                (Name: "disabled-valid-reciprocal", Mode: 4, Disabled: true, Error: false),
                (Name: "disabled-valid-boundary", Mode: 0, Disabled: true, Error: false),
                (Name: "disabled-flag-excluded-neighbor-uninspected", Mode: 5, Disabled: true, Error: false),
                (Name: "disabled-external-target-triangle-out-of-range", Mode: 6, Disabled: true, Error: false),
                (Name: "disabled-external-local-index-out-of-range", Mode: 7, Disabled: true, Error: true),
                (Name: "disabled-directed-external-valid", Mode: 8, Disabled: true, Error: false),
                (Name: "enabled-out-of-range", Mode: 1, Disabled: false, Error: true)
            };
            foreach (var item in cases)
            {
                File.WriteAllBytes(plugin, DisabledNavigationFixture(item.Mode, item.Disabled));
                var before = new[] { plugin, archive, ini, configuration }.ToDictionary(path => path,
                    path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
                using var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                    activePlugins: [Plugin], archiveIniPath: ini);
                using var records = FalloutPluginStack.Load(source.PluginSources);
                var original = records.GetEffective(Key(0x810));
                var decoded = FalloutNavigationMesh.Read(original);
                Require(decoded.Triangles.Length == (item.Mode is 2 or 3 or 4 ? 2 : 1) &&
                    ((original.Flags & 0x800) != 0) == item.Disabled,
                    "The authored negative was rejected by layout framing or lost its actual disabled source flag.");
                var projected = new CellGraphAudit.CellAuditResources(source, .1f).Navigation(records, Key(0x800));
                Require(projected.Meshes.Select(mesh => mesh.Form).SequenceEqual(item.Disabled ? new[] { Key(0x812) } : new[] { Key(0x810), Key(0x812) }) &&
                    projected.Failures.Length == (item.Disabled ? 0 : 1),
                    "Source adjacency validation admitted a disabled mesh into current-state navigation or changed the enabled graph's existing refusal.");
                var output = Path.Combine(directory, item.Name);
                var options = CellGraphAudit.ParseOptions([game, output, "--runtime-config", configuration]);
                Directory.CreateDirectory(output);
                Require(!CellGraphAudit.RunComponent(source, records, options, .1f, before[configuration]),
                    "A structural NAVM check invented native navigation readiness.");
                using var report = Read(Path.Combine(output, "component.private.json"));
                var root = report.RootElement; var graph = root.GetProperty("sourceGraph");
                var rows = graph.GetProperty("rows").EnumerateArray().ToArray();
                var subject = rows.Single(row => row.GetProperty("identity").GetString() == Key(0x810).ToString());
                var failures = subject.GetProperty("failures").EnumerateArray().ToArray();
                var semantics = subject.GetProperty("sourceSemantics");
                var externalError = item.Mode is 6 or 7;
                Require(subject.GetProperty("initiallyDisabled").GetBoolean() == item.Disabled &&
                    semantics.GetProperty("sharedAdjacencyValidated").GetBoolean() == !item.Error &&
                    failures.Length == (item.Error ? 1 : 0) + (externalError ? 1 : 0) && (!item.Error || failures.Any(failure =>
                        failure.GetProperty("lane").GetString() == "navm-source-adjacency" &&
                        failure.GetProperty("error").GetString()!.Contains("unclassified adjacency", StringComparison.Ordinal))) &&
                    (!externalError || failures.Any(failure => failure.GetProperty("lane").GetString() == "navm-external-target-bounds")),
                    "Disabled NAVM bounds/reciprocity/shared-edge failures were omitted, attributed to another owner or reported as inspected success.");
                Require(semantics.GetProperty("adjacencyTrianglesExcludedByFlags").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(
                    item.Mode == 5 ? new[] { 0 } : Array.Empty<int>()),
                    "The shared validator's flag-excluded triangle adjacency was declared inspected or lost its source index.");
                var uninspected = semantics.GetProperty("externalTargetTriangleBoundsUninspected").EnumerateArray().ToArray();
                var declarations = semantics.GetProperty("externalTargetDeclarations").EnumerateArray().ToArray();
                Require(uninspected.Length == 0 && declarations.Length == (item.Mode is 6 or 7 or 8 ? 1 : 0) && (declarations.Length == 0 ||
                    declarations[0].GetProperty("sourceEntry").GetInt32() == 0 &&
                    declarations[0].GetProperty("target").GetString() == Key(0x811).ToString() &&
                    declarations[0].GetProperty("triangle").GetInt32() == (item.Mode == 8 ? 0 : 9) &&
                    declarations[0].GetProperty("targetTriangles").GetInt32() == 1 &&
                    declarations[0].GetProperty("initiallyDisabled").GetBoolean() &&
                    declarations[0].GetProperty("bounds").GetString() == (externalError ? "out-of-bounds" : "in-bounds")),
                    "Actual disabled target bounds were omitted, substituted or mistaken for native directed traversal.");
                foreach (var good in new[] { 0x811U, 0x812U })
                {
                    var row = rows.Single(value => value.GetProperty("identity").GetString() == Key(good).ToString());
                    Require(row.GetProperty("failures").GetArrayLength() == 0 &&
                        row.GetProperty("sourceSemantics").GetProperty("sharedAdjacencyValidated").GetBoolean() &&
                        row.GetProperty("sourceSemantics").GetProperty("adjacencyTrianglesExcludedByFlags").GetArrayLength() == 0 &&
                        row.GetProperty("sourceSemantics").GetProperty("externalTargetTriangleBoundsUninspected").GetArrayLength() == 0 &&
                        row.GetProperty("sha256").GetString() == Convert.ToHexString(SHA256.HashData(records.GetEffective(Key(good)).ReadData())) &&
                        row.GetProperty("initiallyDisabled").GetBoolean() == (good == 0x811),
                        "A bad disabled neighbor contaminated a healthy unrelated disabled/enabled source declaration.");
                }
                Require(graph.GetProperty("denominator").GetProperty("winningNavMeshes").GetInt32() == 3 &&
                    graph.GetProperty("denominator").GetProperty("initiallyDisabledNavMeshes").GetInt32() == (item.Disabled ? 2 : 1) &&
                    root.GetProperty("coverage").GetProperty("sourceAccountingPassed").GetBoolean() == (!item.Error && !externalError) &&
                    !root.GetProperty("coverage").GetProperty("readinessPassed").GetBoolean(),
                    "Source NAVM validation dropped a declaration, cleared its failure or awarded runtime readiness.");
                using var cell = Read(Path.Combine(output, "cell-FalloutNV-esm-000800.private.json"));
                Require(cell.RootElement.GetProperty("navMeshes").EnumerateArray().Select(row => row.GetProperty("form").GetString()).SequenceEqual(
                    item.Disabled ? new[] { Key(0x812).ToString() } : new[] { Key(0x810).ToString(), Key(0x812).ToString() }) &&
                    cell.RootElement.GetProperty("navFailures").GetArrayLength() == (item.Disabled ? 0 : 1),
                    "Disabled source inspection changed the report's actual current-state mesh denominator or enabled refusal.");
                Require(before.All(pair => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) == pair.Value),
                    "Adjacency validation changed an authored source/input file.");
            }
            Console.WriteLine("OPENNV_CELL_GRAPH_DISABLED_NAVM_DECLARATIONS_PASS actualRecordReader=true disabledOutOfBoundsRefused=true nonreciprocalRefused=true wrongSharedEdgeRefused=true validReciprocal=true externalLocalIndexRefused=true flagExcludedAdjacencyUninspected=true externalTargetBoundsRefused=true directedExternalNoInverseRequired=true unrelatedDisabledAndEnabled=true runtimeExclusionUnchanged=true enabledRefusalUnchanged=true sourceFailuresRetained=true sourceReadOnly=true readinessUnverified=true");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static byte[] DisabledNavigationFixture(int mode, bool disabled)
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        byte[] Navigation(uint id, int shape, bool off)
        {
            var paired = shape is 2 or 3 or 4;
            var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, 0x800);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), shape == 3 ? 5U : paired ? 4U : 3U);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), paired ? 2U : 1U);
            if (shape is 6 or 7 or 8) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);
            float[] points = shape == 3 ? [0, 0, 0, 10, 0, 0, 0, 10, 0, 10, 10, 0, 20, 10, 0] :
                paired ? [0, 0, 0, 10, 0, 0, 0, 10, 0, 10, -10, 0] : [0, 0, 0, 10, 0, 0, 0, 10, 0];
            var vertices = new byte[points.Length * 4];
            for (var index = 0; index < points.Length; index++) BinaryPrimitives.WriteSingleLittleEndian(vertices.AsSpan(index * 4), points[index]);
            var triangles = new byte[(paired ? 2 : 1) * 16];
            for (var index = 0; index < (paired ? 2 : 1); index++)
            {
                var row = triangles.AsSpan(index * 16, 16);
                ushort[] corners = index == 0 ? [0, 1, 2] : shape == 3 ? [2, 3, 4] : [1, 0, 3];
                for (var corner = 0; corner < 3; corner++) BinaryPrimitives.WriteUInt16LittleEndian(row[(corner * 2)..], corners[corner]);
                for (var offset = 6; offset <= 10; offset += 2) BinaryPrimitives.WriteInt16LittleEndian(row[offset..], -1);
                if (index == 0 && (paired || shape is 1 or 5 or 7))
                    BinaryPrimitives.WriteInt16LittleEndian(row[6..], paired ? (short)1 : (short)9);
                if (index == 0 && (shape is 6 or 8)) BinaryPrimitives.WriteInt16LittleEndian(row[6..], 0);
                if (shape == 5) BinaryPrimitives.WriteUInt16LittleEndian(row[12..], 8);
                if (shape is 6 or 7 or 8) BinaryPrimitives.WriteUInt16LittleEndian(row[12..], 1);
                if (index == 1 && shape != 2) BinaryPrimitives.WriteInt16LittleEndian(row[6..], 0);
            }
            var fields = new List<byte[]> { Field("NVER", BitConverter.GetBytes(11U)), Field("DATA", data),
                Field("NVVX", vertices), Field("NVTR", triangles) };
            if (shape is 6 or 7 or 8)
            {
                var external = new byte[10]; BinaryPrimitives.WriteUInt32LittleEndian(external.AsSpan(4), 0x811);
                BinaryPrimitives.WriteUInt16LittleEndian(external.AsSpan(8), shape == 8 ? (ushort)0 : (ushort)9);
                fields.Add(Field("NVEX", external));
            }
            return Record("NAVM", id, off ? 0x800U : 0, fields.ToArray());
        }
        return Join(Record("TES4", 0, 0, Field("HEDR", header)), Cell(0x800, "IndependentNavigationDeclarations"),
            Group(0x800, Navigation(0x810, mode, disabled), Navigation(0x811, 0, true), Navigation(0x812, 0, false)));
    }
}
