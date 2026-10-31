using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OpenNV.AnimationCompare;

// Same exclusive publication lane as the ordinary LiveHarness sequential
// input client. Constructing this class never publishes a game command.
internal sealed class RigFileCommandChannel : IDisposable
{
    private readonly string _directory, _ownerPath, _token;
    private readonly Process _process;
    private readonly FileStream _owner;
    private readonly int _pid;
    private long _cursor;
    private long? _unreturned;
    private bool _disposed;
    internal string DirectoryPath => _directory;

    internal RigFileCommandChannel(string directory, int processId)
    {
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory) || processId <= 0)
            throw new ArgumentException("Rig control requires an existing absolute channel and actual PID.");
        _directory = Path.GetFullPath(directory); _pid = processId;
        _process = Process.GetProcessById(processId);
        _ = _process.Handle;
        _ = _process.StartTime; // Keep actual process identity/handle, not a reusable PID alone.
        _ownerPath = Path.Combine(_directory, "sequential-input.owner");
        _token = JsonSerializer.Serialize(new { owner = "OpenNV.AnimationCompare", token = Guid.NewGuid(), process = Environment.ProcessId, targetProcess = processId });
        using var writer = LockWriter();
        FileStream? owned = null;
        try
        {
            owned = new(_ownerPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            _owner = owned;
            var bytes = Encoding.UTF8.GetBytes(_token); _owner.Write(bytes); _owner.Flush(true);
            _cursor = ReadState().GetProperty("nextCommandRequest").GetInt64();
            var commands = Directory.EnumerateFiles(_directory, "*.command").Take(100_001).ToArray();
            if (commands.Length > 100_000 || commands.Any(path => !long.TryParse(Path.GetFileNameWithoutExtension(path),
                NumberStyles.None, CultureInfo.InvariantCulture, out var request) || request >= _cursor))
                throw new IOException("Rig channel has pending/unbounded/unowned commands; use a quiescent channel.");
        }
        catch
        {
            owned?.Dispose();
            if (owned is not null) File.Delete(_ownerPath);
            _process.Dispose(); throw;
        }
    }

    internal JsonElement ReadState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process.HasExited) throw new IOException("The configured rig game process exited.");
        var state = ReadJson(Path.Combine(_directory, "live-state.json"), 4 * 1024 * 1024, retryMissingPublication: true);
        if (state.GetProperty("process").GetInt32() != _pid) throw new IOException("Rig state belongs to another process.");
        var age = Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency) - state.GetProperty("sampledNanoseconds").GetDouble();
        if (!double.IsFinite(age) || age < 0 || age > 5_000_000_000)
            throw new IOException("Rig state is stale; no old snapshot can acknowledge this command.");
        return state;
    }

    internal async Task<(long Request, JsonElement Receipt)> SendAsync(object command, CancellationToken cancellationToken)
    {
        await SettleAsync(cancellationToken);
        var text = JsonSerializer.Serialize(command);
        if (Encoding.UTF8.GetByteCount(text) > 4096) throw new ArgumentException("Rig command exceeds existing harness extent.");
        var request = _cursor;
        using (var writer = LockWriter())
        {
            using var owner = new FileStream(_ownerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(owner, Encoding.UTF8);
            if (reader.ReadToEnd() != _token) throw new IOException("Rig command channel ownership changed.");
            if (ReadState().GetProperty("nextCommandRequest").GetInt64() != request)
                throw new IOException("Another producer changed the rig command cursor.");
            var path = Path.Combine(_directory, $"{request:D10}.command");
            if (File.Exists(path) || File.Exists(Path.Combine(_directory, $"{request:D10}.receipt.json")))
                throw new IOException("Rig request identity already exists.");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                { var bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes); stream.Flush(true); }
                File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            _unreturned = request; _cursor = checked(request + 1);
        }
        var receipt = await SettleAsync(cancellationToken);
        return (request, receipt!.Value);
    }

    internal async Task<JsonElement?> SettleAsync(CancellationToken cancellationToken)
    {
        if (_unreturned is not { } request) return null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = ReadState();
            var path = Path.Combine(_directory, $"{request:D10}.receipt.json");
            if (File.Exists(path))
            {
                var receipt = ReadJson(path, 64 * 1024);
                if (receipt.GetProperty("request").GetInt64() != request) throw new InvalidDataException("Mismatched rig delivery receipt.");
                var cursor = state.GetProperty("nextCommandRequest").GetInt64();
                if (cursor > _cursor) throw new IOException("A competing producer advanced the rig channel.");
                if (cursor == _cursor)
                {
                    _unreturned = null;
                    if (!receipt.GetProperty("delivered").GetBoolean())
                        throw new NotSupportedException(receipt.GetProperty("message").GetString());
                    return receipt;
                }
            }
            await Task.Delay(20, cancellationToken);
        }
    }

    internal static JsonElement ReadJson(string path, long maximumBytes, bool retryMissingPublication = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > maximumBytes) throw new InvalidDataException("Rig JSON artifact exceeds its bounded extent.");
                using var document = JsonDocument.Parse(stream);
                return document.RootElement.Clone();
            }
            catch (IOException error) when (attempt < 5 &&
                ((error.HResult & 0xffff) is 32 or 33 || retryMissingPublication && error is FileNotFoundException))
            {
                // Replacement of the live publication can briefly hide its name
                // or hold it exclusively. Keep retries bounded; missing receipts,
                // malformed JSON and stale/process-mismatched state still fail.
                Thread.Sleep(10);
            }
        }
    }

    private FileStream LockWriter() => new(Path.Combine(_directory, "command-publication.lock"), FileMode.OpenOrCreate,
        FileAccess.ReadWrite, FileShare.None);

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try
        {
            using var writer = LockWriter();
            _owner.Dispose();
            var current = File.ReadAllText(_ownerPath);
            if (current == _token) File.Delete(_ownerPath);
        }
        finally { _process.Dispose(); }
    }
}
