namespace OpenNV.Runtime.Gameplay.State;

internal static class RuntimeAtomicSaveFile
{
    internal static void Write(string path, ReadOnlySpan<byte> bytes, bool overwrite = true)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
