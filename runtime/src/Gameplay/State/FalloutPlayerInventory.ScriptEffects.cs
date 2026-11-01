using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInventory
{
    internal FalloutPlayerInventory PrepareScriptStage() => Copy();

    internal float EquippedWeaponHealthPercentage(FalloutPluginStack records) =>
        EquippedObject(records, 5) is { } weapon ? FalloutWeaponCondition.SelectedCondition(Item(weapon) ??
            throw new InvalidDataException("Equipped weapon has no current inventory instance.")) * 100 : 100;

    internal void SetEquippedWeaponHealthPercentage(FalloutPluginStack records, double percentage)
    {
        var input = (float)percentage;
        if (!double.IsFinite(percentage) || !float.IsFinite(input))
            throw new InvalidDataException("Weapon percentage is not finite Float32.");
        if (EquippedObject(records, 5) is not { } weapon) return;
        var item = Item(weapon) ?? throw new InvalidDataException("Equipped weapon has no inventory instance.");
        var selected = SelectedWeapon(weapon);
        var source = FalloutWeaponPresentation.Read(records, weapon, weaponMods: selected.Extra.WeaponMods);
        if (source.ConditionHealth <= 1)
            throw new NotSupportedException("Weapon percentage setter needs its degenerate maximum-health owner.");
        var fraction = (float)(input / 100d);
        var desired = (float)(source.ConditionHealth * (double)fraction);
        var previous = (selected.Extra.Condition ?? 1) * source.ConditionHealth;
        var delta = desired - previous;
        if (!float.IsFinite(delta)) throw new InvalidDataException("Weapon condition delta is not finite.");
        if (delta < 0)
            throw new NotSupportedException("Decreasing weapon percentage requires its actor weapon-damage publication owner.");
        if (delta == 0) return;
        var health = Math.Min(desired, source.ConditionHealth - 1);
        if (health < 0 || health > source.ConditionHealth)
            throw new InvalidDataException("Weapon percentage repair exceeds its native health storage.");
        var condition = health / source.ConditionHealth;
        ApplyRemovalTransaction(null, (staged, _) =>
            staged.ReplaceInstances(weapon, [(selected.Variant, 1)],
                [selected.Extra with { Count = 1, Condition = condition }]));
    }
}
