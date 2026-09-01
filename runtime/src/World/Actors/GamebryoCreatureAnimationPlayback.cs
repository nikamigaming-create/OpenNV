namespace OpenNV.Runtime.World.Actors;

internal sealed class GamebryoCreatureAnimationPlayback
{
    internal const string IdleRole = "idle";
    internal const string LocomotionRole = "locomotion";
    internal const string MeleeRole = "melee";
    internal const string HitRole = "hit";

    private static readonly string[] RequiredRoles =
        [IdleRole, LocomotionRole, MeleeRole, HitRole];

    private readonly ActorModelSlice.LoadedActor _actor;
    private readonly IReadOnlyDictionary<string, ActorModelSlice.LoadedAnimation> _roles;
    private ActorAnimationPlayback _playback;
    private bool _stopped;

    private GamebryoCreatureAnimationPlayback(
        ActorModelSlice.LoadedActor actor,
        IReadOnlyDictionary<string, ActorModelSlice.LoadedAnimation> roles,
        ActorAnimationPlayback playback)
    {
        _actor = actor;
        _roles = roles;
        _playback = playback;
    }

    internal string Role => _playback.Animation.Role ?? throw new InvalidOperationException(
        "Source creature animation has no role.");

    internal double PositionSeconds => _playback.PositionSeconds;

    internal ActorModelSlice.LoadedAnimation Animation(string role)
    {
        if (!_roles.TryGetValue(role, out var animation))
            throw new InvalidOperationException(
                $"Owned creature animation role is absent: {role}");
        return animation;
    }

    internal static GamebryoCreatureAnimationPlayback Start(
        ActorModelSlice.LoadedActor actor,
        string role = IdleRole,
        double positionSeconds = 0.0)
    {
        var roles = actor.LoadedAnimations
            .Where(value => value.Role is not null)
            .GroupBy(value => value.Role!, StringComparer.Ordinal)
            .ToDictionary(
                value => value.Key,
                value => value.Single(),
                StringComparer.Ordinal);
        if (!RequiredRoles.ToHashSet(StringComparer.Ordinal).SetEquals(roles.Keys))
            throw new InvalidOperationException(
                "Owned creature animation roles are incomplete or unsupported.");
        if (!roles.TryGetValue(role, out var animation))
            throw new InvalidOperationException(
                $"Owned creature animation role is absent: {role}");
        return new GamebryoCreatureAnimationPlayback(
            actor,
            roles,
            ActorAnimationPlayback.Start(actor, animation, positionSeconds));
    }

    internal void Play(string role)
    {
        if (!_roles.TryGetValue(role, out var animation))
            throw new InvalidOperationException(
                $"Owned creature animation role is absent: {role}");
        _playback.Stop();
        _playback = ActorAnimationPlayback.Start(_actor, animation);
        _stopped = false;
    }

    internal void Advance(double deltaSeconds)
    {
        if (_stopped)
            return;
        _playback.Advance(deltaSeconds);
        if (_playback.Terminal && Role != IdleRole)
            Play(IdleRole);
    }

    internal void Stop()
    {
        _playback.Stop();
        _stopped = true;
    }
}
