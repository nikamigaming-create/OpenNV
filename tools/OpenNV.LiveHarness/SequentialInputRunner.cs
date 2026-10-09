using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Diagnostics.Parity;

namespace OpenNV.LiveHarness;

internal sealed record RetailInputRecordConfiguration(string RetailCommandDirectory, int RetailProcessId,
    string RetailExecutablePath, string? RetailCheckpointPath, string ActionsPath, string InputTapePath,
    string ReceiptJournalPath, string Alignment = "unjoined-diagnostic", string? OpenNvCommandDirectory = null,
    int? OpenNvProcessId = null, string? RetailStatePath = null, int ReceiptTimeoutMilliseconds = 5000,
    long MaximumDispatchLatenessMicroseconds = 250_000, long MaximumDeliveryWindowMicroseconds = 250_000,
    int MaximumDurationMilliseconds = 1_800_000);

internal sealed record GodotInputReplayConfiguration(string OpenNvCommandDirectory, int OpenNvProcessId,
    string InputTapePath, string TransportJournalPath, string DeliveryJournalPath, string? CheckpointId = null,
    long MaximumLatenessMicroseconds = 250_000, int ReceiptTimeoutMilliseconds = 5000,
    int MaximumDurationMilliseconds = 1_800_000);

internal sealed record ScheduledOrdinaryInput(long Microseconds, JsonElement Input);

internal sealed record OrdinaryInputDeliveryConfiguration(string CommandDirectory, string Engine,
    int ProcessId, JsonElement Input, int ReceiptTimeoutMilliseconds = 5000);

