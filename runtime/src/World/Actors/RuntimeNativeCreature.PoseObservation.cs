using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

// Read-only diagnostic values from the existing owners. This never selects,
// advances, captures a save continuation, or publishes an animation.
internal readonly record struct NativeCreaturePoseOwners(string Owner, double DefaultElapsedSeconds, float DefaultSourceSeconds,
    double? PackageElapsedSeconds, float? EventSourceSeconds, float? ScriptSourceSeconds,
    bool CombatOwnsPose, bool PackageOwnsPose, bool Moving, FalloutFormKey? Package, string? PackageAnimation,
    FalloutFormKey? EventIdle, FalloutFormKey? ScriptIdle, string? AnimationError, string? SourceError, string? PackageError,
    FalloutCreatureScriptIdlePhase? ScriptPhase, IReadOnlyList<object> ActiveAnimationLayers);

internal sealed partial class RuntimeNativeCreature
{
    internal NativeCreaturePoseOwners PoseOwners => new(
        Combat?.OwnsPose == true ? "combat" : AmbientIdleActive ? "ambient-idle" : _scriptIdleClock is not null ? "script-idle" :
            _eventIdleClock is not null ? Combat?.PackageOwnsPose == true ? "package+event-idle" : "default+event-idle" :
            Combat?.PackageOwnsPose == true ? "package" : "default",
        _clock.ElapsedSeconds, SourceSeconds, _aiState?.PackageMotion?.Seconds, _eventIdleClock?.SourceSeconds,
        _scriptIdleClock?.SourceSeconds, Combat?.OwnsPose == true, Combat?.PackageOwnsPose == true, Combat?.PackageMoving == true,
        _aiPackage?.FormKey, _aiState?.PackageMotion?.Animation, _aiState?.PackageIdle?.Idle,
        _aiState?.CreatureScriptIdle?.Animation.Idle, Error, _aiState?.ScriptError, _aiError,
        ScriptIdleActive ? CaptureScriptIdlePhase() : null, ObserveActiveAnimationLayers());
    private IReadOnlyList<object> ObserveActiveAnimationLayers()
    {
        var layers = new List<object>(Combat?.ActivePackageAnimationLayers ?? []);
        if (ScriptIdleActive && _scriptIdleAnimation is { } animation && _scriptIdleClock is { } clock &&
            _aiState?.CreatureScriptIdle is { } saved)
            layers.Add(new
            {
                slot = 7,
                resource = saved.Animation.Resource,
                sourceHash = saved.Animation.Sha256,
                sourceSeconds = clock.SourceSeconds,
                begin = animation.Sequence.StartTime,
                end = animation.Sequence.StopTime,
                frequency = animation.Sequence.Frequency,
                state = _scriptIdleSequenceState,
                weight = animation.Sequence.Weight,
                ease = _scriptIdleDeactivation?.Weight ?? _scriptIdleTransition?.Factors.IncomingWeight ?? 1,
                snapshot = _scriptIdleSequenceState == 5 ? _scriptIdleTransition?.Capture() : null,
                outgoing = _scriptIdleSequenceState == 3,
                channelContributionsQualified = false
            });
        return layers;
    }
}
