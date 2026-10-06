using System.Numerics;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.Bots;

internal static class CombatSkillContracts
{
    internal const string Target = "Fixture.esm:000101", Weapon = "Fixture.esm:000201",
        Ammo = "Fixture.esm:000301", Projectile = "Fixture.esm:000401";

    internal static BotCombatObservation Observation() => new(1, "fixture-cell",
        new("fixture-build", "fixture-owned-source", "flat", "fixture-process/player"),
        Vector3.UnitY, Vector3.UnitZ, Vector3.UnitY, Vector3.UnitZ, Target,
        false, false, false, false, true, true, true, 100, 100,
        new("fire", "aim", "reload", "pause"), new(Weapon, Ammo, Projectile, true, true, 4, 8, 30, true),
        new(0, 0, null, null, null),
        [new(Target, new(0, 0, 10), new(0, 1, 10), "source-BPNT-current-skeleton-pose",
            true, true, true, false, 30, Target, Target, null)]);

    internal static BotCombatObservation Discharge(BotCombatObservation before, float damage = 35, bool delayed = false)
    {
        var health = before.Targets[0].Health!.Value - damage;
        var impact = new BotCombatImpact(Target, Weapon, Projectile, damage, health <= 0, true);
        var shot = new BotCombatShot(before.Firing.Shots + 1, Weapon, Ammo, Projectile, Target,
            "actual-fixture-body-collider", 10, 1, delayed ? 1 : 0, delayed ? null : impact);
        return before with
        {
            Sample = before.Sample + 1,
            Weapon = before.Weapon! with { Loaded = before.Weapon!.Loaded - 1, Action = delayed ? "attack" : null },
            Firing = new(shot.Ordinal, delayed ? 1 : 0, shot, before.Firing.LastImpact, before.Firing.ImpactToken),
            Targets = [before.Targets[0] with
            {
                Health = delayed ? before.Targets[0].Health : health,
                Dead = !delayed && health <= 0, TargetsPlayer = delayed || health > 0
            }]
        };
    }

    internal static void Run()
    {
        SelectionAndPose();
        AttributedImmediateAndDelayedDeaths();
        ReloadAndMissRecovery();
        CancellationAndObservationLoss();
        ReceiptNegatives();
        RetainedVerifiedLibrary();
        NativeObservationShape();
        Console.WriteLine("Combat skill: resident source threats/BPNT, ordinary look/fire/reload, raw health/death attribution, obstruction, bounded recovery, cancellation/stale observations, negative receipts and scoped persisted feedback PASS.");
    }

    private static ReactiveCombatSkill Start(BotCombatObservation observation, VerifiedBotSkillLibrary? library = null)
    {
        var skill = new ReactiveCombatSkill(library ?? new());
        skill.Start(observation, ReactiveCombatSkill.SelectThreat(observation)!);
        return skill;
    }

