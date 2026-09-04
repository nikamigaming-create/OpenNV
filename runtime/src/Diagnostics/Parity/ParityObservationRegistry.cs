using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed record ParityObservationSnapshot(
    IReadOnlyList<ParityTelemetryField> Fields,
    ulong EventOrdinal,
    int Discovered,
    int Observed,
    IReadOnlyList<string> Missing);

internal sealed class ParityObservationRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Observation> _observations =
        new(StringComparer.Ordinal);
    private ulong _eventOrdinal;

    internal void ReplaceScope(
        string scope,
        IEnumerable<(string Identity, ParityCategory Category, byte[] SourceState)> rows)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Parity observation scope is required.", nameof(scope));
        var materialized = rows.ToArray();
        if (materialized.Any(row =>
                string.IsNullOrWhiteSpace(row.Identity) ||
                !Enum.IsDefined(row.Category) ||
                row.SourceState.Length == 0) ||
            materialized.Select(row => row.Identity).Distinct(StringComparer.Ordinal).Count() !=
            materialized.Length)
            throw new InvalidDataException("Parity discovery rows are invalid or duplicated.");
        lock (_gate)
        {
            foreach (var key in _observations.Keys
                         .Where(key => key.StartsWith(scope + "/", StringComparison.Ordinal))
                         .ToArray())
                _observations.Remove(key);
            foreach (var row in materialized)
            {
                var key = $"{scope}/{row.Identity}";
                _observations.Add(key, new Observation(row.Category, row.SourceState, null));
            }
        }
    }

    internal void Observe(string scope, string identity, ReadOnlySpan<byte> runtimeState)
    {
        var key = $"{scope}/{identity}";
        lock (_gate)
        {
            if (!_observations.TryGetValue(key, out var source))
                throw new InvalidOperationException(
                    $"Parity runtime observation was not discovered from source: {key}.");
            if (runtimeState.IsEmpty)
                throw new InvalidDataException("Parity runtime observation state is empty.");
            _observations[key] = source with { RuntimeState = runtimeState.ToArray() };
        }
    }

    internal ulong RecordEvent(
        ParityCategory category,
        string identity,
        ReadOnlySpan<byte> state)
    {
        if (!Enum.IsDefined(category) || string.IsNullOrWhiteSpace(identity) || state.IsEmpty)
            throw new InvalidDataException("Parity event is invalid.");
        lock (_gate)
        {
            _eventOrdinal = checked(_eventOrdinal + 1);
            var key = $"events/{_eventOrdinal:D20}/{identity}";
            _observations.Add(key, new Observation(category, state.ToArray(), state.ToArray()));
            return _eventOrdinal;
        }
    }

    internal ParityObservationSnapshot Snapshot()
    {
        lock (_gate)
        {
            var fields = new List<ParityTelemetryField>(_observations.Count * 2 + 4);
            var missing = new List<string>();
            foreach (var (identity, observation) in _observations.OrderBy(row => row.Key))
            {
                fields.Add(ParityTelemetryField.Bytes(
                    observation.Category,
                    ParityStableId.FromName($"source/{identity}"),
                    observation.SourceState));
                if (observation.RuntimeState is { } runtime)
                    fields.Add(ParityTelemetryField.Bytes(
                        observation.Category,
                        ParityStableId.FromName($"runtime/{identity}"),
                        runtime));
                else
                    missing.Add(identity);
            }
            fields.Add(Count("coverage.discovered", _observations.Count));
            fields.Add(Count("coverage.observed", _observations.Count - missing.Count));
            fields.Add(Count("coverage.missing", missing.Count));
            fields.Add(ParityTelemetryField.Bytes(
                ParityCategory.Coverage,
                ParityStableId.FromName("coverage.missing.identity-sha256"),
                SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', missing)))));
            return new ParityObservationSnapshot(
                fields,
                _eventOrdinal,
                _observations.Count,
                _observations.Count - missing.Count,
                missing);
        }
    }

    private static ParityTelemetryField Count(string name, int value) =>
        ParityTelemetryField.UInt64(
            ParityCategory.Coverage,
            ParityStableId.FromName(name),
            checked((ulong)value));

    private sealed record Observation(
        ParityCategory Category,
        byte[] SourceState,
        byte[]? RuntimeState);
}
