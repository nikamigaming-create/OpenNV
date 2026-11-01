using System.Xml.Linq;

namespace OpenNV.Runtime.Presentation.Ui;

// Bare references inside a cloned source tile can name its direct siblings.
// A menu-wide unique reference remains shared; other ambiguous scopes refuse.
internal sealed class NativeOwnedMenuNamedSources(XElement root)
{
    private readonly Dictionary<XElement, Dictionary<string, XElement>> _cache = [];

    internal void Clear() => _cache.Clear();

    internal XElement Resolve(XElement requester, string name)
    {
        if (!_cache.TryGetValue(requester, out var names))
            _cache.Add(requester, names = new(StringComparer.OrdinalIgnoreCase));
        if (names.TryGetValue(name, out var found)) return found;
        bool Matches(XElement tile) => string.Equals((string?)tile.Attribute("name"), name, StringComparison.OrdinalIgnoreCase) &&
            !tile.AncestorsAndSelf("template").Any();
        var siblings = !ReferenceEquals(requester, root) && requester.Parent is { } parent
            ? parent.Elements().Where(Matches).ToArray() : [];
        var matches = siblings.Length != 0 ? siblings : root.DescendantsAndSelf().Where(Matches).ToArray();
        if (matches.Length != 1)
            throw new NotSupportedException($"Owned tile source {name} is absent or ambiguous ({matches.Length}).");
        names.Add(name, matches[0]);
        return matches[0];
    }
}