    private static void SelectionAndPose()
    {
        var original = Observation();
        var bystander = original.Targets[0] with { Reference = "bystander", Position = new(0, 0, 1), TargetsPlayer = false };
        var missing = original.Targets[0] with { Reference = "nonresident", Position = new(0, 0, 2), Resident = false };
        var disabled = original.Targets[0] with { Reference = "disabled", Position = new(0, 0, 3), Enabled = false };
        var dead = original.Targets[0] with { Reference = "corpse", Position = new(0, 0, 4), Dead = true };
        var state = original with { Targets = [bystander, missing, disabled, dead, original.Targets[0]] };
        Require(ReactiveCombatSkill.SelectThreat(state)?.Reference == Target &&
            ReactiveCombatSkill.SelectThreat(state, "bystander") is null, "The skill fabricated hostility/residency or accepted an explicit nonthreat.");
        var skill = Start(original);
        state = original with { Sample = 2, Forward = Vector3.UnitX, AimedReference = "bystander", Weapon = original.Weapon! with { Aiming = false } };
        var step = skill.Tick(state, .016f);
        Require(!step.Input.Forward && step.Input.Combat?.Fire != true && step.Input.Combat?.Aim == true &&
            step.Input.AimAt == original.Targets[0].BodyPoint, "Ordinary aim did not use the actual posed body or fired before the exact ray.");
        Refusal(original with { Targets = [original.Targets[0] with { BodyPoint = null }] }, "engine-owner");
        Refusal(original with { Targets = [original.Targets[0] with { BodyOwner = "guessed-height" }] }, "engine-owner");
        Refusal(original with { Targets = [original.Targets[0], original.Targets[0]] }, "observation-loss");
        foreach (var blocked in new[]
        {
            original.Targets[0] with { BodyRayReference = "source-wall", Obstruction = "source-wall-collider" },
            original.Targets[0] with { MuzzleRayReference = "bystander", Obstruction = "bystander-collider" }
        })
        {
            state = original with { Targets = [blocked] }; skill = Start(state);
            for (var frame = 0; frame < 4 && skill.Active; frame++)
            {
                state = state with { Sample = state.Sample + 1 };
                step = skill.Tick(state, .4f);
                Require(step.Input.Combat?.Fire != true && !step.Input.Forward, "Obstruction produced fire or fabricated repositioning.");
            }
            Require(skill.Feedback is { Success: false, FailureKind: "bot-policy" }, "Native body/muzzle obstruction was unbounded or mistaken for an engine victory.");
        }
    }

    private static void AttributedImmediateAndDelayedDeaths()
    {
        var state = Observation(); var skill = Start(state);
        var fire = skill.Tick(state, .016f);
        Require(fire.Input.Combat?.Fire == true && !fire.Input.Forward && fire.Input.YawRadians == 0 &&
            fire.Input.PitchRadians == 0 && fire.Input.AimAt is null, "Fire changed the accepted actual ray.");
        Require(skill.Feedback is null && skill.Active, "Input delivery became a combat outcome.");
        var settled = Discharge(state);
        var step = skill.Tick(settled, .016f);
        Require(step.Finished && skill.Feedback is { Success: true, Phase: "death-observed" } &&
            skill.Feedback.Receipts.Single().VerifiedDeath && skill.Feedback.Receipts[0].HealthAfter == -5,
            "The actual raw negative actor health/death receipt was lost.");

        state = Observation() with { Firing = new(0, 0, null, new(Target, Weapon, Projectile, 35, true, true), "identical-old-impact") };
        skill = Start(state); skill.Tick(state, .016f);
        var pending = Discharge(state, delayed: true);
        step = skill.Tick(pending, .016f);
        Require(skill.Active && skill.Feedback is null && step.Input.Combat?.Aim == true &&
            step.Input.Combat?.Fire != true, "Pending impact was treated as death or repeated fire.");
        var completed = pending with
        {
            Sample = pending.Sample + 1, Weapon = pending.Weapon! with { Action = null },
            Firing = pending.Firing with { PendingImpacts = 0 },
            Targets = [pending.Targets[0] with { Dead = true, TargetsPlayer = false, Health = -5 }]
        };
        skill.Tick(completed, .016f);
        Require(skill.Feedback?.Success == true, "An isolated observed pending -> settled transition lost its actual identical impact receipt.");

        state = Observation(); skill = Start(state); skill.Tick(state, .016f);
        pending = Discharge(state, delayed: true);
        completed = pending with
        {
            Weapon = pending.Weapon! with { Action = null },
            Firing = pending.Firing with { PendingImpacts = 0, LastImpact = new(Target, Weapon, Projectile, 35, true, true), ImpactToken = "fresh-impact" },
            Targets = [pending.Targets[0] with { Dead = true, TargetsPlayer = false, Health = -5 }]
        };
        skill.Tick(completed, .016f);
        Require(skill.Feedback?.Success == true, "A fresh isolated delayed impact between observations was rejected.");
    }

