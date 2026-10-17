using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAuditContracts
{
    private static void AlternativeQueryMemoization()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-cell-query-memo-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(directory, "Game"); var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(data);
        try
        {
            var pluginPath = Path.Combine(data, Plugin);
            var archivePath = Path.Combine(data, "FalloutNV.bsa");
            File.WriteAllBytes(pluginPath, AlternativeQueriesFixture());
            ModContentContracts.WriteArchive(archivePath, "unused first-party fixture member");
            var archiveIni = Path.Combine(directory, "archives.ini");
            File.WriteAllText(archiveIni, "[Archive]\nsArchiveList=FalloutNV.bsa\n");
            var defaultsPath = Path.Combine(game, "Fallout_default.ini");
            File.WriteAllText(defaultsPath, "[TerrainManager]\nfBlockLoadDistance=8192\nfSplitDistanceMult=2\nfBlockMorphDistanceMult=0.5\n[Landscape]\nSDefaultLandDiffuseTexture=fixture.dds\nSDefaultLandNormalTexture=fixture.dds\n");
            var inputFiles = new List<string> { pluginPath, archivePath, archiveIni, defaultsPath };
            foreach (var world in new[] { "Shared", "Other" })
            {
                var path = Path.Combine(data, "meshes", "landscape", "lod", world, world + ".level1.x0.y0.nif");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, NifFixture()); inputFiles.Add(path);
            }
            var before = inputFiles.ToDictionary(path => path, QueryInputHash);
            using (var source = Open())
            using (var records = FalloutPluginStack.Load(source.PluginSources))
            {
                var queries = new CellGraphAudit.AlternativeSourceQueries(source, records);
                var missing = QueryRefusal(() => queries.PersistentCell(Key(0x101)));
                var missingAgain = QueryRefusal(() => queries.PersistentCell(Key(0x101)));
                var ambiguous = QueryRefusal(() => queries.PersistentCell(Key(0x102)));
                var ambiguousAgain = QueryRefusal(() => queries.PersistentCell(Key(0x102)));
                Require(missing is InvalidDataException && ReferenceEquals(missing, missingAgain) &&
                    missing.Message == $"World {Key(0x101)} has 0 persistent cells." &&
                    ambiguous is InvalidDataException && ReferenceEquals(ambiguous, ambiguousAgain) &&
                    ambiguous.Message == $"World {Key(0x102)} has 2 persistent cells.",
                    "A repeated persistent-cell refusal was recomputed, substituted or changed its declaring WRLD.");
                Require(queries.PersistentCell(Key(0x103)) == Key(0x820) && queries.PersistentCell(Key(0x103)) == Key(0x820) &&
                    queries.PersistentCell(Key(0x100)) == Key(0x800) && queries.Reuse.PersistentCellQueries == 4 &&
                    queries.Reuse.PersistentCellEntries == 4,
                    "A valid unrelated persistent owner was lost, inherited from a parent or rescanned after caching.");
                Require(FalloutExteriorLod.WorldName(records, Key(0x100)) == "Shared" &&
                    FalloutExteriorLod.WorldName(records, Key(0x101)) == "Shared" &&
                    FalloutExteriorLod.WorldName(records, Key(0x102)) == "Shared",
                    "The fixture's actual WRLD flags did not author the shared inherited LOD prefix.");
                var shared = queries.ResourcePathsUnder("meshes/landscape/lod/Shared");
                var repeated = queries.ResourcePathsUnder("MESHES\\LANDSCAPE\\LOD\\SHARED\\");
                var other = queries.ResourcePathsUnder("meshes/landscape/lod/Other");
                Require(ReferenceEquals(shared, repeated) && shared.SequenceEqual(["meshes\\landscape\\lod\\shared\\shared.level1.x0.y0.nif"]) &&
                    other.SequenceEqual(["meshes\\landscape\\lod\\other\\other.level1.x0.y0.nif"]) &&
                    queries.Reuse.ResourceDirectoryQueries == 2 && queries.Reuse.ResourceDirectoryEntries == 2 &&
                    queries.Reuse.StackId == source.StackId && queries.Reuse.SaveCompatibilityId == source.SaveCompatibilityId,
                    "Prefix reuse changed the original winning logical paths, selection identity or unrelated directory result.");
                try { ((IList<string>)shared)[0] = "invented.nif"; throw new InvalidDataException("A cached directory result remained mutable."); }
                catch (NotSupportedException) { }
                var absent = queries.ResourcePathsUnder("meshes/landscape/lod/Absent");
                Require(absent.Count == 0 && ReferenceEquals(absent, queries.ResourcePathsUnder("MESHES/landscape/lod/ABSENT")) &&
                    queries.Reuse.ResourceDirectoryQueries == 3 && queries.Reuse.ResourceDirectoryEntries == 3,
                    "An empty authored prefix was rescanned or replaced with a fabricated resource path.");
                Require(source.TryResolve(shared[0], null, out var owner) &&
                    owner == inputFiles.Single(path => path.EndsWith("Shared.level1.x0.y0.nif", StringComparison.Ordinal)),
                    "The reused logical dependency no longer resolves to its original selected input owner.");
                var scans = queries.Reuse.ResourceDirectoryQueries;
                Reject(() => queries.ResourcePathsUnder("meshes/../outside"), "escapes");
                Require(queries.Reuse.ResourceDirectoryQueries == scans, "An invalid namespace reached the source directory scan.");
                using var foreignSource = Open();
                Reject(() => new CellGraphAudit.AlternativeSourceQueries(foreignSource, records), "exact owned source instance");
                using var foreignRecords = FalloutPluginStack.Load(foreignSource.PluginSources);
                var independent = new CellGraphAudit.AlternativeSourceQueries(foreignSource, foreignRecords);
                _ = QueryRefusal(() => independent.PersistentCell(Key(0x101)));
                Require(independent.Reuse.PersistentCellQueries == 1 && independent.Reuse.ResourceDirectoryQueries == 0,
                    "A separate source instance reused prior failures or query accounting.");
            }

            var configuration = Path.Combine(directory, "runtime.json");
            File.WriteAllText(configuration, "{\"schema\":\"opennv-runtime-configuration/v1\",\"world\":{\"gameUnitsToMeters\":0.1}}");
            var output = Path.Combine(directory, "audit");
            using (var source = Open())
            using (var records = FalloutPluginStack.Load(source.PluginSources))
            {
                var options = CellGraphAudit.ParseOptions([game, output, "--runtime-config", configuration, "--compress-reports"]);
                Directory.CreateDirectory(output);
                Require(!CellGraphAudit.RunComponent(source, records, options, .1f, QueryInputHash(configuration)),
                    "Query reuse converted source errors or unknown native owners into readiness.");
                using var input = File.OpenRead(Path.Combine(output, "component.private.json.gz"));
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var report = JsonDocument.Parse(gzip);
                var root = report.RootElement;
                var alternatives = root.GetProperty("alternatives");
                var reuse = alternatives.GetProperty("queryReuse");
                Require(reuse.GetProperty("persistentCellQueries").GetInt32() == 3 && reuse.GetProperty("persistentCellEntries").GetInt32() == 3 &&
                    reuse.GetProperty("resourceDirectoryQueries").GetInt32() == 2 && reuse.GetProperty("resourceDirectoryEntries").GetInt32() == 2 &&
                    reuse.GetProperty("stackId").GetString() == source.StackId && reuse.GetProperty("saveCompatibilityId").GetString() == source.SaveCompatibilityId,
                    "The actual alternative inventory still rescanned a failing world or an inherited directory.");
                var worlds = alternatives.GetProperty("worldspaces").EnumerateArray().ToArray();
                Require(worlds.Length == 4 && worlds.Where(world => world.GetProperty("lodName").GetString() == "Shared")
                    .All(world => world.GetProperty("resources").EnumerateArray().Select(value => value.GetString()).SequenceEqual(
                        new[] { "meshes/landscape/lod/shared/shared.level1.x0.y0.nif" })) &&
                    worlds.Count(world => world.GetProperty("lodName").GetString() == "Shared") == 3,
                    "Shared directory memoization dropped a per-world resource dependency row.");
                var failures = alternatives.GetProperty("failures").EnumerateArray().Where(row =>
                    row.TryGetProperty("lane", out var lane) && lane.GetString() == "land-reader").ToArray();
                var graphRows = root.GetProperty("sourceGraph").GetProperty("rows").EnumerateArray().ToArray();
                Require(failures.Length == 6 && root.GetProperty("sourceGraph").GetProperty("denominator").GetProperty("winningCells").GetInt32() == 10,
                    "Memoization removed an affected LAND or unrelated source CELL from the complete denominator.");
                for (uint index = 1; index <= 6; index++)
                {
                    var id = Key(0xa00 + index).ToString();
                    var error = failures.Single(row => row.GetProperty("landscape").GetString() == id).GetProperty("error").GetString();
                    var expected = index <= 2 ? $"World {Key(0x101)} has 0 persistent cells." :
                        index <= 4 ? $"World {Key(0x102)} has 2 persistent cells." : null;
                    Require(expected is null ? error!.Contains("DATA must contain one uint32 flag field", StringComparison.Ordinal) : error == expected,
                        "A cached refusal changed its LAND owner or suppressed an unrelated downstream source-reader failure.");
                    Require(graphRows.Single(row => row.GetProperty("identity").GetString() == id).GetProperty("failures").EnumerateArray()
                        .Any(row => row.GetProperty("lane").GetString() == "land-reader" && row.GetProperty("error").GetString() == error),
                        "The original per-LAND graph failure disappeared behind shared query metadata.");
                }
                Require(!root.GetProperty("coverage").GetProperty("readinessPassed").GetBoolean(), "A complete inventory invented native readiness.");
            }
            Require(inputFiles.All(path => QueryInputHash(path) == before[path]), "Query reuse modified its authored source inputs.");

            // Actual selected BSA warmup failure, not a fabricated query delegate.
            var badGame = Path.Combine(directory, "BadGame"); var badData = Path.Combine(badGame, "Data");
            Directory.CreateDirectory(badData);
            File.WriteAllBytes(Path.Combine(badData, Plugin), AlternativeQueriesFixture());
            File.WriteAllBytes(Path.Combine(badGame, "Fallout_default.ini"), File.ReadAllBytes(defaultsPath));
            var badArchive = Path.Combine(badData, "FalloutNV.bsa"); File.WriteAllBytes(badArchive, [0, 1, 2, 3]);
            var badBefore = QueryInputHash(badArchive);
            using (var source = RuntimeLiveContentSource.Open(badGame, RuntimeLiveContentSource.FalloutNewVegasGame,
                activePlugins: [Plugin], archiveIniPath: archiveIni,
                settings: [new("Landscape", "SDefaultLandDiffuseTexture", "fixture.dds"),
                    new("Landscape", "SDefaultLandNormalTexture", "fixture.dds")]))
            using (var records = FalloutPluginStack.Load(source.PluginSources))
            {
                var queries = new CellGraphAudit.AlternativeSourceQueries(source, records);
                var failed = QueryRefusal(() => queries.ResourcePathsUnder("meshes/landscape/lod/Shared"));
                var failedAgain = QueryRefusal(() => queries.ResourcePathsUnder("MESHES\\LANDSCAPE\\LOD\\SHARED"));
                Require(ReferenceEquals(failed, failedAgain) && failed.ToString().Contains("Not a Fallout BSA archive", StringComparison.Ordinal) &&
                    failed.ToString().Contains(badArchive, StringComparison.Ordinal) && queries.Reuse.ResourceDirectoryQueries == 1 &&
                    queries.Reuse.ResourceDirectoryEntries == 1 && queries.PersistentCell(Key(0x103)) == Key(0x820),
                    "A failed source directory scan was retried, lost its original BSA owner or contaminated an unrelated record query.");
                var failedOutput = Path.Combine(directory, "failed-archive-audit");
                var options = CellGraphAudit.ParseOptions([badGame, failedOutput, "--runtime-config", configuration, "--compress-reports"]);
                Directory.CreateDirectory(failedOutput);
                Require(!CellGraphAudit.RunComponent(source, records, options, .1f, QueryInputHash(configuration)),
                    "A refused archive directory was reported as native readiness.");
                using var input = File.OpenRead(Path.Combine(failedOutput, "component.private.json.gz"));
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var report = JsonDocument.Parse(gzip);
                var alternatives = report.RootElement.GetProperty("alternatives");
                var worlds = alternatives.GetProperty("worldspaces").EnumerateArray().ToArray();
                Require(worlds.Length == 4 && worlds.All(world => world.GetProperty("errors").EnumerateArray().Any(issue =>
                    issue.GetProperty("lane").GetString() == "world-lod-declarations" &&
                    issue.GetProperty("error").GetString()!.Contains(badArchive, StringComparison.Ordinal))) &&
                    alternatives.GetProperty("failures").EnumerateArray().Count(row => row.TryGetProperty("world", out _)) == 4 &&
                    alternatives.GetProperty("queryReuse").GetProperty("resourceDirectoryQueries").GetInt32() == 2,
                    "Cached BSA refusals removed a declaring world failure or repeatedly scanned its inherited directory.");
            }
            Require(QueryInputHash(badArchive) == badBefore, "Refused archive inventory changed its malformed first-party input.");
            Console.WriteLine("OPENNV_CELL_GRAPH_ALTERNATIVE_QUERY_MEMO_PASS originalPersistentSuccess=true originalMissingRefusal=true originalAmbiguousRefusal=true inheritedPrefix=true perWorldDependencies=true perLandFailures=true downstreamFailure=true sourceInstanceBound=true immutableResults=true actualArchiveFailure=true compressedDenominator=true sourceReadOnly=true readinessUnverified=true");

            RuntimeLiveContentSource Open() => RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                activePlugins: [Plugin], archiveIniPath: archiveIni,
                settings: [new("TerrainManager", "fBlockLoadDistance", "8192"), new("TerrainManager", "fSplitDistanceMult", "2"),
                    new("TerrainManager", "fBlockMorphDistanceMult", "0.5"), new("Landscape", "SDefaultLandDiffuseTexture", "fixture.dds"),
                    new("Landscape", "SDefaultLandNormalTexture", "fixture.dds")]);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static Exception QueryRefusal(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or AggregateException) { return error; }
        throw new InvalidDataException("An original source query refusal was silently accepted.");
    }

    private static string QueryInputHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static byte[] AlternativeQueriesFixture()
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        byte[] World(uint id, string editor, uint? parent = null) => parent is { } owner
            ? Record("WRLD", id, 0, Field("EDID", Text(editor)), Field("WNAM", BitConverter.GetBytes(owner)), Field("PNAM", [2, 0]))
            : Record("WRLD", id, 0, Field("EDID", Text(editor)));
        byte[] Persistent(uint id) => Record("CELL", id, 0x400, Field("DATA", [0]));
        byte[] LandCell(uint cell, uint land, int coordinate)
        {
            var coordinates = new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(coordinates, coordinate);
            return Join(Record("CELL", cell, 0, Field("DATA", [0]), Field("XCLC", coordinates)),
                Group(cell, Record("LAND", land, 0, Field("DATA", [1]))));
        }
        return Join(Record("TES4", 0, 0, Field("HEDR", header)),
            World(0x100, "Shared"), GraphGroup(0x100, 1, Persistent(0x800)),
            World(0x101, "Missing", 0x100), GraphGroup(0x101, 1, LandCell(0x901, 0xa01, 0), LandCell(0x902, 0xa02, 1)),
            World(0x102, "Ambiguous", 0x100), GraphGroup(0x102, 1, Persistent(0x810), Persistent(0x811),
                LandCell(0x903, 0xa03, 0), LandCell(0x904, 0xa04, 1)),
            World(0x103, "Other"), GraphGroup(0x103, 1, Persistent(0x820), LandCell(0x905, 0xa05, 0), LandCell(0x906, 0xa06, 1)));
    }
}
