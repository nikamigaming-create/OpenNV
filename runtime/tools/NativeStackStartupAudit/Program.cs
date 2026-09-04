using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

const string NewVegasGame = "fallout-new-vegas";
const string Fallout3Game = "fallout-3";
const string NewVegasMaster = "FalloutNV.esm";
const string Fallout3Master = "Fallout3.esm";
const uint NewVegasInitialCell = 0x103df9;
const uint Fallout3InitialCell = 0x28138;
const double MaximumManifestMilliseconds = 500.0;
const double MaximumHeaderScanMilliseconds = 5_000.0;
const double MaximumWinnerMilliseconds = 3_000.0;
const double MaximumInitialCellMilliseconds = 2_000.0;
const double MaximumFirstResourceMilliseconds = 2_000.0;
const double MaximumTotalMilliseconds = 15_000.0;

if (args.Length is not 5 and not 6 and not 7 and not 8 ||
    args[0] is not ("--source-stack" or "--source-stack-stdin") ||
    args[0] == "--source-stack" && args.Length is not 6 and not 8 ||
    args[0] == "--source-stack-stdin" && args.Length is not 5 and not 7)
    throw new ArgumentException(
        "Usage: NativeStackStartupAudit --source-stack <json> --edition <name> --index-mode eager|demand " +
        "or --source-stack-stdin --edition <name> --index-mode eager|demand " +
        "[--bsa-mode sequential|offset]");

var cursor = 1;
var total = Stopwatch.StartNew();
var phase = Stopwatch.StartNew();
var manifestBytes = args[0] == "--source-stack"
    ? File.ReadAllBytes(Path.GetFullPath(args[cursor++]))
    : Encoding.UTF8.GetBytes(Console.In.ReadToEnd());
if (args[cursor++] != "--edition") throw new ArgumentException("Missing --edition.");
var edition = args[cursor++];
if (args[cursor++] != "--index-mode") throw new ArgumentException("Missing --index-mode.");
var indexMode = args[cursor];
var eager = indexMode switch
{
    "eager" => true,
    "demand" => false,
    _ => throw new ArgumentException("--index-mode must be eager or demand."),
};
var bsaMode = "sequential";
if (cursor + 1 < args.Length)
{
    if (args[++cursor] != "--bsa-mode") throw new ArgumentException("Unexpected startup audit option.");
    bsaMode = args[++cursor];
}
var useOffsetBsaDirectory = bsaMode switch
{
    "sequential" => false,
    "offset" => true,
    _ => throw new ArgumentException("--bsa-mode must be sequential or offset."),
};

var manifest = ValidatedManifest.Load(manifestBytes, useOffsetBsaDirectory);
phase.Stop();
var manifestMilliseconds = phase.Elapsed.TotalMilliseconds;

phase.Restart();
using var stack = FalloutPluginStack.Load(
    manifest.PluginSources,
    eager,
    out var loadMetrics);
phase.Stop();
var stackMilliseconds = phase.Elapsed.TotalMilliseconds;

phase.Restart();
var cellKey = manifest.Game switch
{
    NewVegasGame => new FalloutFormKey(NewVegasMaster, NewVegasInitialCell),
    Fallout3Game => new FalloutFormKey(Fallout3Master, Fallout3InitialCell),
    _ => throw new InvalidDataException($"Unsupported native game {manifest.Game}."),
};
var cell = FalloutCellSceneReader.Read(stack, cellKey);
phase.Stop();
var cellMilliseconds = phase.Elapsed.TotalMilliseconds;

phase.Restart();
var firstModel = cell.BaseObjects.Values
    .Where(value => value.ModelPath is not null)
    .OrderBy(value => stack.RuntimeFormId(value.FormKey))
    .FirstOrDefault() ?? throw new InvalidDataException("Initial CELL has no model resource demand.");
if (!manifest.Resources.TryResolve(firstModel.ModelPath!, out var resourceSource))
    throw new FileNotFoundException($"Initial CELL model is missing: {firstModel.ModelPath}");
phase.Stop();
total.Stop();

if (manifestMilliseconds > MaximumManifestMilliseconds ||
    loadMetrics.PluginHeaderScan.TotalMilliseconds > MaximumHeaderScanMilliseconds ||
    loadMetrics.WinnerConstruction.TotalMilliseconds > MaximumWinnerMilliseconds ||
    cellMilliseconds > MaximumInitialCellMilliseconds ||
    phase.Elapsed.TotalMilliseconds > MaximumFirstResourceMilliseconds ||
    total.Elapsed.TotalMilliseconds > MaximumTotalMilliseconds)
    throw new InvalidDataException(
        $"Native startup exceeded a bounded no-cache phase budget: totalMs={total.Elapsed.TotalMilliseconds:F3}.");

