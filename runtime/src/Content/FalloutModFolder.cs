namespace OpenNV.Runtime.Content;

/// <summary>Finds Data roots inside extracted packages without moving their contents.</summary>
internal static class FalloutModFolder
{
    private static readonly string[] ResourceDirectories = ["textures", "meshes", "sound", "music", "menus", "nvse", "config", "uio"];

    internal static string Resolve(string selected, Func<FalloutContentLayers, bool>? matches = null)
    {
        var root = Path.GetFullPath(selected);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Selected package folder is missing: {root}");
        var candidates = new List<string>();
        Find(root, new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new InvalidDataException("No matching game data was found. Choose the extracted mod folder or its Data folder."),
            _ => throw new InvalidDataException("More than one matching Data folder was found. Choose the individual package folder."),
        };

        void Find(string directory, HashSet<string> ancestors)
        {
            var info = new DirectoryInfo(directory);
            var actual = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName;
            if (!ancestors.Add(actual)) throw new InvalidDataException($"Package folders contain a directory-link cycle: {directory}");
            try
            {
                var children = Directory.GetDirectories(directory);
                var data = children.Where(path => Path.GetFileName(path).Equals("Data", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (data.Length > 1) throw new InvalidDataException($"Ambiguous Data folder in {directory}.");
                if (data.Length == 1)
                {
                    Find(data[0], ancestors);
                    return;
                }
                var layer = new FalloutContentLayers([directory]);
                var files = layer.TopLevelFiles().Keys;
                var isData = files.Any(name => Path.GetExtension(name).Equals(".esm", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(name).Equals(".esp", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(name).Equals(".bsa", StringComparison.OrdinalIgnoreCase)) ||
                    children.Any(path => ResourceDirectories.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));
                // Do not descend into runtime resource trees and mistake meshes or textures for packages.
                if (isData || matches?.Invoke(layer) == true)
                {
                    if (matches is null || matches(layer)) candidates.Add(directory);
                    return;
                }
                foreach (var child in children.Where(path => !Path.GetFileName(path).Equals("fomod", StringComparison.OrdinalIgnoreCase)))
                    Find(child, ancestors);
            }
            finally { ancestors.Remove(actual); }
        }
    }
}
