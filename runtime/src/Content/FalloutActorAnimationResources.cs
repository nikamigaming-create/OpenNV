using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

// Source KF variants retain both their authored filename and exported group.
// The mounted content owner shares this index across player and NPC bodies.
internal sealed class FalloutActorAnimationResources
{
    private readonly Func<string, bool> _exists;
    private readonly Func<string, IReadOnlyList<string>> _enumerate;
    private readonly Func<string, IReadOnlyList<string>> _groups;
    private readonly Dictionary<string, string[]> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _variants = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _exportedGroups = new(StringComparer.OrdinalIgnoreCase);

    internal FalloutActorAnimationResources(RuntimeLiveContentSource content) : this(
        path => content.TryResolve(path, null, out _), content.ResourcePathsUnder, path =>
        {
            if (!content.TryRead(path, null, out var bytes, out _))
                throw new FileNotFoundException($"Source animation variant is missing: {path}", path);
            var source = FalloutNifFile.Read(bytes);
            return source.Roots.Select(source.ReadObject).OfType<FalloutNifControllerSequence>().Select(sequence => sequence.Name).ToArray();
        })
    { }

    internal FalloutActorAnimationResources(Func<string, bool> exists, Func<string, IReadOnlyList<string>> enumerate,
        Func<string, IReadOnlyList<string>> groups)
    { _exists = exists; _enumerate = enumerate; _groups = groups; }

    internal string? Find(string directory, string group)
    {
        var requested = Canonical(directory + "/" + group + ".kf");
        if (_resolved.TryGetValue(requested, out var resolved)) return resolved;
        if (_exists(requested)) return _resolved[requested] = requested;
        var candidates = Candidates(requested, includeExact: false);
        if (candidates.Count > 1)
            throw new NotSupportedException($"Source animation group {requested} has ambiguous variants: {string.Join(", ", candidates)}.");
        return _resolved[requested] = candidates.SingleOrDefault();
    }

    // This catalog does not choose a sequence. Callers need an explicit action
    // owner before admitting multiple compatible source variants.
    internal IReadOnlyList<string> Variants(string directory, string group)
    {
        var requested = Canonical(directory + "/" + group + ".kf");
        if (!_variants.TryGetValue(requested, out var candidates))
            _variants.Add(requested, candidates = Candidates(requested, includeExact: true));
        return candidates;
    }

    private IReadOnlyList<string> Candidates(string requested, bool includeExact)
    {
        var split = requested.LastIndexOf('/');
        var folder = requested[..split];
        var stem = requested[(split + 1)..^3];
        if (!_directories.TryGetValue(folder, out var paths))
            _directories.Add(folder, paths = _enumerate(folder).Select(Canonical)
                .Where(path => path[..path.LastIndexOf('/')].Equals(folder, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".kf", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray());
        var candidates = paths.Where(path => path.StartsWith(folder + "/" + stem + "_", StringComparison.OrdinalIgnoreCase) ||
            includeExact && path.Equals(requested, StringComparison.OrdinalIgnoreCase)).ToList();
        if (includeExact && _exists(requested) && !candidates.Contains(requested, StringComparer.OrdinalIgnoreCase))
            candidates.Add(requested);
        candidates.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var path in candidates)
        {
            var variant = path[(split + 1)..^3];
            if (!_exportedGroups.TryGetValue(path, out var names))
                _exportedGroups.Add(path, names = _groups(path).ToArray());
            var matching = names.Count(name =>
            {
                var suffix = name.IndexOf('_');
                return !string.IsNullOrEmpty(name) && (suffix > 0
                    ? stem.EndsWith(name[..suffix], StringComparison.OrdinalIgnoreCase)
                    : stem.EndsWith(name, StringComparison.OrdinalIgnoreCase)) &&
                    variant.EndsWith(name, StringComparison.OrdinalIgnoreCase);
            });
            if (matching != 1) throw new InvalidDataException($"Source animation variant {path} has no unique compatible exported group for {requested}.");
        }
        return Array.AsReadOnly(candidates.ToArray());
    }

    internal string Require(string directory, string group) => Find(directory, group) ??
        throw new FileNotFoundException($"Source animation group is missing: {directory}/{group}.kf", directory + "/" + group + ".kf");

    private static string Canonical(string path) => FalloutBsaArchive.CanonicalPath(path).Replace('\\', '/');
}
