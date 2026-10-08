using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CorpusInventoryContracts
{
    internal static void Run()
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "opennv-corpus-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            WinningBytesAndDeletion(directory);
            WinningArchiveBytes(directory);
            ArchiveCompressionRefusals(directory);
            SourceFailures(directory);
            ByteReuseAndChanges(directory);
            SelectionAdmission(directory);
            CommandRefusals(directory);
            Console.WriteLine("OPENNV_CORPUS_CONTRACT_PASS winners=effective-and-deleted resources=ordinary-winning-bytes failures=nonzero reuse=exact-original-source selection=catalog-derived decoding=uninspected runtimeReady=false");
        }
        finally
        {
            var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (!directory.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(directory).StartsWith("opennv-corpus-", StringComparison.Ordinal))
                throw new InvalidOperationException("Authored corpus fixture cleanup escaped its temporary workspace.");
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WinningArchiveBytes(string directory)
    {
        var game = Game(directory, "archive-winner");
        var output = Path.Combine(directory, "archive-winner-report");
        var before = FileHashes(game);
        using (var source = Open(game))
        using (var records = FalloutPluginStack.Load(source.PluginSources))
        {
            var reads = 0;
            source.ResourceReadObserver = (logical, _, _) => { if (logical == "textures\\shared.dds") ++reads; };
            Require(CorpusInventory.Run(records, source, output) == 0 && reads == 1,
                "The corpus did not perform the actual ordinary winning BSA payload read.");
            var row = Rows(output, "winning-resources.jsonl").Single(value => value.GetProperty("logical").GetString() == "textures\\shared.dds");
            Require(row.GetProperty("sha256").GetString() == Hash(Encoding.UTF8.GetBytes("base fixture")) &&
                row.GetProperty("readKind").GetString() == "ordinary-resource-read" &&
                row.GetProperty("decoding").GetString() == "uninspected", "Archive byte evidence became decoder acceptance.");
        }
        Require(before.SequenceEqual(FileHashes(game)), "The winning archive audit changed original authored bytes.");
    }

    private static void WinningBytesAndDeletion(string directory)
    {
        var game = Game(directory, "winning");
        var data = Path.Combine(game, "Data");
        var patch = Path.Combine(directory, "winning-patch");
        Directory.CreateDirectory(patch);
        Append(data, "FalloutNV.esm", Record("GMST", 0x42, 0, Field("EDID", Text("fCorpus")), Field("DATA", BitConverter.GetBytes(1f))),
            Record("STAT", 0x43, 0, Field("EDID", Text("RetiredFixture"))),
            Record("SCPT", 0x44, 0, Field("SCHR", new byte[20]), Field("SCDA", [0x10, 0x20]),
                Field("SCTX", Text("scn CorpusFixture\nbegin GameMode\nend\n"))));
        ModInstallationContracts.WritePlugin(patch, "Override.esp", "FalloutNV.esm");
        Append(patch, "Override.esp", Record("GMST", 0x42, 0, Field("EDID", Text("fCorpus")), Field("DATA", BitConverter.GetBytes(2f))),
            Record("STAT", 0x43, FalloutPluginRecord.DeletedFlag, Field("EDID", Text("RetiredFixture"))));
        ModInstallationContracts.WritePlugin(data, "Inactive.esp");
        ModContentContracts.WriteArchive(Path.Combine(patch, "Override-Main.bsa"), "later archive fixture");
        ModContentContracts.WriteArchive(Path.Combine(data, "Unselected.bsa"), "inactive container fixture");
        var texture = Path.Combine(patch, "Textures");
        Directory.CreateDirectory(texture);
        File.WriteAllBytes(Path.Combine(texture, "SHARED.DDS"), [7, 5, 3, 1]);
        var unknownDirectory = Path.Combine(patch, "novel-format", "inactive-arm");
        Directory.CreateDirectory(unknownDirectory);
        File.WriteAllBytes(Path.Combine(unknownDirectory, "body.bin"), [11, 22, 33]);
        File.WriteAllText(Path.Combine(data, "unlisted.txt"), "complete top-level bytes");
        var output = Path.Combine(directory, "winning-report");
        var before = FileHashes(game, patch);
        using (var content = Open(game, [patch], ["Override.esp"]))
        using (var records = FalloutPluginStack.Load(content.PluginSources))
        {
            Require(CorpusInventory.Run(records, content, output) == 0, "A valid byte/layout inventory failed.");
            Require(records.GetEffective(new("FalloutNV.esm", 0x42)).Plugin.Name == "Override.esp", "The fixture did not reach the actual winner owner.");
            Require(records.TryGetWinner(new("FalloutNV.esm", 0x43), out var deleted) && deleted.IsDeleted,
                "The fixture did not retain the actual deletion winner.");
            var winnerRows = Rows(output, "winning-records.jsonl");
            Require(winnerRows.Length == 3 && winnerRows.Count(row => row.GetProperty("owner").GetProperty("disposition").GetString() == "deleted") == 1,
                "Deleted winners were omitted or duplicated in the corpus denominator.");
            var scalar = winnerRows.Single(row => row.GetProperty("owner").GetProperty("identity").GetString() == "FalloutNV.esm:000042");
            Require(scalar.GetProperty("owner").GetProperty("winningPlugin").GetString() == "Override.esp" &&
                scalar.GetProperty("payloadSha256").GetString() == Hash(records.GetEffective(new("FalloutNV.esm", 0x42)).ReadData()),
                "The corpus bytes came from a losing override.");
            var resourceRows = Rows(output, "winning-resources.jsonl");
            var shared = resourceRows.Single(row => row.GetProperty("logical").GetString() == "textures\\shared.dds");
            Require(shared.GetProperty("winningSource").GetString() == Path.Combine(texture, "SHARED.DDS") &&
                shared.GetProperty("sha256").GetString() == Hash([7, 5, 3, 1]), "The corpus bypassed ordinary loose/BSA precedence.");
            Require(resourceRows.Any(row => row.GetProperty("logical").GetString() == "novel-format\\inactive-arm\\body.bin") &&
                resourceRows.Any(row => row.GetProperty("logical").GetString() == "unlisted.txt") &&
                resourceRows.Any(row => row.GetProperty("logical").GetString() == "inactive.esp") &&
                resourceRows.Any(row => row.GetProperty("logical").GetString() == "unselected.bsa"),
                "A directory whitelist or active-container filter silently narrowed loose byte coverage.");
            Require(Rows(output, "archive-stored-extents.jsonl").Length == 2, "Selected BSA stored extents were replaced by winning-byte counts.");
            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "summary.json")));
            Require(!summary.RootElement.GetProperty("runtimeReady").GetBoolean() &&
                summary.RootElement.GetProperty("resourceDecoding").GetString() == "uninspected" &&
                shared.GetProperty("decoding").GetString() == "uninspected", "Authored invalid DDS bytes became decoding or runtime acceptance.");
            var retained = Hash(File.ReadAllBytes(Path.Combine(output, "summary.json")));
            Reject(() => CorpusInventory.Run(records, content, output));
            Reject(() => CorpusInventory.Run(records, content, Path.Combine(data, "forbidden-output")));
            Require(retained == Hash(File.ReadAllBytes(Path.Combine(output, "summary.json"))) && !Directory.Exists(Path.Combine(data, "forbidden-output")),
                "Fresh evidence or owned-input output protection failed.");
        }
        Require(before.SequenceEqual(FileHashes(game, patch)), "The byte audit changed authored original input files.");
    }

    private static string Game(string directory, string name)
    {
        var game = Path.Combine(directory, name);
        var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(data);
        ModInstallationContracts.WritePlugin(data, "FalloutNV.esm");
        ModContentContracts.WriteArchive(Path.Combine(data, "FalloutNV.bsa"), "base fixture");
        File.WriteAllText(Path.Combine(game, "archives.ini"), "[Archive]\nsArchiveList=FalloutNV.bsa\n");
        return game;
    }

    private static RuntimeLiveContentSource Open(string game, string[]? roots = null, string[]? plugins = null) =>
        RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame, roots,
            plugins ?? ["FalloutNV.esm"], Path.Combine(game, "archives.ini"));

    private static void Append(string directory, string plugin, params byte[][] records)
    {
        using var stream = new FileStream(Path.Combine(directory, plugin), FileMode.Append, FileAccess.Write);
        foreach (var record in records) stream.Write(record);
    }

    private static byte[] Record(string signature, uint id, uint flags, params byte[][] fields)
    {
        var payload = fields.SelectMany(field => field).ToArray();
        var result = new byte[FalloutPlugin.RecordHeaderSize + payload.Length];
        Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id);
        payload.CopyTo(result, FalloutPlugin.RecordHeaderSize);
        return result;
    }

    private static byte[] Field(string signature, byte[] payload)
    {
        var result = new byte[6 + payload.Length];
        Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)payload.Length));
        payload.CopyTo(result, 6);
        return result;
    }

    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value + "\0");
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string[] FileHashes(params string[] roots) => roots.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        .Order(StringComparer.Ordinal).Select(path => path + ":" + Hash(File.ReadAllBytes(path))).ToArray();
    private static JsonElement[] Rows(string directory, string name) => File.ReadLines(Path.Combine(directory, name)).Select(line =>
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }).ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or NotSupportedException) { return; }
        throw new InvalidOperationException("Corpus source refusal was not preserved.");
    }
}
