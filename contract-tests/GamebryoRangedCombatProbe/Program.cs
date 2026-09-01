using System.Runtime.CompilerServices;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;
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

var death = new GamebryoDeathCounterContract(1, 2, 45, 50);
var early = GamebryoDeathCounter.Advance(
    death,
    new GamebryoDeathCounterState(0, 35, false, false));
if (early.Count != 1 || !early.Stages.SequenceEqual([45]) || early.ResetAi)
    throw new InvalidOperationException("Gamebryo early combat death differs.");
var completion = GamebryoDeathCounter.Advance(
    death,
    new GamebryoDeathCounterState(1, 45, true, false));
if (completion.Count != 2 || !completion.Stages.SequenceEqual([50]) ||
    !completion.ResetAi)
    throw new InvalidOperationException("Gamebryo combat death threshold differs.");
var ordered = GamebryoDeathCounter.Advance(
    death,
    new GamebryoDeathCounterState(1, 35, false, false));
if (!ordered.Stages.SequenceEqual([45, 50]) || !ordered.ResetAi)
    throw new InvalidOperationException("Gamebryo death stage ordering differs.");
if (!Rejects(() => GamebryoDeathCounter.Advance(
        death,
        new GamebryoDeathCounterState(2, 50, true, true))))
    throw new InvalidOperationException("Completed death counter did not fail closed.");

var emptyCampaignState = (OpeningCampaignState)RuntimeHelpers.GetUninitializedObject(
    typeof(OpeningCampaignState));
using var savedCampaign = JsonDocument.Parse(JsonSerializer.Serialize(emptyCampaignState));
if (!savedCampaign.RootElement.TryGetProperty(
        nameof(OpeningCampaignState.CombatHealthByReferenceFormId), out _) ||
    !savedCampaign.RootElement.TryGetProperty(
        nameof(OpeningCampaignState.OrdinaryActorTransforms), out _) ||
    !savedCampaign.RootElement.TryGetProperty(
        nameof(OpeningCampaignState.GuidePackage), out _))
    throw new InvalidOperationException(
        "Opening campaign continuation properties are not serialized.");

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
