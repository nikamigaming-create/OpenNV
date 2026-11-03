using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private sealed record OutgoingScriptIdle(string Owner, FalloutActorPackageIdleAnimation Source,
        RuntimeNativeNifAnimation Animation, FalloutIdleAnimationPlayback Clock,
        FalloutIdleDeactivation Fade, FalloutFurnitureClipSnapshot Other, int Slot);

    private FalloutActorIdleRequestSnapshot? _queuedScriptIdle;
    private readonly List<OutgoingScriptIdle> _outgoingScriptIdles = [];
    private FalloutIdleDeactivation? _scriptIdleEaseIn;
    private FalloutFurnitureClipSnapshot? _scriptIdleEaseFrom;
    private bool ScriptIdleSequenceActive => _scriptIdleClock is not null &&
        _scriptIdleRequestState == 2 && _scriptIdleSequenceState != 0;

    private void RequestCreatureScriptIdle(FalloutPluginStack stack, FalloutPluginRecord record)
        => RequestCreatureIdle(stack, record, "script");

    private void RequestCreatureIdle(FalloutPluginStack stack, FalloutPluginRecord record, string owner)
    {
        RequireCreatureIdleAdmission(stack, owner);
        var queued = new FalloutActorIdleRequestSnapshot(record.FormKey, Hash(record.ReadData()), owner, 1, 0);
        queued.Validate(stack);
        // The shared native duplicate predicate tests the actual bound IDLE;
        // it does not turn a different overlapping request into a VM fault.
        if (ScriptIdleSequenceActive && _aiState!.CreatureScriptIdle!.Animation.Idle == record.FormKey) return;
        _aiState!.RecordIdleRequest(stack, record.FormKey);
        _queuedScriptIdle = queued;
        UpdateCreatureIdleRequestReceipt();
        ProcessQueuedCreatureIdle();
    }

    private void RequireCreatureIdleAdmission(FalloutPluginStack stack, string owner)
    {
        if (!ReferenceEquals(stack, _aiRecords) || _aiState is not { } state || !IsInsideTree())
            throw new InvalidOperationException("Creature script idle has no live source binding.");
        var combat = Combat ?? throw new InvalidOperationException("Creature script idle has no physical pose owner.");
        if (Error is not null || state.ScriptError is not null || _scriptIdleSourceCancelled ||
            _eventIdleClock is not null || _conversationTarget is not null ||
            combat is not { Dead: false, OwnsPose: false } ||
            owner != "ambient" && (combat.PackageMoving || combat.AnimationWeapon is not null) ||
            state.Unconscious || state.Restrained || state.KnockedDown || !_aiWorld!.IsEnabled(state.Reference))
            throw new NotSupportedException("Creature script idle cannot compose its current pose or failed source owner.");
    }

    private void ProcessQueuedCreatureIdle()
    {
        if (_queuedScriptIdle is not { } queued || ScriptIdleSequenceActive && _scriptIdleSequenceState is 2 or 5) return;
        try
        {
            queued.Validate(_aiRecords!);
            PlayCreatureIdle(_aiRecords!, _aiRecords!.GetEffective(queued.Idle), queued.Owner, promoted: true);
            _queuedScriptIdle = null;
            UpdateCreatureIdleRequestReceipt();
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            _aiState!.ScriptError ??= $"Queued creature idle {queued.Idle}: {error.Message}";
            throw;
        }
    }

    private void RetainOutgoingCreatureIdle(RuntimeNativeNifAnimation incoming, FalloutFurnitureClipSnapshot other)
    {
        _scriptIdleEaseIn = null; _scriptIdleEaseFrom = null;
        if (!ScriptIdleSequenceActive) return;
        var saved = _aiState!.CreatureScriptIdle ?? throw new InvalidDataException("Outgoing creature idle lost its source.");
        var fade = new FalloutIdleDeactivation(GroupKeys(_scriptIdleAnimation!).Blend, ScriptIdleTransitionSettings(), GroupKeys(incoming).Blend);
        if (fade.Complete) return;
        var source = saved.Animation with { Clock = _scriptIdleClock!.Capture(), ResidualPose = CaptureScriptIdlePose() };
        _outgoingScriptIdles.Add(new(saved.Owner, source, _scriptIdleAnimation!, _scriptIdleClock!, fade, other,
            FalloutIdleTransition.ExecutionSlot.FromRecord(FalloutIdleAnimationData.Read(_aiRecords!.GetEffective(source.Idle)).Group).Index));
        _scriptIdleEaseIn = new(GroupKeys(_scriptIdleAnimation!).Blend, ScriptIdleTransitionSettings(), GroupKeys(incoming).Blend);
        _scriptIdleEaseFrom = new(saved.Animation.Idle, saved.Animation.IdleSha256,
            saved.Animation.Resource, saved.Animation.Sha256);
    }

    private void AdvanceOutgoingCreatureIdles(double delta)
    {
        _scriptIdleEaseIn?.Advance(delta);
        if (_scriptIdleEaseIn?.Complete == true)
        {
            _scriptIdleEaseIn = null; _scriptIdleEaseFrom = null;
            if (_scriptIdleSequenceState == 2) _scriptIdleSequenceState = 1;
        }
        for (var index = 0; index < _outgoingScriptIdles.Count;)
        {
            var outgoing = _outgoingScriptIdles[index];
            outgoing.Fade.Advance(delta);
            if (outgoing.Fade.Complete) { _outgoingScriptIdles.RemoveAt(index); continue; }
            outgoing.Clock.Advance(delta, interval => DispatchIdleInterval(outgoing.Animation, interval,
                outgoing.Source.Idle), processGroup: false);
            ++index;
        }
    }

    private FalloutCreatureIdleRequests? CaptureCreatureIdleRequests()
    {
        if (_queuedScriptIdle is null && _outgoingScriptIdles.Count == 0 && _scriptIdleEaseIn is null) return null;
        var outgoing = _outgoingScriptIdles.Select(value => new FalloutCreatureOutgoingIdle(value.Owner,
            value.Source with { Clock = value.Clock.Capture() }, value.Fade.Capture(), value.Other,
            value.Animation.AccumulationTime)).ToArray();
        return new(_queuedScriptIdle, outgoing, _scriptIdleEaseIn?.Capture(), _scriptIdleEaseFrom);
    }

    private void UpdateCreatureIdleRequestReceipt()
    {
        if (_aiState?.CreatureScriptIdle is { } saved)
            _aiState.CreatureScriptIdle = saved with { Requests = CaptureCreatureIdleRequests() };
    }

    private void RestoreCreatureIdleRequests(FalloutActorStoppedIdleAnimation saved)
    {
        if (saved.Requests is not { } requests) return;
        requests.Validate(_aiRecords!);
        _queuedScriptIdle = requests.Queued;
        foreach (var outgoing in requests.Outgoing)
        {
            var old = outgoing.Animation;
            var idle = FalloutActorIdleSource.Resolve(_aiRecords!, _aiRecords!.GetEffective(old.Idle));
            var source = ReadIdle(idle, externalAccumulationRoot: outgoing.Owner == "ambient");
            if (!source.Hash.Equals(old.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Outgoing creature idle differs from its winning KF.");
            var seq = source.Animation.Sequence;
            var clock = new FalloutIdleAnimationPlayback(seq.StartTime, seq.StopTime, seq.Frequency, seq.CycleType,
                source.Animation.TextKeys.Select(key => (key.Time, key.Value)).ToArray(), old.Clock.SelectedAdditionalLoops, seq.Name);
            clock.Restore(old.Clock);
            source.Animation.RestoreAccumulationTime(outgoing.AccumulationTime);
            var next = ReadIdle(FalloutActorIdleSource.Resolve(_aiRecords!, _aiRecords!.GetEffective(outgoing.Other.Idle)));
            ValidateCreatureIdleClip(outgoing.Other, next.Hash);
            var fade = new FalloutIdleDeactivation(GroupKeys(source.Animation).Blend, ScriptIdleTransitionSettings(), GroupKeys(next.Animation).Blend);
            fade.Restore(outgoing.Ease);
            _outgoingScriptIdles.Add(new(outgoing.Owner, old, source.Animation, clock, fade, outgoing.Other,
                FalloutIdleTransition.ExecutionSlot.FromRecord(FalloutIdleAnimationData.Read(_aiRecords!.GetEffective(old.Idle)).Group).Index));
        }
        if (requests.IncomingEase is { } retained)
        {
            var from = requests.IncomingFrom!;
            var previous = ReadIdle(FalloutActorIdleSource.Resolve(_aiRecords!, _aiRecords!.GetEffective(from.Idle)));
            ValidateCreatureIdleClip(from, previous.Hash);
            _scriptIdleEaseIn = new(GroupKeys(previous.Animation).Blend, ScriptIdleTransitionSettings(), GroupKeys(_scriptIdleAnimation!).Blend);
            _scriptIdleEaseIn.Restore(retained); _scriptIdleEaseFrom = from;
        }
    }

    private void ValidateCreatureIdleClip(FalloutFurnitureClipSnapshot clip, string sourceHash)
    {
        clip.Validate();
        var record = _aiRecords!.GetEffective(clip.Idle);
        if (record.Signature != "IDLE" || !Hash(record.ReadData()).Equals(clip.IdleSha256, StringComparison.OrdinalIgnoreCase) ||
            !FalloutActorIdleSource.Resolve(_aiRecords, record).AnimationPath.Equals(clip.Resource, StringComparison.OrdinalIgnoreCase) ||
            !sourceHash.Equals(clip.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Creature blend source differs from its winning IDLE/KF.");
    }

    private void ClearCreatureIdleRequests()
    {
        _queuedScriptIdle = null; _outgoingScriptIdles.Clear(); _scriptIdleEaseIn = null; _scriptIdleEaseFrom = null;
    }
}
