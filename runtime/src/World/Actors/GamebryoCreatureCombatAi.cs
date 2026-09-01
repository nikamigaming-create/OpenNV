using Godot;

namespace OpenNV.Runtime.World.Actors;

internal enum GamebryoCreatureCombatPhase
{
    Chase,
    Melee,
    Dead,
}

internal sealed record GamebryoCreatureCombatContract(
    float LocomotionSpeedGameUnitsPerSecond,
    float MeleeRootSpeedGameUnitsPerSecond,
    float ContactRangeGameUnits,
    float MeleeDurationSeconds,
    float HitTimeSeconds,
    int Damage);

internal sealed record GamebryoCreatureCombatState(
    GamebryoCreatureCombatPhase Phase,
    float MeleeClockSeconds,
    bool HitApplied);

internal readonly record struct GamebryoCreatureCombatStep(
    Transform3D Transform,
    GamebryoCreatureCombatState State,
    bool BeganLocomotion,
    bool BeganMelee,
    int Damage);

internal sealed class GamebryoCreatureCombatAi
{
    private readonly GamebryoCreatureCombatContract _contract;
    private GamebryoCreatureCombatState _state;

    private GamebryoCreatureCombatAi(
        GamebryoCreatureCombatContract contract,
        GamebryoCreatureCombatState state)
    {
        Validate(contract, state);
        _contract = contract;
        _state = state;
    }

    internal GamebryoCreatureCombatState State => _state;

    internal static GamebryoCreatureCombatContract Contract(
        GamebryoCreatureAnimationPlayback animation,
        float contactRangeGameUnits,
        int damage)
    {
        var locomotion = animation.Animation(
            GamebryoCreatureAnimationPlayback.LocomotionRole);
        var melee = animation.Animation(GamebryoCreatureAnimationPlayback.MeleeRole);
        var hitKeys = (melee.TextKeys ?? Array.Empty<ActorModelSlice.LoadedTextKey>())
            .Where(value => value.Value.Equals("Hit", StringComparison.Ordinal))
            .ToArray();
        if (locomotion.RootMotion is not { } locomotionRoot ||
            melee.RootMotion is not { } meleeRoot || hitKeys.Length != 1)
            throw new InvalidOperationException(
                "Owned creature locomotion or melee timing is incomplete.");
        return new GamebryoCreatureCombatContract(
            locomotionRoot.SpeedGameUnitsPerSecond,
            meleeRoot.SpeedGameUnitsPerSecond,
            contactRangeGameUnits,
            melee.StopSeconds - melee.StartSeconds,
            hitKeys[0].TimeSeconds,
            damage);
    }

    internal static GamebryoCreatureCombatAi Start(
        GamebryoCreatureCombatContract contract) => new(
            contract,
            new GamebryoCreatureCombatState(
                GamebryoCreatureCombatPhase.Chase,
                0.0f,
                false));

    internal static GamebryoCreatureCombatAi Restore(
        GamebryoCreatureCombatContract contract,
        GamebryoCreatureCombatState state) => new(contract, state);

