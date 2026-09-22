using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace OpenNV.Runtime.Content;

// UI tiles are source-owned mutable state. Godot receives the evaluated result
// later; scripts never mutate a presentation-only copy. The store intentionally
// keeps UI state out of campaign saves because menu components are recreated by
// the source menu lifetime.
internal sealed class FalloutUiComponentStore
{
    private sealed class Tile
    {
        internal readonly XElement Source;
        internal readonly Tile? Parent;
        internal readonly List<Tile> Children = [];
        internal readonly string Name;
        internal readonly string MenuName;
        internal readonly int SameNameIndex;

        internal Tile(XElement source, Tile? parent, string menuName, int sameNameIndex)
        {
            Source = source;
            Parent = parent;
            MenuName = menuName;
            Name = (string?)source.Attribute("name") ??
                throw new InvalidDataException("Owned UI component has no name.");
            SameNameIndex = sameNameIndex;
        }

        internal string Path
        {
            get
            {
                var segment = SameNameIndex == 0 ? Name : $"{Name}:{SameNameIndex}";
                return Parent is null ? MenuName : $"{Parent.Path}/{segment}";
            }
        }
    }

    private sealed class Menu
    {
        internal readonly Tile Root;
        internal Menu(Tile root) => Root = root;
    }

    private readonly FalloutPluginStack? _records;
    private readonly Dictionary<string, Menu> _menus;
    private readonly Dictionary<string, float> _floatOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _stringOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _detachedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Tile> _tiles = new(StringComparer.OrdinalIgnoreCase);

    private FalloutUiComponentStore(FalloutPluginStack? records, IEnumerable<XElement> roots)
    {
        _records = records;
        _menus = [];
        foreach (var root in roots)
        {
            var name = (string?)root.Attribute("name");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Owned UI menu has no name.");
            if (_menus.ContainsKey(name)) throw new InvalidDataException($"Owned UI menu is duplicated: {name}.");
            var tile = Build(root, null, name, 0);
            _menus.Add(name, new(tile));
        }
        if (_menus.Count == 0) throw new InvalidDataException("Owned UI source has no menus.");
    }

    internal static FalloutUiComponentStore Open(FalloutPluginStack records)
    {
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned UI source is absent.");
        return Open(records, source);
    }

    internal static FalloutUiComponentStore Open(FalloutPluginStack records, RuntimeLiveContentSource source)
    {
        var roots = new[]
        {
            MenuRoot(FalloutMenuXml.Expand(source, FalloutMenuXml.Read(source, "menus/options/start_menu.xml")), "StartMenu"),
            MenuRoot(FalloutMenuXml.Expand(source, FalloutMenuXml.Read(source, "menus/main/hud_main_menu.xml")), "HUDMainMenu"),
            MenuRoot(FalloutMenuXml.Expand(source, FalloutMenuXml.Read(source, "menus/main/inventory_menu.xml")), "InventoryMenu"),
        };
        return new(records, roots);
    }

    internal static FalloutUiComponentStore Synthetic(params XElement[] roots) => new(null, roots);

    internal object State => new
    {
        menus = _menus.Count,
        components = _tiles.Count,
        detached = _detachedPaths.Count,
        floatOverrides = _floatOverrides.Count,
        stringOverrides = _stringOverrides.Count,
        persistence = "menu-session-only",
    };

    internal float GetFloat(string path, bool alt = false)
    {
        if (!TrySplitTrait(path, out var segments, out var trait)) return 0;
        var tile = ResolveTile(segments, alt);
        if (tile is null || IsDetached(tile)) return 0;
        if (trait.Equals("childcount", StringComparison.OrdinalIgnoreCase)) return tile.Children.Count;
        var key = Key(tile, trait);
        if (_floatOverrides.TryGetValue(key, out var value)) return value;
        return EvaluateFloat(tile, trait, []);
    }

    internal string GetString(string path, bool alt = false)
    {
        if (!TrySplitTrait(path, out var segments, out var trait)) return string.Empty;
        var tile = ResolveTile(segments, alt);
        if (tile is null || IsDetached(tile)) return string.Empty;
        var key = Key(tile, trait);
        if (_stringOverrides.TryGetValue(key, out var value)) return value;
        var property = Property(tile, trait);
        if (property is null) return string.Empty;
        if (property.HasElements)
        {
            var copy = property.Elements().SingleOrDefault(element =>
                element.Name.LocalName.Equals("copy", StringComparison.OrdinalIgnoreCase));
            if (copy?.Attribute("src") is { } source && copy.Attribute("trait") is { } sourceTrait)
                return GetStringFrom(EvaluateSource(tile, source.Value), sourceTrait.Value, []);
            return string.Empty;
        }
        var text = property.Value.Trim();
        if (_records is not null && text.StartsWith("entity_-", StringComparison.Ordinal))
            return FalloutMenuXml.String(property, _records);
        return text;
    }

