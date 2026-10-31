using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutAmbientIdleTree? _ambientIdleTree;
    private void AdvanceAutomaticIdle(double delta)
    {
        if (_aiReferenceState is not { } state || _aiStack is not { } records || Combat is not { } combat) return;
        var automatic = state.AmbientIdle ??= new();
        var ready = IdleRequestsReady && !_idleRetirementPending && !IdleSnapshotActive && _animation is null &&
            _conversationTarget is null && AnimationError is null && _aiError is null && state.ScriptError is null &&
            !state.Unconscious && !state.Restrained && !state.KnockedDown && !combat.Dead && !combat.OwnsPose &&
            combat.AutomaticIdleRequestReady && _aiWorld!.IsEnabled(state.Reference);
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Automatic idle has no owned source.");
        if (!NativeActorAmbientIdleAdmission.Advance(automatic, records, this, combat.PackagePlayer,
            () => CurrentWorldBound(content).Radius, Skeleton.UnitsToMetres, delta, ready)) return;
        var tree = _ambientIdleTree ??= new(records, Appearance.SkeletonPath);
        var selected = tree.Select(condition => condition.Kind == FalloutConditionFunction.GetRandomPercent
            ? FalloutSharedUnsignedRandom.Process.NextBounded(100) : EvaluateAiCondition(condition), _idleReplays.CanSelect);
        if (selected is not null)
        {
            if (FalloutIdleTransition.ExecutionSlot.FromRecord(FalloutIdleAnimationData.Read(selected).Group).Index != 7)
                throw new NotSupportedException("Automatic NPC IDLE requires its selected source execution-slot replacement owner.");
            PlayIdle(records, selected.FormKey, "ambient");
        }
    }
    internal IReadOnlyList<object> ObserveActiveAnimationLayers()
    {
        var layers = new List<object>(Combat?.ActivePackageAnimationLayers ?? []);
        void Add(RuntimeNativeNifAnimation animation, string resource, float seconds, int state, float ease, bool outgoing, object? snapshot, byte group)
            => layers.Add(new
            {
                slot = FalloutIdleTransition.ExecutionSlot.FromRecord(group).Index,
                resource,
                sourceHash = animation.Source.Sha256,
                sourceSeconds = seconds,
                begin = animation.Sequence.StartTime,
                end = animation.Sequence.StopTime,
                frequency = animation.Sequence.Frequency,
                state,
                weight = animation.Sequence.Weight,
                ease,
                outgoing,
                snapshot,
                channelContributionsQualified = false
            });
        if (_animation is { } current && _idleAnimationResource is { } resource)
            Add(current, resource, _animationSeconds, _currentIdleRequest?.SequenceState ?? 1,
                _currentIdleEaseOut?.Weight ?? (_idleEaseIn is { } incoming ? 1 - incoming.Weight : _idleSnapshotTransition?.Factors.IncomingWeight ?? 1),
                false, _idleSnapshotTransition?.Capture(), _idleData!.Group);
        foreach (var outgoing in _outgoingIdles)
            Add(outgoing.Animation, outgoing.Request.Resource!, outgoing.Clock.SourceSeconds, 3, outgoing.Deactivation.Weight, true, null, outgoing.Timing.Group);
        return layers;
    }
    internal void AppendConcurrentIdleLayers(List<(RuntimeNativeNifAnimation Animation, float Time, float Ease)> layers,
        List<RuntimeNativeNifAnimation.ControllerSnapshot> snapshots)
    {
        foreach (var outgoing in _outgoingIdles)
            layers.Add((outgoing.Animation, outgoing.Clock.SourceSeconds, outgoing.Deactivation.Weight));
        if (_animation is not { } current) return;
        layers.Add((current, _animationSeconds, _currentIdleEaseOut?.Weight ?? (_idleEaseIn is { } ease ? 1 - ease.Weight : 1)));
        if (_idleSnapshotTransition is { } transition)
        {
            var factors = transition.Factors;
            snapshots.Add(new(current, _idleSnapshotSamples!, _idleSnapshotTargets!, _idleSnapshotScalars!,
                factors.SnapshotWeight, factors.IncomingWeight));
        }
    }
}
