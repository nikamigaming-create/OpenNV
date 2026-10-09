using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutHardcoreValue(float Base, float Modifier0, float Modifier1, float Modifier2)
{
    internal float Current => (float)((double)Base + Modifier0 + Modifier1 + Modifier2);
    internal bool IsFinite => new[] { Base, Modifier0, Modifier1, Modifier2, Current }.All(float.IsFinite);
}
internal sealed record FalloutHardcoreNeedSnapshot(string SourceSha256, bool ClockInitialized, float CurrentMinute,
    float LastDehydrationMinute, float LastStarvationMinute, float LastSleepMinute, long Attempt,
    IReadOnlyDictionary<int, FalloutHardcoreValue> Pools, string? Failure)
{
    internal void Validate()
    {
        if (!FalloutPlayerPhysicalSource.Digest(SourceSha256) || Attempt < 0 ||
            new[] { CurrentMinute, LastDehydrationMinute, LastStarvationMinute, LastSleepMinute }.Any(value => !float.IsFinite(value)) ||
            Pools.Count != 3 || new[] { 73, 74, 75 }.Any(slot => !Pools.TryGetValue(slot, out var value) || !value.IsFinite) ||
            Failure is not null && (Attempt == 0 || string.IsNullOrWhiteSpace(Failure)))
            throw new InvalidDataException("Hardcore needs have an invalid current source/pool/clock prefix.");
    }
}

// Actual mutable AV pools and minute baselines share this owner. New-game base
// pools must be supplied by the selected AV initialization consumer; absence is
// not silently replaced by three zero values. Conditions/stage effects run on
// the committed pool and retain failures without retry through a cold save.
internal sealed class FalloutPlayerHardcoreNeeds
{
    private readonly FalloutSleepWaitSource _source;
    private readonly FalloutPluginStack _records;
    private readonly Action<int, FalloutHardcoreValue> _publishStageEffects;
    private readonly Action _consumeSourceReset;
    private readonly Func<FalloutGameTimeStamp> _readClock;
    private readonly Dictionary<int, FalloutHardcoreValue> _pools;
    private bool _initialized, _busy;
    private float _minute, _dehydration, _starvation, _sleep;
    private long _attempt;
    private string? _failure;
    internal string? Failure => _failure;
    internal string? SaveBlocker => _busy ? "hardcore-needs-operation-prefix" : null;
    internal object State => new
    {
        source = _source.Identity,
        initialized = _initialized,
        currentMinute = _minute,
        lastDehydrationMinute = _dehydration,
        lastStarvationMinute = _starvation,
        lastSleepMinute = _sleep,
        attempt = _attempt,
        pools = new Dictionary<int, FalloutHardcoreValue>(_pools),
        failure = _failure,
        saveBlocker = SaveBlocker
    };

