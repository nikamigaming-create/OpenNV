using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerStatisticHost(
    Func<FalloutStatisticMutation, uint, FalloutStatisticChallengeReceipt>? Challenge,
    Func<uint, FalloutStatisticMenuObservation>? ObserveStatsMenu,
    Func<FalloutStatisticMutation, FalloutStatisticMenuObservation, FalloutStatisticRefreshReceipt>? RefreshStatsMenu);

internal sealed class FalloutPlayerStatistics
{
    internal FalloutMiscellaneousStatisticSource Source { get; }
    private readonly int[] _values;
    private readonly FalloutPlayerStatisticHost _host;
    private readonly Stack<Frame> _executing = [];
    private Frame? _last;
    private FalloutStatisticOperation? _restoredLast;
    private FalloutChallenges? _challengeOwner;
    private IFalloutStatisticMenuSource? _menuOwner;
    private bool _retired;
    internal long Operations { get; private set; }
    internal FalloutStatisticOperation? LastOperation => _last?.Capture() ?? _restoredLast;
    internal string? Failure => LastOperation is { } last ? FindFailure(last) : null;
    internal string? SaveBlocker => _executing.Count != 0 ? "miscellaneous-statistic-attempted-prefix" : null;
    internal object State => new
    {
        Source,
        values = _values.ToArray(),
        Operations,
        LastOperation,
        retired = _retired,
        saveBlocker = SaveBlocker
    };

