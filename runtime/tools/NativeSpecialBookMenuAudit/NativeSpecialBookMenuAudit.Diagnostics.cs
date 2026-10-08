public partial class NativeSpecialBookMenuAudit
{
    internal static string DiagnosticPath(string path, IEnumerable<string> protectedRoots)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("SPECIAL book visual diagnostic requires an absolute fresh .png path.");
        var destination = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var selected in protectedRoots)
        {
            var root = Path.TrimEndingDirectorySeparator(ResolveDirectory(selected));
            var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            if (destination.Equals(root, comparison) || destination.StartsWith(prefix, comparison))
                throw new ArgumentException("SPECIAL book visual diagnostic must be outside the repository and every owned input root.");
        }
        for (var parent = new DirectoryInfo(Path.GetDirectoryName(destination)!); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("SPECIAL book visual diagnostic contains a linked directory.");
        if (Path.Exists(destination)) throw new IOException("SPECIAL book visual diagnostic must be fresh.");
        return destination;
    }

    internal static FileStream CreateDiagnostic(string path) => new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

    private static string ResolveDirectory(string selected)
    {
        var path = Path.GetFullPath(selected);
        var root = Path.GetPathRoot(path)!;
        foreach (var part in path[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            root = Path.Combine(root, part);
            var entry = new DirectoryInfo(root);
            if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                root = entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? throw new IOException("Owned input root has an unresolved link.");
        }
        return Path.GetFullPath(root);
    }
}
