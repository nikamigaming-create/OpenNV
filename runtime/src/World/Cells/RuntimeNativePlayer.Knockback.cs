using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutHitKnockback? _hitKnockback;
    private bool _knockbackNotPushable;
    private float _previousKnockbackVertical;
    internal FalloutHitKnockback? CaptureHitKnockback() => _hitKnockback;

    internal void ConfigureHitKnockback(FalloutPluginStack records, FalloutPluginRecord actor, FalloutHitKnockback? restore)
    {
        FalloutHitKnockback.Validate(restore);
        _knockbackNotPushable = FalloutHitKnockback.NotPushable(FalloutActorTemplateOwner.Resolve(records, actor,
            (ushort)FalloutActorTemplateGroup.BaseData));
        if (_knockbackNotPushable && restore is not null)
            throw new InvalidDataException("A non-pushable player has saved hit knockback.");
        _hitKnockback = restore;
        _previousKnockbackVertical = 0;
    }

    internal void ReceiveMeleeKnockback(Vector3 attackerPosition, float weaponBaseDamage)
    {
        if (IsDefeated?.Invoke() == true || _knockbackNotPushable) return;
        var direction = GlobalPosition - attackerPosition;
        _hitKnockback = FalloutHitKnockback.Request(
            _presentationRecords ?? throw new InvalidOperationException("Player knockback has no source owner."),
            (_combatActorValue ?? throw new InvalidOperationException("Player knockback has no actor values."))(10),
            weaponBaseDamage, new(direction.X, direction.Y, direction.Z), UnitsToMeters, _hitKnockback);
    }

    private Vector3 AdvanceHitKnockback(double delta)
    {
        if (IsDefeated?.Invoke() == true) _hitKnockback = null;
        var velocity = _hitKnockback?.Velocity ?? System.Numerics.Vector3.Zero;
        _hitKnockback = _hitKnockback?.Advance(delta);
        return new(velocity.X, velocity.Y, velocity.Z);
    }
}
