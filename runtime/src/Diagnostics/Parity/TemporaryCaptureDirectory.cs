using System.Text.Json;

namespace OpenNV.Runtime.Diagnostics.Parity;

// Only directories created for a capture belong to this owner. Ordinary game
// inputs, saves, export destinations and historical evidence are never adopted.
internal sealed class TemporaryCaptureDirectory : IDisposable
{
    private const string MarkerName = ".opennv-temporary-capture.json";
    internal string Path { get; }

    internal TemporaryCaptureDirectory(string directory)
    {
        Path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(directory));
        if (System.IO.Path.Exists(Path)) throw new IOException($"Capture directory already exists: {Path}");
        CheckAncestors(Path);
        Directory.CreateDirectory(Path);
        var marker = System.IO.Path.Combine(Path, MarkerName);
        try { File.WriteAllText(marker, JsonSerializer.Serialize(new Ownership(Path, Guid.NewGuid()))); }
        catch
        {
            File.Delete(marker);
            Directory.Delete(Path);
            throw;
        }
    }

    public void Dispose() => DeleteIfOwned(Path);

    internal static void DeleteIfOwned(string directory)
    {
        var root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(directory));
        if (!Directory.Exists(root)) return;
        CheckAncestors(root);
        var marker = System.IO.Path.Combine(root, MarkerName);
        if (!File.Exists(marker)) return;
        if ((File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Capture ownership marker is a link.");
        var ownership = JsonSerializer.Deserialize<Ownership>(File.ReadAllText(marker));
        if (ownership is null || ownership.Id == Guid.Empty ||
            !string.Equals(root, ownership.Directory, StringComparison.OrdinalIgnoreCase) ||
            root == System.IO.Path.GetPathRoot(root))
            throw new IOException("Capture cleanup target does not match its ownership marker.");
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var parent))
            foreach (var entry in Directory.EnumerateFileSystemEntries(parent))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Refusing to follow a link during capture cleanup: {entry}");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        Directory.Delete(root, recursive: true);
    }

    private static void CheckAncestors(string path)
    {
        for (var entry = new DirectoryInfo(path); entry is not null; entry = entry.Parent)
            if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Temporary capture path contains a link: {entry.FullName}");
    }

    private sealed record Ownership(string Directory, Guid Id);
}
