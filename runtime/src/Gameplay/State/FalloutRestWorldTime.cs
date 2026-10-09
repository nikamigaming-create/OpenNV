using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutRestWorldTimeOrigin { SourceFrame, RestHour }
internal sealed record FalloutRestWorldTimeReceipt(ulong Mutation, FalloutRestWorldTimeOrigin Origin,
    uint BeforeBits, uint AddedBits, uint AfterBits, RuntimeSaveProcessIdentity Process,
    ulong? Frame, long? RestRequest, long? RestHour);
internal sealed record FalloutRestWorldTimeSnapshot(string Schema, string SourceSha256, uint ValueBits,
    ulong Mutations, FalloutRestWorldTimeReceipt? Last);

// The source elapsed-time field is separate from calendar globals. Advancing it
// neither dispatches a quest nor pretends to retire a magic/process effect.
internal sealed class FalloutRestWorldTime
{
    internal const string Schema = "opennv-rest-world-time/v1";
    private static readonly HashSet<(Guid Boot, string Engine)> ProcessInitializations = [];
    private readonly FalloutRestWorldTimeSource _source;
    private readonly Func<FalloutAdvancementActivityObservation> _sourceFrame;
    private ulong? _lastFrame;
    private long? _lastRestRequest, _lastRestHour;
    private float _value;
    private ulong _mutations;
    internal FalloutRestWorldTimeReceipt? Last { get; private set; }
    internal float Value => _value;
    internal string SourceSha256 => _source.Identity;

    internal FalloutRestWorldTime(FalloutSleepWaitSource restSource,
        Func<FalloutAdvancementActivityObservation> observeActualSourceFrame,
        FalloutRestWorldTimeSnapshot? restore = null)
    {
        ArgumentNullException.ThrowIfNull(observeActualSourceFrame);
        _source = FalloutRestWorldTimeSource.Read(restSource); _sourceFrame = observeActualSourceFrame;
        if (restore is not null)
        {
            RequireSnapshot(_source, restore);
            lock (ProcessInitializations)
                ProcessInitializations.Add((RuntimeSaveProcessIdentity.Current.Boot, restSource.EngineSha256));
            _value = BitConverter.UInt32BitsToSingle(restore.ValueBits); _mutations = restore.Mutations; Last = restore.Last;
            // Original loading restores this static value. A new native frame
            // lease is still required; no saved frame or hour is replayed.
            if (Last is { Origin: FalloutRestWorldTimeOrigin.RestHour })
            { _lastRestRequest = Last.RestRequest; _lastRestHour = Last.RestHour; }
            return;
        }
        // This is the reviewed original process-entry declaration, not a
        // per-CELL/session reset. Re-entering New Game in the same source
        // process needs its actual reset consumer and cannot reuse this zero.
        lock (ProcessInitializations)
            if (!ProcessInitializations.Add((RuntimeSaveProcessIdentity.Current.Boot, restSource.EngineSha256)))
                throw new NotSupportedException("Repeated fresh source world-time initialization has no admitted original reset owner.");
        _value = BitConverter.UInt32BitsToSingle(_source.InitialProcessBits);
    }

    internal void AdvanceActualSourceFrame(ulong frame, float simulationSeconds)
    {
        var observed = _sourceFrame() ?? throw new InvalidDataException("World-time frame observation is absent.");
        observed.Validate();
        if (observed.State != FalloutAdvancementActivityState.Satisfied)
            throw new NotSupportedException("Cumulative world time has no current source-frame admission: " + observed.Owner);
        if (_lastFrame is { } previous && frame <= previous)
            throw new InvalidOperationException("Cumulative world time cannot consume the same or an older source frame.");
        RequireDelta(simulationSeconds);
        _lastFrame = frame;
        Commit(FalloutRestWorldTimeOrigin.SourceFrame, simulationSeconds, frame, null, null);
    }

