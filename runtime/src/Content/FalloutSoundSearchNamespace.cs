namespace OpenNV.Runtime.Content;

// Source startup still registers one DATA prefix. The product's selected
// read-only layers provide that namespace; they are not additional source
// search-root setters and do not merge invalidation or auxiliary file contents.
internal sealed class FalloutSoundSearchNamespace
{
    private readonly FalloutContentLayers _installation;
    private readonly FalloutContentLayers _data;
    internal IReadOnlyList<string> PhysicalRoots { get; }

    internal FalloutSoundSearchNamespace(string installation, string contentRoot, FalloutContentLayers data)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, "Data")));
        if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRoot)).Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            !data.Roots.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("Source sound namespace requires the selected installation's actual DATA prefix.");
        _installation = new([root]); _data = data;
        PhysicalRoots = Array.AsReadOnly(new[] { root, prefix });
    }

    internal void RequireLocalMasterPath(string value)
    {
        RequireRoots();
        var relative = value.Replace('\\', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
            !Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(PhysicalRoots[0], relative)))
                .Equals(PhysicalRoots[1], StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Changed source DATA prefix requires its actual runtime search-root setter.");
    }

    internal string? FindAuxiliaryFile(string name)
    {
        RequireRoots();
        FalloutArchiveStartupInput.RequireName(name);
        return RequireFile(_installation.ResolveEntry(name));
    }

    internal string? FindDataFile(string name)
    {
        RequireRoots();
        FalloutArchiveStartupInput.RequireName(name);
        return RequireFile(_data.ResolveEntry(name));
    }

    internal string? FindStartupFile(string name) => FindAuxiliaryFile(name) ?? FindDataFile(name);

    internal string? FindPhysicalFile(string raw)
    {
        RequireRoots();
        _ = FalloutMenuSoundSelectionSource.PathBytes(raw);
        var relative = raw.Replace('/', '\\');
        if (relative.StartsWith('\\') || relative.Contains(':') ||
            relative.Split('\\').Any(segment => segment is "" or "." or ".."))
            throw new NotSupportedException("Original menu physical read requires its actual relative source namespace.");
        // A direct DATA path and a prefixed relative lookup address the same
        // winning namespace. Looking up the unlayered installation's DATA first
        // would silently ignore the selected loose-file override.
        const string dataPrefix = "data\\";
        if (relative.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
            return RequireFile(_data.ResolveEntry(relative[dataPrefix.Length..]));
        return RequireFile(_installation.ResolveEntry(relative)) ?? RequireFile(_data.ResolveEntry(relative));
    }

    internal string RequirePhysicalDirectoryRoot()
    {
        RequireRoots();
        if (_data.Roots.Count != 1)
            throw new NotSupportedException("Layered loose sound directory enumeration requires its actual merged native order producer.");
        return PhysicalRoots[1];
    }

    private void RequireRoots()
    {
        if (!Directory.Exists(PhysicalRoots[0]))
            throw new DirectoryNotFoundException("Selected source sound installation namespace is missing.");
        foreach (var root in _data.Roots)
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Selected source sound DATA layer is missing: " + root);
    }

    private static string? RequireFile(string? path)
    {
        if (path is null) return null;
        if (!File.Exists(path)) throw new InvalidDataException("Original sound input resolved a non-file source entity.");
        return path;
    }
}
