using System.Text.Json;
using OpenNV.Runtime.Content;

if (args.Length is not 1 and not 2 ||
    args.Length == 2 && args[0] != "--source-stack" ||
    args.Length == 1 && args[0] != "--source-stack-stdin")
    throw new ArgumentException(
        "Usage: FalloutActorCellLedgerProbe --source-stack <mod-stack.json> | --source-stack-stdin");

var manifestBytes = args.Length == 2
    ? File.ReadAllBytes(Path.GetFullPath(args[1]))
    : System.Text.Encoding.UTF8.GetBytes(Console.In.ReadToEnd());
using var document = JsonDocument.Parse(manifestBytes);
var root = document.RootElement;
if (root.GetProperty("schema").GetString() != "opennv-mod-stack/v2" ||
    root.GetProperty("status").GetString() != "registered-read-only-source-stack")
    throw new InvalidDataException("Actor-cell ledger requires a registered native mod stack.");
var stackId = root.GetProperty("stackId").GetString() ?? string.Empty;
var stackEdition = root.GetProperty("edition").GetString() ?? string.Empty;
if (root.GetProperty("saveCompatibilityId").GetString() != $"{stackEdition}:{stackId}")
    throw new InvalidDataException("Actor-cell ledger requires a stack-scoped v2 save identity.");
var game = root.GetProperty("game").GetString() ?? string.Empty;
var roots = root.GetProperty("roots").EnumerateArray().ToDictionary(
    row => row.GetProperty("id").GetString()!,
    row => Path.GetFullPath(row.GetProperty("root").GetString()!),
    StringComparer.Ordinal);
var sources = root.GetProperty("plugins").EnumerateArray().Select(row =>
{
    var rootId = row.GetProperty("rootId").GetString()!;
    var file = row.GetProperty("file").GetString()!;
    return new FalloutPluginSource(
        file,
        Path.Combine(roots[rootId], file),
        row.GetProperty("bytes").GetInt64(),
        row.GetProperty("mtimeMs").GetInt64());
}).ToArray();

using var stack = FalloutPluginStack.Load(sources);
var ownedSource = new ManifestActorResourceSource(root, roots);
var cells = stack.EffectiveRecords("CELL").ToDictionary(record => record.FormKey);
var rows = new Dictionary<FalloutFormKey, PlacementCounts>();
var actorPlacements = 0;
var creaturePlacements = 0;
foreach (var signature in new[] { "ACHR", "ACRE" })
{
    foreach (var placement in stack.EffectiveRecords(signature))
    {
        var parent = FalloutCellSceneReader.ParentCell(placement) ??
            throw new InvalidDataException($"Effective {signature} {placement.FormKey} has no parent CELL group.");
        if (!cells.ContainsKey(parent))
            throw new InvalidDataException(
                $"Effective {signature} {placement.FormKey} points to missing/deleted CELL {parent}.");
        if (!rows.TryGetValue(parent, out var counts))
            counts = new PlacementCounts();
        if (signature == "ACHR")
        {
            counts = counts with { Actors = counts.Actors + 1 };
            actorPlacements++;
        }
        else
        {
            counts = counts with { Creatures = counts.Creatures + 1 };
            creaturePlacements++;
        }
        rows[parent] = counts;
    }
}

var ordered = rows.OrderBy(pair => stack.RuntimeFormId(pair.Key)).ToArray();
if (ordered.Length == 0 || actorPlacements == 0 || creaturePlacements == 0)
    throw new InvalidDataException("The registered stack has no complete actor/creature CELL denominator.");
Console.WriteLine(
    $"OPENNV_ACTOR_CELL_LEDGER_PASS plugins={stack.Plugins.Count} cells={cells.Count} " +
    $"populationCells={ordered.Length} actors={actorPlacements} creatures={creaturePlacements} " +
    $"mixedCells={ordered.Count(pair => pair.Value.Actors > 0 && pair.Value.Creatures > 0)} " +
    $"stackId={stackId} source=live-owned-stack cache=none");

var renderLedger = FalloutActorCreatureLedgerBuilder.Build(
    stack,
    game,
    ownedSource.TryResolve);
var edition = game == RuntimeOwnedContentSource.Fallout3Game
    ? "standalone-fo3"
    : root.TryGetProperty("orderSource", out var orderSource) &&
        orderSource.ValueKind == JsonValueKind.Object &&
        orderSource.GetProperty("kind").GetString() == "ttw-profile"
        ? "ttw"
        : "standalone-fnv";
