using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutReferenceWorld? _hitReferences;
    private Func<ulong, FalloutFormKey?>? _hitReference;
    private string? _hitEventError;

    internal void ConfigureHitEvents(FalloutReferenceWorld references, Func<ulong, FalloutFormKey?> collisionReference)
    {
        _hitReferences = references;
        _hitReference = collisionReference;
    }

    private FalloutFormKey? HitReference(Node? collider) => collider is null ? null :
        _hitReference?.Invoke(collider.GetInstanceId());

    private bool MarkWeaponHit(FalloutFormKey? reference, FalloutFormKey attacker, FalloutFormKey weapon,
        FalloutReferenceHitKind kind)
    {
        if (reference is null) return false;
        try
        {
            (_hitReferences ?? throw new NotSupportedException("Weapon contact has no shared reference event owner."))
                .HitEvents.Mark(reference.Value, attacker, weapon, kind);
            _hitEventError = null;
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException)
        {
            _hitEventError = error.Message;
            GD.PushError($"OPENNV_WEAPON_HIT_EVENT_UNBOUND reference={reference} weapon={weapon} {error.Message}");
            return false;
        }
    }
}