// This runner records acknowledged ordinary adapter requests, then delivers that
// private tape to the existing Godot input owner. Retail state is never gameplay
// authority. Source/checkpoint binding and unjoined diagnostics are separate CLI
// routes; neither establishes matched state, effect, frame or pixel parity.
internal static class SequentialInputRunner
{
    internal static bool TryRun(string[] args, out int result)
    {
        result = 0;
        if (args is not ["--record-retail-input" or "--replay-input" or "--replay-unjoined-input" or "--send-ordinary-input", _]) return false;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            object report = args[0] switch
            {
                "--record-retail-input" => RecordAsync(ReadConfiguration<RetailInputRecordConfiguration>(args[1]), cancellation.Token).GetAwaiter().GetResult(),
                "--send-ordinary-input" => SendOrdinaryAsync(ReadConfiguration<OrdinaryInputDeliveryConfiguration>(args[1]), cancellation.Token).GetAwaiter().GetResult(),
                _ => ReplayAsync(ReadConfiguration<GodotInputReplayConfiguration>(args[1]), args[0] == "--replay-unjoined-input", cancellation.Token).GetAwaiter().GetResult()
            };
            Console.WriteLine(JsonSerializer.Serialize(report, Program.Json));
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-sequential-input-failure/v1",
                operation = args[0], errorType = failure.GetType().FullName, error = failure.ToString(),
                retry = "refused; entered delivery prefixes may exist", gameplayParity = "unverified", framesRecorded = false }, Program.Json));
            result = 1;
        }
        finally { Console.CancelKeyPress -= handler; }
        return true;
    }

    private static T ReadConfiguration<T>(string path) where T : class
    {
        Absolute(path);
        var json = SequentialInputCommandClient.ReadJson(path);
        return json.Deserialize<T>(RecordedInputTape.Json) ?? throw new InvalidDataException("Sequential input configuration is absent.");
    }

    private static async Task<object> SendOrdinaryAsync(OrdinaryInputDeliveryConfiguration configuration, CancellationToken cancellation)
    {
        RecordedInputTape.ValidateInput(configuration.Input);
        var clock = Stopwatch.StartNew();
        long Microseconds() => checked((long)((decimal)clock.ElapsedTicks * 1_000_000 / Stopwatch.Frequency));
        using var client = new SequentialInputCommandClient(configuration.CommandDirectory, configuration.Engine,
            configuration.ProcessId, configuration.ReceiptTimeoutMilliseconds, Microseconds);
        try
        {
            var command = configuration.Engine == "retail" ? RetailCommand(configuration.Input) :
                JsonSerializer.Serialize(configuration.Input, Program.Json);
            var receipt = await client.SendAsync(command, cancellation).ConfigureAwait(false);
            return new { schema = "opennv-ordinary-input-delivery/v1", engine = configuration.Engine,
                process = configuration.ProcessId, input = configuration.Input, receipt,
                ordinaryInput = true, gameplayEffect = "unverified", framesRecorded = false };
        }
        catch
        {
            client.RequestStop();
            throw;
        }
    }

    private static async Task<object> RecordAsync(RetailInputRecordConfiguration configuration, CancellationToken cancellation)
    {
        Validate(configuration);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        bounded.CancelAfter(configuration.MaximumDurationMilliseconds); cancellation = bounded.Token;
        var clockOrigin = Stopwatch.GetTimestamp();
        var clock = Stopwatch.StartNew();
        long Microseconds() => checked((long)((decimal)(Stopwatch.GetTimestamp() - clockOrigin) * 1_000_000 / Stopwatch.Frequency));
        using var retail = new SequentialInputCommandClient(configuration.RetailCommandDirectory, "retail",
            configuration.RetailProcessId, configuration.ReceiptTimeoutMilliseconds, Microseconds);
        var actualExecutable = ActualProcessExecutable(configuration.RetailProcessId);
        var executableHash = HashFile(actualExecutable);
        if (executableHash != HashFile(configuration.RetailExecutablePath))
            throw new InvalidDataException("The selected executable bytes differ from the actual retail process executable.");
        var checkpointHash = configuration.RetailCheckpointPath is { } retailCheckpoint ? HashFile(retailCheckpoint) : null;
        RecordedInputBinding? binding = null;
        JsonElement? exportedBinding = null;
        if (configuration.Alignment == "checkpoint-bound")
        {
            using var opennv = new SequentialInputCommandClient(configuration.OpenNvCommandDirectory!, "opennv",
                configuration.OpenNvProcessId!.Value, configuration.ReceiptTimeoutMilliseconds, Microseconds);
            exportedBinding = await ExportActualBinding(opennv, cancellation).ConfigureAwait(false);
            binding = exportedBinding.Value.GetProperty("binding").Deserialize<RecordedInputBinding>(RecordedInputTape.Json)
                ?? throw new InvalidDataException("The actual OpenNV checkpoint binding is absent.");
            RecordedInputTape.ValidateBinding(binding);
            if (ReadRetailScene(configuration) != binding.StateKey)
                throw new InvalidDataException("The actual initial neutral retail scene differs from the prepared OpenNV checkpoint scene.");
        }
        var header = new RecordedInputHeader(binding is null ? RecordedInputTape.UnjoinedSchema : RecordedInputTape.Schema,
            "retail", binding, checkpointHash, "receipt-observed", executableHash);
        var started = Microseconds();
        long SegmentClock() => Microseconds() - started;
        using var journal = new RecordedInputDeliveryJournal(configuration.ReceiptJournalPath, new
        {
            engine = "retail", configuration.RetailProcessId, executableHash, checkpointHash, exportedBinding,
            alignment = configuration.Alignment, timing = "receipt-observed; true delivery lies within sent/observed interval",
            nativeTimestamp = "unavailable", retailCheckpointLoad = "unverified", gameplayStateAlignment = "unverified",
            clockOriginTicks = clockOrigin, frequency = Stopwatch.Frequency, segmentOffsetMicroseconds = started,
            actualExecutablePath = actualExecutable, initialAdapterState = retail.ReadState(), inputAdapter = "separate native device command bridge",
            limits = new { configuration.MaximumDispatchLatenessMicroseconds, configuration.MaximumDeliveryWindowMicroseconds }
        });
        using var writer = new RecordedInputWriter(configuration.InputTapePath, header);
        var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var completed = 0L;
        var maximumWindow = 0L;
        try
        {
            var file = configuration.ActionsPath == "-" ? Console.In :
                new StreamReader(new FileStream(configuration.ActionsPath, FileMode.Open, FileAccess.Read, FileShare.Read), new UTF8Encoding(false, true));
            if (configuration.ActionsPath == "-")
                Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-live-input-recording-ready/v1", ready = true,
                    alignment = configuration.Alignment, header, input = "ordinary key/look JSON lines on stdin; EOF explicitly completes",
                    gameplayParity = "unverified", framesRecorded = false }, Program.Json));
            long previous = 0;
            try
            {
                while (await file.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
                {
                    if (line.Length is < 1 or > 8192) throw new InvalidDataException("The ordinary action row is empty or exceeds its bounded extent.");
                    using var parsed = JsonDocument.Parse(line); RecordedInputTape.RequireUniqueFields(parsed.RootElement);
                    var action = configuration.ActionsPath == "-" && parsed.RootElement.TryGetProperty("op", out _)
                        ? new ScheduledOrdinaryInput(SegmentClock(), parsed.RootElement.Clone())
                        : parsed.RootElement.Deserialize<ScheduledOrdinaryInput>(RecordedInputTape.Json)
                            ?? throw new InvalidDataException("An ordinary action is absent.");
                    if (action.Microseconds < previous || action.Microseconds < 0)
                        throw new InvalidDataException("The requested ordinary input schedule regressed.");
                    var command = RetailCommand(action.Input);
                    previous = action.Microseconds;
                    await WaitFor(action.Microseconds, SegmentClock, configuration.MaximumDurationMilliseconds, cancellation).ConfigureAwait(false);
                    var late = SegmentClock() - action.Microseconds;
                    if (late > configuration.MaximumDispatchLatenessMicroseconds)
                        throw new TimeoutException("A scheduled retail input is late; its recorded history will not be compressed or retried.");
                    await Deliver(action.Input, command, action.Microseconds).ConfigureAwait(false);
                }
            }
            finally { if (!ReferenceEquals(file, Console.In)) file.Dispose(); }
            // Normal completion explicitly retires every held key through the
            // same actual adapter. Lease expiry is still independently native.
            foreach (var key in held.ToArray())
            {
                var input = JsonSerializer.SerializeToElement(new { op = "key", key, pressed = false, leaseMilliseconds = 20 });
                await Deliver(input, RetailCommand(input), SegmentClock()).ConfigureAwait(false);
            }
            if (HashFile(actualExecutable) != executableHash ||
                configuration.RetailCheckpointPath is { } original && HashFile(original) != checkpointHash)
                throw new InvalidDataException("An original executable/checkpoint changed during the private input segment.");
            journal.Finish(SegmentClock(), null, new { acknowledgedInputs = completed, maximumReceiptWindowMicroseconds = maximumWindow,
                actualDeliveryTimestamp = "unavailable", adapterHeldKeys = held.Count });
            writer.Finish(SegmentClock());
        }
        catch (Exception failure)
        {
            Exception retained = failure;
            try { retail.RequestStop(); journal.Append("stop-requested", SegmentClock(), new { actualInputRetirement = "unobserved" }); }
            catch (Exception cleanup) { retained = new AggregateException(retained, cleanup); }
            try { writer.Finish(SegmentClock(), retained.ToString()); }
            catch (Exception footer) { retained = new AggregateException(retained, footer); }
            try { journal.Finish(SegmentClock(), retained.ToString(), new { acknowledgedInputs = completed, held = held.ToArray(), retry = "refused" }); }
            catch (Exception footer) { retained = new AggregateException(retained, footer); }
            throw new InvalidOperationException("The input segment retains its actual acknowledged prefix and refuses replay as complete.", retained);
        }
        return new { schema = "opennv-retail-input-record-run/v1", complete = true, inputs = completed,
            configuration.Alignment, inputTape = configuration.InputTapePath, inputTapeSha256 = HashFile(configuration.InputTapePath),
            receiptJournal = configuration.ReceiptJournalPath, receiptJournalSha256 = HashFile(configuration.ReceiptJournalPath),
            header, maximumReceiptWindowMicroseconds = maximumWindow, framesRecorded = false,
            nativeDeliveryTime = "bounded by receipt interval, not measured", gameplayParity = "unverified" };

        async Task Deliver(JsonElement input, string command, long requested)
        {
            Deadline(clock, configuration.MaximumDurationMilliseconds);
            var before = ReadRetailScene(configuration);
            journal.Append("delivery-entering", SegmentClock(), new { ordinal = completed + 1, requestedMicroseconds = requested, input, stateKey = before });
            var receipt = await retail.SendAsync(command, cancellation).ConfigureAwait(false);
            var observed = receipt.ObservedMicroseconds - started;
            var sent = receipt.SentMicroseconds - started;
            maximumWindow = Math.Max(maximumWindow, observed - sent);
            // Append only after the actual acknowledgement. Even a subsequent
            // evidence-window refusal retains this genuine committed prefix.
            if (receipt.Delivered)
            {
                writer.Append(observed, before, input); ++completed;
                if (input.GetProperty("op").GetString() == "key")
                {
                    var key = input.GetProperty("key").GetString()!;
                    if (input.GetProperty("pressed").GetBoolean()) held.Add(key); else held.Remove(key);
                }
            }
            journal.Append("adapter-returned", observed, new { ordinal = receipt.Delivered ? completed : completed + 1,
                input, receipt.Request, sentMicroseconds = sent, observedMicroseconds = observed,
                deliveryIntervalMicroseconds = observed - sent, receipt.Delivered, receipt.Message,
                receipt.LoopNotification, receipt.SentMonotonicTicks, receipt.ObservedMonotonicTicks, receipt.Frequency,
                stateBefore = before, adapterState = receipt.State, consumerDelivery = "unverified" });
            if (!receipt.Delivered) throw new InvalidOperationException(receipt.Message);
            if (observed - sent > configuration.MaximumDeliveryWindowMicroseconds)
                throw new TimeoutException("A native input was acknowledged outside the declared delivery interval; its prefix is retained and the segment refuses completion.");
        }
    }

    private static async Task<object> ReplayAsync(GodotInputReplayConfiguration configuration, bool diagnostic, CancellationToken cancellation)
    {
        Validate(configuration);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        bounded.CancelAfter(configuration.MaximumDurationMilliseconds); cancellation = bounded.Token;
        var tape = RecordedInputTape.Read(configuration.InputTapePath);
        if (tape.Unjoined != diagnostic)
            throw new InvalidDataException("Unjoined diagnostics and checkpoint-bound replay use separate CLI routes; no join is inferred.");
        var clockOrigin = Stopwatch.GetTimestamp();
        var clock = Stopwatch.StartNew();
        long Microseconds() => checked((long)((decimal)(Stopwatch.GetTimestamp() - clockOrigin) * 1_000_000 / Stopwatch.Frequency));
        using var opennv = new SequentialInputCommandClient(configuration.OpenNvCommandDirectory, "opennv",
            configuration.OpenNvProcessId, configuration.ReceiptTimeoutMilliseconds, Microseconds);
        using var journal = new RecordedInputDeliveryJournal(configuration.TransportJournalPath, new
        {
            engine = "opennv", configuration.OpenNvProcessId, tape = tape.Header, tapeDigest = tape.Footer.Sha256,
            alignment = diagnostic ? "unjoined-diagnostic" : "checkpoint-bound; retail-state-alignment-unverified",
            clockOriginTicks = clockOrigin, frequency = Stopwatch.Frequency,
            retailStateAuthority = false, framesRecorded = false
        });
        JsonElement? exported = null;
        try
        {
            if (!diagnostic)
            {
                if (!Guid.TryParseExact(configuration.CheckpointId, "N", out var checkpoint))
                    throw new InvalidDataException("Bound replay requires the genuine current campaign save-slot GUID.");
                await Send(new { op = "checkpoint.load", id = checkpoint.ToString("N"), pauseAfterLoad = false }).ConfigureAwait(false);
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested(); Deadline(clock, configuration.MaximumDurationMilliseconds);
                    var state = opennv.ReadState();
                    if (!state.GetProperty("checkpointTransitioning").GetBoolean() &&
                        state.TryGetProperty("checkpointRestored", out var restored) && restored.ValueKind == JsonValueKind.Object &&
                        Guid.TryParse(restored.GetProperty("id").GetString(), out var actual) && actual == checkpoint) break;
                    await Task.Delay(20, cancellation).ConfigureAwait(false);
                }
                exported = await ExportActualBinding(opennv, cancellation).ConfigureAwait(false);
                var binding = exported.Value.GetProperty("binding").Deserialize<RecordedInputBinding>(RecordedInputTape.Json)
                    ?? throw new InvalidDataException("The actual replay checkpoint binding is absent.");
                RecordedInputTape.RequireBinding(tape.Header.Binding ?? throw new InvalidDataException("A bound tape has no actual checkpoint."), binding);
                journal.Append("actual-checkpoint-prepared", Microseconds(), exported.Value);
            }
            else if (configuration.CheckpointId is not null)
                throw new InvalidDataException("Diagnostic replay does not imply or silently restore a checkpoint join.");
            var start = await Send(new { op = diagnostic ? "input.replay.diagnostic.start" : "input.replay.start",
                path = configuration.InputTapePath, maximumLatenessMicroseconds = configuration.MaximumLatenessMicroseconds,
                receiptJournal = configuration.DeliveryJournalPath }).ConfigureAwait(false);
            JsonElement playback;
            while (true)
            {
                cancellation.ThrowIfCancellationRequested(); Deadline(clock, configuration.MaximumDurationMilliseconds);
                var state = opennv.ReadState();
                var owner = state.GetProperty("recordedInput");
                if (owner.GetProperty("replayRequest").GetInt64() == start.Request && owner.TryGetProperty("playback", out playback) &&
                    playback.ValueKind == JsonValueKind.Object && !playback.GetProperty("active").GetBoolean()) break;
                await Task.Delay(20, cancellation).ConfigureAwait(false);
            }
            playback = playback.Clone();
            if (!playback.GetProperty("complete").GetBoolean())
                throw new InvalidOperationException("The actual Godot replay owner refused: " + playback.GetProperty("error").GetString());
            // Completion and the fresh runtime journal must both exist. The
            // journal footer is checked independently of a prior live snapshot.
            RequireCompleteJournal(configuration.DeliveryJournalPath, tape, configuration.OpenNvProcessId, start.Request);
            journal.Finish(Microseconds(), null, new { playback, exported });
            return new { schema = "opennv-sequential-input-replay-run/v1", complete = true, playback, exported,
                diagnostic, configuration.InputTapePath, inputTapeSha256 = HashFile(configuration.InputTapePath),
                deliveryJournal = configuration.DeliveryJournalPath, deliveryJournalSha256 = HashFile(configuration.DeliveryJournalPath),
                transportJournal = configuration.TransportJournalPath, transportJournalSha256 = HashFile(configuration.TransportJournalPath),
                retailStateAuthority = false, gameplayParity = "unverified", framesRecorded = false };
        }
        catch (Exception failure)
        {
            Exception retained = failure;
            try { opennv.RequestStop(); journal.Append("stop-requested", Microseconds(), new { actualInputRetirement = "unobserved" }); }
            catch (Exception cleanup) { retained = new AggregateException(retained, cleanup); }
            try { journal.Finish(Microseconds(), retained.ToString()); }
            catch (Exception footer) { retained = new AggregateException(retained, footer); }
            throw new InvalidOperationException("The Godot replay retains its exact delivered/attempted prefix and cannot be retried as continuation.", retained);
        }

        async Task<SequentialInputReceipt> Send(object command)
        {
            Deadline(clock, configuration.MaximumDurationMilliseconds);
            journal.Append("command-entering", Microseconds(), command);
            var receipt = await opennv.SendAsync(JsonSerializer.Serialize(command, Program.Json), cancellation).ConfigureAwait(false);
            journal.Append("command-returned", Microseconds(), receipt);
            if (!receipt.Delivered) throw new InvalidOperationException(receipt.Message);
            return receipt;
        }
    }

    private static async Task<JsonElement> ExportActualBinding(SequentialInputCommandClient client, CancellationToken cancellation)
    {
        var receipt = await client.SendAsync("{\"op\":\"input.binding\"}", cancellation).ConfigureAwait(false);
        if (!receipt.Delivered) throw new InvalidOperationException("No actual prepared OpenNV checkpoint binding: " + receipt.Message);
        var source = SequentialInputCommandClient.ReadJson(Path.Combine(client.Directory, $"{receipt.Request:D10}.input-binding.json"));
        if (source.GetProperty("schema").GetString() != "opennv-current-input-binding/v1" ||
            source.GetProperty("request").GetInt64() != receipt.Request || source.GetProperty("process").GetInt32() != client.ProcessId ||
            !source.GetProperty("prepared").GetBoolean())
            throw new InvalidDataException("Binding publication differs from its actual process/request/prepared checkpoint.");
        var binding = source.GetProperty("binding").Deserialize<RecordedInputBinding>(RecordedInputTape.Json)
            ?? throw new InvalidDataException("The native checkpoint binding is absent.");
        RecordedInputTape.ValidateBinding(binding);
        if (HashFile(source.GetProperty("checkpoint").GetProperty("path").GetString()!) != binding.CheckpointSha256)
            throw new InvalidDataException("The binding is not the actual unchanged OpenNV checkpoint file.");
        return source;
    }

    private static string ReadRetailScene(RetailInputRecordConfiguration configuration)
    {
        if (configuration.RetailStatePath is null) return "unjoined:retail-native-input";
        var observation = SequentialInputCommandClient.ReadJson(configuration.RetailStatePath);
        if (observation.GetProperty("process").GetInt32() != configuration.RetailProcessId)
            throw new InvalidDataException("The independent neutral retail scene belongs to another process.");
        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(configuration.RetailStatePath);
        if (age < TimeSpan.FromSeconds(-1) || age > TimeSpan.FromSeconds(5))
            throw new IOException("The independent retail scene observation is stale.");
        var key = observation.GetProperty("stateKey").GetString();
        return !string.IsNullOrWhiteSpace(key) ? key : throw new InvalidDataException("The independent retail observer has no neutral scene identity.");
    }

    // Reuses the currently admitted HarnessWindow physical DIK mapping. Unknown
    // devices/keys remain refusal; no SendInput, foreground or internal callback.
    private static readonly Dictionary<string, int> RetailKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["W"] = 17, ["A"] = 30, ["S"] = 31, ["D"] = 32,
        ["E"] = 18, ["F"] = 33, ["R"] = 19, ["Q"] = 16,
        ["Space"] = 57, ["Tab"] = 15, ["Enter"] = 28, ["Escape"] = 1,
        ["Up"] = 200, ["Down"] = 208, ["Left"] = 203, ["Right"] = 205,
        ["Shift"] = 42, ["Control"] = 29, ["1"] = 2, ["2"] = 3
    };

    private static string RetailCommand(JsonElement input)
    {
        RecordedInputTape.ValidateInput(input);
        switch (input.GetProperty("op").GetString())
        {
            case "key":
                if (!RetailKeys.TryGetValue(input.GetProperty("key").GetString()!, out var scanCode))
                    throw new NotSupportedException("This physical key has no admitted native DIK/Godot mapping.");
                return input.GetProperty("pressed").GetBoolean()
                    ? $"native.hold {scanCode} {input.GetProperty("leaseMilliseconds").GetInt32()}" : $"ReleaseKey {scanCode}";
            case "look":
                if (!input.GetProperty("dx").TryGetInt32(out var dx) || !input.GetProperty("dy").TryGetInt32(out var dy) ||
                    dx is < -32767 or > 32767 || dy is < -32767 or > 32767)
                    throw new InvalidDataException("Retail look requires bounded integral native mouse counts.");
                return $"native.look {dx} {dy}";
            default: throw new NotSupportedException("This retail recorder admits ordinary native key/look input; console, pointer, button callbacks and state writes are absent.");
        }
    }

    private static async Task WaitFor(long target, Func<long> clock, int durationMilliseconds, CancellationToken cancellation)
    {
        if (target > durationMilliseconds * 1000L) throw new TimeoutException("The requested action exceeds the bounded segment duration.");
        while (clock() < target)
            await Task.Delay((int)Math.Clamp((target - clock()) / 1000, 1, 10), cancellation).ConfigureAwait(false);
    }
    private static void Deadline(Stopwatch clock, int milliseconds)
    { if (clock.ElapsedMilliseconds >= milliseconds) throw new TimeoutException("The sequential input segment exceeded its real wall-clock deadline."); }
    private static string ActualProcessExecutable(int id)
    {
        using var process = Process.GetProcessById(id);
        return process.MainModule?.FileName ?? throw new IOException("The actual native process executable identity is unavailable.");
    }
    private static string HashFile(string path)
    {
        Absolute(path);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(source));
    }
    private static void Absolute(string path)
    { if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Sequential input paths must be absolute private paths."); }
    private static void Fresh(string path)
    { Absolute(path); if (File.Exists(path) || System.IO.Directory.Exists(path)) throw new IOException("Input evidence never overwrites an existing path."); }

    private static void Validate(RetailInputRecordConfiguration value)
    {
        foreach (var path in new[] { value.RetailCommandDirectory, value.RetailExecutablePath }) Absolute(path);
        if (value.ActionsPath != "-") Absolute(value.ActionsPath);
        Fresh(value.InputTapePath); Fresh(value.ReceiptJournalPath);
        if (!value.InputTapePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Recorded input requires a private .jsonl tape path.");
        if (value.RetailCheckpointPath is { } checkpoint) Absolute(checkpoint);
        if (value.RetailStatePath is { } state) Absolute(state);
        if (value.Alignment is not ("unjoined-diagnostic" or "checkpoint-bound") ||
            value.Alignment == "checkpoint-bound" && (value.OpenNvCommandDirectory is null || value.OpenNvProcessId is null ||
                value.RetailCheckpointPath is null || value.RetailStatePath is null))
            throw new InvalidDataException("Bound retail input requires real checkpoint identities and independent neutral scene observations.");
        if (value.Alignment == "unjoined-diagnostic" && (value.OpenNvCommandDirectory is not null || value.OpenNvProcessId is not null))
            throw new InvalidDataException("Unjoined recording cannot silently export or imply a paired checkpoint.");
        Limits(value.MaximumDurationMilliseconds, value.MaximumDispatchLatenessMicroseconds, value.ReceiptTimeoutMilliseconds);
        if (value.MaximumDeliveryWindowMicroseconds < 0) throw new ArgumentOutOfRangeException(nameof(value));
        DistinctPaths(value.InputTapePath, value.ReceiptJournalPath, value.RetailExecutablePath,
            value.ActionsPath == "-" ? null : value.ActionsPath, value.RetailCheckpointPath);
        OutsideInstallation(value.RetailExecutablePath, value.InputTapePath, value.ReceiptJournalPath);
    }
    private static void Validate(GodotInputReplayConfiguration value)
    {
        Absolute(value.OpenNvCommandDirectory); Absolute(value.InputTapePath); Fresh(value.TransportJournalPath); Fresh(value.DeliveryJournalPath);
        DistinctPaths(value.InputTapePath, value.TransportJournalPath, value.DeliveryJournalPath);
        Limits(value.MaximumDurationMilliseconds, value.MaximumLatenessMicroseconds, value.ReceiptTimeoutMilliseconds);
    }
    private static void Limits(int duration, long lateness, int receiptTimeout)
    {
        if (duration is < 20 or > 86_400_000 || lateness is < 0 or > 10_000_000 || receiptTimeout is < 20 or > 60_000)
            throw new ArgumentException("Input segment duration/receipt/lateness bounds are invalid.");
    }
    private static void DistinctPaths(params string?[] paths)
    {
        var canonical = paths.Where(path => path is not null).Select(path => Path.GetFullPath(path!)).ToArray();
        if (canonical.Distinct(StringComparer.OrdinalIgnoreCase).Count() != canonical.Length)
            throw new InvalidDataException("Private evidence paths alias an original/input or each other.");
    }
    private static void OutsideInstallation(string executable, params string[] paths)
    {
        var installation = Path.GetDirectoryName(Path.GetFullPath(executable))! + Path.DirectorySeparatorChar;
        if (paths.Any(path => Path.GetFullPath(path).StartsWith(installation, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Recorded inputs/receipts must remain outside the original installation.");
    }

    private static void RequireCompleteJournal(string path, RecordedInputTape tape, int process, long request)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false, true));
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long rows = 0;
        var footer = false;
        var headerSeen = false;
        while (reader.ReadLine() is { } line)
        {
            if (footer) throw new InvalidDataException("Delivery journal has trailing bytes after retirement.");
            using var parsed = JsonDocument.Parse(line); var value = parsed.RootElement;
            RecordedInputTape.RequireUniqueFields(value);
            if (value.TryGetProperty("complete", out var complete))
            {
                if (!complete.GetBoolean() || value.GetProperty("error").ValueKind != JsonValueKind.Null ||
                    value.GetProperty("entries").GetInt64() != rows ||
                    value.GetProperty("sha256").GetString() != Convert.ToHexString(digest.GetHashAndReset()))
                    throw new InvalidDataException("Delivery journal is failed, truncated or changed.");
                footer = true;
            }
            else
            {
                if (value.TryGetProperty("ordinal", out var ordinal))
                {
                    if (!headerSeen || ordinal.GetInt64() != ++rows || rows > tape.Inputs.Count)
                        throw new InvalidDataException("Delivery journal lost an input ordinal.");
                    var delivered = value.GetProperty("value");
                    if (value.GetProperty("phase").GetString() != "Godot-input-dispatch-returned" ||
                        delivered.GetProperty("inputOrdinal").GetInt64() != rows ||
                        JsonSerializer.Serialize(delivered.GetProperty("input"), RecordedInputTape.Json) !=
                        JsonSerializer.Serialize(tape.Inputs[(int)rows - 1].Input, RecordedInputTape.Json))
                        throw new InvalidDataException("Delivery journal differs from the actual input tape/dispatch prefix.");
                }
                else
                {
                    if (headerSeen || value.GetProperty("schema").GetString() != "opennv-input-delivery-journal/v1")
                        throw new InvalidDataException("Delivery journal has an invalid header.");
                    var header = value.GetProperty("header");
                    if (header.GetProperty("engine").GetString() != "opennv" || header.GetProperty("process").GetInt32() != process ||
                        header.GetProperty("request").GetInt64() != request || header.GetProperty("tapeDigest").GetString() != tape.Footer.Sha256)
                        throw new InvalidDataException("Delivery journal belongs to another native process/request/input tape.");
                    headerSeen = true;
                }
                RecordedInputTape.AppendHash(digest, line);
            }
        }
        if (!headerSeen || !footer || rows != tape.Inputs.Count)
            throw new InvalidDataException("Actual Godot delivery journal did not retire its complete input tape.");
    }
}
