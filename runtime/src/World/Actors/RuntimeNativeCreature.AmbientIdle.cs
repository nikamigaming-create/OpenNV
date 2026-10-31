using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutAmbientIdleTree? _ambientIdleTree;
    private void AdvanceAutomaticIdle(double delta)
    {
        if (_aiState is not { } state || _aiRecords is not { } records || Combat is not { } combat) return;
        var automatic = state.AmbientIdle ??= new();
        automatic.Replay.Advance((float)delta);
        var ready = !ScriptIdleActive && _eventIdleClock is null && _conversationTarget is null &&
            Error is null && state.ScriptError is null && !state.Unconscious && !state.Restrained &&
            !state.KnockedDown && !combat.Dead && !combat.OwnsPose && combat.AutomaticIdleRequestReady &&
            _aiWorld!.IsEnabled(state.Reference);
        if (!NativeActorAmbientIdleAdmission.Advance(automatic, records, this, combat.PackagePlayer,
            () => CurrentWorldBound(_content).Radius, Skeleton.UnitsToMetres, delta, ready)) return;
        var tree = _ambientIdleTree ??= new(records, Appearance.SkeletonPath);
        var selected = tree.Select(condition => condition.Kind == FalloutConditionFunction.GetRandomPercent
            ? FalloutSharedUnsignedRandom.Process.NextBounded(100) : PackageCondition(condition), automatic.Replay.CanSelect);
        if (selected is not null) PlayCreatureIdle(records, selected, "ambient");
    }

    private void ObserveAmbientInterruption(double delta)
    {
        if (!AmbientIdleActive) return;
        if (_aiState?.CreatureScriptIdle is null || Combat?.Dead == true)
        { ClearScriptIdleOwner(); return; }
        var saved = _aiState.CreatureScriptIdle!;
        if (_scriptIdleDeactivation is null && _scriptIdleSequenceState == 5)
        {
            var held = _scriptIdleTransition!.Advance(delta);
            if (held) _scriptIdleClock!.DispatchHeldBoundary(interval => DispatchIdleInterval(_scriptIdleAnimation!, interval, saved.Animation.Idle));
            else _scriptIdleSequenceState = 1;
            // Ordinary StopIdle refuses an easing/snapshot sequence. Retain
            // the request and finish its transition before retiring it.
            _aiState.CreatureScriptIdle = saved with
            {
                Animation = saved.Animation with { Clock = _scriptIdleClock!.Capture() },
                Transition = saved.Transition! with { Phase = _scriptIdleTransition.Capture() },
                ScriptPhase = CaptureScriptIdlePhase()
            };
            return;
        }
        if (_scriptIdleDeactivation is null) { _ambientIdleInterrupted = true; BeginScriptIdleDeactivation(); }
        if (!AmbientIdleActive) return;
        _scriptIdleDeactivation!.Advance(delta);
        if (_scriptIdleDeactivation.Complete) { CompleteScriptIdle(); return; }
        _scriptIdleClock!.Advance(delta, interval => DispatchIdleInterval(_scriptIdleAnimation!, interval, saved.Animation.Idle), processGroup: false);
        _aiState.CreatureScriptIdle = saved with
        { Animation = saved.Animation with { Clock = _scriptIdleClock.Capture() }, ScriptPhase = CaptureScriptIdlePhase() };
    }
    internal void AppendConcurrentIdleLayers(List<(RuntimeNativeNifAnimation Animation, float Time, float Ease)> layers,
        List<RuntimeNativeNifAnimation.ControllerSnapshot> snapshots)
    {
        if (!AmbientIdleActive) return;
        layers.Add((_scriptIdleAnimation!, _scriptIdleClock!.SourceSeconds, _scriptIdleDeactivation?.Weight ?? 1));
        if (_scriptIdleSequenceState == 5)
        {
            var factors = _scriptIdleTransition!.Factors;
            snapshots.Add(new(_scriptIdleAnimation!, _scriptIdleSnapshot!, [], _scriptIdleSnapshotScalars!,
                factors.SnapshotWeight, factors.IncomingWeight));
        }
    }
}