    internal FalloutPlayerHardcoreNeeds(FalloutSleepWaitSource source, FalloutPluginStack records,
        IReadOnlyDictionary<int, FalloutHardcoreValue>? actualInitialPools, Action<int, FalloutHardcoreValue> publishStageEffects,
        Action consumeSourceReset, Func<FalloutGameTimeStamp> readClock,
        FalloutHardcoreNeedSnapshot? restore = null)
    {
        source.Validate(); _source = source; _records = records;
        if (!source.HasHardcoreConsumer) throw new NotSupportedException("Selected source has no Hardcore needs consumer.");
        ArgumentNullException.ThrowIfNull(publishStageEffects); ArgumentNullException.ThrowIfNull(consumeSourceReset);
        ArgumentNullException.ThrowIfNull(readClock);
        _publishStageEffects = publishStageEffects; _consumeSourceReset = consumeSourceReset; _readClock = readClock;
        _pools = new(actualInitialPools ?? (restore?.Pools ?? throw new NotSupportedException("Hardcore new-game pools have no selected source initialization consumer.")));
        if (restore is not null)
        {
            restore.Validate();
            if (restore.SourceSha256 != source.Identity) throw new InvalidDataException("Saved Hardcore clocks/pools belong to another source.");
            _pools = new(restore.Pools); _initialized = restore.ClockInitialized;
            _minute = restore.CurrentMinute;
            _dehydration = restore.LastDehydrationMinute; _starvation = restore.LastStarvationMinute;
            _sleep = restore.LastSleepMinute; _attempt = restore.Attempt; _failure = restore.Failure;
        }
        Capture().Validate();
    }
    internal float Read(int actorValue, FalloutActorValueRead kind)
    {
        RequireHealthy(); var value = Pool(actorValue);
        return kind switch
        {
            FalloutActorValueRead.Base => value.Base,
            FalloutActorValueRead.Permanent => throw new NotSupportedException("Hardcore permanent-value query requires its independently selected getter."),
            FalloutActorValueRead.Current => value.Current,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
    internal void AddSourceModifier(int slot, int sourceModifier, float amount) => Execute("source-modifier:" + sourceModifier, () =>
    {
        if (!float.IsFinite(amount)) throw new InvalidDataException("Hardcore pool input is not finite.");
        var old = Pool(slot);
        Commit(slot, sourceModifier switch
        {
            0 => old with { Modifier0 = old.Modifier0 + amount },
            1 => old with { Modifier1 = old.Modifier1 + amount },
            2 => old with { Modifier2 = old.Modifier2 + amount },
            _ => throw new NotSupportedException("Hardcore pool operation has no source modifier owner."),
        });
    });
    internal void Update(FalloutGameTimeStamp time, bool sleeping, FalloutRestObservation sourceNeedsControl,
        bool sourceForceReset)
    {
        RequireHealthy(); time.Validate(); sourceNeedsControl.Validate();
        if (sourceNeedsControl.State == FalloutRestFactState.Unowned)
            throw new NotSupportedException("Hardcore control predicate has no source owner: " + sourceNeedsControl.Reason);
        if (sourceNeedsControl.State == FalloutRestFactState.Denied) return;
        var minute = time.DaysPassed * 24f * 60f;
        if (!float.IsFinite(minute)) throw new InvalidDataException("Hardcore source minute clock overflowed.");
        var dehydrationInterval = Interval("fHCDehydrationRate", time.TimeScale);
        var sleepInterval = Interval("fHCSleepDeprivationRate", time.TimeScale);
        var starvationInterval = Interval("fHCStarvationRate", time.TimeScale);
        Execute("update-original-needs-minutes", () =>
        {
            _minute = minute;
            if (!_initialized || _dehydration == 0 || sourceForceReset)
            { _dehydration = _starvation = _sleep = minute; _initialized = true; _consumeSourceReset(); }
            UpdatePool(73, dehydrationInterval, ref _dehydration, minute, true);
            UpdatePool(75, sleepInterval, ref _sleep, minute, !sleeping);
            UpdatePool(74, starvationInterval, ref _starvation, minute, true);
        });
    }
    private float Interval(string setting, float timeScale)
    {
        var interval = (float)((double)_records.NumericSettings.Float(setting) * timeScale / 60.0);
        if (!float.IsFinite(interval) || interval <= 0)
            throw new NotSupportedException("Hardcore source interval has no positive finite consumer: " + setting);
        return interval;
    }
    private void UpdatePool(int slot, float interval, ref float previous, float minute, bool change)
    {
        var elapsed = minute - previous;
        if (elapsed <= interval) return;
        var quotient = Math.Truncate((double)elapsed / interval);
        if (quotient < int.MinValue || quotient > int.MaxValue)
            throw new NotSupportedException("Hardcore interval exceeds its source integer quotient.");
        var amount = (int)quotient;
        if (change) Commit(slot, Pool(slot) with { Modifier2 = Pool(slot).Modifier2 + amount });
        // It reads the current minute again after the pool/stage callback and
        // drops the residual interval. A failed callback retains the old
        // baseline; it cannot return to this operation through a cold replay.
        var current = _readClock(); current.Validate();
        previous = current.DaysPassed * 24f * 60f;
        if (!float.IsFinite(previous)) throw new InvalidDataException("Hardcore post-effect minute clock overflowed.");
    }
    internal void RestoreSleepDebt(FalloutRestHour hour) => Execute("restore-sleep-deprivation", () =>
    {
        if (!hour.Sleeping) return;
        var current = Pool(75);
        var amount = Math.Min(current.Current, _records.NumericSettings.Float("fHCSleepRestorationMod"));
        if (!float.IsFinite(amount)) throw new InvalidDataException("Hardcore sleep restoration is not finite.");
        Commit(75, current with { Modifier1 = current.Modifier1 - amount });
    });
    private FalloutHardcoreValue Pool(int slot) => _pools.TryGetValue(slot, out var value) ? value :
        throw new NotSupportedException("Actor value is not a selected Hardcore needs pool.");
    private void Commit(int slot, FalloutHardcoreValue value)
    {
        if (!value.IsFinite) throw new InvalidDataException("Hardcore pool commit overflowed.");
        _pools[slot] = value; _publishStageEffects(slot, value);
    }
    private void Execute(string operation, Action action)
    {
        RequireHealthy(); if (_busy) throw new InvalidOperationException("Hardcore needs reentry has no source ordering owner.");
        _attempt = checked(_attempt + 1); _busy = true;
        try { action(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { _failure ??= operation + ": " + error.GetType().Name + ": " + error.Message; throw; }
        finally { _busy = false; }
    }
    internal void RequireHealthy()
    { if (_failure is { } failure) throw new InvalidOperationException("Hardcore committed prefix failed: " + failure); }
    internal FalloutHardcoreNeedSnapshot Capture()
    {
        if (_busy) throw new NotSupportedException("Capture cannot split Hardcore needs pool/clock mutation.");
        var snapshot = new FalloutHardcoreNeedSnapshot(_source.Identity, _initialized, _minute, _dehydration, _starvation,
            _sleep, _attempt, new Dictionary<int, FalloutHardcoreValue>(_pools), _failure);
        snapshot.Validate(); return snapshot;
    }
}
