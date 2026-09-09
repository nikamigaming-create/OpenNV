using System.Text;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal static class LiveHarnessAtomicFile
{
    private static readonly UTF8Encoding Encoding = new(false);

    internal static bool TryRead(string path, out string text, out string? failure)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding);
            text = reader.ReadToEnd();
            failure = null;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            text = string.Empty;
            failure = error.Message;
            return false;
        }
    }

    internal static void Write(string path, string text)
    {
        var pending = path + ".pending";
        File.WriteAllText(pending, text, Encoding);
        // Windows Move(overwrite) rejects a destination with an open reader,
        // even when it permits delete sharing. Replace preserves that reader's
        // complete old snapshot while new readers receive the complete new one.
        if (File.Exists(path)) File.Replace(pending, path, null);
        else File.Move(pending, path);
    }
}
