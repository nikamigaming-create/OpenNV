using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private ulong _lastKnockbackMotionFrame = ulong.MaxValue;
    private float _previousKnockbackVertical;
    private string? _hitKnockbackError;

    internal void ReceiveMeleeKnockback(Vector3 attackerPosition, float weaponBaseDamage)
    {
        if (Dead || _state.KnockedDown) return;
        var basis = FalloutActorTemplateOwner.Resolve(_records, _records.GetEffective(_state.Base),
            (ushort)FalloutActorTemplateGroup.BaseData, _state.Templates);
        if (FalloutHitKnockback.NotPushable(basis)) return;
        var direction = _actor.GlobalPosition - attackerPosition;
        _state.HitKnockback = FalloutHitKnockback.Request(_records, _world.ActorValue(_state.Reference, "Agility"),
            weaponBaseDamage, new(direction.X, direction.Y, direction.Z), _skeleton.UnitsToMetres, _state.HitKnockback);
        _hitKnockbackError = null;
    }

    private Vector3 AdvanceHitKnockbackMotion(double delta)
    {
        var frame = Engine.GetPhysicsFrames();
        if (_lastKnockbackMotionFrame == frame) return Vector3.Zero;
        _lastKnockbackMotionFrame = frame;
        var velocity = _state.HitKnockback?.Velocity ?? System.Numerics.Vector3.Zero;
        _state.HitKnockback = _state.HitKnockback?.Advance(delta);
        return new(velocity.X, velocity.Y, velocity.Z);
    }

    private void FinishHitKnockbackPhysics(double delta)
    {
        try { FinishHitKnockbackPhysicsCore(delta); }
        catch (Exception error)
        {
            if (_hitKnockbackError != error.Message)
                GD.PushError($"OPENNV_ACTOR_KNOCKBACK_UNBOUND reference={_state.Reference} {error.Message}");
            _hitKnockbackError = error.Message;
        }
    }

    private void FinishHitKnockbackPhysicsCore(double delta)
    {
        if (_state.HitKnockback is null) return;
        if (Dead) { _state.HitKnockback = null; return; }
        if (_lastKnockbackMotionFrame == Engine.GetPhysicsFrames()) return;
        if (_state.KnockedDown || _state.Unconscious)
        {
            _state.HitKnockback = _state.HitKnockback?.Advance(delta);
            return;
        }
        if (_context is null || !EffectiveEnabled || !_context.Resident(_actor.GlobalPosition)) return;
        PrepareMovement();
        MoveActor(Vector3.Zero, delta);
        _hitKnockbackError = null;
    }
}
