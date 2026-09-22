using System.Xml.Linq;

namespace OpenNV.Runtime.Content;

// UIO is a source-resource organizer, not a second menu implementation. Its
// manifests select XML fragments and the named tile at which the source engine
// inserts each fragment. Conditions are evaluated against the winning plugin
// graph before the ordinary menu include expander runs.
internal sealed class FalloutUiOrganizer
{
    private readonly Func<string, byte[]?> _read;
    private readonly Func<string, IReadOnlyList<string>> _paths;
    private readonly IReadOnlySet<string> _activePlugins;

    internal FalloutUiOrganizer(Func<string, byte[]?> read, IEnumerable<string> activePlugins,
        Func<string, IReadOnlyList<string>>? paths = null)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _paths = paths ?? (_ => []);
        _activePlugins = activePlugins.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static FalloutUiOrganizer Current()
    {
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("UI organizer source is absent.");
        return From(source);
    }

    internal static FalloutUiOrganizer From(RuntimeLiveContentSource source)
    {
        return new(path => source.TryRead(path, null, out var bytes, out _) ? bytes : null,
            source.PluginSources.Select(plugin => plugin.Name), source.ResourcePathsUnder);
    }

    internal XElement Apply(string logicalPath, XElement document)
    {
        var root = document.Elements().SingleOrDefault();
        if (root is null) throw new InvalidDataException("Owned menu document has no root tile.");
        var targetName = root.Attribute("name")?.Value ?? root.Name.LocalName;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in Manifests())
        {
            var text = _read(manifest);
            if (text is null) continue;
            ApplyManifest(targetName, document, text, seen);
        }
        return document;
    }

    private IEnumerable<string> Manifests()
    {
        if (_read("uio/supported.txt") is not null) yield return "uio/supported.txt";
        var publicPaths = ResourcePathsUnder("uio/public");
        foreach (var path in publicPaths.Where(path => path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
            yield return path;
    }

    private IReadOnlyList<string> ResourcePathsUnder(string directory)
    {
        return _paths(directory);
    }

    private void ApplyManifest(string rootName, XElement document, byte[] bytes, HashSet<string> seen)
    {
        var lines = System.Text.Encoding.UTF8.GetString(bytes)
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
            .Select(line => line.Trim()).Where(line => line.Length != 0 && !line.StartsWith('#')).ToArray();
        for (var index = 0; index < lines.Length; ++index)
        {
            var declaration = lines[index];
            var separator = declaration.IndexOf(':');
            if (separator <= 0) continue;
            var sourcePath = declaration[..separator].Trim();
            var target = declaration[(separator + 1)..].Trim();
            if (sourcePath.Length == 0 || target.Length == 0) continue;
            var condition = "true";
            if (index + 1 < lines.Length && IsCondition(lines[index + 1])) condition = lines[++index];
            if (!EvaluateCondition(condition)) continue;
            var targetParts = target.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (targetParts.Length == 0 || !string.Equals(targetParts[0], rootName, StringComparison.OrdinalIgnoreCase)) continue;
            var fragment = sourcePath.Replace('\\', '/');
            var resourcePath = fragment.StartsWith("menus/", StringComparison.OrdinalIgnoreCase)
                ? fragment : "menus/prefabs/" + fragment;
            // A supported.txt entry is a capability declaration, not proof
            // that the optional donor resource is installed. UIO injects only
            // a fragment present in the selected owned overlay.
            if (_read(resourcePath) is null) continue;
            var includePath = resourcePath["menus/prefabs/".Length..].Replace('/', '\\');
            var identity = $"{includePath}\0{target}";
            if (!seen.Add(identity)) continue;
            var destination = FindTarget(document, targetParts[1..]);
            destination.Add(new XElement("include", new XAttribute("src", includePath)));
        }
    }

    private static XElement FindTarget(XElement document, IReadOnlyList<string> path)
    {
        var current = document.Elements().Single();
        foreach (var name in path)
        {
            current = current.DescendantsAndSelf().SingleOrDefault(element =>
                string.Equals((string?)element.Attribute("name"), name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"UI organizer target tile is absent: {name}.");
        }
        return current;
    }

    private bool EvaluateCondition(string source)
    {
        var expression = source.Replace("\t", " ", StringComparison.Ordinal).Trim();
        if (expression.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (expression.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        var orTerms = expression.Split("||", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return orTerms.Any(term => term.Split("&&", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .All(EvaluateAtom));
    }

    private bool EvaluateAtom(string atom)
    {
        if (atom.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (atom.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (atom.Length < 2 || atom[0] is not ('?' or '!'))
            throw new InvalidDataException($"UI organizer condition is unbound: {atom}.");
        var loaded = _activePlugins.Contains(atom[1..]);
        return atom[0] == '?' ? loaded : !loaded;
    }
    private static bool IsCondition(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
        value.Contains('?') || value.Contains('!') || value.Contains("&&", StringComparison.Ordinal) || value.Contains("||", StringComparison.Ordinal);
}