    private static void ReloadAndMissRecovery()
    {
        var state = Observation() with { Weapon = Observation().Weapon! with { Loaded = 0, Reserve = 4 } };
        var skill = Start(state);
        Require(skill.Tick(state, .016f).Input.Combat?.Reload == true && skill.Feedback is null, "Empty magazine did not request ordinary reload.");
        state = state with { Sample = 2, Weapon = state.Weapon! with { Loaded = 4, Reserve = 0 } };
        Require(skill.Tick(state, .016f).Input.Combat?.Fire == true && skill.Phase == "awaiting-shot",
            "Conserved settled magazine receipt did not return to combat.");
        skill.Tick(Discharge(state), .016f);
        Require(skill.Feedback is { Success: true, Reloads: 1 }, "Reload plus actual shot/death did not retain source feedback.");

        state = Observation() with { Weapon = Observation().Weapon! with { Loaded = 0, Reserve = 4 } };
        skill = Start(state); skill.Tick(state, .016f);
        for (var frame = 0; frame < 8 && skill.Active; frame++)
        {
            state = state with { Sample = state.Sample + 1, Weapon = state.Weapon! with { Loaded = 4, Reserve = 4 } };
            Require(skill.Tick(state, 1).Input.Combat?.Fire != true, "Reload created ammunition without a conserved receipt.");
        }
        Require(skill.Feedback is { Success: false, FailureKind: "receipt-refusal" }, "Unconserved reload was not bounded.");

        state = Observation(); skill = Start(state);
        for (var miss = 0; miss <= ReactiveCombatSkill.MaximumMisses; miss++)
        {
            Require(skill.Tick(state, .016f).Input.Combat?.Fire == true, "A bounded re-aim did not issue one fire edge.");
            var attempted = Discharge(state) with { Targets = state.Targets };
            state = attempted with
            {
                Firing = attempted.Firing with
                { Last = attempted.Firing.Last! with { Reference = null, Collider = "far-world", DistanceMeters = 20, ImmediateImpact = null } }
            };
            skill.Tick(state, .016f);
            state = state with { Sample = state.Sample + 1 };
        }
        Require(skill.Feedback is { Success: false, FailureKind: "bot-policy", AimRecoveries: 3 },
            "Repeated actual misses received unbounded input or victory.");
    }

    private static void CancellationAndObservationLoss()
    {
        foreach (var change in new Func<BotCombatObservation, BotCombatObservation>[]
        {
            value => value with { Paused = true }, value => value with { Loading = true }, value => value with { ModalInput = true }
        })
        {
            var state = Observation(); var skill = Start(state); skill.Tick(state, .016f);
            var step = skill.Tick(change(state) with { Sample = 2 }, .016f);
            Require(step.Finished && step.Input == default && skill.Feedback is { Success: false, FailureKind: "cancelled" },
                "Pause/loading/modal cancellation retained combat input or accepted an outcome.");
        }
        var original = Observation(); var stale = Start(original); stale.Tick(original, .016f);
        var forged = Discharge(original) with { Sample = original.Sample };
        for (var frame = 0; frame < 6; frame++)
            Require(stale.Tick(forged, .2f).Input == default, "Stale observation held or injected combat input.");
        Require(stale.Feedback is { Success: false, FailureKind: "observation-loss" }, "Stale death/ammo observations became skill proof.");
        Refusal(original with { Binding = original.Binding with { Build = "other-build" } }, "observation-loss", original);
        Refusal(original with { Binding = original.Binding with { Source = "other-source" } }, "observation-loss", original);
        Refusal(original with { Binding = original.Binding with { Generation = "other-player" } }, "observation-loss", original);
        Refusal(original with { Scene = "other-scene" }, "observation-loss", original);
        Refusal(original with { Targets = [] }, "observation-loss", original);
        Refusal(original with { Defeated = true }, "safety-stop");
        Refusal(original with { HitPoints = 10 }, "safety-stop");
        Refusal(original with { FightingEnabled = false }, "engine-owner");
        Refusal(original with { Binding = original.Binding with { InputMode = "physical-xr" } }, "input-adapter");
        Refusal(original with { ExecutionFault = "source combat continuation is unowned" }, "engine-owner");
        Refusal(original with { Weapon = original.Weapon! with { Supported = false, Limit = "flight weapon skill is unowned" } }, "skill-limit");
    }

