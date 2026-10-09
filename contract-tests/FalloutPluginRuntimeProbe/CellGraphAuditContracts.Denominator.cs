using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAuditContracts
{
    private static void CompleteDenominator()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-cell-denominator-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(directory, "Game"); var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(data);
        try
        {
            File.WriteAllBytes(Path.Combine(data, Plugin), DenominatorFixture(false));
            File.WriteAllBytes(Path.Combine(data, "Overrides.esp"), DenominatorOverrides());
            ModContentContracts.WriteArchive(Path.Combine(data, "FalloutNV.bsa"), "unused fixture member");
            var archives = Path.Combine(directory, "archives.ini"); File.WriteAllText(archives, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
            File.WriteAllText(Path.Combine(game, "Fallout_default.ini"), "[TerrainManager]\nfBlockLoadDistance=8192\nfSplitDistanceMult=2\nfBlockMorphDistanceMult=0.5\n[Landscape]\nSDefaultLandDiffuseTexture=fixture.dds\nSDefaultLandNormalTexture=fixture.dds\n");
            foreach (var path in new[] { "box.nif", "creatures/own.nif", "creatures/a/skeleton.nif", "creatures/b/skeleton.nif", "creatures/a/body.nif" })
            {
                var target = Path.Combine(data, "meshes", path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, NifFixture());
            }
            var configuration = Path.Combine(directory, "runtime.json");
            File.WriteAllText(configuration, "{\"schema\":\"opennv-runtime-configuration/v1\",\"world\":{\"gameUnitsToMeters\":0.1}}");
            var pluginBefore = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(data, Plugin))));
            var forbiddenOutput = Path.Combine(data, "audit-must-not-become-input");
            Require(CellGraphAudit.RunCommand([game, forbiddenOutput, "--runtime-config", configuration]) == 2 &&
                !Directory.Exists(forbiddenOutput), "A cell audit wrote its report into the owned resource namespace.");
            var first = Audit("all-source");
            var compressedPath = Audit("all-source-compressed", compress: true);
            using (var original = Read(first))
            using (var input = File.OpenRead(compressedPath))
            using (var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress))
            using (var compressed = JsonDocument.Parse(gzip))
            {
                Require(original.RootElement.GetProperty("sourceGraph").GetRawText() ==
                    compressed.RootElement.GetProperty("sourceGraph").GetRawText(),
                    "Compressed source output lost a winning field, identity, failure or source disposition.");
                using var compact = Read(Path.Combine(Path.GetDirectoryName(compressedPath)!, "summary.json"));
                Require(compact.RootElement.GetProperty("winningCells").GetInt32() == 6 &&
                    compact.RootElement.GetProperty("selectedCells").GetInt32() == 5 &&
                    compact.RootElement.GetProperty("componentPath").GetString() == compressedPath &&
                    compact.RootElement.GetProperty("inventoryFinished").GetBoolean() &&
                    !compact.RootElement.GetProperty("readinessPassed").GetBoolean(),
                    "The compact report disagreed with its complete compressed source inventory or invented readiness.");
            }
            using (var report = Read(first))
            {
                var root = report.RootElement; var graph = root.GetProperty("sourceGraph"); var totals = graph.GetProperty("denominator");
                Require(root.GetProperty("selection").GetProperty("completeSource").GetBoolean() &&
                    root.GetProperty("selection").GetProperty("totalSelectedCells").GetInt32() == 5 &&
                    totals.GetProperty("winningCells").GetInt32() == 6 && totals.GetProperty("deletedCells").GetInt32() == 1 &&
                    totals.GetProperty("winningWorldspaces").GetInt32() == 2 && totals.GetProperty("deletedWorldspaces").GetInt32() == 1,
                    "Complete selection dropped exterior, disconnected, empty or deleted CELL/WRLD winners.");
                Require(totals.GetProperty("winningReferences").GetInt32() == 11 && totals.GetProperty("deletedReferences").GetInt32() == 1 &&
                    totals.GetProperty("initiallyDisabledReferences").GetInt32() == 1,
                    "Winning deletion/disabled dispositions disappeared or an overridden reference was counted twice.");
                var rows = graph.GetProperty("rows").EnumerateArray().ToArray();
                JsonElement Row(uint id) => rows.Single(row => row.GetProperty("identity").GetString() == Key(id).ToString());
                Require(Row(0x815).GetProperty("winner").GetString() == "Overrides.esp" &&
                    Row(0x815).GetProperty("parentCell").GetString() == Key(0x802).ToString() &&
                    Row(0x814).GetProperty("projection").GetString() == "excluded-by-winning-deletion" &&
                    Row(0x820).GetProperty("failures").EnumerateArray().Any(issue => issue.GetProperty("lane").GetString() == "reference-cell-owner"),
                    "Winning ancestry/deletion was replaced by a losing source or a live child of a deleted CELL was silently dropped.");
                Require(Row(0x830).GetProperty("failures").GetArrayLength() != 0 &&
                    Row(0x821).GetProperty("disposition").GetString() == "unowned-cell-or-world-child" &&
                    totals.GetProperty("initiallyDisabledNavMeshes").GetInt32() == 1 && Row(0x822).GetProperty("failures").GetArrayLength() != 0 &&
                    Row(0x823).GetProperty("failures").EnumerateArray().Any(issue => issue.GetProperty("lane").GetString() == "land-reader"),
                    "Orphan, unknown child, disabled NAVM or malformed LAND declarations escaped source accounting.");
                Require(Row(0x812).GetProperty("projection").GetString() == "failed-scene-projection" &&
                    Row(0x810).GetProperty("projection").GetString() == "failed-scene-projection" &&
                    root.GetProperty("edges").EnumerateArray().Any(edge => edge.GetProperty("exteriorDestination").GetBoolean() &&
                        !edge.GetProperty("exteriorBoundary").GetBoolean()),
                    "A malformed CELL lost healthy sibling identities or an exterior portal remained a denominator boundary.");
                var alternatives = root.GetProperty("alternatives");
                var models = alternatives.GetProperty("resources").EnumerateArray().ToArray();
                Require(models.Any(model => model.GetProperty("path").GetString() == "meshes/creatures/a/skeleton.nif") &&
                    models.Any(model => model.GetProperty("path").GetString() == "meshes/creatures/b/skeleton.nif") &&
                    models.Any(model => model.GetProperty("path").GetString() == "meshes/creatures/b/body.nif" && model.GetProperty("failures").GetArrayLength() != 0) &&
                    models.Any(model => model.GetProperty("path").GetString() == "meshes/missing-disabled.nif" && model.GetProperty("failures").GetArrayLength() != 0),
                    "The unchosen high-level actor resource or disabled reference was replaced by a diagnostic encounter outcome.");
                Require(graph.GetProperty("invariants").EnumerateObject().All(invariant => invariant.Value.GetBoolean()) &&
                    !root.GetProperty("coverage").GetProperty("readinessPassed").GetBoolean() &&
                    root.GetProperty("coverage").GetProperty("runtimeOwnersUnverified").GetInt32() > 0,
                    "Accounting invariants failed or a source-only inventory reported native/gameplay readiness.");
                Require(Row(0x824).GetProperty("sourceSemantics").GetProperty("triangles").GetInt32() == 1,
                    "Indexed navigation source lost its original pathable mesh.");
            }
            using (var cell = Read(Path.Combine(Path.GetDirectoryName(first)!, "cell-FalloutNV-esm-000803.private.json")))
                Require(cell.RootElement.GetProperty("navMeshes").GetArrayLength() == 2 &&
                    cell.RootElement.GetProperty("navFailures").EnumerateArray().Any(issue => issue.GetProperty("lane").GetString() == "runtime-navigation-graph" &&
                        issue.GetProperty("error").GetString()!.Contains("unclassified adjacency", StringComparison.Ordinal)),
                    "Indexed source navigation bypassed the shared malformed-adjacency refusal.");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(data, Plugin)))) == pluginBefore,
                "A graph audit changed its input plugin.");
            File.WriteAllBytes(Path.Combine(data, Plugin), DenominatorFixture(true));
            using (var changed = Read(Audit("foreign-master")))
            {
                var rows = changed.RootElement.GetProperty("sourceGraph").GetProperty("rows").EnumerateArray();
                Require(rows.Single(row => row.GetProperty("identity").GetString() == Key(0x817).ToString()).GetProperty("failures").GetArrayLength() != 0 &&
                    changed.RootElement.GetProperty("sourceGraph").GetProperty("denominator").GetProperty("winningReferences").GetInt32() == 11,
                    "A changed NAME outside declaring master scope was accepted or lost its original source row.");
            }
            var emptyHeader = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(emptyHeader, 1.34f);
            File.WriteAllBytes(Path.Combine(data, Plugin), Join(Record("TES4", 0, 0, Field("HEDR", emptyHeader)), Cell(0x800, "EmptySource")));
            using (var empty = Read(Audit("empty-source", false)))
                Require(!empty.RootElement.GetProperty("coverage").GetProperty("sourceAccountingPassed").GetBoolean() &&
                    !empty.RootElement.GetProperty("coverage").GetProperty("readinessPassed").GetBoolean() &&
                    empty.RootElement.GetProperty("sourceGraph").GetProperty("denominator").GetProperty("runtimeOwnersUnverified").GetInt32() == 1 &&
                    empty.RootElement.GetProperty("alternatives").GetProperty("resources").EnumerateArray()
                        .Any(row => row.GetProperty("path").GetString() == "meshes/characters/_1stperson/skeleton.nif" &&
                            row.GetProperty("failures").GetArrayLength() != 0),
                    "An empty source CELL concealed its missing first-person input or established native/gameplay readiness.");
            Reject(() => CellGraphAudit.ParseOptions([game, "x", "--scope", "component", "--runtime-config", configuration]), "--seed");
            Reject(() => CellGraphAudit.ParseOptions([game, "x", "--scope", "universe", "--runtime-config", configuration]), "scope");
            Reject(() => CellGraphAudit.ParseOptions([game, "x", "--runtime-config", configuration, "--snapshot", "x.json"]), "--seed");
            var all = CellGraphAudit.ParseOptions([game, "x", "--runtime-config", configuration]);
            Require(all.CompleteSource && all.Seed is null, "Default all-source audit required a fabricated seed.");
            Console.WriteLine("OPENNV_CELL_GRAPH_DENOMINATOR_CONTRACT_PASS wholeWinningGraph=true exterior=true deleted=true disabled=true movedWinner=true orphan=true unknownChild=true disabledNavm=true failedCellSiblings=true unchosenActorResources=true declaringMasterRefused=true readinessUnverified=true sourceReadOnly=true");

            string Audit(string name, bool includeOverrides = true, bool compress = false)
            {
                using var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                    activePlugins: includeOverrides ? [Plugin, "Overrides.esp"] : [Plugin], archiveIniPath: archives,
                    settings: [new("TerrainManager", "fBlockLoadDistance", "8192"), new("TerrainManager", "fSplitDistanceMult", "2"),
                        new("TerrainManager", "fBlockMorphDistanceMult", "0.5")]);
                using var records = FalloutPluginStack.Load(source.PluginSources);
                if (includeOverrides)
                {
                    foreach (var firstRole in new[] { "animation", "lod-model" })
                    {
                        var reuse = new CellGraphAudit.CellAuditResources(source, .1f);
                        var initial = reuse.Read("meshes/box.nif", firstRole);
                        Require(initial.Kind == firstRole && initial.CollisionShapes == 0,
                            "The independent non-placed resource role acquired fabricated placed collision.");
                        var model = reuse.ReadModel("meshes/box.nif");
                        Require(ReferenceEquals(initial, model.Report) && model.Report.Kind == "model" && model.Report.CollisionShapes == 1 &&
                            model.Collision.Count == 12 && model.Report.InspectionRoles.SetEquals([firstRole, "model"]),
                            "Path-only resource reuse skipped the later required placed-model inspection.");
                        _ = reuse.Read("meshes/box.nif", "texture");
                        Require(model.Report.Failures.Any(issue => JsonSerializer.SerializeToElement(issue).GetProperty("lane").GetString() == "resource-role-owner"),
                            "An incompatible texture role silently reused a NIF format pass.");
                    }
                }
                var boxReads = 0;
                source.ResourceReadObserver = (logical, _, _) =>
                {
                    if (logical.Replace('\\', '/').Equals("meshes/box.nif", StringComparison.OrdinalIgnoreCase)) boxReads++;
                };
                var options = CellGraphAudit.ParseOptions([game, Path.Combine(directory, name), "--runtime-config", configuration,
                    .. compress ? new[] { "--compress-reports" } : Array.Empty<string>()]);
                Directory.CreateDirectory(options.Output);
                Require(!CellGraphAudit.RunComponent(source, records, options, .1f, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(configuration)))),
                    "Unverified whole-source audit returned readiness.");
                Require(includeOverrides ? boxReads == 1 : boxReads == 0,
                    "Shared source model was decoded/read once per cell instead of reused, or the empty source invented an asset read.");
                return Path.Combine(options.Output, "component.private.json" + (compress ? ".gz" : ""));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static byte[] DenominatorFixture(bool foreignMaster)
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var templateFlags = new byte[24]; BinaryPrimitives.WriteUInt16LittleEndian(templateFlags.AsSpan(22), 66);
        byte[] Leveled(ushort level, uint actor)
        {
            var row = new byte[12]; BinaryPrimitives.WriteUInt16LittleEndian(row, level); BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(4), actor);
            BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(8), 1); return row;
        }
        byte[] Creature(uint id, string skeleton, bool template = false, params byte[][] extra) => Record("CREA", id, 0,
            new[] { Field("ACBS", template ? templateFlags : new byte[24]), Field("MODL", Text(skeleton)), Field("BNAM", BitConverter.GetBytes(1f)) }.Concat(extra).ToArray());
        byte[] Placed(string signature, uint id, uint basis, uint flags = 0, bool malformed = false, uint? teleport = null)
        {
            var fields = new List<byte[]> { Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[malformed ? 4 : 24]) };
            if (teleport is { } target)
            { var bytes = new byte[32]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, target); fields.Add(Field("XTEL", bytes)); }
            return Record(signature, id, flags, fields.ToArray());
        }
        byte[] Navigation(uint id, bool wrongAdjacency)
        {
            var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, 0x803);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 3); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 1);
            var vertices = new byte[36]; BinaryPrimitives.WriteSingleLittleEndian(vertices.AsSpan(12), 10);
            BinaryPrimitives.WriteSingleLittleEndian(vertices.AsSpan(28), 10);
            var triangle = new byte[16]; BinaryPrimitives.WriteUInt16LittleEndian(triangle.AsSpan(2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(triangle.AsSpan(4), 2);
            BinaryPrimitives.WriteInt16LittleEndian(triangle.AsSpan(6), wrongAdjacency ? (short)9 : (short)-1);
            BinaryPrimitives.WriteInt16LittleEndian(triangle.AsSpan(8), -1); BinaryPrimitives.WriteInt16LittleEndian(triangle.AsSpan(10), -1);
            return Record("NAVM", id, 0, Field("NVER", BitConverter.GetBytes(11U)), Field("DATA", data), Field("NVVX", vertices), Field("NVTR", triangle));
        }
        return Join(Record("TES4", 0, 0, Field("HEDR", header)),
            Record("DOOR", 0x100, 0), Record("STAT", 0x101, 0, Field("MODL", Text("box.nif"))),
            Record("STAT", 0x102, 0, Field("MODL", Text("missing-disabled.nif"))),
            Creature(0x180, "creatures/own.nif", true, Field("TPLT", BitConverter.GetBytes(0x190U))),
            Creature(0x181, "creatures/a/skeleton.nif", false, Field("NIFZ", Text("body.nif"))),
            Creature(0x182, "creatures/b/skeleton.nif", false, Field("NIFZ", Text("body.nif"))),
            Record("LVLC", 0x190, 0, Field("LVLD", [25]), Field("LVLF", [0]), Field("LVLO", Leveled(1, 0x181)), Field("LVLO", Leveled(50, 0x182))),
            Cell(0x800, "Root"), Group(0x800, Placed("REFR", 0x810, 0x101), Placed("REFR", 0x811, 0x102, 0x800),
                Placed("REFR", 0x812, 0x101, malformed: true), Placed("ACRE", 0x813, 0x180), Placed("REFR", 0x814, 0x101), Placed("REFR", 0x815, 0x101)),
            Record("WRLD", 0x900, 0, Field("EDID", Text("FixtureWorld"))),
            GraphGroup(0x900, 1, Record("CELL", 0x801, 0x400, Field("DATA", [0])), Group(0x801, Placed("REFR", 0x816, 0x101)),
                Record("CELL", 0x802, 0, Field("DATA", [0]), Field("XCLC", new byte[8])),
                Group(0x802, Placed("REFR", 0x817, foreignMaster ? 0x02000100U : 0x100U, teleport: 0x818),
                    Record("ABCD", 0x821, 0), Record("NAVM", 0x822, 0x800, Field("NVER", BitConverter.GetBytes(99U)), Field("DATA", new byte[24])),
                    Record("LAND", 0x823, 0, Field("DATA", [1])))),
            Cell(0x803, "DetachedEmpty"), Group(0x803, Navigation(0x824, false), Navigation(0x825, true)),
            Cell(0x804, "PortalInterior"), Group(0x804, Placed("REFR", 0x818, 0x100, teleport: 0x817)),
            Cell(0x805, "DeletedCell"), Group(0x805, Placed("REFR", 0x820, 0x101)),
            Record("WRLD", 0x901, 0, Field("EDID", Text("DeletedWorld"))), Placed("REFR", 0x830, 0x101));
    }

    private static byte[] DenominatorOverrides()
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        return Join(Record("TES4", 0, 0, Field("HEDR", header), Field("MAST", Text(Plugin)), Field("DATA", new byte[8])),
            Group(0x800, Record("REFR", 0x814, 0x20)),
            GraphGroup(0x900, 1, Group(0x802, Reference(0x815, 0x101))),
            Record("CELL", 0x805, 0x20), Record("WRLD", 0x901, 0x20));
    }

    private static byte[] GraphGroup(uint label, int type, params byte[][] records)
    {
        var payload = Join(records); var bytes = new byte[24 + payload.Length]; "GRUP"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), label);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), type); payload.CopyTo(bytes, 24); return bytes;
    }
}
