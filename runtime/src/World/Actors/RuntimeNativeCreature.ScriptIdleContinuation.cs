using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutIdleDeactivation? _scriptIdleDeactivation;
    private int _scriptIdleRequestState, _scriptIdleSequenceState;
    private bool _scriptIdleEndPending;
    private bool _ambientIdleInterrupted;

    private FalloutCreatureScriptIdlePhase CaptureScriptIdlePhase() =>
        new(_scriptIdleRequestState, _scriptIdleSequenceState, _scriptIdleDeactivation?.Capture(), _ambientIdleInterrupted,
            _scriptIdleAnimation?.AccumulationTime);

    private void RestoreScriptIdlePhase(FalloutActorStoppedIdleAnimation saved)
    {
        // An existing selected receipt already owns its RNG, events and held
        // snapshot. Bind that same phase rather than issuing another PlayIdle.
        var phase = saved.ScriptPhase ?? new(2, saved.Animation.Clock.SourceSeconds == 0 ? 5 : 1);
        phase.Validate(saved.Transition);
        _scriptIdleAnimation!.RestoreAccumulationTime(phase.AccumulationTime);
        _scriptIdleRequestState = phase.RequestState; _scriptIdleSequenceState = phase.SequenceState;
        _scriptIdleEndPending = false; _scriptIdleDeactivation = null; _ambientIdleInterrupted = phase.Interrupted;
        if (phase.Deactivation is { } retained)
        {
            if (!phase.Interrupted && !FinalScriptIdleEndWasConsumed(saved.Animation.Clock))
                throw new InvalidDataException("Creature idle return has no consumed final source End marker.");
            var fade = new FalloutIdleDeactivation(GroupKeys(_scriptIdleAnimation!).Blend, ScriptIdleTransitionSettings());
            fade.Restore(retained); _scriptIdleDeactivation = fade;
        }
    }

    private void RequireScriptIdleEnd(RuntimeNativeNifAnimation animation, FalloutIdleAnimationPlayback clock)
    {
        if (!clock.HasGroupTiming && !clock.Endless && !ScriptIdleEndTimes(animation).Any())
            throw new NotSupportedException("Finite creature script idle has no authored group End owner.");
    }

    private IEnumerable<float> ScriptIdleEndTimes(RuntimeNativeNifAnimation animation)
    {
        var declarations = GroupKeys(animation);
        for (var ordinal = 0; ordinal < animation.TextKeys.Count; ++ordinal)
        {
            var key = animation.TextKeys[ordinal];
            var values = key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            for (var segment = 0; segment < values.Length; ++segment)
                if (declarations.Kind(ordinal, segment) == FalloutIdleGroupKeyKind.GroupMarker &&
                    values[segment].Trim().Equals("End", StringComparison.OrdinalIgnoreCase) &&
                    key.Time >= animation.Sequence.StartTime && key.Time <= animation.Sequence.StopTime)
                    yield return key.Time;
        }
    }

    private bool FinalScriptIdleEndWasConsumed(FalloutIdleAnimationPlaybackSnapshot clock) =>
        clock.GroupMarkerState is not null ? clock.Complete :
        clock.RemainingAdditionalLoops == 0 && !clock.IncludeStart &&
        ScriptIdleEndTimes(_scriptIdleAnimation!).Any(time => time <= clock.SourceSeconds);

    private void BeginScriptIdleDeactivation()
    {
        _scriptIdleEndPending = false;
        if (_scriptIdleDeactivation is not null) return;
        var fade = new FalloutIdleDeactivation(GroupKeys(_scriptIdleAnimation!).Blend, ScriptIdleTransitionSettings());
        _scriptIdleDeactivation = fade; _scriptIdleSequenceState = 3;
        // End is dispatched during this animation update: anchor the ordinary
        // ease-out now, independently of the remaining/clamped KF phase.
        fade.Advance(0);
        if (fade.Complete) CompleteScriptIdle();
    }

    private void PublishCurrentScriptIdlePose()
    {
        if (AmbientIdleActive)
        {
            var layers = new List<(RuntimeNativeNifAnimation Animation, float Time, float Ease)>
            { (_scriptIdleAnimation!, _scriptIdleClock!.SourceSeconds, _scriptIdleDeactivation?.Weight ?? 1) };
            RuntimeNativeNifAnimation.ControllerSnapshot? snapshot = null;
            if (_scriptIdleSequenceState == 5)
            {
                var factors = _scriptIdleTransition!.Factors;
                snapshot = new(_scriptIdleAnimation!, _scriptIdleSnapshot!, [], _scriptIdleSnapshotScalars!,
                    factors.SnapshotWeight, factors.IncomingWeight);
            }
            Combat!.ComposeIdleLayers(layers, _animation, SourceSeconds, snapshot);
            return;
        }
        if (_scriptIdleDeactivation is { } fade)
        {
            RuntimeNativeNifAnimation.ApplyWeightedLayers([
                (_animation, SourceSeconds, 1),
                (_scriptIdleAnimation!, _scriptIdleClock!.SourceSeconds, fade.Weight)]);
        }
        else if (_scriptIdleSequenceState == 5)
        {
            var factors = _scriptIdleTransition!.Factors;
            _scriptIdleAnimation!.ApplySnapshotTransition(_scriptIdleClock!.SourceSeconds, _scriptIdleSnapshot!,
                factors.SnapshotWeight, factors.IncomingWeight, scalars: _scriptIdleSnapshotScalars);
        }
        else RuntimeNativeNifAnimation.ApplyLayers((_animation, SourceSeconds), (_scriptIdleAnimation!, _scriptIdleClock!.SourceSeconds));
    }

    private void CompleteScriptIdle()
    {
        if (_scriptIdleDeactivation is not { Complete: true } || _scriptIdleRequestState != 2 || _scriptIdleSequenceState != 3)
            throw new InvalidOperationException("Creature idle cannot release an active source sequence.");
        var saved = _aiState!.CreatureScriptIdle!;
        var ambient = AmbientIdleActive;
        if (!ambient) _animation.ApplySourceTime(SourceSeconds);
        _scriptIdleSequenceState = 0; _scriptIdleRequestState = 3;
        Godot.GD.Print($"OPENNV_CREATURE_SCRIPT_IDLE_END reference={Appearance.Reference} idle={saved.Animation.Idle} " +
            $"revision={saved.Animation.Revision} sourceSeconds={_scriptIdleClock!.SourceSeconds:R} request=3 sequence=0");
        ClearScriptIdleOwner();
        if (ambient && Combat?.OwnsPose != true) Combat?.ComposeIdleLayers([], _animation, SourceSeconds);
    }

    private void ClearScriptIdleOwner()
    {
        _automaticIdleOwner = false;
        _scriptIdleAnimation = null; _scriptIdleClock = null; _scriptIdleTransition = null; _scriptIdleSnapshot = null;
        _scriptIdleSnapshotScalars = null;
        _scriptIdleDeactivation = null; _scriptIdleEndPending = false; _scriptIdlePosePublished = false;
        _scriptIdleRequestState = 0; _scriptIdleSequenceState = 0; _ambientIdleInterrupted = false;
        if (_aiState is { } state) state.CreatureScriptIdle = null;
    }

    private void ObserveScriptIdleInterruption()
    {
        if (!ScriptIdleActive || Error is not null || _aiState is not { } state || state.ScriptError is not null) return;
        if (Combat is not { Dead: false, OwnsPose: false } || state.Unconscious || state.Restrained || state.KnockedDown ||
            _conversationTarget is not null)
        {
            var error = $"Script idle {state.CreatureScriptIdle!.Animation.Idle}: creature pose interruption requires its source cancellation owner.";
            state.ScriptError = error; Error = error;
            Godot.GD.PushError($"OPENNV_CREATURE_SCRIPT_IDLE_INTERRUPTION reference={state.Reference}: {error}");
        }
    }
}