    internal FalloutPlayerStatistics(FalloutMiscellaneousStatisticSource source, FalloutPlayerStatisticHost host,
        FalloutPlayerStatisticsSnapshot? restore = null)
    {
        source.Validate(); Source = source; ArgumentNullException.ThrowIfNull(host); _host = host;
        _values = source.Rows.Select(row => row.InitialValue).ToArray();
        if (restore is null) return;
        restore.Validate(); restore.Source.RequireCurrent(source);
        _values = restore.Values.ToArray(); Operations = restore.Operations; _restoredLast = restore.LastOperation;
    }
    internal void BindChallengeOwner(FalloutChallenges owner)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_challengeOwner is not null || _executing.Count != 0 || owner.Source is null)
            throw new InvalidOperationException("Statistic challenge ownership requires one actual campaign binding.");
        owner.Source.Require(Source); _challengeOwner = owner;
    }
    internal void BindStatsMenuSource(IFalloutStatisticMenuSource producer)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        ArgumentNullException.ThrowIfNull(producer);
        if (_menuOwner is not null || _executing.Count != 0)
            throw new InvalidOperationException("The campaign statistic menu producer is bound or still executing.");
        producer.RequireSource(Source); _menuOwner = producer;
    }
    internal void RequireChallengeMutation(FalloutStatisticMutation mutation)
    {
        if (_retired || !_executing.TryPeek(out var frame) || frame.Value.Mutation != mutation ||
            frame.Value.Prefix != FalloutStatisticPrefix.ChallengeEntered || _values[mutation.Index] != mutation.After)
            throw new InvalidDataException("CHAL11 has no matching actual entered/committed counter operation.");
    }
    internal FalloutStatisticMutation RequireCommittedMutation(long ordinal, ushort index, int delta, string origin)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        var mutation = Find(LastOperation)?.Mutation;
        if (mutation is null || mutation.SourceSha256 != Source.Identity || mutation.Index != index ||
            mutation.Delta != delta || mutation.Origin != origin)
            throw new InvalidDataException("Challenge completion counter has no exact actual committed statistic operation.");
        return mutation;
        FalloutStatisticOperation? Find(FalloutStatisticOperation? row)
        {
            if (row is null || ordinal < row.Mutation.Ordinal) return null;
            if (row.Mutation.Ordinal == ordinal) return row;
            return (row.Children ?? []).Select(Find).FirstOrDefault(found => found is not null);
        }
    }
    internal int Read(ushort index)
    { ObjectDisposedException.ThrowIf(_retired, this); RequireIndex(index); return _values[index]; }

    internal void Mod(ushort index, int delta, string origin)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (delta == 0) return;
        if (_executing.Count != 0 && _challengeOwner?.AllowsNestedStatistic(this) != true)
            throw new InvalidOperationException("Statistic mutation reentered without its actual source completion/compiled reward owner.");
        if (Failure is not null) throw new InvalidOperationException("A refused statistic prefix cannot be retried: " + Failure);
        RequireIndex(index); ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        var mutation = new FalloutStatisticMutation(checked(Operations + 1), Source.Identity, index, delta,
            _values[index], unchecked(_values[index] + delta), origin);
        var current = new Frame(new(mutation, FalloutStatisticPrefix.ValueCommitted, null, null, null,
            null, null, [], mutation.Ordinal));
        if (_executing.TryPeek(out var parent)) parent.Children.Add(current); else _last = current;
        _executing.Push(current);
        try
        {
            _values[index] = mutation.After; Operations = mutation.Ordinal;
            if (Source.ChallengeEvent is { } kind)
            {
                Prefix(FalloutStatisticPrefix.ChallengeEntered);
                var challenge = _challengeOwner is { } actual ? actual.ConsumeStatistic(mutation, kind) :
                    (_host.Challenge ?? throw new NotSupportedException("source-statistic-challenge-event-producer-unbound"))(mutation, kind);
                if (challenge is null || challenge.Mutation != mutation || challenge.Event != kind || challenge.CompletedEventOrdinal <= 0 ||
                    !FalloutAdvancementRuntimeReceipt.Digest(challenge.ProducerSha256) ||
                    _challengeOwner is { } owner && challenge.ProducerSha256 != owner.Source!.Identity)
                    throw new InvalidDataException("Statistic event returned a different/incomplete actual consumer receipt.");
                current.Value = current.Value with { Challenge = challenge, Prefix = FalloutStatisticPrefix.ChallengeReturned };
            }
            Prefix(FalloutStatisticPrefix.MenuProbeEntered);
            var menu = (_menuOwner is { } actualMenu ? actualMenu.Observe(this, Source.StatsMenuId) :
                (_host.ObserveStatsMenu ?? throw new NotSupportedException("source-statistic-native-menu-probe-unbound"))(Source.StatsMenuId)) ??
                throw new InvalidDataException("Statistic native menu producer returned no observation.");
            menu.Validate(Source.StatsMenuId);
            _menuOwner?.RequireCurrent(menu);
            current.Value = current.Value with
            {
                Menu = menu,
                Prefix = menu.Present ? FalloutStatisticPrefix.MenuPresent : FalloutStatisticPrefix.MenuAbsent
            };
            if (menu.Present)
            {
                Prefix(FalloutStatisticPrefix.RefreshEntered);
                var refresh = _menuOwner is { } actualRefresh ? actualRefresh.Refresh(this, mutation, menu) :
                    (_host.RefreshStatsMenu ?? throw new NotSupportedException("source-statistic-native-menu-refresh-unbound"))(mutation, menu);
                if (refresh is null || refresh.Mutation != mutation || refresh.Menu != menu || refresh.CompletedCallOrdinal <= 0)
                    throw new InvalidDataException("Statistic native refresh returned a different/incomplete actual consumer receipt.");
                _menuOwner?.RequireCurrent(refresh);
                current.Value = current.Value with { Refresh = refresh, Prefix = FalloutStatisticPrefix.RefreshReturned };
            }
            if (current.Children.Any(child => FindFailure(child.Capture()) is not null))
                throw new InvalidOperationException("A source callback swallowed a retained nested statistic failure.");
            Prefix(FalloutStatisticPrefix.Complete);
        }
        catch (Exception error)
        {
            current.Value = current.Value with { FailureType = error.GetType().FullName ?? error.GetType().Name, Error = error.Message };
            throw;
        }
        finally { current.ThroughOrdinal = Operations; _executing.Pop(); }
        void Prefix(FalloutStatisticPrefix prefix) => current.Value = current.Value with { Prefix = prefix };
    }
    private static string? FindFailure(FalloutStatisticOperation operation) => operation.Error ??
        (operation.Children ?? []).Select(FindFailure).FirstOrDefault(error => error is not null);
    private void RequireIndex(ushort index)
    {
        if (index >= _values.Length) throw new NotSupportedException("Statistic enum has no actual selected catalogue row/error-console consumer.");
    }
    internal FalloutPlayerStatisticsSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_executing.Count != 0) throw new InvalidOperationException("Statistic operation is still executing its actual suffix.");
        var saved = new FalloutPlayerStatisticsSnapshot(Source, Array.AsReadOnly(_values.ToArray()), Operations, LastOperation);
        saved.Validate(); return saved;
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_executing.Count != 0) throw new InvalidOperationException("Statistic retirement reentered its committed consumer prefix.");
        _retired = true; _menuOwner = null;
    }
    private sealed class Frame(FalloutStatisticOperation value)
    {
        internal FalloutStatisticOperation Value = value;
        internal readonly List<Frame> Children = [];
        internal long ThroughOrdinal = value.Mutation.Ordinal;
        internal FalloutStatisticOperation Capture() => Value with
        { Children = Children.Select(child => child.Capture()).ToArray(), ThroughOrdinal = ThroughOrdinal };
    }
}
