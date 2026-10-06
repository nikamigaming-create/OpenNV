using System.Diagnostics;
using System.Text;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal readonly record struct LiveHarnessFileWrite(
    int Attempts, double Milliseconds, int? LastRetryError, string? LastRetryFailure);

internal static class LiveHarnessAtomicFile
{
    private static readonly UTF8Encoding Encoding = new(false);
    private const int SharingRetryMilliseconds = 50;

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

    internal static LiveHarnessFileWrite Write(string path, string text)
    {
        var started = Stopwatch.GetTimestamp();
        var pending = path + ".pending";
        File.WriteAllText(pending, text, Encoding);
        // Windows Move(overwrite) rejects a destination with an open reader,
        // even when it permits delete sharing. Replace preserves that reader's
        // complete old snapshot while new readers receive the complete new one.
        return Commit(() =>
        {
            if (File.Exists(path)) File.Replace(pending, path, null);
            else File.Move(pending, path);
        }, () => File.Exists(pending), started);
    }

    internal static LiveHarnessFileWrite Delete(string path) =>
        Commit(() => File.Delete(path), () => File.Exists(path), Stopwatch.GetTimestamp());

    private static LiveHarnessFileWrite Commit(Action commit, Func<bool> retained, long started)
    {
        var attempts = 0;
        int? lastRetryError = null;
        string? lastRetryFailure = null;
        while (true)
        {
            ++attempts;
            try
            {
                commit();
                return new(attempts, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    lastRetryError, lastRetryFailure);
            }
            catch (IOException error) when (OperatingSystem.IsWindows() &&
                (error.HResult & 0xffff) is 32 or 33 or 1175 && retained())
            {
                var remaining = SharingRetryMilliseconds - Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (remaining <= 0) throw;
                lastRetryError = error.HResult & 0xffff;
                lastRetryFailure = error.Message;
                Thread.Sleep((int)Math.Ceiling(Math.Min(5, remaining)));
            }
        }
    }
}
