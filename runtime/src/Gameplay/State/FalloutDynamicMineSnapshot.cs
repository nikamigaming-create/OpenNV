using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutDynamicMineSnapshot(uint RuntimeId, FalloutFormKey Projectile, FalloutFormKey Weapon,
    FalloutFormKey Creator, FalloutItemVariant Item, FalloutReferencePlacement Placement,
    FalloutMineState Mine, FalloutWeaponDamage Damage, double AgeSeconds, ulong SoundRandomState,
    FalloutAnimationSoundEventsSnapshot Sounds, IReadOnlyList<FalloutObjectPhysicsSnapshot> Physics, bool Taken = false)
{
    internal FalloutDynamicMineSnapshot Copy() => this with
    {
        Placement = Placement.Copy(),
        Physics = Physics.Select(body => body.Copy()).ToArray(),
        Sounds = Sounds with { Events = Sounds.Events.Select(entry => entry.Copy()).ToArray(), Faults = Sounds.Faults?.ToArray() }
    };

    internal FalloutMineSource Source(FalloutPluginStack records)
    {
        var weapon = FalloutWeaponPresentation.Read(records, Weapon, weaponMods: Item.WeaponMods);
        if (!weapon.IsMine || weapon.HasAmmunitionSource)
            throw new NotSupportedException("Persistent player mine deployment requires its consuming-WEAP source family.");
        var shot = FalloutWeaponModifications.Apply(records, FalloutWeaponShot.Read(records, Weapon, null, false), Item.WeaponMods);
        if (shot.Projectile.Form != Projectile || FalloutMineSource.Read(records, Projectile) is not { } source)
            throw new InvalidDataException("Dynamic mine projectile differs from its actual source weapon.");
        return new(shot.Projectile, source.Sha256);
    }

    internal void Validate(FalloutPluginStack records, FalloutRuntimeReferences references)
    {
        var identity = FalloutSpatialReferenceId.Runtime(RuntimeId); identity.Validate();
        if (references.Require(RuntimeId) is not { Kind: FalloutRuntimeReferenceKind.Projectile } runtime ||
            runtime.Base != Projectile || runtime.Retired != Taken || Item is null || Item.Count != 1 ||
            Item.Hotkey is not null || Item.ScriptState is not null || Item.Magazine is not null ||
            Item.Instance == Guid.Empty || (Item.WeaponMods & ~7) != 0 ||
            Item.Condition is { } condition && (!float.IsFinite(condition) || condition is < 0 or > 1) ||
            !FalloutEngineIdentity.IsPlayerReference(records, Creator) || !double.IsFinite(AgeSeconds) || AgeSeconds < 0 ||
            !float.IsFinite(Damage.Amount) || Damage.Amount < 0 || !float.IsFinite(Damage.LimbMultiplier) || Damage.LimbMultiplier < 0 ||
            !float.IsFinite(Damage.Skill) || !float.IsFinite(Damage.Condition) || Damage.AmmoEffects is { Count: > 0 } ||
            Sounds is null || Sounds.Reference != identity || Physics is not { Count: 1 })
            throw new InvalidDataException("Dynamic mine lost its runtime identity, source item, actor, clock, damage or physical continuation.");
        Placement.Validate();
        if (records.GetEffective(Placement.Cell).Signature != "CELL") throw new InvalidDataException("Dynamic mine has no source cell/world owner.");
        Mine.Validate(Source(records));
        FalloutObjectPhysicsSnapshot.ValidateSet(Physics);
        FalloutAnimationSoundEvents.ValidateSource(Sounds, records, identity);
        if (Mine.Phase == FalloutMinePhase.Detonated && !Taken)
            throw new NotSupportedException("Active dynamic mine explosion needs its natural effect completion before capture.");
        if (Mine.Error is not null) throw new NotSupportedException("Dynamic mine continuation failed: " + Mine.Error);
    }
}