    internal GamebryoCreatureCombatStep Advance(
        double deltaSeconds,
        Transform3D transform,
        Vector3 target,
        Vector3 nextWaypoint)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0 ||
            !transform.IsFinite() || !target.IsFinite() || !nextWaypoint.IsFinite())
            throw new InvalidOperationException("Creature combat AI input is invalid.");
        if (_state.Phase == GamebryoCreatureCombatPhase.Dead)
            return new GamebryoCreatureCombatStep(
                transform, _state, false, false, 0);

        var targetOffset = Horizontal(target - transform.Origin);
        var targetDistance = targetOffset.Length();
        if (_state.Phase == GamebryoCreatureCombatPhase.Chase &&
            targetDistance <= _contract.ContactRangeGameUnits)
        {
            _state = new GamebryoCreatureCombatState(
                GamebryoCreatureCombatPhase.Melee,
                0.0f,
                false);
            return new GamebryoCreatureCombatStep(
                Face(transform, targetOffset), _state, false, true, 0);
        }
        if (_state.Phase == GamebryoCreatureCombatPhase.Chase)
        {
            var pathOffset = Horizontal(nextWaypoint - transform.Origin);
            if (pathOffset.IsZeroApprox())
                throw new InvalidOperationException(
                    "Creature combat navigation has no forward progress.");
            var distance = Math.Min(
                pathOffset.Length(),
                _contract.LocomotionSpeedGameUnitsPerSecond * (float)deltaSeconds);
            transform = Face(transform, pathOffset);
            transform = new Transform3D(
                transform.Basis,
                transform.Origin + pathOffset.Normalized() * distance);
            return new GamebryoCreatureCombatStep(
                transform, _state, true, false, 0);
        }

        var priorClock = _state.MeleeClockSeconds;
        var clock = Math.Min(
            _contract.MeleeDurationSeconds,
            priorClock + (float)deltaSeconds);
        transform = Face(transform, targetOffset);
        if (!targetOffset.IsZeroApprox())
        {
            var maximumAdvance = Math.Max(
                0.0f,
                targetDistance - _contract.ContactRangeGameUnits);
            var advance = Math.Min(
                maximumAdvance,
                _contract.MeleeRootSpeedGameUnitsPerSecond * (clock - priorClock));
            transform = new Transform3D(
                transform.Basis,
                transform.Origin + targetOffset.Normalized() * advance);
        }
        var appliesHit = !_state.HitApplied &&
            priorClock < _contract.HitTimeSeconds &&
            clock >= _contract.HitTimeSeconds &&
            Horizontal(target - transform.Origin).Length() <=
                _contract.ContactRangeGameUnits;
        var completed = clock >= _contract.MeleeDurationSeconds;
        _state = completed
            ? new GamebryoCreatureCombatState(
                GamebryoCreatureCombatPhase.Chase, 0.0f, false)
            : new GamebryoCreatureCombatState(
                GamebryoCreatureCombatPhase.Melee,
                clock,
                _state.HitApplied || appliesHit);
        return new GamebryoCreatureCombatStep(
            transform,
            _state,
            completed,
            false,
            appliesHit ? _contract.Damage : 0);
    }

    internal void Kill() => _state = new GamebryoCreatureCombatState(
        GamebryoCreatureCombatPhase.Dead,
        0.0f,
        false);

    internal void Interrupt() => _state = new GamebryoCreatureCombatState(
        GamebryoCreatureCombatPhase.Chase,
        0.0f,
        false);

    private static Vector3 Horizontal(Vector3 value) => new(value.X, 0.0f, value.Z);

    private static Transform3D Face(Transform3D transform, Vector3 direction)
    {
        if (direction.IsZeroApprox())
            return transform;
        return new Transform3D(
            Basis.LookingAt(direction.Normalized(), Vector3.Up),
            transform.Origin);
    }

    private static void Validate(
        GamebryoCreatureCombatContract contract,
        GamebryoCreatureCombatState state)
    {
        if (!float.IsFinite(contract.LocomotionSpeedGameUnitsPerSecond) ||
            contract.LocomotionSpeedGameUnitsPerSecond <= 0.0f ||
            !float.IsFinite(contract.MeleeRootSpeedGameUnitsPerSecond) ||
            contract.MeleeRootSpeedGameUnitsPerSecond <= 0.0f ||
            !float.IsFinite(contract.ContactRangeGameUnits) ||
            contract.ContactRangeGameUnits <= 0.0f ||
            !float.IsFinite(contract.MeleeDurationSeconds) ||
            !float.IsFinite(contract.HitTimeSeconds) ||
            contract.MeleeDurationSeconds <= 0.0f ||
            contract.HitTimeSeconds <= 0.0f ||
            contract.HitTimeSeconds >= contract.MeleeDurationSeconds ||
            contract.Damage <= 0 ||
            !float.IsFinite(state.MeleeClockSeconds) ||
            state.MeleeClockSeconds < 0.0f ||
            state.MeleeClockSeconds > contract.MeleeDurationSeconds ||
            state.Phase != GamebryoCreatureCombatPhase.Melee &&
                state.MeleeClockSeconds != 0.0f)
            throw new InvalidOperationException(
                "Creature combat AI contract or state is invalid.");
    }
}
