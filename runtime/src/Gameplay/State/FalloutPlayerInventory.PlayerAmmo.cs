using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInventory
{
    private bool _playerProcessEquipment;
    private IFalloutFormLists? _playerAmmoLists;
    internal void BindPlayerEquipmentProcess(IFalloutFormLists formLists)
    {
        _playerProcessEquipment = true; _playerAmmoLists = formLists;
    }

    // Actual WEAP selection invokes the source's unanimated process-ammo
    // materialization. Queries, construction and cold restore never invoke it.
    internal void MaterializeEquippedPlayerMagazine(FalloutWeaponPresentation weapon, FalloutFormKey? previousAmmunition)
    {
        var item = Item(weapon.Form) ?? throw new InvalidOperationException("Selected weapon is absent.");
        if (item.RecordType != "WEAP" || !_equipped.Contains(item.RuntimeFormId) || weapon.ClipSize < 0)
            throw new InvalidOperationException("Ammo materialization has no actual equipped weapon process.");
        var selected = EnsureWeaponInstance(weapon.Form);
        // Carried totals already include the process's loaded rounds. A
        // stowed gun is not an additional process-ammo reservation.
        Publish(Items.Where(value => value.RecordType == "WEAP" && value.Variants?.Any(extra => extra.Magazine is { Generated: false }) == true)
            .Select(value => value with
            {
                Variants = value.Variants!.Select(extra => extra.Magazine is { Generated: false }
                    ? extra with { Magazine = null } : extra).ToArray()
            }).ToArray());
        if (weapon.AmmoRegenerationSeconds > 0)
        {
            FalloutWeaponRecharge.Materialize(this, weapon);
            return;
        }
        if (!weapon.HasAmmunitionSource || weapon.AmmoUse == 0 || weapon.IsMine || weapon.IsThrownWeapon) return;
        var ammunition = previousAmmunition is { } previous && weapon.Ammunition.Contains(previous) && Item(previous) is { Count: > 0 }
            ? previousAmmunition : weapon.Ammunition.Where(ammo => Item(ammo) is { Count: > 0 }).Select(ammo => (FalloutFormKey?)ammo).FirstOrDefault();
        ammunition ??= weapon.Ammunition.Select(ammo => (FalloutFormKey?)ammo).FirstOrDefault();
        if (ammunition is not { } source) return;
        SetWeaponMagazine(weapon.Form, selected.Extra.Instance!.Value,
            new(source, Math.Min(weapon.ClipSize, Item(source)?.Count ?? 0)));
    }
}
