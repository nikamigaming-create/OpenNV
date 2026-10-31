using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorMovementAnimation(string RequestedGroup, string ResolvedGroup, string Path);

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
    private readonly Dictionary<string, IReadOnlyList<string>> _declaredVariants = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _exportedGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _selectedGroups = new(StringComparer.OrdinalIgnoreCase);

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
        return _resolved[requested] = candidates.Count == 1 ? candidates[0] : FindExportedMovement(requested);
    }

    private string? FindExportedMovement(string requested)
    {
        var split = requested.LastIndexOf('/');
        var folder = requested[..split];
        var stem = requested[(split + 1)..^3];
        // Movement groups use the MT filename family and an exported action
        // name without that prefix. Search only the same locomotion folder;
        // never borrow a weapon family, variant suffix or nested directory.
        if (!folder.EndsWith("/locomotion", StringComparison.OrdinalIgnoreCase) ||
            !stem.StartsWith("mt", StringComparison.OrdinalIgnoreCase) || stem.Contains('_')) return null;
        var action = stem[2..];
        if (action.Length == 0) return null;
        var matches = new List<(string Path, string Group)>();
        foreach (var path in _directories[folder])
        {
            if (!_exportedGroups.TryGetValue(path, out var names))
                _exportedGroups.Add(path, names = _groups(path).ToArray());
            foreach (var name in names.Where(name => name.Equals(action, StringComparison.OrdinalIgnoreCase)))
                matches.Add((path, name));
        }
        if (matches.Count > 1)
            throw new NotSupportedException($"Source movement group {requested} has ambiguous exported matches: {string.Join(", ", matches.Select(match => match.Path))}.");
        if (matches.Count == 0) return null;
        var selected = matches[0];
        if (_selectedGroups.TryGetValue(selected.Path, out var prior) && !prior.Equals(selected.Group, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Source animation {selected.Path} resolves to incompatible exported groups.");
        _selectedGroups[selected.Path] = selected.Group;
        return selected.Path;
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

    internal string? SelectedGroup(string path) => _selectedGroups.GetValueOrDefault(Canonical(path));

    internal IReadOnlyList<string> Variants(string directory, string group, IReadOnlyList<string> declarations)
    {
        var assigned = DeclaredVariants(declarations, group);
        return assigned.Count > 0 ? assigned : Variants(directory, group);
    }

    // CREA KFFZ supplies explicit owned paths, including SpecialAnims. It does
    // not authorize scanning arbitrary nested folders or mixing directional
    // groups into the requested base action's variants.
    internal IReadOnlyList<string> DeclaredVariants(IReadOnlyList<string> declarations, string group)
    {
        if (string.IsNullOrWhiteSpace(group) || group.IndexOfAny(['/', '\\', '\0']) >= 0)
            throw new InvalidDataException("Declared animation action group is invalid.");
        var paths = declarations.Select(Canonical).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var key = group + '\0' + string.Join('\0', paths);
        if (_declaredVariants.TryGetValue(key, out var resolved)) return resolved;
        var candidates = paths.Where(path =>
        {
            if (!path.EndsWith(".kf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Declared actor animation is not KF.");
            var stem = path[(path.LastIndexOf('/') + 1)..^3];
            return stem.Equals(group, StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith(group + "_", StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        foreach (var path in candidates)
            if (!_exists(path)) throw new FileNotFoundException($"Declared source animation is missing: {path}", path);
        BindGroups(candidates, group, "KFFZ:" + group);
        return _declaredVariants[key] = Array.AsReadOnly(candidates);
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
        BindGroups(candidates, stem, requested);
        return Array.AsReadOnly(candidates.ToArray());
    }

    private void BindGroups(IReadOnlyList<string> candidates, string stem, string requested)
    {
        var selectedGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in candidates)
        {
            var variant = path[(path.LastIndexOf('/') + 1)..^3];
            if (!_exportedGroups.TryGetValue(path, out var names))
                _exportedGroups.Add(path, names = _groups(path).ToArray());
            var compatible = names.Where(name =>
            {
                var suffix = name.IndexOf('_');
                return !string.IsNullOrEmpty(name) && (suffix > 0
                    ? stem.EndsWith(name[..suffix], StringComparison.OrdinalIgnoreCase)
                    : stem.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            }).ToArray();
            var matching = compatible.Where(name => variant.EndsWith(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            // A variant filename can export its unsuffixed action group. Prefer
            // an exact variant group when both are present, then require one
            // compatible base group. The catalog never selects an action/RNG.
            if (matching.Length == 0 && variant.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase))
                matching = compatible.Where(name => !name.Contains('_')).ToArray();
            if (matching.Length != 1) throw new InvalidDataException($"Source animation variant {path} has no unique compatible exported group for {requested}.");
            selectedGroups.Add(path, matching[0]);
        }
        foreach (var (path, group) in selectedGroups)
            if (_selectedGroups.TryGetValue(path, out var prior) && !prior.Equals(group, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Source animation {path} resolves to incompatible exported groups.");
        foreach (var (path, group) in selectedGroups) _selectedGroups[path] = group;
    }

    internal string Require(string directory, string group) => Find(directory, group) ??
        throw new FileNotFoundException($"Source animation group is missing: {directory}/{group}.kf", directory + "/" + group + ".kf");

    internal FalloutActorMovementAnimation ResolveUnmodifiedMovement(string directory, string requestedGroup,
        IReadOnlyList<string> folders)
    {
        // The native sequence-map resolver remaps FastForward to Forward,
        // then clears the action component to Idle when no sequence exists.
        // This entry point covers unmodified movement actions; weapon/stance
        // modifier inheritance requires its own qualified group mapping.
        string[] actions = requestedGroup switch
        {
            "mtfastforward" => ["mtfastforward", "mtforward", "mtidle"],
            "mtforward" => ["mtforward", "mtidle"],
            _ => throw new ArgumentException("An unmodified forward movement group is required.", nameof(requestedGroup)),
        };
        return ResolveMovementCandidates(directory, requestedGroup, folders, actions);
    }

    internal FalloutActorMovementAnimation ResolveTurn(string directory, bool left, bool sneaking,
        IReadOnlyList<string> folders)
    {
        var action = left ? "mtturnleft" : "mtturnright";
        var requested = sneaking ? "sneak" + action : action;
        // Missing stance-specific movement first tries the same action with
        // the stance cleared. Only then does it clear the action to Idle,
        // repeating that stance inheritance for the idle. Weapon modifiers
        // are outside this unarmed movement selector.
        string[] actions = sneaking ? [requested, action, "sneakmtidle", "mtidle"] : [action, "mtidle"];
        return ResolveMovementCandidates(directory, requested, folders, actions);
    }

    private FalloutActorMovementAnimation ResolveMovementCandidates(string directory, string requestedGroup,
        IReadOnlyList<string> folders, IReadOnlyList<string> actions)
    {
        foreach (var action in actions)
            foreach (var folder in folders)
                if (Find(directory, folder.Length == 0 ? action : folder + "/" + action) is { } path)
                    return new(requestedGroup, action, path);
        throw new FileNotFoundException("Source actor group is absent: " + string.Join(',', actions));
    }

    private static string Canonical(string path) => FalloutBsaArchive.CanonicalPath(path).Replace('\\', '/');
}