    private static void ReceiptNegatives()
    {
        var initial = Observation();
        foreach (var change in new Func<BotCombatObservation, BotCombatObservation>[]
        {
            value => value with { Weapon = value.Weapon! with { Loaded = initial.Weapon!.Loaded } },
            value => value with { Firing = value.Firing with { Shots = 0 } },
            value => value with { Firing = value.Firing with { Shots = 2 } },
            value => value with { Firing = value.Firing with { Last = value.Firing.Last! with { Ordinal = 99 } } },
            value => value with { Firing = value.Firing with { Last = value.Firing.Last! with { Reference = "another-actor" } } },
            value => value with { Firing = value.Firing with { Last = value.Firing.Last! with { Weapon = "another-weapon" } } },
            value => value with { Firing = value.Firing with { Last = value.Firing.Last! with { ImmediateImpact = null } } },
            value => value with { Firing = value.Firing with { Last = value.Firing.Last! with { ImmediateImpact = value.Firing.Last!.ImmediateImpact! with { Died = false } } } },
            value => value with { Targets = [value.Targets[0] with { Health = -20 }] },
            value => value with { Firing = value.Firing with { Error = "actual Hit continuation fault" } }
        })
        {
            var skill = Start(initial); skill.Tick(initial, .016f);
            var rejected = change(Discharge(initial));
            var step = skill.Tick(rejected, .016f);
            Require(step.Finished && skill.Feedback?.Success == false && step.Input == default,
                "Counter/ammo/wrong-source/stale contact/unattributed death/source fault became successful combat.");
        }
        var waiting = Start(initial); waiting.Tick(initial, .016f);
        var unchanged = initial;
        for (var frame = 0; frame < 6 && waiting.Active; frame++)
        { unchanged = unchanged with { Sample = unchanged.Sample + 1 }; waiting.Tick(unchanged, 1); }
        Require(waiting.Feedback is { Success: false, FailureKind: "receipt-refusal" }, "Delivered fire with no actual shot was unbounded.");

        var oldImpact = new BotCombatImpact(Target, Weapon, Projectile, 35, true, true);
        initial = initial with { Firing = initial.Firing with { LastImpact = oldImpact, ImpactToken = "old" } };
        waiting = Start(initial); waiting.Tick(initial, .016f);
        var hiddenPending = Discharge(initial, delayed: true);
        hiddenPending = hiddenPending with
        {
            Weapon = hiddenPending.Weapon! with { Action = null }, Firing = hiddenPending.Firing with { PendingImpacts = 0 },
            Targets = [hiddenPending.Targets[0] with { Health = -5, Dead = true, TargetsPlayer = false }]
        };
        waiting.Tick(hiddenPending, .016f);
        Require(waiting.Feedback?.Success == false, "Unchanged old impact without an observed pending transition was attributed to a new death.");
    }