var blockers = renderLedger.Rows.Where(row => row.Blocker is not null)
    .GroupBy(row => row.Blocker!, StringComparer.Ordinal)
    .OrderBy(group => group.Key, StringComparer.Ordinal)
    .ToArray();
Console.WriteLine(
    $"OPENNV_ACTOR_RENDER_READINESS_LEDGER edition={edition} plugins={stack.Plugins.Count} " +
    $"cells={renderLedger.EffectiveCells} populationCells={renderLedger.CellsWithActors} " +
    $"achr={renderLedger.HumanoidReferences} acre={renderLedger.CreatureReferences} " +
    $"npcBases={renderLedger.UniqueHumanoidBases} creatureBases={renderLedger.UniqueCreatureBases} " +
    $"disabled={renderLedger.InitiallyDisabledReferences} modelResolved={renderLedger.ModelResolvedReferences} " +
    $"modelMissing={renderLedger.ModelMissingReferences} modelAbsent={renderLedger.ModelAbsentReferences} " +
    $"requiresActorAssembly={renderLedger.ModelAbsentReferences} " +
    $"templateLinkedBases={renderLedger.TemplateLinkedBases} blocked={renderLedger.BlockedReferences} " +
    $"stackId={stackId} saveIdentity={edition}:{stackId} decode=none writes=0 renderClaim=false");
foreach (var group in blockers)
    Console.WriteLine($"OPENNV_ACTOR_RENDER_BLOCKER edition={edition} reason={group.Key} count={group.Count()}");

internal readonly record struct PlacementCounts(int Actors = 0, int Creatures = 0);

internal sealed class ManifestActorResourceSource
{
    private readonly Dictionary<string, string> _loose = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _archives = [];
    private readonly Dictionary<string, FalloutBsaArchive> _opened =
        new(StringComparer.OrdinalIgnoreCase);

    internal ManifestActorResourceSource(
        JsonElement root,
        IReadOnlyDictionary<string, string> roots)
    {
        foreach (var row in root.GetProperty("looseFiles").EnumerateArray())
        {
            var rootId = row.GetProperty("rootId").GetString()!;
            var logical = FalloutBsaArchive.CanonicalPath(row.GetProperty("path").GetString()!);
            var path = Path.GetFullPath(Path.Combine(
                roots[rootId],
                row.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar)));
            ValidateFile(row, path, allowEmpty: true);
            _loose[logical] = path;
        }
        foreach (var row in root.GetProperty("archives").EnumerateArray())
        {
            var path = Path.Combine(
                roots[row.GetProperty("rootId").GetString()!],
                row.GetProperty("file").GetString()!);
            ValidateFile(row, path, allowEmpty: false);
            _archives.Add(path);
        }
    }

    internal bool TryResolve(string logicalPath, string? preferredArchive, out string source)
    {
        var canonical = FalloutBsaArchive.CanonicalPath(logicalPath);
        if (_loose.TryGetValue(canonical, out source!)) return true;
        for (var index = _archives.Count - 1; index >= 0; --index)
        {
            var archive = Archive(_archives[index]);
            if (!archive.Contains(canonical)) continue;
            source = $"{_archives[index]}::{canonical}";
            return true;
        }
        if (!string.IsNullOrWhiteSpace(preferredArchive))
        {
            var matches = _archives.Where(path => Path.GetFileName(path).Equals(
                preferredArchive, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException("Preferred actor BSA is inactive or ambiguous.");
            if (Archive(matches[0]).Contains(canonical))
            {
                source = $"{matches[0]}::{canonical}";
                return true;
            }
        }
        source = string.Empty;
        return false;
    }

    private FalloutBsaArchive Archive(string path)
    {
        if (!_opened.TryGetValue(path, out var archive))
        {
            archive = new FalloutBsaArchive(path);
            _opened.Add(path, archive);
        }
        return archive;
    }

    private static void ValidateFile(JsonElement row, string path, bool allowEmpty)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Declared actor source is missing.", path);
        var info = new FileInfo(path);
        var bytes = row.GetProperty("bytes").GetInt64();
        var mtime = row.GetProperty("mtimeMs").GetInt64();
        var actualMtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
        if (bytes < 0 || !allowEmpty && bytes == 0 || info.Length != bytes || actualMtime != mtime)
            throw new InvalidDataException($"Declared actor source changed: {path}.");
    }
}