static string Ms(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

Console.WriteLine(
    $"OPENNV_NATIVE_STARTUP_AUDIT_PASS edition={edition} mode={indexMode} " +
    $"stackId={manifest.StackId} " +
    $"plugins={stack.Plugins.Count} winners={stack.WinnerRecordCount} effective={stack.EffectiveRecordCount} " +
    $"manifestMs={Ms(manifestMilliseconds)} headerScanMs={Ms(loadMetrics.PluginHeaderScan.TotalMilliseconds)} " +
    $"winnerMs={Ms(loadMetrics.WinnerConstruction.TotalMilliseconds)} stackMs={Ms(stackMilliseconds)} " +
    $"cellMs={Ms(cellMilliseconds)} firstResourceMs={Ms(phase.Elapsed.TotalMilliseconds)} " +
    $"bsaMode={bsaMode} bsaOpened={manifest.Resources.OpenedArchiveCount} " +
    $"bsaFolders={manifest.Resources.OpenedFolderCount} bsaFiles={manifest.Resources.OpenedFileCount} " +
    $"bsaFolderPayloadReads={manifest.Resources.DirectoryTableReadOperations} " +
    $"bsaDirectoryBytes={manifest.Resources.DirectoryTableBytes} " +
    $"totalMs={Ms(total.Elapsed.TotalMilliseconds)} cell={cell.Cell.FormKey} references={cell.References.Count} " +
    $"resource={firstModel.ModelPath} source={resourceSource} cache=none writes=0");

internal sealed record ValidatedManifest(
    string Game,
    string StackId,
    IReadOnlyList<FalloutPluginSource> PluginSources,
    ManifestResources Resources)
{
    internal static ValidatedManifest Load(byte[] bytes, bool useOffsetBsaDirectory)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "opennv-mod-stack/v2" ||
            root.GetProperty("status").GetString() != "registered-read-only-source-stack")
            throw new InvalidDataException("Startup audit requires a registered source stack.");
        var game = root.GetProperty("game").GetString() ?? string.Empty;
        var stackId = root.GetProperty("stackId").GetString() ?? string.Empty;
        var edition = root.GetProperty("edition").GetString() ?? string.Empty;
        if (root.GetProperty("saveCompatibilityId").GetString() != $"{edition}:{stackId}")
            throw new InvalidDataException("Startup audit requires a stack-scoped v2 save identity.");
        var roots = root.GetProperty("roots").EnumerateArray().ToDictionary(
            row => row.GetProperty("id").GetString()!,
            row => Path.GetFullPath(row.GetProperty("root").GetString()!),
            StringComparer.Ordinal);
        foreach (var path in roots.Values)
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        var plugins = root.GetProperty("plugins").EnumerateArray().Select(row =>
        {
            var file = row.GetProperty("file").GetString()!;
            var path = Path.Combine(roots[row.GetProperty("rootId").GetString()!], file);
            ValidateFile(row, path, allowEmpty: false);
            return new FalloutPluginSource(
                file,
                path,
                row.GetProperty("bytes").GetInt64(),
                row.GetProperty("mtimeMs").GetInt64());
        }).ToArray();
        return new ValidatedManifest(
            game,
            stackId,
            plugins,
            new ManifestResources(root, roots, useOffsetBsaDirectory));
    }

    internal static void ValidateFile(JsonElement row, string path, bool allowEmpty)
    {
        var info = new FileInfo(path);
        var bytes = row.GetProperty("bytes").GetInt64();
        var mtime = row.GetProperty("mtimeMs").GetInt64();
        var actualMtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
        if (!info.Exists || bytes < 0 || !allowEmpty && bytes == 0 ||
            info.Length != bytes || actualMtime != mtime)
            throw new InvalidDataException($"Registered source changed: {path}");
    }
}

internal sealed class ManifestResources
{
    private readonly Dictionary<string, string> _loose = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _archives = [];
    private readonly Dictionary<string, FalloutBsaArchive> _opened =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _useOffsetDirectory;

    internal int OpenedArchiveCount => _opened.Count;
    internal int OpenedFolderCount => _opened.Values.Sum(archive => archive.FolderCount);
    internal int OpenedFileCount => _opened.Values.Sum(archive => archive.FileCount);
    internal int DirectoryTableReadOperations =>
        _opened.Values.Sum(archive => archive.DirectoryTableReadOperations);
    internal long DirectoryTableBytes => _opened.Values.Sum(archive => archive.DirectoryTableBytes);

    internal ManifestResources(
        JsonElement root,
        IReadOnlyDictionary<string, string> roots,
        bool useOffsetDirectory)
    {
        _useOffsetDirectory = useOffsetDirectory;
        foreach (var row in root.GetProperty("looseFiles").EnumerateArray())
        {
            var logical = FalloutBsaArchive.CanonicalPath(row.GetProperty("path").GetString()!);
            var path = Path.Combine(
                roots[row.GetProperty("rootId").GetString()!],
                row.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            ValidatedManifest.ValidateFile(row, path, allowEmpty: true);
            _loose[logical] = path;
        }
        foreach (var row in root.GetProperty("archives").EnumerateArray())
        {
            var path = Path.Combine(
                roots[row.GetProperty("rootId").GetString()!],
                row.GetProperty("file").GetString()!);
            ValidatedManifest.ValidateFile(row, path, allowEmpty: false);
            _archives.Add(path);
        }
    }

    internal bool TryResolve(string logicalPath, out string source)
    {
        var canonical = FalloutBsaArchive.CanonicalPath(logicalPath);
        if (_loose.TryGetValue(canonical, out source!)) return true;
        for (var index = _archives.Count - 1; index >= 0; --index)
        {
            if (!_opened.TryGetValue(_archives[index], out var archive))
            {
                archive = new FalloutBsaArchive(_archives[index], _useOffsetDirectory);
                _opened.Add(_archives[index], archive);
            }
            if (!archive.Contains(canonical)) continue;
            source = $"{_archives[index]}::{canonical}";
            return true;
        }
        source = string.Empty;
        return false;
    }
}