    private static void RetainedVerifiedLibrary()
    {
        var state = Observation(); var library = new VerifiedBotSkillLibrary(); var skill = Start(state, library);
        skill.Tick(state, .016f); skill.Tick(Discharge(state), .016f);
        library.Record(skill.Feedback!);
        var restored = new VerifiedBotSkillLibrary(); restored.Restore(library.Serialize());
        Require(restored.IsVerified(state.Binding with { Generation = "new-process/same-owners" }, Weapon, Ammo),
            "Persistent verified policy could not be retrieved in another native generation.");
        Require(!restored.IsVerified(state.Binding with { Build = "new-build" }, Weapon, Ammo) &&
            !restored.IsVerified(state.Binding with { Source = "new-stack" }, Weapon, Ammo) &&
            !restored.IsVerified(state.Binding with { InputMode = "simulator" }, Weapon, Ammo),
            "Old proof was promoted across build/source/input boundaries.");
        skill = Start(state, restored); skill.Tick(state, .016f); skill.Cancel("ordinary takeover cancelled");
        Require(skill.Feedback is { Success: false, ReusedVerification: true },
            "A reusable policy bypassed live evidence or lost cancellation feedback.");
        var valid = library.Feedback[0];
        try
        {
            restored.Record(valid with { Attempt = Guid.NewGuid(), Receipts = [valid.Receipts[0] with { LoadedAfter = 4 }] });
            throw new Exception("Forged persisted skill success was admitted.");
        }
        catch (InvalidDataException) { }
        var count = restored.Feedback.Count;
        try { restored.Restore("{\"schema\":\"unknown\",\"feedback\":[]}"); throw new Exception("Unknown library schema was admitted."); }
        catch (InvalidDataException) { }
        Require(restored.Feedback.Count == count, "Rejected library data destroyed the previous valid evidence.");
        for (var entry = 0; entry <= VerifiedBotSkillLibrary.MaximumFeedback; entry++)
            restored.Record(valid with { Attempt = Guid.NewGuid() });
        Require(restored.Feedback.Count == VerifiedBotSkillLibrary.MaximumFeedback, "Skill evidence accumulated without its retention bound.");
    }

    private static void NativeObservationShape()
    {
        using var document = JsonDocument.Parse("""
            {"error":null,"firing":{"shots":7,"pendingHitscanImpacts":0,"pendingHitscan":[],
             "error":null,"damageError":null,"hitEventError":null,"muzzleError":null,"effectErrors":{},
             "last":{"ordinal":7,"weapon":"Fixture.esm:000201","ammunition":"Fixture.esm:000301",
              "projectile":"Fixture.esm:000401","reference":"Fixture.esm:000101","collider":"/actual/body",
              "distanceMeters":10,"projectiles":1,"projectileDamageEventsPending":0,
              "damage":{"reference":{"plugin":"Fixture.esm","objectId":257},"healthBefore":30,"healthAfter":-5,
               "healthDamage":35,"died":true}},"lastHitscanImpact":null}}
            """);
        var firing = BotCombatTelemetry.ReadFiring(document.RootElement);
        Require(firing is { Shots: 7, PendingImpacts: 0, Error: null } &&
            firing.Last?.ImmediateImpact is { Reference: Target, HealthDamage: 35, Died: true, HitEventMarked: true },
            "Native source FormKey/shot/contact observation was not read exactly.");
        using var delayed = JsonDocument.Parse("""
            {"firing":{"shots":8,"pendingHitscanImpacts":1,
             "pendingHitscan":[{"weapon":"Fixture.esm:000201","projectile":"Fixture.esm:000401","reference":"another-actor"}],
             "last":{"ordinal":8,"weapon":"Fixture.esm:000201","ammunition":"Fixture.esm:000301",
              "projectile":"Fixture.esm:000401","reference":"Fixture.esm:000101","collider":"/actual/body",
              "distanceMeters":10,"projectiles":1,"projectileDamageEventsPending":1,"damage":null},
             "lastHitscanImpact":null}}
            """);
        Require(BotCombatTelemetry.ReadFiring(delayed.RootElement).Error is not null, "A different source pending impact was silently bound to this shot.");
    }

    private static void Refusal(BotCombatObservation rejected, string kind, BotCombatObservation? initial = null)
    {
        var before = initial ?? Observation(); var skill = Start(before);
        var step = skill.Tick(rejected with { Sample = before.Sample + 1 }, .016f);
        Require(step.Finished && step.Input == default && skill.Feedback is { Success: false } &&
            skill.Feedback.FailureKind == kind, "Combat refusal/cancellation did not release input with its exact owner classification: " + kind);
    }

    internal static void Require(bool condition, string error) { if (!condition) throw new Exception(error); }
}
