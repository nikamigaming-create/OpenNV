using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAuditContracts
{
    private static void ModeledLightProjection()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-cell-light-projection-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(directory, "Game"); var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(Path.Combine(data, "meshes"));
        try
        {
            var plugin = Path.Combine(data, Plugin); var model = Path.Combine(data, "meshes", "light-box.nif");
            File.WriteAllBytes(plugin, ModeledLightFixture()); File.WriteAllBytes(model, NifFixture());
            var archive = Path.Combine(data, "FalloutNV.bsa"); ModContentContracts.WriteArchive(archive, "unused authored fixture member");
            var ini = Path.Combine(directory, "archives.ini"); File.WriteAllText(ini, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
            var configuration = Path.Combine(directory, "runtime.json");
            var before = new[] { plugin, model, archive }.ToDictionary(path => path,
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            foreach (var units in new[] { .1f, .2f })
            {
                File.WriteAllText(configuration, JsonSerializer.Serialize(new
                { schema = "opennv-runtime-configuration/v1", world = new { gameUnitsToMeters = units } }));
                using var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                    activePlugins: [Plugin], archiveIniPath: ini);
                using var records = FalloutPluginStack.Load(source.PluginSources);
                var output = Path.Combine(directory, units == .1f ? "first" : "double-units");
                var options = CellGraphAudit.ParseOptions([game, output, "--runtime-config", configuration]);
                Directory.CreateDirectory(output);
                Require(!CellGraphAudit.RunComponent(source, records, options, units,
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(configuration)))),
                    "A source light projection invented native light/controller readiness.");
                using var light = CellReport(0x800);
                using var disabled = CellReport(0x801);
                using var ordinary = CellReport(0x802);
                using var rotatedLight = CellReport(0x803);
                using var rotatedOrdinary = CellReport(0x804);
                Compare(light.RootElement, ordinary.RootElement, 0x810, 0x812, 388 * units);
                Compare(rotatedLight.RootElement, rotatedOrdinary.RootElement, 0x813, 0x814, 122 * units);
                var off = disabled.RootElement; var offReference = off.GetProperty("references").EnumerateArray().Single();
                Require(!offReference.GetProperty("sourceEnabled").GetBoolean() &&
                    off.GetProperty("denominator").GetProperty("collisionTrianglesEvaluated").GetInt32() == 0 &&
                    off.GetProperty("projectionCandidates").GetProperty("triangleCount").GetInt32() == 0 &&
                    off.GetProperty("denominator").GetProperty("navSupportWithoutPackedOrBoxHit").GetInt32() == 1 &&
                    off.GetProperty("navCentroidSupport").EnumerateArray().Single().GetProperty("sampledCollisionHeight").ValueKind == JsonValueKind.Null &&
                    off.GetProperty("resources").EnumerateArray().Single().GetProperty("collisionShapes").GetInt32() == 1,
                    "A disabled modeled light contributed fabricated support or lost its inspected source collision declaration.");
                using var component = Read(Path.Combine(output, "component.private.json"));
                Require(component.RootElement.GetProperty("selection").GetProperty("totalSelectedCells").GetInt32() == 5 &&
                    component.RootElement.GetProperty("sourceGraph").GetProperty("denominator").GetProperty("winningReferences").GetInt32() == 5 &&
                    component.RootElement.GetProperty("sourceGraph").GetProperty("denominator").GetProperty("initiallyDisabledReferences").GetInt32() == 1 &&
                    !component.RootElement.GetProperty("coverage").GetProperty("readinessPassed").GetBoolean(),
                    "The modeled-light correction changed source selection/disabled accounting or awarded runtime readiness.");

                JsonDocument CellReport(uint cell) => Read(Path.Combine(output, "cell-FalloutNV-esm-" + cell.ToString("x6") + ".private.json"));
            }
            Require(before.All(pair => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))) == pair.Value),
                "The light projection audit modified an authored source input.");
            Console.WriteLine("OPENNV_CELL_GRAPH_MODELED_LIGHT_PROJECTION_PASS actualEsmNifReaders=true enabledSourceCollision=true disabledSourceExcluded=true translatedScaled=true clockwiseRotation=true units=true ordinaryModelComparison=true sourceNativeLightRefusalRetained=true sourceReadOnly=true readinessUnverified=true");
        }
        finally { Directory.Delete(directory, recursive: true); }

        static void Compare(JsonElement light, JsonElement ordinary, uint lightReference, uint ordinaryReference, float expectedHeight)
        {
            foreach (var cell in new[] { light, ordinary })
            {
                Require(cell.GetProperty("denominator").GetProperty("collisionTrianglesEvaluated").GetInt32() == 12 &&
                    cell.GetProperty("projectionCandidates").GetProperty("triangleCount").GetInt32() == 12 &&
                    cell.GetProperty("denominator").GetProperty("navSupportWithoutPackedOrBoxHit").GetInt32() == 0,
                    "An enabled placed light/model omitted original box triangles or reported missing NAVM support.");
                var hit = cell.GetProperty("navCentroidSupport").EnumerateArray().Single();
                Require(MathF.Abs(hit.GetProperty("nativeHeight").GetSingle() - expectedHeight) < 1e-4f &&
                    MathF.Abs(hit.GetProperty("sampledCollisionHeight").GetSingle() - expectedHeight) < 1e-4f &&
                    MathF.Abs(hit.GetProperty("heightDifference").GetSingle()) < 1e-4f,
                    "The independently authored top plane disagreed with the placed reference transform or unit conversion.");
            }
            var lightRow = light.GetProperty("references").EnumerateArray().Single();
            Require(lightRow.GetProperty("signature").GetString() == "LIGH" && lightRow.GetProperty("sourceEnabled").GetBoolean() &&
                lightRow.GetProperty("failures").EnumerateArray().Any(issue => issue.GetProperty("lane").GetString() == "light-admission"),
                "Source mathematics cleared the real native modeled-light/controller refusal.");
            var left = light.GetProperty("navCentroidSupport").EnumerateArray().Single();
            var right = ordinary.GetProperty("navCentroidSupport").EnumerateArray().Single();
            Require(left.GetProperty("reference").GetString() == Key(lightReference).ToString() &&
                right.GetProperty("reference").GetString() == Key(ordinaryReference).ToString() &&
                left.GetProperty("shape").GetInt32() == right.GetProperty("shape").GetInt32() &&
                left.GetProperty("sampledCollisionHeight").GetRawText() == right.GetProperty("sampledCollisionHeight").GetRawText() &&
                left.GetProperty("normalY").GetRawText() == right.GetProperty("normalY").GetRawText(),
                "Modeled lights and ordinary model references used different projection owners or replaced the source hit identity.");
        }
    }

    private static byte[] ModeledLightFixture()
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var light = new byte[32]; BinaryPrimitives.WriteInt32LittleEndian(light, -1);
        BinaryPrimitives.WriteUInt32LittleEndian(light.AsSpan(4), 200); light[8] = 80; light[9] = 120; light[10] = 160;
        BinaryPrimitives.WriteSingleLittleEndian(light.AsSpan(16), 1); BinaryPrimitives.WriteSingleLittleEndian(light.AsSpan(20), 90);
        byte[] Placed(uint id, uint basis, float[] position, float scale, bool rotated, bool disabled = false)
        {
            var transform = new byte[24];
            for (var index = 0; index < 3; index++) BinaryPrimitives.WriteSingleLittleEndian(transform.AsSpan(index * 4), position[index]);
            if (rotated) BinaryPrimitives.WriteSingleLittleEndian(transform.AsSpan(20), MathF.PI / 2);
            return Record("REFR", id, disabled ? 0x800U : 0, Field("NAME", BitConverter.GetBytes(basis)),
                Field("DATA", transform), Field("XSCL", BitConverter.GetBytes(scale)));
        }
        byte[] Navigation(uint id, uint cell, bool rotated)
        {
            var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, cell);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 3); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 1);
            float[] points = rotated ? [309, -206, 122, 311, -206, 122, 310, -203, 122] :
                [118, 238, 388, 122, 238, 388, 120, 244, 388];
            var vertices = new byte[36];
            for (var index = 0; index < points.Length; index++) BinaryPrimitives.WriteSingleLittleEndian(vertices.AsSpan(index * 4), points[index]);
            var triangle = new byte[16]; BinaryPrimitives.WriteUInt16LittleEndian(triangle.AsSpan(2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(triangle.AsSpan(4), 2);
            for (var offset = 6; offset <= 10; offset += 2) BinaryPrimitives.WriteInt16LittleEndian(triangle.AsSpan(offset), -1);
            return Record("NAVM", id, 0, Field("NVER", BitConverter.GetBytes(11U)), Field("DATA", data), Field("NVVX", vertices), Field("NVTR", triangle));
        }
        return Join(Record("TES4", 0, 0, Field("HEDR", header)),
            Record("LIGH", 0x100, 0, Field("MODL", Text("light-box.nif")), Field("DATA", light)),
            Record("STAT", 0x101, 0, Field("MODL", Text("light-box.nif"))),
            Cell(0x800, "EnabledModeledLight"), Group(0x800, Placed(0x810, 0x100, [100, 200, 300], 2, false), Navigation(0x820, 0x800, false)),
            Cell(0x801, "DisabledModeledLight"), Group(0x801, Placed(0x811, 0x100, [100, 200, 300], 2, false, true), Navigation(0x821, 0x801, false)),
            Cell(0x802, "OrdinaryPlacedModel"), Group(0x802, Placed(0x812, 0x101, [100, 200, 300], 2, false), Navigation(0x822, 0x802, false)),
            Cell(0x803, "RotatedModeledLight"), Group(0x803, Placed(0x813, 0x100, [300, -200, 100], .5f, true), Navigation(0x823, 0x803, true)),
            Cell(0x804, "RotatedPlacedModel"), Group(0x804, Placed(0x814, 0x101, [300, -200, 100], .5f, true), Navigation(0x824, 0x804, true)));
    }
}
