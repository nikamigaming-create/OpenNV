namespace OpenNV.Runtime.World.Interactions;

internal sealed record GamebryoRangedAttack(
    string WeaponFormId,
    string AmmunitionFormId,
    int Damage);

internal sealed record GamebryoCombatantState(
    string ReferenceFormId,
    int MaximumHealth,
    int CurrentHealth,
    bool Dead);

internal sealed record GamebryoRangedHitOutcome(
    GamebryoCombatantState Target,
    bool Died);

internal static class GamebryoRangedCombat
{
    internal static GamebryoRangedHitOutcome ApplyHit(
        GamebryoRangedAttack attack,
        string equippedWeaponFormId,
        GamebryoCombatantState target)
    {
        if (string.IsNullOrWhiteSpace(attack.WeaponFormId) ||
            string.IsNullOrWhiteSpace(attack.AmmunitionFormId) ||
            attack.Damage <= 0 ||
            string.IsNullOrWhiteSpace(target.ReferenceFormId) ||
            target.MaximumHealth <= 0 || target.CurrentHealth < 0 ||
            target.CurrentHealth > target.MaximumHealth ||
            target.Dead != (target.CurrentHealth == 0))
            throw new InvalidOperationException(
                "Gamebryo ranged-combat contract is invalid.");
        if (!equippedWeaponFormId.Equals(
                attack.WeaponFormId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Gamebryo ranged-combat equipped weapon differs.");
        if (target.Dead)
            return new GamebryoRangedHitOutcome(target, false);
        var health = Math.Max(0, target.CurrentHealth - attack.Damage);
        var updated = target with { CurrentHealth = health, Dead = health == 0 };
        return new GamebryoRangedHitOutcome(updated, updated.Dead);
    }
}