    internal bool SetFloat(string path, float value, bool alt = false)
    {
        if (!float.IsFinite(value) || !TrySplitTrait(path, out var segments, out var trait)) return false;
        var tile = ResolveTile(segments, alt);
        if (tile is null || IsDetached(tile)) return false;
        _floatOverrides[Key(tile, trait)] = value;
        return true;
    }

    internal bool SetString(string path, string value, bool alt = false, string? formatting = null)
    {
        if (!TrySplitTrait(path, out var segments, out var trait)) return false;
        var tile = ResolveTile(segments, alt);
        if (tile is null || IsDetached(tile)) return false;
        _stringOverrides[Key(tile, trait)] = Format(value, formatting);
        return true;
    }

    internal bool Unload(string path)
    {
        if (!TrySplitComponent(path, out var segments)) return false;
        var tile = ResolveTile(segments, alt: true);
        var canonical = Normalize(path);
        _detachedPaths.Add(tile?.Path ?? canonical);
        if (tile is not null)
        {
            foreach (var descendant in Descendants(tile)) _detachedPaths.Add(descendant.Path);
            RemoveOverrides(tile);
        }
        return tile is not null;
    }

    internal void Reset()
    {
        _floatOverrides.Clear();
        _stringOverrides.Clear();
        _detachedPaths.Clear();
    }

    private static XElement MenuRoot(XElement document, string expected)
    {
        var root = document.Descendants().SingleOrDefault(element =>
            element.Attribute("name")?.Value.Equals(expected, StringComparison.OrdinalIgnoreCase) == true);
        return root ?? throw new InvalidDataException($"Owned menu has no {expected} root tile.");
    }

