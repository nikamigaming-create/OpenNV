using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    // Only a genuine contact setter makes this process receipt known. It is
    // deliberately absent from campaign snapshots until cold/process lifetime
    // semantics have their own qualified continuation owner.
    private readonly Dictionary<FalloutFormKey, FalloutFormKey> _outgoingWeaponTargets = [];

    internal void RecordActorWeaponContact(FalloutFormKey target, FalloutFormKey attacker, FalloutFormKey? sourceWeapon, Guid? sourceInstance)
    {
        FalloutReferenceHitEvents.RequireActor(records, attacker);
        if (!FalloutReferenceHitEvents.IsActor(records, target)) return;
        _outgoingWeaponTargets[attacker] = target;
        if (sourceWeapon is not { } weapon ||
            FalloutScriptLocals.AttachedWeaponScript(records, weapon) is null) return;
        var inventory = attacker == records.RuntimeFormKey(0x14) ? _playerInventory :
            _instances.TryGetValue(attacker, out var actor) ? actor.Inventory : null;
        if (inventory is null)
            throw new NotSupportedException("Weapon OnHit has no actual attacking inventory process.");
        inventory.Contents.AdmitInventoryWeaponHit(weapon, sourceInstance, target);
    }

    internal FalloutFormKey? OwnerLastTarget(FalloutFormKey? container)
    {
        if (container is not { } actor || !FalloutReferenceHitEvents.IsActor(records, actor)) return null;
        return _outgoingWeaponTargets.TryGetValue(actor, out var target) ? target :
            throw new NotSupportedException("GetOwnerLastTarget requires a known actual outgoing hit process; cold or retired process state is unqualified.");
    }

    internal void RetireOutgoingWeaponTarget(FalloutFormKey actor) => _outgoingWeaponTargets.Remove(actor);

    internal bool IsCreatureType(FalloutFormKey reference, int type)
    {
        if (FalloutEngineIdentity.IsPlayerReference(records, reference)) return false;
        if (records.GetEffective(reference).Signature != "ACRE") return false;
        var actor = Get(reference);
        var source = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), 1, actor.Templates);
        var data = source.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
        if (source.Signature != "CREA" || data.Length != 17)
            throw new InvalidDataException("Creature classification requires complete winning CREA DATA.");
        return unchecked((sbyte)data.Span[0]) == type;
    }

    private void RequireWeaponHitCapture()
    {
        if (_playerInventory?.Contents.HasPendingInventoryWeaponHit == true ||
            _instances.Values.Any(actor => actor.Inventory?.Contents.HasPendingInventoryWeaponHit == true))
            throw new NotSupportedException("Saving pending inventory OnHit requires its outgoing process continuation.");
    }
}
