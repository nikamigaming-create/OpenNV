using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutNativePluginPrivateIo
{
    // This producer represents the physical loose-file namespace consumed by
    // public Win32 Find APIs. Archive members are not fabricated filesystem
    // entries, extracted assets or metadata-only native files.
    private static Func<string, NativePluginDirectorySelection> DirectoryResolver(RuntimeLiveContentSource source,
        string runtimeDirectory, IReadOnlyDictionary<string, NativePluginIoReadWinner> declared)
    {
        var roots = source.ContentRoots.Select(NativePluginPrivateIo.Canonical).ToArray();
        var declarations = declared.ToArray();
        return Resolve;

        NativePluginDirectorySelection Resolve(string virtualDirectory)
        {
            var directory = NativePluginPrivateIo.Canonical(virtualDirectory);
            var paths = Paths(directory).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var stamps = paths.Select(path => (Path: path, Stamp: Stamp(path))).ToArray();
            var entries = paths.Where(Directory.Exists).Select(path =>
                new NativePluginDirectorySource(path, "selected-loose-directory:" + source.StackId + ":" + path)).ToArray();
            return new(entries, WinningDirectory, Verify, "selected-physical-directory-graph:" + source.StackId + ":" + directory);

            void Verify()
            {
                foreach (var original in stamps)
                    if (!StringComparer.Ordinal.Equals(original.Stamp, Stamp(original.Path)))
                        throw new InvalidDataException("Selected native directory source changed during its actual search lifetime: " + original.Path);
            }
        }
        string? WinningDirectory(string virtualDirectory)
        {
            var paths = Paths(NativePluginPrivateIo.Canonical(virtualDirectory)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Any(File.Exists) && paths.Any(Directory.Exists))
                throw new NotSupportedException("Selected native namespace has an unowned file/directory override collision: " + virtualDirectory);
            return paths.FirstOrDefault(Directory.Exists);
        }
        IEnumerable<string> Paths(string virtualDirectory)
        {
            // Keep the same logical-root election as Create's actual file
            // resolver. Later selected roots override earlier roots.
            var root = roots.FirstOrDefault(candidate => NativePluginPrivateIo.Within(candidate, virtualDirectory));
            if (root is not null)
            {
                var relative = Path.GetRelativePath(root, virtualDirectory);
                foreach (var layer in roots.Reverse())
                {
                    var path = NativePluginPrivateIo.Canonical(Path.GetFullPath(relative, layer));
                    NativePluginPrivateIo.NoReparse(path); yield return path;
                }
                yield break;
            }
            var selected = declarations.Where(row => StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(row.Key), virtualDirectory)).ToArray();
            if (selected.Length != 0)
            {
                foreach (var row in selected)
                {
                    if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(row.Key), Path.GetFileName(row.Value.PhysicalPath)))
                        throw new NotSupportedException("Native directory declaration renames a physical source file without a filename/alias owner.");
                    var parent = NativePluginPrivateIo.Canonical(Path.GetDirectoryName(row.Value.PhysicalPath)!);
                    NativePluginPrivateIo.NoReparse(parent); yield return parent;
                }
                yield break;
            }
            if (NativePluginPrivateIo.Within(runtimeDirectory, virtualDirectory))
            {
                // Real runtime folders are source metadata, not automatic file
                // admission. Every reached file still needs Create's winner.
                NativePluginPrivateIo.NoReparse(virtualDirectory); yield return virtualDirectory; yield break;
            }
            throw new NotSupportedException("Native directory has no selected source logical-root/configuration declaration owner: " + virtualDirectory);
        }
    }
    private static string Stamp(string directory)
    {
        NativePluginPrivateIo.NoReparse(directory);
        if (!Directory.Exists(directory)) return File.Exists(directory) ? "file" : "absent";
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var name = Path.GetFileName(path);
            if (!names.TryAdd(name, path)) throw new InvalidDataException("Selected native source directory has an ambiguous case identity: " + name);
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in names.OrderBy(row => row.Key, StringComparer.OrdinalIgnoreCase))
        {
            NativePluginPrivateIo.NoReparse(row.Value);
            var attributes = File.GetAttributes(row.Value); var bytes = Encoding.UTF8.GetBytes(row.Key + "\0"); hash.AppendData(bytes);
            hash.AppendData(BitConverter.GetBytes((uint)attributes));
            hash.AppendData(BitConverter.GetBytes(File.GetLastWriteTimeUtc(row.Value).Ticks));
            if ((attributes & FileAttributes.Directory) == 0) hash.AppendData(BitConverter.GetBytes(new FileInfo(row.Value).Length));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
