using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenNV.LiveHarness;

internal sealed record SequentialInputReceipt(long Request, long SentMicroseconds, long ObservedMicroseconds,
    bool Delivered, string Message, long? LoopNotification, JsonElement State,
    long SentMonotonicTicks, long ObservedMonotonicTicks, long Frequency,
    bool RecipientExited = false, bool? CursorSettled = null, int? RecipientExitCode = null);

// The separate private adapter owns retail device input. This client knows
// only its public command/log envelope; it never calls a retail UI or engine API.
internal sealed class SequentialInputCommandClient : IDisposable
{
    private readonly string _directory, _engine, _token;
    private readonly int _processId, _timeoutMilliseconds;
    private readonly Func<long> _clock;
    private readonly FileStream _ownership;
    private readonly Process _process;
    private readonly string _ownershipPath;
    private long _nextRequest, _retailLogOffset;
    private bool _disposed;
    private readonly long? _keyboardDropped, _mouseDropped;
    internal int ProcessId => _processId;
    internal string Directory => _directory;

    internal SequentialInputCommandClient(string directory, string engine, int processId, int timeoutMilliseconds, Func<long> clock)
    {
        if (!Path.IsPathFullyQualified(directory) || !System.IO.Directory.Exists(directory) || engine is not ("retail" or "opennv") ||
            processId <= 0 || timeoutMilliseconds is < 20 or > 60_000)
            throw new ArgumentException("A sequential input client requires an existing absolute private channel, actual process and bounded receipt deadline.");
        (_directory, _engine, _processId, _timeoutMilliseconds, _clock) = (Path.GetFullPath(directory), engine, processId, timeoutMilliseconds, clock);
        _ownershipPath = Path.Combine(_directory, "sequential-input.owner");
        _token = JsonSerializer.Serialize(new { owner = "OpenNV.SequentialInput", token = Guid.NewGuid(),
            process = Environment.ProcessId, targetProcess = processId, engine }, Program.Json);
        using var publication = LockWriter(_directory);
        _ownership = new(_ownershipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        Process? process = null;
        try
        {
            process = Process.GetProcessById(_processId);
            _ = process.Handle; // Retain this recipient, including after exit; never reopen a reused PID.
            _process = process;
            var identity = Encoding.UTF8.GetBytes(_token); _ownership.Write(identity); _ownership.Flush(true);
            var state = ReadState();
            var published = PublishedRequests(_directory);
            if (engine == "opennv")
            {
                _nextRequest = state.GetProperty("nextCommandRequest").GetInt64();
                if (published.Any(value => value >= _nextRequest))
                    throw new InvalidOperationException("A prior OpenNV command remains queued; no sequential runner may overtake it.");
            }
            else
            {
                var logPath = Path.Combine(_directory, "bridge.log");
                var returned = new HashSet<long>();
                if (File.Exists(logPath))
                {
                    using var log = OpenShared(logPath);
                    if (log.Length > 64 * 1024 * 1024) throw new InvalidDataException("Private bridge history exceeds this bounded client; start a fresh channel.");
                    using var reader = new StreamReader(log, new UTF8Encoding(false, true));
                    while (reader.ReadLine() is { } line)
                        if (Returned.Match(line) is { Success: true } match)
                            returned.Add(long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
                    _retailLogOffset = log.Length;
                }
                if (published.Any(value => !returned.Contains(value)))
                    throw new InvalidOperationException("A prior retail command has no actual returned acknowledgement; no runner may overtake it.");
                _nextRequest = checked(Math.Max(published.DefaultIfEmpty().Max(), returned.DefaultIfEmpty().Max()) + 1);
                _keyboardDropped = state.GetProperty("keyboardDroppedEdges").GetInt64();
                _mouseDropped = state.GetProperty("mouseDroppedEdges").GetInt64();
            }
            if (_nextRequest < 1) throw new InvalidDataException("The live command cursor is invalid.");
        }
        catch
        {
            process?.Dispose();
            _ownership.Dispose();
            File.Delete(_ownershipPath);
            throw;
        }
    }

    private static readonly Regex Returned = new("^returned request=([0-9]+) consoleAccepted=([01])$", RegexOptions.CultureInvariant);
    private static readonly Regex Dispatched = new("^dispatch request=([0-9]+) loopNotification=([0-9]+)$", RegexOptions.CultureInvariant);
    private sealed class InputRecipientRetiredException() : IOException("The actual input process has retired.") { }

    internal JsonElement ReadState()
    {
        if (_process.HasExited) throw new InputRecipientRetiredException();
        var path = Path.Combine(_directory, "live-state.json");
        // A live producer may briefly replace its state filename between two
        // successful observations. Wait only for that publication gap; never
        // resend a command or treat malformed/stale/foreign state as transient.
        var publication = Stopwatch.StartNew();
        JsonElement state;
        while (true)
        {
            try { state = ReadJson(path); break; }
            catch (FileNotFoundException) when (publication.ElapsedMilliseconds < Math.Min(_timeoutMilliseconds, 250) && !_process.HasExited)
            { Thread.Sleep(5); }
            catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) is 32 or 33 or 1175 &&
                publication.ElapsedMilliseconds < Math.Min(_timeoutMilliseconds, 250) && !_process.HasExited)
            { Thread.Sleep(5); }
        }
        if (state.GetProperty("process").GetInt32() != _processId)
            throw new InvalidDataException("The command channel belongs to another native process.");
        if (_engine == "retail")
        {
            if (state.GetProperty("schema").GetString() != "opennv-private-native-bridge-state/v1" ||
                state.GetProperty("engine").GetString() != "retail" || state.GetProperty("inputProvider").GetString() != "harness-native-device")
                throw new NotSupportedException("Retail input has no admitted ordinary native-device adapter.");
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            if (age < TimeSpan.FromSeconds(-1) || age > TimeSpan.FromSeconds(5))
                throw new IOException("Retail native input state is stale; callback/loop counters cannot supply a missing clock.");
            if (_keyboardDropped is { } keyboard && state.GetProperty("keyboardDroppedEdges").GetInt64() != keyboard ||
                _mouseDropped is { } mouse && state.GetProperty("mouseDroppedEdges").GetInt64() != mouse)
                throw new IOException("The retail native device reports dropped input edges.");
        }
        else
        {
            var age = Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency) - state.GetProperty("sampledNanoseconds").GetDouble();
            if (!double.IsFinite(age) || age < 0 || age > 5_000_000_000)
                throw new IOException("OpenNV live input observation is stale.");
        }
        return state;
    }

    internal async Task<SequentialInputReceipt> SendAsync(string command, CancellationToken cancellation, bool expectedRecipientExit = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (expectedRecipientExit && _engine != "opennv")
            throw new ArgumentException("Expected recipient retirement requires an OpenNV delivery receipt.");
        if (command.Length is < 1 or > 4096 || command.IndexOfAny(['\r', '\n', '\0']) >= 0 ||
            _engine == "retail" && (Encoding.UTF8.GetByteCount(command) > 512 ||
                !command.StartsWith("native.hold ", StringComparison.Ordinal) &&
                !command.StartsWith("ReleaseKey ", StringComparison.Ordinal) && !command.StartsWith("native.look ", StringComparison.Ordinal)))
            throw new ArgumentException("Sequential retail recording admits bounded ordinary key/relative-look commands only.");
        var state = ReadState();
        var request = _nextRequest;
        var logPath = Path.Combine(_directory, "bridge.log");
        if (_engine == "retail")
        {
            using var log = OpenShared(logPath);
            if (log.Length < _retailLogOffset) throw new IOException("The native receipt log was replaced or truncated.");
            _retailLogOffset = log.Length;
        }
        var sentTicks = Stopwatch.GetTimestamp();
        var sent = _clock();
        using (var publication = LockWriter(_directory))
        {
            if (ReadOwnershipToken() != _token) throw new IOException("The sequential input publisher lost its real channel lease.");
            PublishNewCommand(_directory, request, command);
        }
        _nextRequest = checked(request + 1);
        var timeout = Stopwatch.StartNew();
        long? loop = null;
        var echoed = false;
        var scannedBytes = _retailLogOffset;
        while (timeout.ElapsedMilliseconds < _timeoutMilliseconds)
        {
            cancellation.ThrowIfCancellationRequested();
            if (_engine == "opennv")
            {
                var receiptPath = Path.Combine(_directory, $"{request:D10}.receipt.json");
                if (File.Exists(receiptPath))
                {
                    var receipt = ReadJson(receiptPath);
                    if (receipt.GetProperty("request").GetInt64() != request) throw new InvalidDataException("OpenNV acknowledged a different command request.");
                    var observed = _clock();
                    var observedTicks = Stopwatch.GetTimestamp();
                    var delivered = receipt.GetProperty("delivered").GetBoolean();
                    var message = receipt.GetProperty("message").GetString() ?? "";
                    // The real receipt can precede the asynchronous live-state
                    // publication. Preserve its timestamp and wait for that
                    // same entered command's cursor before releasing ownership.
                    // An explicitly expected normal exit retains the actual last
                    // observation instead of inventing a final cursor or state.
                    while (true)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (_process.HasExited)
                        {
                            if (!expectedRecipientExit || !delivered || _process.ExitCode != 0)
                                throw new IOException("The acknowledged input recipient exited without the expected normal retirement; the entered input is not retried.");
                            return new(request, sent, observed, delivered, message, null, state,
                                sentTicks, observedTicks, Stopwatch.Frequency, true,
                                state.GetProperty("nextCommandRequest").GetInt64() == checked(request + 1), _process.ExitCode);
                        }
                        try { state = ReadState(); }
                        catch (InputRecipientRetiredException) when (expectedRecipientExit) { continue; }
                        var cursor = state.GetProperty("nextCommandRequest").GetInt64();
                        if (cursor > checked(request + 1))
                            throw new IOException("A foreign command advanced OpenNV's cursor beyond the owned delivery.");
                        if (cursor == checked(request + 1) && !expectedRecipientExit)
                            return new(request, sent, observed, delivered, message, null, state,
                                sentTicks, observedTicks, Stopwatch.Frequency, CursorSettled: true);
                        if (timeout.ElapsedMilliseconds >= _timeoutMilliseconds)
                            throw new TimeoutException(expectedRecipientExit
                                ? "OpenNV returned a command receipt but its expected normal exit was not observed; the entered input is not retried."
                                : "OpenNV returned a command receipt but did not publish its committed cursor; the entered input is not retried.");
                        await Task.Delay(5, cancellation).ConfigureAwait(false);
                    }
                }
                state = ReadState();
            }
            else
            {
                state = ReadState();
                using var log = OpenShared(logPath);
                if (log.Length < scannedBytes) throw new IOException("The native receipt stream lost its byte cursor.");
                if (log.Length - _retailLogOffset > 1_048_576) throw new IOException("The native input receipt exceeded its bounded extent.");
                log.Position = scannedBytes;
                using var reader = new StreamReader(log, new UTF8Encoding(false, true));
                var text = reader.ReadToEnd();
                var end = text.LastIndexOf('\n');
                if (end >= 0)
                {
                    var complete = text[..(end + 1)];
                    scannedBytes += Encoding.UTF8.GetByteCount(complete);
                    foreach (var line in complete.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var dispatch = Dispatched.Match(line);
                        if (dispatch.Success)
                        {
                            if (long.Parse(dispatch.Groups[1].Value, CultureInfo.InvariantCulture) != request || loop is not null)
                                throw new IOException("A concurrent or duplicate native command dispatch broke sequential ownership.");
                            loop = long.Parse(dispatch.Groups[2].Value, CultureInfo.InvariantCulture);
                        }
                        else if (line == command && loop is not null && !echoed) echoed = true;
                        else if (Returned.Match(line) is { Success: true } returned)
                        {
                            if (long.Parse(returned.Groups[1].Value, CultureInfo.InvariantCulture) != request || loop is null || !echoed)
                                throw new InvalidDataException("Native acknowledgement has no matching actual command dispatch/echo.");
                            _retailLogOffset = scannedBytes;
                            var accepted = returned.Groups[2].Value == "1";
                            return new(request, sent, _clock(), accepted,
                                accepted ? "Native adapter returned accepted ordinary device input; gameplay consumption is observed separately." : "Native adapter refused ordinary input.", loop, state,
                                sentTicks, Stopwatch.GetTimestamp(), Stopwatch.Frequency);
                        }
                        else if (line.StartsWith("command rejected:", StringComparison.Ordinal))
                            throw new InvalidDataException("The native adapter rejected the complete command envelope.");
                    }
                }
            }
            await Task.Delay(5, cancellation).ConfigureAwait(false);
        }
        throw new TimeoutException($"Input request {request} has no genuine delivery receipt within {_timeoutMilliseconds}ms; an entered native prefix may exist.");
    }

    internal void RequestStop()
    {
        var path = Path.Combine(_directory, "stop.request");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            File.WriteAllText(temporary, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static JsonElement ReadJson(string path)
    {
        using var stream = OpenShared(path);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("A command/source observation exceeds the bounded JSON extent.");
        using var document = JsonDocument.Parse(stream);
        OpenNV.Runtime.Diagnostics.Parity.RecordedInputTape.RequireUniqueFields(document.RootElement);
        return document.RootElement.Clone();
    }

    private static FileStream OpenShared(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private string ReadOwnershipToken()
    {
        // This owner still holds a read/write handle. A default ReadAllText
        // reader denies that existing writer on Windows, even in this process.
        using var stream = OpenShared(_ownershipPath);
        if (stream.Length > 4096) throw new InvalidDataException("The private channel ownership token exceeds its bounded extent.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        return reader.ReadToEnd();
    }

    private static long[] PublishedRequests(string directory)
    {
        var rows = System.IO.Directory.EnumerateFiles(directory, "*.command").Take(100_001).ToArray();
        if (rows.Length > 100_000) throw new InvalidDataException("Command history exceeds the bounded sequential owner; use a fresh private channel.");
        return rows.Select(path => long.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value : throw new InvalidDataException("Command directory contains an unowned filename.")).ToArray();
    }

    private static FileStream LockWriter(string directory) => new(Path.Combine(directory, "command-publication.lock"),
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static void PublishNewCommand(string directory, long request, string text)
    {
        var path = Path.Combine(directory, $"{request:D10}.command");
        if (File.Exists(Path.Combine(directory, $"{request:D10}.receipt.json")))
            throw new IOException("This command request already has a receipt and cannot be reused.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { var bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // Narrow existing HarnessWindow.Send join: every producer uses the same
    // allocation/lease gate, including after the sequential runner has retired.
    internal static long PublishHarnessCommand(string directory, long preferredRequest, string text)
    {
        using var writer = LockWriter(directory);
        if (File.Exists(Path.Combine(directory, "sequential-input.owner")))
            throw new InvalidOperationException("A sequential input segment owns this private channel; stop that segment before other input.");
        var request = Math.Max(preferredRequest, checked(PublishedRequests(directory).DefaultIfEmpty().Max() + 1));
        PublishNewCommand(directory, request, text);
        return request;
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try
        {
            using var writer = LockWriter(_directory);
            bool same;
            try { same = ReadOwnershipToken() == _token; }
            finally { _ownership.Dispose(); }
            if (!same) throw new IOException("The command channel ownership changed; its replacement is retained.");
            File.Delete(_ownershipPath);
        }
        finally { _ownership.Dispose(); _process.Dispose(); }
    }
}
