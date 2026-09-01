using System.Runtime.CompilerServices;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Interactions;
using OpenNV.Runtime.World.Actors;
using Godot;

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

var meleeContract = new GamebryoCreatureCombatContract(4.0f, 2.0f, 1.0f, 2.0f, 1.0f, 3);
var melee = GamebryoCreatureCombatAi.Start(meleeContract);
var chasing = melee.Advance(
    0.25,
    Transform3D.Identity,
    new Vector3(3.0f, 0.0f, 0.0f),
    new Vector3(3.0f, 0.0f, 0.0f));
if (!chasing.BeganLocomotion || chasing.Transform.Origin.DistanceTo(
        new Vector3(1.0f, 0.0f, 0.0f)) > 0.0001f)
    throw new InvalidOperationException("Creature source locomotion differs.");
var beganMelee = melee.Advance(
    0.0,
    chasing.Transform,
    new Vector3(2.0f, 0.0f, 0.0f),
    new Vector3(2.0f, 0.0f, 0.0f));
if (!beganMelee.BeganMelee || beganMelee.Damage != 0)
    throw new InvalidOperationException("Creature melee entry differs.");
var beforeHit = melee.Advance(
    0.5,
    beganMelee.Transform,
    new Vector3(2.0f, 0.0f, 0.0f),
    new Vector3(2.0f, 0.0f, 0.0f));
var atHit = melee.Advance(
    0.5,
    beforeHit.Transform,
    new Vector3(2.0f, 0.0f, 0.0f),
    new Vector3(2.0f, 0.0f, 0.0f));
if (beforeHit.Damage != 0 || atHit.Damage != 3)
    throw new InvalidOperationException("Creature source hit timing differs.");
melee.Kill();
var dead = melee.Advance(
    1.0,
    atHit.Transform,
    new Vector3(2.0f, 0.0f, 0.0f),
    new Vector3(2.0f, 0.0f, 0.0f));
if (dead.State.Phase != GamebryoCreatureCombatPhase.Dead || dead.Damage != 0)
    throw new InvalidOperationException("Creature dead AI state differs.");

var emptyCampaignState = (OpeningCampaignState)RuntimeHelpers.GetUninitializedObject(
    typeof(OpeningCampaignState));
using var savedCampaign = JsonDocument.Parse(JsonSerializer.Serialize(emptyCampaignState));
if (!savedCampaign.RootElement.TryGetProperty(
        nameof(OpeningCampaignState.CombatHealthByReferenceFormId), out _) ||
    !savedCampaign.RootElement.TryGetProperty(
        nameof(OpeningCampaignState.CombatActorAnimations), out _) ||
    !savedCampaign.RootElement.TryGetProperty(
        nameof(OpeningCampaignState.CombatActorAi), out _) ||
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
