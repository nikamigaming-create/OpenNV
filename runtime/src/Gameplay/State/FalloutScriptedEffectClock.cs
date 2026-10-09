using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutScriptedEffectClockSnapshot(string Schema, string SourceSha256,
    ulong LastConsumedMutation, FalloutRestWorldTimeSnapshot WorldPrefix, string? Failure);

// This is a lease on an actual committed gameplay clock mutation. Calling a
// method with an arbitrary Godot delta cannot create an effect update.
internal sealed class FalloutScriptedEffectPulse
{
    internal FalloutScriptedEffectClock Owner { get; }
    internal FalloutRestWorldTimeReceipt Receipt { get; }
    internal float Seconds => BitConverter.UInt32BitsToSingle(Receipt.AddedBits);
    internal float ConditionInterval => Owner.ConditionInterval;
    internal bool CompareElapsedAsFloat32 => Owner.CompareElapsedAsFloat32;
    internal ulong Mutation => Receipt.Mutation;
    internal FalloutScriptedEffectPulse(FalloutScriptedEffectClock owner, FalloutRestWorldTimeReceipt receipt)
    { Owner = owner; Receipt = receipt; }
    internal void Require() => Owner.RequirePulse(this);
}

internal sealed class FalloutScriptedEffectClock
{
    internal const string Schema = "opennv-scripted-effect-clock/v1";
    private readonly FalloutRestWorldTime _world;
    private readonly FalloutScriptedEffectClockSource _source;
    private readonly FalloutNumericGameSettings _settings;
    private readonly Func<FalloutAdvancementActivityObservation> _observeActualEffectUpdate;
    private ulong _consumed;
    private string? _failure;
    private FalloutScriptedEffectPulse? _entered;
    internal string SourceSha256 => _source.Identity;
    internal ulong LastConsumedMutation => _consumed;
    internal bool CompareElapsedAsFloat32 => _source.CompareElapsedAsFloat32;

    internal FalloutScriptedEffectClock(FalloutRestWorldTime world, FalloutSleepWaitSource source,
        FalloutNumericGameSettings settings, Func<FalloutAdvancementActivityObservation> observeActualEffectUpdate,
        FalloutScriptedEffectClockSnapshot? restore = null)
    {
        ArgumentNullException.ThrowIfNull(world); ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(observeActualEffectUpdate);
        _world = world; _source = FalloutScriptedEffectClockSource.Read(source); _settings = settings;
        _observeActualEffectUpdate = observeActualEffectUpdate;
        if (_source.WorldTimeSha256 != world.SourceSha256)
            throw new InvalidDataException("Effect clock is not attached to the actual selected world-time owner.");
        var current = world.Capture();
        if (restore is null) { _consumed = current.Mutations; return; }
        Validate(restore);
        if (restore.SourceSha256 != _source.Identity || restore.WorldPrefix != current &&
            !SameWorld(restore.WorldPrefix, current))
            throw new InvalidDataException("Saved effect clock does not share the current world-time prefix.");
        _consumed = restore.LastConsumedMutation; _failure = restore.Failure;
    }

    internal float ConditionInterval
    {
        get
        {
            // The original consumer reads the current setting on every poll.
            // A session setting change is not a copied/cold-baked interval.
            var value = _settings.Float("fActiveEffectConditionUpdateInterval");
            if (!float.IsFinite(value) || value <= 0)
                throw new NotSupportedException("Active-effect condition interval has no finite positive bucket domain.");
            return value;
        }
    }

    internal FalloutScriptedEffectPulse EnterSourceFrame()
    {
        if (_entered is not null) throw new InvalidOperationException("Effect clock update is reentrant.");
        if (_failure is not null) throw new NotSupportedException(_failure);
        var observed = _observeActualEffectUpdate() ?? throw new InvalidDataException("Actual effect-update observation is absent.");
        observed.Validate();
        if (observed.State != FalloutAdvancementActivityState.Satisfied)
            throw new NotSupportedException("Effects have no current actual player-update admission: " + observed.Owner);
        var receipt = _world.Last ?? throw new InvalidOperationException("Effects have no committed gameplay-clock mutation.");
        receipt.Process.Validate();
        if (receipt.Process != RuntimeSaveProcessIdentity.Current || receipt.Mutation != checked(_consumed + 1) ||
            receipt.Origin != FalloutRestWorldTimeOrigin.SourceFrame || receipt.Frame is null ||
            receipt.RestRequest is not null || receipt.RestHour is not null)
        {
            _failure = "Effect update is missing its next actual source frame; rest-hour effect traversal remains unowned.";
            throw new NotSupportedException(_failure);
        }
        var seconds = BitConverter.UInt32BitsToSingle(receipt.AddedBits);
        if (!float.IsFinite(seconds) || seconds < 0 || _world.SourceSha256 != _source.WorldTimeSha256)
            throw new InvalidDataException("Effect clock mutation has changed its source/delta.");
        _entered = new(this, receipt);
        return _entered;
    }

    internal void RequirePulse(FalloutScriptedEffectPulse pulse)
    {
        if (!ReferenceEquals(_entered, pulse) || !ReferenceEquals(pulse.Owner, this) ||
            !ReferenceEquals(_world.Last, pulse.Receipt) || pulse.Receipt.Process != RuntimeSaveProcessIdentity.Current ||
            pulse.Mutation != checked(_consumed + 1))
            throw new InvalidOperationException("Effect update lost its actual gameplay-clock lease.");
    }

    internal void Retire(FalloutScriptedEffectPulse pulse, Exception? failure)
    {
        if (!ReferenceEquals(_entered, pulse) || !ReferenceEquals(pulse.Owner, this))
            throw new InvalidOperationException("Effect-clock retirement belongs to another mutation.");
        // Even a failed callback may have committed elapsed time or a script
        // prefix. The actual pulse cannot be replayed after warm/cold restore.
        _consumed = pulse.Mutation;
        _failure = failure?.Message;
        if (!ReferenceEquals(_world.Last, pulse.Receipt))
            _failure = "Gameplay time changed during an entered scripted-effect update." +
                (failure is null ? "" : " Retained callback failure: " + failure.Message);
        _entered = null;
        if (failure is null && _failure is not null) throw new NotSupportedException(_failure);
    }

    internal FalloutScriptedEffectClockSnapshot Capture()
    {
        if (_entered is not null) throw new NotSupportedException("Saving an entered effect-clock pulse requires its suspension owner.");
        var world = _world.Capture();
        if (_failure is null && world.Mutations != _consumed)
            throw new NotSupportedException("Current gameplay-clock mutation has not retired its scripted-effect consumer.");
        return new(Schema, _source.Identity, _consumed, world, _failure);
    }

    internal static void Validate(FalloutScriptedEffectClockSnapshot state)
    {
        if (state is null || state.Schema != Schema || !FalloutAdvancementRuntimeReceipt.Digest(state.SourceSha256) ||
            state.WorldPrefix is null || state.LastConsumedMutation > state.WorldPrefix.Mutations ||
            state.Failure is { Length: 0 } || state.Failure is null && state.LastConsumedMutation != state.WorldPrefix.Mutations)
            throw new InvalidDataException("Saved scripted-effect clock lost its actual consumed/failure prefix.");
    }

    private static bool SameWorld(FalloutRestWorldTimeSnapshot a, FalloutRestWorldTimeSnapshot b) =>
        a.Schema == b.Schema && a.SourceSha256 == b.SourceSha256 && a.ValueBits == b.ValueBits &&
        a.Mutations == b.Mutations && a.Last == b.Last;
}