    internal void AdvanceRestHour(FalloutSleepWait rest, float simulationSeconds)
    {
        ArgumentNullException.ThrowIfNull(rest); _source.RequireSource(rest.Source);
        if (rest.LastHour is not { PreludeCommitted: true, WorldSecondsCommitted: false } hour ||
            hour.CalendarCommitted || hour.Finished || rest.Phase != FalloutRestPhase.Running ||
            rest.Request is null || hour.Ordinal != checked(rest.CommittedHours + 1) ||
            BitConverter.SingleToUInt32Bits(hour.SimulationSeconds) != BitConverter.SingleToUInt32Bits(simulationSeconds) ||
            _lastRestRequest is { } request && (rest.RequestOrdinal < request ||
                rest.RequestOrdinal == request && _lastRestHour is { } priorHour && hour.Ordinal <= priorHour))
            throw new InvalidOperationException("World time requires the actual next pre-calendar source rest-hour receipt.");
        RequireDelta(simulationSeconds);
        _lastRestRequest = rest.RequestOrdinal; _lastRestHour = hour.Ordinal;
        Commit(FalloutRestWorldTimeOrigin.RestHour, simulationSeconds, null, rest.RequestOrdinal, hour.Ordinal);
    }

    private void Commit(FalloutRestWorldTimeOrigin origin, float delta, ulong? frame, long? request, long? hour)
    {
        var nextMutation = checked(_mutations + 1);
        var before = BitConverter.SingleToUInt32Bits(_value);
        var next = _value + delta; // The source publishes this Float32 store before validation.
        if (!float.IsFinite(next) || next > _source.ResetAbove) next = 0;
        _value = next; _mutations = nextMutation;
        Last = new(nextMutation, origin, before, BitConverter.SingleToUInt32Bits(delta),
            BitConverter.SingleToUInt32Bits(next), RuntimeSaveProcessIdentity.Current, frame, request, hour);
    }

    private static void RequireDelta(float value)
    {
        if (!float.IsFinite(value) || value < 0) throw new InvalidDataException("Source elapsed-world-time delta is not finite/nonnegative.");
    }

    internal FalloutRestWorldTimeSnapshot Capture() => new(Schema, _source.Identity,
        BitConverter.SingleToUInt32Bits(_value), _mutations, Last);

    internal static void RequireSnapshot(FalloutRestWorldTimeSource source, FalloutRestWorldTimeSnapshot snapshot)
    {
        var value = BitConverter.UInt32BitsToSingle(snapshot.ValueBits);
        if (snapshot.Schema != Schema || snapshot.SourceSha256 != source.Identity || !float.IsFinite(value) ||
            value > source.ResetAbove || (snapshot.Mutations == 0) != (snapshot.Last is null) ||
            snapshot.Mutations == 0 && snapshot.ValueBits != source.InitialProcessBits)
            throw new InvalidDataException("Cumulative world time lacks its complete current source/value prefix.");
        if (snapshot.Last is not { } last) return;
        last.Process.Validate();
        var before = BitConverter.UInt32BitsToSingle(last.BeforeBits);
        var delta = BitConverter.UInt32BitsToSingle(last.AddedBits);
        var sum = before + delta;
        if (!float.IsFinite(sum) || sum > source.ResetAbove) sum = 0;
        if (last.Mutation != snapshot.Mutations || !Enum.IsDefined(last.Origin) || !float.IsFinite(before) ||
            before > source.ResetAbove || !float.IsFinite(delta) || delta < 0 || last.AfterBits != snapshot.ValueBits ||
            last.AfterBits != BitConverter.SingleToUInt32Bits(sum) ||
            (last.Origin == FalloutRestWorldTimeOrigin.SourceFrame) != (last.Frame is not null) ||
            (last.Origin == FalloutRestWorldTimeOrigin.RestHour) != (last.RestRequest is > 0 && last.RestHour is > 0) ||
            last.Origin == FalloutRestWorldTimeOrigin.SourceFrame && (last.RestRequest is not null || last.RestHour is not null) ||
            last.Origin == FalloutRestWorldTimeOrigin.RestHour && last.Frame is not null)
            throw new InvalidDataException("Cumulative world time changed its actual frame/hour mutation receipt.");
    }
}
