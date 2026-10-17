using System.Collections.Concurrent;

namespace OpenNV.Runtime.Content;

/// <summary>One case-insensitive resource namespace over ordered, read-only folders.</summary>
internal sealed class FalloutContentLayers
{
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _directories =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal FalloutContentLayers(IEnumerable<string> roots)
    {
        Roots = roots.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).Reverse()
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Reverse().ToArray();
        if (Roots.Count == 0) throw new ArgumentException("At least one owned content folder is required.", nameof(roots));
        foreach (var root in Roots)
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Selected content folder is missing: {root}");
    }

    // Later roots override earlier roots. A selected folder is never copied into the game.
    internal IReadOnlyList<string> Roots { get; }

    internal string? ResolveFile(string logicalPath)
    {
        var canonical = FalloutBsaArchive.CanonicalPath(logicalPath);
        for (var index = Roots.Count - 1; index >= 0; --index)
        {
            var path = ResolveIn(Roots[index], canonical);
            if (path is not null && File.Exists(path)) return path;
        }
        return null;
    }

    internal IReadOnlyDictionary<string, string> TopLevelFiles()
    {
        var winners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots)
            foreach (var (name, path) in Entries(root))
                if (File.Exists(path)) winners[name] = path;
        return winners;
    }

    internal IEnumerable<string> ResourcePathsUnder(string logicalDirectory)
    {
        var canonical = FalloutBsaArchive.CanonicalPath(logicalDirectory);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots)
        {
            var directory = ResolveIn(root, canonical);
            if (directory is null || !Directory.Exists(directory)) continue;
            Add(directory, canonical, new HashSet<string>(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
        }
        return paths.Order(StringComparer.OrdinalIgnoreCase);

        void Add(string directory, string prefix, HashSet<string> ancestors)
        {
            var info = new DirectoryInfo(directory);
            var actual = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName;
            if (!ancestors.Add(actual)) throw new InvalidDataException($"Resource folders contain a directory-link cycle: {directory}");
            try
            {
                foreach (var (name, path) in Entries(directory))
                {
                    var logical = prefix + "\\" + name.ToLowerInvariant();
                    if (Directory.Exists(path)) Add(path, logical, ancestors);
                    else if (File.Exists(path)) paths.Add(logical);
                }
            }
            finally { ancestors.Remove(actual); }
        }
    }

    private string? ResolveIn(string root, string canonical)
    {
        var current = root;
        foreach (var segment in canonical.Split('\\'))
        {
            if (!Directory.Exists(current) || !Entries(current).TryGetValue(segment, out var path)) return null;
            current = path;
        }
        return current;
    }

    private IReadOnlyDictionary<string, string> Entries(string directory) => _directories.GetOrAdd(directory, path =>
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            if (!entries.TryAdd(Path.GetFileName(entry), entry))
                throw new InvalidDataException($"Ambiguous case-insensitive resource name in {path}: {Path.GetFileName(entry)}");
        return entries;
    });
}
