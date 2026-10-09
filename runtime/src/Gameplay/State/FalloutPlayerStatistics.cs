using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerStatisticHost(
    Func<FalloutStatisticMutation, uint, FalloutStatisticChallengeReceipt>? Challenge,
    Func<uint, FalloutStatisticMenuObservation>? ObserveStatsMenu,
    Func<FalloutStatisticMutation, FalloutStatisticMenuObservation, FalloutStatisticRefreshReceipt>? RefreshStatsMenu);

// A real counter commit and its source suffix are one attempted operation.
// Ordinary callback exceptions retain that exact prefix, including cold.
internal sealed class FalloutPlayerStatistics
{
    internal FalloutMiscellaneousStatisticSource Source { get; }
    private readonly int[] _values;
    private readonly FalloutPlayerStatisticHost _host;
    private bool _busy;
    private bool _retired;
    internal long Operations { get; private set; }
    internal FalloutStatisticOperation? LastOperation { get; private set; }
    internal string? Failure => LastOperation?.Error;
    internal string? SaveBlocker => _busy ? "miscellaneous-statistic-attempted-prefix" : null;
    internal object State => new { Source, values = _values.ToArray(), Operations, LastOperation, retired = _retired, saveBlocker = SaveBlocker };

    internal FalloutPlayerStatistics(FalloutMiscellaneousStatisticSource source, FalloutPlayerStatisticHost host,
        FalloutPlayerStatisticsSnapshot? restore = null)
    {
        source.Validate(); Source = source; ArgumentNullException.ThrowIfNull(host); _host = host;
        _values = source.Rows.Select(row => row.InitialValue).ToArray();
        if (restore is null) return;
        restore.Validate(); restore.Source.RequireCurrent(source);
        _values = restore.Values.ToArray(); Operations = restore.Operations; LastOperation = restore.LastOperation;
        // Consumers are not rerun on restore, including a failed entered suffix.
    }
    internal int Read(ushort index)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        RequireIndex(index); return _values[index];
    }
    internal void Mod(ushort index, int delta, string origin)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        // The source returns before touching index/value/UI when delta is zero.
        if (delta == 0) return;
        if (_busy) throw new InvalidOperationException("Statistic mutation reentered its actual suffix.");
        if (Failure is not null) throw new InvalidOperationException("A refused statistic prefix cannot be retried: " + Failure);
        RequireIndex(index); ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        var mutation = new FalloutStatisticMutation(checked(Operations + 1), Source.Identity, index, delta,
            _values[index], unchecked(_values[index] + delta), origin);
        _busy = true;
        try
        {
            _values[index] = mutation.After; Operations = mutation.Ordinal;
            LastOperation = new(mutation, FalloutStatisticPrefix.ValueCommitted, null, null, null, null, null);
            if (Source.ChallengeEvent is { } kind)
            {
                Prefix(FalloutStatisticPrefix.ChallengeEntered);
                var challenge = (_host.Challenge ?? throw new NotSupportedException("source-statistic-challenge-event-producer-unbound"))(mutation, kind);
                if (challenge is null || challenge.Mutation != mutation || challenge.Event != kind || challenge.CompletedEventOrdinal <= 0 ||
                    !FalloutAdvancementRuntimeReceipt.Digest(challenge.ProducerSha256))
                    throw new InvalidDataException("Statistic event returned a different/incomplete actual consumer receipt.");
                LastOperation = LastOperation! with { Challenge = challenge, Prefix = FalloutStatisticPrefix.ChallengeReturned };
            }
            Prefix(FalloutStatisticPrefix.MenuProbeEntered);
            var menu = (_host.ObserveStatsMenu ?? throw new NotSupportedException("source-statistic-native-menu-probe-unbound"))(Source.StatsMenuId) ??
                throw new InvalidDataException("Statistic native menu producer returned no observation.");
            menu.Validate(Source.StatsMenuId);
            LastOperation = LastOperation! with { Menu = menu,
                Prefix = menu.Present ? FalloutStatisticPrefix.MenuPresent : FalloutStatisticPrefix.MenuAbsent };
            if (menu.Present)
            {
                Prefix(FalloutStatisticPrefix.RefreshEntered);
                var refresh = (_host.RefreshStatsMenu ?? throw new NotSupportedException("source-statistic-native-menu-refresh-unbound"))(mutation, menu);
                if (refresh is null || refresh.Mutation != mutation || refresh.Menu != menu || refresh.CompletedCallOrdinal <= 0)
                    throw new InvalidDataException("Statistic native refresh returned a different/incomplete actual consumer receipt.");
                LastOperation = LastOperation! with { Refresh = refresh, Prefix = FalloutStatisticPrefix.RefreshReturned };
            }
            Prefix(FalloutStatisticPrefix.Complete);
        }
        catch (Exception error)
        {
            if (LastOperation is not null) LastOperation = LastOperation with
            { FailureType = error.GetType().FullName ?? error.GetType().Name, Error = error.Message };
            throw;
        }
        finally { _busy = false; }
    }
    private void Prefix(FalloutStatisticPrefix prefix) => LastOperation = LastOperation! with { Prefix = prefix };
    private void RequireIndex(ushort index)
    {
        if (index >= _values.Length) throw new NotSupportedException("Statistic enum has no actual selected catalogue row/error-console consumer.");
    }
    internal FalloutPlayerStatisticsSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy) throw new InvalidOperationException("Statistic operation is still executing its actual suffix.");
        var saved = new FalloutPlayerStatisticsSnapshot(Source, Array.AsReadOnly(_values.ToArray()), Operations, LastOperation);
        saved.Validate(); return saved;
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_busy) throw new InvalidOperationException("Statistic retirement reentered its actual committed consumer prefix.");
        _retired = true;
    }
}