    private Tile Build(XElement source, Tile? parent, string menuName, int sameNameIndex)
    {
        var tile = new Tile(source, parent, menuName, sameNameIndex);
        _tiles[tile.Path] = tile;
        var duplicateOrdinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in source.Elements().Where(IsTile))
        {
            var name = (string?)child.Attribute("name") ?? throw new InvalidDataException("Owned UI tile has no name.");
            var index = duplicateOrdinals.GetValueOrDefault(name);
            duplicateOrdinals[name] = index + 1;
            tile.Children.Add(Build(child, tile, menuName, index));
        }
        return tile;
    }

    private static bool IsTile(XElement element) => element.Attribute("name") is not null;

    private Tile? ResolveTile(IReadOnlyList<string> segments, bool alt)
    {
        if (segments.Count == 0 || !_menus.TryGetValue(segments[0], out var menu)) return null;
        var current = menu.Root;
        for (var index = 1; index < segments.Count; ++index)
        {
            var (name, ordinal) = Segment(segments[index]);
            var candidates = current.Children.Where(child => name == "*" ||
                child.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0) return null;
            if (ordinal is { } selected)
            {
                // The suffix is the child ordinal used by the NVSE UI path
                // grammar. Both old and Alt functions retain the same source
                // tree; the explicit switch keeps their owners distinct so a
                // later dynamic-template adapter cannot silently merge them.
                if (selected < 0 || selected >= candidates.Length) return null;
                current = candidates[selected];
            }
            else current = candidates[0];
        }
        return current;
    }

    private float EvaluateFloat(Tile tile, string trait, HashSet<string> visiting)
    {
        if (trait.Equals("childcount", StringComparison.OrdinalIgnoreCase)) return tile.Children.Count;
        var key = Key(tile, trait);
        if (!visiting.Add(key)) throw new InvalidDataException($"Owned UI trait cycle: {key}");
        try
        {
            var property = Property(tile, trait);
            if (property is null) return 0;
            return FalloutMenuXml.Number(property, (source, sourceTrait) =>
            {
                var target = EvaluateSource(tile, source) ??
                    throw new InvalidDataException($"Owned UI trait source is absent: {source}.");
                return EvaluateFloat(target, sourceTrait, visiting);
            });
        }
        finally { visiting.Remove(key); }
    }

    private string GetStringFrom(Tile? tile, string trait, HashSet<string> visiting)
    {
        if (tile is null) throw new InvalidDataException($"Owned UI string source is absent for trait {trait}.");
        if (IsDetached(tile)) return string.Empty;
        var key = Key(tile, trait);
        if (!visiting.Add(key)) throw new InvalidDataException($"Owned UI string trait cycle: {key}");
        try
        {
            if (_stringOverrides.TryGetValue(key, out var value)) return value;
            var property = Property(tile, trait);
            if (property is null || property.HasElements is false)
                return property is null ? string.Empty : property.Value.Trim();
            var copy = property.Elements().SingleOrDefault(element =>
                element.Name.LocalName.Equals("copy", StringComparison.OrdinalIgnoreCase));
            return copy?.Attribute("src") is { } source && copy.Attribute("trait") is { } sourceTrait
                ? GetStringFrom(EvaluateSource(tile, source.Value), sourceTrait.Value, visiting)
                : string.Empty;
        }
        finally { visiting.Remove(key); }
    }

    private Tile? EvaluateSource(Tile tile, string source)
    {
        var token = source.Trim();
        if (token.Equals("me()", StringComparison.OrdinalIgnoreCase)) return tile;
        if (token.Equals("parent()", StringComparison.OrdinalIgnoreCase)) return tile.Parent;
        if (token.Equals("grandparent()", StringComparison.OrdinalIgnoreCase)) return tile.Parent?.Parent;
        if (token.Equals("child()", StringComparison.OrdinalIgnoreCase))
            return tile.Children.Count == 1
                ? tile.Children[0]
                : throw new NotSupportedException("Owned child() UI source is absent or ambiguous.");
        if (token.Equals("screen()", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("globals()", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Owned UI source {token} needs a presentation trait owner.");
        if (token.StartsWith("sibling(", StringComparison.OrdinalIgnoreCase) && token.EndsWith(')'))
            return tile.Parent?.Children.FirstOrDefault(child => child.Name.Equals(token[8..^1], StringComparison.OrdinalIgnoreCase));
        if (token.StartsWith("child(", StringComparison.OrdinalIgnoreCase) && token.EndsWith(')'))
            return tile.Children.FirstOrDefault(child => child.Name.Equals(token[6..^1], StringComparison.OrdinalIgnoreCase));
        if (token.Contains('/', StringComparison.Ordinal) || token.Contains('\\', StringComparison.Ordinal))
            return ResolveTile(Split(token), alt: true);
        var menu = _menus.Values.FirstOrDefault(value => value.Root.Name.Equals(tile.MenuName, StringComparison.OrdinalIgnoreCase));
        return menu?.Root.Children.FirstOrDefault(child => child.Name.Equals(token, StringComparison.OrdinalIgnoreCase)) ??
            (menu?.Root.Name.Equals(token, StringComparison.OrdinalIgnoreCase) == true ? menu.Root :
                _tiles.Values.FirstOrDefault(candidate => candidate.MenuName.Equals(tile.MenuName, StringComparison.OrdinalIgnoreCase) &&
                    candidate.Name.Equals(token, StringComparison.OrdinalIgnoreCase)));
    }

    private static XElement? Property(Tile tile, string trait) => tile.Source.Elements().LastOrDefault(element =>
        element.Name.LocalName.Equals(trait, StringComparison.OrdinalIgnoreCase) && !IsTile(element));

    private bool IsDetached(Tile tile) => tile.Parent is not null &&
        (IsDetached(tile.Parent) || _detachedPaths.Contains(tile.Path)) || _detachedPaths.Contains(tile.Path);

    private void RemoveOverrides(Tile tile)
    {
        var prefix = tile.Path + "/";
        foreach (var key in _floatOverrides.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
            _floatOverrides.Remove(key);
        foreach (var key in _stringOverrides.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
            _stringOverrides.Remove(key);
    }

    private IEnumerable<Tile> Descendants(Tile tile)
    {
        foreach (var child in tile.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static string Key(Tile tile, string trait) => $"{tile.Path}/{trait}";
    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    private static bool TrySplitTrait(string path, out string[] segments, out string trait)
    {
        segments = [];
        trait = string.Empty;
        var values = Split(path);
        if (values.Length < 2) return false;
        trait = values[^1];
        segments = values[..^1];
        return trait.Length != 0;
    }

    private static bool TrySplitComponent(string path, out string[] segments)
    {
        segments = Split(path);
        return segments.Length >= 2;
    }

    private static string[] Split(string path) => path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static (string Name, int? Ordinal) Segment(string segment)
    {
        var separator = segment.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(segment[(separator + 1)..], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var ordinal)) return (segment, null);
        return (segment[..separator], ordinal);
    }

    private static string Format(string value, string? formatting)
    {
        if (string.IsNullOrEmpty(formatting) || !value.Contains('%', StringComparison.Ordinal)) return value;
        var builder = new StringBuilder(value.Length + formatting.Length);
        var used = false;
        for (var index = 0; index < value.Length; ++index)
        {
            if (value[index] == '%' && index + 1 < value.Length && value[index + 1] != '%')
            {
                builder.Append(formatting);
                ++index;
                used = true;
            }
            else if (value[index] == '%' && index + 1 < value.Length && value[index + 1] == '%')
            {
                builder.Append('%');
                ++index;
            }
            else builder.Append(value[index]);
        }
        return used ? builder.ToString() : value;
    }
}
