using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNV.Runtime.Diagnostics.Parity;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("Live parity comparison requires Windows named memory.");

var options = ParseOptions(args);
var retailChannel = Required(options, "retail-channel");
var openNvChannel = Required(options, "opennv-channel");
if (retailChannel.Equals(openNvChannel, StringComparison.Ordinal))
    throw new ArgumentException("Retail and OpenNV parity channels must be distinct.");
var pairCount = PositiveInteger(options, "pairs", 1, 100000);
var timeoutSeconds = PositiveInteger(options, "timeout-seconds", 15, 3600);
var maximumPending = PositiveInteger(options, "maximum-pending", 256, 65536);
var outputRoot = options.GetValueOrDefault("output");

ParityTraceWriter? retailTrace = null;
ParityTraceWriter? openNvTrace = null;
if (outputRoot is not null)
{
    outputRoot = Path.GetFullPath(outputRoot);
    if (File.Exists(outputRoot) || Directory.Exists(outputRoot))
        throw new IOException($"Refusing to overwrite parity join output: {outputRoot}");
    Directory.CreateDirectory(outputRoot);
    retailTrace = new ParityTraceWriter(Path.Combine(outputRoot, "retail.onvtrace"));
    openNvTrace = new ParityTraceWriter(Path.Combine(outputRoot, "opennv.onvtrace"));
}

var rows = new List<JoinReport>(pairCount);
var joiner = new ParityLiveJoiner(maximumPending);
string? failure = null;
try
{
    using var retailRing = ParitySharedMemoryRing.CreateOrOpen(retailChannel);
    using var openNvRing = ParitySharedMemoryRing.CreateOrOpen(openNvChannel);
    var nextRetailRingSequence = 1L;
    var nextOpenNvRingSequence = 1L;
    var stopwatch = Stopwatch.StartNew();
    while (rows.Count < pairCount && stopwatch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
    {
        var progressed = false;
        progressed |= Drain(
            retailRing,
            ParityEngine.Retail,
            ref nextRetailRingSequence,
            retailTrace,
            joiner,
            rows,
            pairCount);
        progressed |= Drain(
            openNvRing,
            ParityEngine.OpenNv,
            ref nextOpenNvRingSequence,
            openNvTrace,
            joiner,
            rows,
            pairCount);
        if (!progressed)
            Thread.Sleep(2);
    }
    if (rows.Count != pairCount)
        throw new TimeoutException(
            $"Timed out after {timeoutSeconds}s waiting for {pairCount} candidate state pairs: " +
            $"candidates={rows.Count}, pendingRetail={joiner.PendingRetailFrames}, " +
            $"pendingOpenNV={joiner.PendingOpenNvFrames}.");
}
catch (Exception exception)
{
    failure = exception.Message;
    throw;
}
finally
{
    retailTrace?.Dispose();
    openNvTrace?.Dispose();
    if (outputRoot is not null)
        File.WriteAllText(
        Path.Combine(outputRoot, "join-report.json"),
        JsonSerializer.Serialize(
            new
            {
                schema = "opennv-parity-live-join-report/v2",
                retailChannel,
                openNvChannel,
                failure,
                pendingRetail = joiner.PendingRetailFrames,
                pendingOpenNv = joiner.PendingOpenNvFrames,
                finalFrames = "unobserved",
                pairs = rows,
            },
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            }));
}
Console.WriteLine(
    $"OPENNV_PARITY_LIVE_JOIN_COMPLETE pairs={rows.Count} " +
    $"state-bytes-exact={rows.Count(row => row.Exact)} diverged={rows.Count(row => !row.Exact)} " +
    $"unaligned={rows.Count(row => !row.ComparableState)} final-frames=unobserved " +
    $"output={outputRoot ?? "none"}");

static bool Drain(
    ParitySharedMemoryRing ring,
    ParityEngine expectedEngine,
    ref long nextRingSequence,
    ParityTraceWriter? trace,
    ParityLiveJoiner joiner,
    ICollection<JoinReport> rows,
    int requestedPairs)
{
    var progressed = false;
    while (rows.Count < requestedPairs && ring.TryRead(nextRingSequence, out var packet))
    {
        progressed = true;
        trace?.Append(packet);
        var frame = ParityTelemetryCodec.Decode(packet);
        if (frame.Engine != expectedEngine)
            throw new InvalidDataException(
                $"Parity channel expected {expectedEngine}, received {frame.Engine}.");
        var joined = joiner.Push(frame);
        if (joined is not null)
        {
            var comparison = joined.Comparison;
            var report = new JoinReport(
                joined.Retail.Sequence,
                joined.OpenNv.Sequence,
                joined.Retail.StateKey,
                joined.Retail.EventOrdinal,
                comparison.ComparableState,
                comparison.AlignmentFailure,
                comparison.ExactStateMatch,
                comparison.FirstStateByteOffset,
                comparison.SimulationTickDelta,
                comparison.MonotonicNanosecondsDelta,
                comparison.EventOrdinalDelta,
                comparison.Deltas);
            rows.Add(report);
            Console.WriteLine(
                $"OPENNV_PARITY_CANDIDATE pair={rows.Count} state={report.StateKey} " +
                $"event={report.EventOrdinal} retail={report.RetailSequence} " +
                $"opennv={report.OpenNvSequence} exact={(report.Exact ? 1 : 0)} " +
                $"aligned={(report.ComparableState ? 1 : 0)} deltas={report.FieldDeltas.Count}");
        }
        nextRingSequence = checked(nextRingSequence + 1);
    }
    return progressed;
}

static Dictionary<string, string> ParseOptions(string[] arguments)
{
    if (arguments.Length == 0 || arguments.Length % 2 != 0)
        throw new ArgumentException(
            "Usage: OpenNV.ParityLiveComparator --retail-channel <name> " +
            "--opennv-channel <name> [--pairs <count>] [--timeout-seconds <seconds>] " +
            "[--maximum-pending <count>] [--output <new-directory>]");
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (!arguments[index].StartsWith("--", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(arguments[index + 1]) ||
            !result.TryAdd(arguments[index][2..], arguments[index + 1]))
            throw new ArgumentException("Parity live comparator options are invalid or duplicated.");
    }
    var allowed = new HashSet<string>(
        ["retail-channel", "opennv-channel", "pairs", "timeout-seconds", "maximum-pending", "output"],
        StringComparer.Ordinal);
    if (result.Keys.Any(key => !allowed.Contains(key)))
        throw new ArgumentException("Parity live comparator received an unknown option.");
    return result;
}

static string Required(IReadOnlyDictionary<string, string> options, string name) =>
    options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required --{name} option.");

static int PositiveInteger(
    IReadOnlyDictionary<string, string> options,
    string name,
    int fallback,
    int maximum) =>
    options.TryGetValue(name, out var value)
        ? int.TryParse(value, out var parsed) && parsed > 0 && parsed <= maximum
            ? parsed
            : throw new ArgumentException($"--{name} must be between 1 and {maximum}.")
        : fallback;

internal sealed record JoinReport(
    ulong RetailSequence,
    ulong OpenNvSequence,
    string StateKey,
    ulong EventOrdinal,
    bool ComparableState,
    string? AlignmentFailure,
    bool Exact,
    int? FirstStateByteOffset,
    long SimulationTickDelta,
    long MonotonicNanosecondsDelta,
    long EventOrdinalDelta,
    IReadOnlyList<ParityFieldDelta> FieldDeltas);
