using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicMapLevel(string Path, string Sha256, Fallout1NativeMap Map, Fallout1NativeObjectGraph Objects);

/// <summary>One live namespace for either classic campaign, with reusable decoded map and prototype data.</summary>
internal sealed class ClassicMapCatalog : IFalloutClassicOwnedSource
{
    private readonly IFalloutClassicOwnedSource _source;
    private readonly Dictionary<string, (byte[] Bytes, int Index)> _resources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ClassicMapLevel> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _loadOrder = new();
    private readonly Dictionary<int, string> _mapIndices = [];
    private readonly Dictionary<string, int> _pathIndices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _mapDescriptions = [];
    private readonly Dictionary<int, string> _messages = [];
    internal string Campaign { get; }
    public string ProfileId => _source.ProfileId;
    internal IReadOnlyList<string> Maps { get; }
    internal string StartingMap => Campaign == "fallout-1" ? "maps/v13ent.map" : "maps/artemple.map";

    internal ClassicMapCatalog(IFalloutClassicOwnedSource source, string campaign)
    {
        if (campaign is not ("fallout-1" or "fallout-2")) throw new ArgumentException("Unknown classic campaign.");
        _source = source; Campaign = campaign;
        Maps = source.EffectiveLogicalPaths("maps/", ".map").Select(Canonical).Order().ToArray();
        if (Maps.Count == 0) throw new InvalidDataException("The owned installation contains no MAP levels.");
        byte[]? messageBytes = null;
        try { messageBytes = Read("text/english/game/map.msg", out _); }
        catch (FileNotFoundException) { }
        if (messageBytes is not null)
        {
            foreach (Match match in Regex.Matches(Encoding.Latin1.GetString(messageBytes), @"^\s*\{(\d+)\}\{[^}]*\}\{([^}]*)\}", RegexOptions.Multiline))
            {
                var id = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (!_messages.TryAdd(id, match.Groups[2].Value.Trim()))
                    throw new InvalidDataException($"Duplicate source map message {id}.");
            }
        }
        byte[]? catalog = null;
        try { catalog = Read("data/maps.txt", out _); }
        catch (FileNotFoundException) { }
        if (catalog is null)
        {
            // Fallout 1's actual map-index table lives in MAP.MSG. Header
            // scanning is reserved for installations without either table.
            foreach (var (id, name) in _messages.Where(row => row.Value.EndsWith(".map", StringComparison.OrdinalIgnoreCase)))
                _mapIndices.Add(id, Canonical("maps/" + name));
            IndexPaths(); return;
        }
        var mapIndex = -1;
        foreach (var line in Encoding.Latin1.GetString(catalog).Replace('\r', '\n').Split('\n'))
        {
            var text = line.Split(';', 2)[0].Trim();
            var section = Regex.Match(text, @"^\[Map\s+(\d+)\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (section.Success) { mapIndex = int.Parse(section.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture); continue; }
            var field = text.Split('=', 2, StringSplitOptions.TrimEntries);
            if (mapIndex < 0 || field.Length != 2) continue;
            if (field[0].Equals("lookup_name", StringComparison.OrdinalIgnoreCase))
            { _mapDescriptions[mapIndex] = field[1]; continue; }
            if (!field[0].Equals("map_name", StringComparison.OrdinalIgnoreCase)) continue;
            var name = field[1].Trim();
            var path = Canonical("maps/" + name + (name.EndsWith(".map", StringComparison.OrdinalIgnoreCase) ? "" : ".map"));
            if (!_mapIndices.TryAdd(mapIndex, path)) throw new InvalidDataException($"Duplicate source map index {mapIndex}.");
        }
        IndexPaths();
    }

    private void IndexPaths()
    {
        foreach (var (index, path) in _mapIndices)
        {
            if (!_pathIndices.TryAdd(path, index))
                throw new InvalidDataException("Source map table contains duplicate map paths: " + path);
            if (!_mapDescriptions.ContainsKey(index) && _messages.TryGetValue(100 + index, out var name) && name.Length > 0)
                _mapDescriptions[index] = name;
        }
    }

    internal string DisplayName(string path)
    {
        path = Canonical(path); var filename = Path.GetFileName(path).ToUpperInvariant();
        return _pathIndices.TryGetValue(path, out var index) && _mapDescriptions.TryGetValue(index, out var name)
            ? $"{name} · {filename}" : filename;
    }

    internal string ElevationName(string path, int elevation)
    {
        var index = _pathIndices.GetValueOrDefault(Canonical(path), -1);
        return index >= 0 && _messages.TryGetValue(200 + index * 3 + elevation, out var name) && name.Length > 0
            ? name : $"Level {elevation + 1}";
    }

    internal static string Canonical(string path) => path.Replace('\\', '/').ToLowerInvariant();

    public byte[] Read(string logicalPath, out int sourceIndex)
    {
        var path = Canonical(logicalPath);
        if (_resources.TryGetValue(path, out var cached)) { sourceIndex = cached.Index; return cached.Bytes; }
        var bytes = _source.Read(path, out sourceIndex);
        // Names/prototypes are shared across thousands of source placements.
        // Large FRM and MAP bytes live with their decoded scene instead.
        if (path.EndsWith(".lst", StringComparison.Ordinal) || path.EndsWith(".pro", StringComparison.Ordinal))
            _resources.Add(path, (bytes, sourceIndex));
        return bytes;
    }

    internal ClassicMapLevel Load(string path)
    {
        path = Canonical(path);
        if (_loaded.TryGetValue(path, out var level)) return level;
        if (!Maps.Contains(path)) throw new FileNotFoundException("Source map is absent from the active installation: " + path);
        var bytes = Read(path, out _); var map = Fallout1NativeMapReader.Read(bytes);
        if (Campaign == "fallout-1" && map.Version != 19)
            throw new InvalidDataException("Fallout 1 requires its own MAP format and campaign data.");
        level = new(path, ClassicPremadeReader.Hash(bytes), map, Fallout1NativeObjectGraphReader.Read(bytes, map, this));
        _loaded.Add(path, level); _loadOrder.Enqueue(path);
        while (_loadOrder.Count > 4) _loaded.Remove(_loadOrder.Dequeue());
        return level;
    }

    internal byte[] ReadForNativeSaveRecovery(string path) => _source is Fallout1OwnedContentSource owned
        ? owned.ReadForNativeSaveRecovery(path) : throw new NotSupportedException("This source does not support DAT1 native-save recovery.");

    internal string ResolveMap(int index)
    {
        if (index < 0) throw new NotSupportedException("This exit requires the campaign world map or a source script destination.");
        if (_mapIndices.TryGetValue(index, out var path))
            return Maps.Contains(path) ? path : throw new FileNotFoundException("The source map table names an absent map: " + path);
        // Older installations may lack maps.txt; the MAP header retains its index.
        var matches = Maps.Where(candidate =>
        {
            var bytes = Read(candidate, out _);
            return bytes.Length >= 0xec && BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0x34)) == index;
        }).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"Source map index {index} resolves to {matches.Length} maps.");
        _mapIndices.Add(index, matches[0]); return matches[0];
    }

    public IReadOnlyList<string> EffectiveLogicalPaths(string prefix, string extension) => _source.EffectiveLogicalPaths(prefix, extension);
    public void Dispose() { _loaded.Clear(); _resources.Clear(); }
}
