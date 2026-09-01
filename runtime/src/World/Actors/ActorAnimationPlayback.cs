using Godot;

namespace OpenNV.Runtime.World.Actors;

internal sealed record SourceActorAnimation(
    string LogicalPath,
    string Sha256,
    string SequenceName,
    float StartSeconds,
    float StopSeconds,
    int CycleType,
    string AccumulationRootTranslationDisposition);

internal sealed class ActorAnimationPlayback
{
    internal const int LoopCycleType = 0;
    internal const int ClampCycleType = 2;

    private const double PhaseToleranceSeconds = 0.0001;
    private double _positionSeconds;

    private ActorAnimationPlayback(
        ActorModelSlice.LoadedAnimation animation,
        double positionSeconds)
    {
        Animation = animation;
        _positionSeconds = positionSeconds;
    }

    internal ActorModelSlice.LoadedAnimation Animation { get; }

    internal double PositionSeconds => _positionSeconds;

    internal bool Terminal { get; private set; }

    internal static ActorAnimationPlayback Start(
        ActorModelSlice.LoadedActor actor,
        SourceActorAnimation source,
        double positionSeconds = 0.0)
    {
        var animation = Resolve(actor, source);
        return Start(actor, animation, positionSeconds);
    }

    internal static ActorModelSlice.LoadedAnimation Resolve(
        ActorModelSlice.LoadedActor actor,
        SourceActorAnimation source)
    {
        var expectedPath = ActorModelSlice.NormalizeAnimationPath(source.LogicalPath);
        var matches = actor.LoadedAnimations.Where(value =>
                ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                    expectedPath,
                    StringComparison.OrdinalIgnoreCase) &&
                value.SourceSha256.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase) &&
                value.SequenceName.Equals(source.SequenceName, StringComparison.Ordinal) &&
                value.StartSeconds == source.StartSeconds &&
                value.StopSeconds == source.StopSeconds &&
                value.CycleType == source.CycleType &&
                value.AccumulationRootTranslationDisposition.Equals(
                    source.AccumulationRootTranslationDisposition,
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"Source actor animation is absent or ambiguous: {source.LogicalPath}");
        return matches[0];
    }

    internal static ActorAnimationPlayback Start(
        ActorModelSlice.LoadedActor actor,
        ActorModelSlice.LoadedAnimation animation,
        double positionSeconds = 0.0)
    {
        if (!double.IsFinite(positionSeconds) ||
            positionSeconds < animation.StartSeconds ||
            positionSeconds >= animation.StopSeconds)
            throw new InvalidOperationException(
                $"Source actor animation phase is outside its clip: {animation.LogicalPath}");

        var resource = animation.Player.GetAnimation(animation.RuntimeName) ??
            throw new InvalidOperationException(
                $"Source actor animation resource is absent: {animation.LogicalPath}");
        if (Math.Abs(resource.Length - animation.StopSeconds) > PhaseToleranceSeconds)
            throw new InvalidOperationException(
                $"Source actor animation duration differs: {animation.LogicalPath}");
        resource.LoopMode = LoopModeForCycleType(animation.CycleType);
        foreach (var player in actor.LoadedAnimations.Select(value => value.Player).Distinct())
        {
            player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
            player.Stop();
        }
        animation.Player.Play(animation.RuntimeName);
        animation.Player.Seek(positionSeconds, update: true);
        var playback = new ActorAnimationPlayback(animation, positionSeconds);
        playback.RequirePublishedPhase();
        return playback;
    }

    internal void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
            throw new InvalidOperationException("Source actor animation delta is invalid.");
        if (Terminal || deltaSeconds == 0.0)
            return;
        var next = AdvanceClock(
            _positionSeconds,
            deltaSeconds,
            Animation.StartSeconds,
            Animation.StopSeconds,
            Animation.CycleType);
        var runtimeDelta = Math.Min(
            deltaSeconds,
            Animation.StopSeconds - _positionSeconds);
        Animation.Player.Advance(runtimeDelta);
        _positionSeconds = next.PositionSeconds;
        Terminal = next.Terminal;
        if (!Animation.Player.CurrentAnimation.ToString().Equals(
                Animation.RuntimeName,
                StringComparison.Ordinal))
            Animation.Player.Play(Animation.RuntimeName);
        Animation.Player.Seek(_positionSeconds, update: true);
        if (Terminal)
            Animation.Player.Pause();
        RequirePublishedPhase();
    }

    internal void Stop()
    {
        Animation.Player.Stop(keepState: true);
        Terminal = true;
    }

    internal void PublishPhase(double positionSeconds)
    {
        if (!double.IsFinite(positionSeconds) ||
            positionSeconds < Animation.StartSeconds ||
            positionSeconds > Animation.StopSeconds)
            throw new InvalidOperationException(
                $"Source actor animation phase is outside its clip: {Animation.LogicalPath}");
        _positionSeconds = positionSeconds;
        Terminal = Animation.CycleType == ClampCycleType &&
            positionSeconds >= Animation.StopSeconds;
        Animation.Player.Play(Animation.RuntimeName);
        Animation.Player.Seek(positionSeconds, update: true);
        if (Terminal)
            Animation.Player.Pause();
        RequirePublishedPhase();
    }

    internal static Godot.Animation.LoopModeEnum LoopModeForCycleType(int cycleType) =>
        cycleType switch
        {
            LoopCycleType => Godot.Animation.LoopModeEnum.Linear,
            ClampCycleType => Godot.Animation.LoopModeEnum.None,
            _ => throw new InvalidOperationException(
                $"Source actor animation cycle type is unsupported: {cycleType}"),
        };

    internal static ActorAnimationClock AdvanceClock(
        double positionSeconds,
        double deltaSeconds,
        double startSeconds,
        double stopSeconds,
        int cycleType)
    {
        if (!double.IsFinite(positionSeconds) || !double.IsFinite(deltaSeconds) ||
            !double.IsFinite(startSeconds) || !double.IsFinite(stopSeconds) ||
            deltaSeconds < 0.0 || stopSeconds <= startSeconds ||
            positionSeconds < startSeconds || positionSeconds > stopSeconds)
            throw new InvalidOperationException("Source actor animation clock is invalid.");
        var advanced = positionSeconds + deltaSeconds;
        return cycleType switch
        {
            LoopCycleType => new ActorAnimationClock(
                startSeconds + (advanced - startSeconds) % (stopSeconds - startSeconds),
                false),
            ClampCycleType => new ActorAnimationClock(
                Math.Min(advanced, stopSeconds),
                advanced >= stopSeconds),
            _ => throw new InvalidOperationException(
                $"Source actor animation cycle type is unsupported: {cycleType}"),
        };
    }

    private void RequirePublishedPhase()
    {
        if (!Animation.Player.CurrentAnimation.ToString().Equals(
                Animation.RuntimeName,
                StringComparison.Ordinal) ||
            Math.Abs(Animation.Player.CurrentAnimationPosition - _positionSeconds) >
                PhaseToleranceSeconds)
            throw new InvalidOperationException(
                $"Source actor animation phase was not published: {Animation.LogicalPath}");
    }
}

internal readonly record struct ActorAnimationClock(
    double PositionSeconds,
    bool Terminal);
