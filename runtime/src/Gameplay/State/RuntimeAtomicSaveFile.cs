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
            Publish(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Publish(string temporary, string path, bool overwrite)
    {
        for (var attempt = 0; ; ++attempt)
        {
            try { File.Move(temporary, path, overwrite); return; }
            catch (Exception error) when (attempt < 5 && IsBusyReplacement(error, path, overwrite))
            {
                // A current-file reader can briefly deny Windows rename even
                // after the complete candidate is flushed. Retry publication
                // only; the authoritative writer and its effects never replay.
                Thread.Sleep(20 << attempt);
            }
        }
    }

    private static bool IsBusyReplacement(Exception error, string path, bool overwrite)
    {
        if (!OperatingSystem.IsWindows() || !overwrite || !File.Exists(path) ||
            error is not (IOException or UnauthorizedAccessException)) return false;
        var code = error.HResult & 0xffff;
        if (code is 32 or 33) return true;
        // Windows also reports a held existing destination as ACCESS_DENIED.
        // Read-only files and unrelated I/O errors are permanent refusals.
        if (code != 5) return false;
        try { return (File.GetAttributes(path) & FileAttributes.ReadOnly) == 0; }
        catch (Exception attributes) when (attributes is IOException or UnauthorizedAccessException) { return false; }
    }
}
