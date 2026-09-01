using OpenNV.Runtime.World.Interactions;

var attack = new GamebryoRangedAttack("weapon", "ammunition", 4);
var target = new GamebryoCombatantState("target", 5, 5, false);
var first = GamebryoRangedCombat.ApplyHit(attack, "WEAPON", target);
if (first.Died || first.Target.CurrentHealth != 1)
    throw new InvalidOperationException("Gamebryo ranged damage differs.");
var second = GamebryoRangedCombat.ApplyHit(attack, "weapon", first.Target);
if (!second.Died || second.Target.CurrentHealth != 0)
    throw new InvalidOperationException("Gamebryo ranged death differs.");
var repeated = GamebryoRangedCombat.ApplyHit(attack, "weapon", second.Target);
if (repeated.Died || repeated.Target != second.Target)
    throw new InvalidOperationException("Gamebryo repeated death differs.");
if (!Rejects(() => GamebryoRangedCombat.ApplyHit(
        attack, "other-weapon", target)) ||
    !Rejects(() => GamebryoRangedCombat.ApplyHit(
        attack, "weapon", target with { CurrentHealth = 0, Dead = false })))
    throw new InvalidOperationException("Invalid ranged combat did not fail closed.");

Console.WriteLine("Gamebryo ranged combat probe passed.");

static bool Rejects(Action action)
{
    try
    {
        action();
        return false;
    }
    catch (InvalidOperationException)
    {
        return true;
    }
}
