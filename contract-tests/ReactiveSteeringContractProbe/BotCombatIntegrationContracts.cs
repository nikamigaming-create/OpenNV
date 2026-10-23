using System.Numerics;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.Bots;
using static CombatSkillContracts;

internal static class BotCombatIntegrationContracts
{
    internal static void Run()
    {
        PreservedNavigationGoal();
        StopAndFailureProtection();
        PersistenceFailureReleasesInput();
        OptionalCombatLimits();
        CombatPhysicsClock();
        CombatLimitReviewRegressions();
        CombatLimitAdmission();
        CombatLimitFaultPrecedence();
        CombatLimitPersistenceOrdering();
        Console.WriteLine("Reference bot combat integration: navigation resumption, ordinary combat receipts, optional direct-physics health/time stops, fault precedence, safe pause, human yield and input release before evidence persistence PASS.");
    }

    private static BotObservation Goal(BotCombatObservation combat) => new(combat.Scene, Vector3.Zero,
        Vector3.UnitY, Vector3.UnitZ, new(0, 0, 8), new(0, 1, 8), null, combat.Paused,
        true, true, true, null, "unchanged", ModalInput: combat.ModalInput, Loading: combat.Loading,
        Defeated: combat.Defeated, Combat: combat);

    private static void PreservedNavigationGoal()
    {
        var combat = Observation() with { Targets = [] };
        SteeringIntent input = default;
        var routes = 0; var cancelled = 0; var persisted = 0;
        var identities = new List<string>();
        var library = new VerifiedBotSkillLibrary();
        var bot = new ReactiveReferenceBot(identity => { identities.Add(identity); return Goal(combat); },
            (_, end, _) => { routes++; return new([end], end, end, true, "fixture-NAVM"); },
            (intent, activate) => { input = intent; Require(!activate, "Combat used activation instead of native fire."); },
            cancelRoute: () => cancelled++, skills: library, persistSkills: () => persisted++);
        bot.Start("Fixture.esm:000501", "travel", 1);
        bot.Tick(.016f);
        Require(input.Forward && routes == 1, "Existing ordinary travel was detached from the capsule/NAVM owner.");
        combat = Observation() with { Sample = 2 };
        bot.Tick(.016f);
        Require(input.Combat?.Fire == true && !input.Forward && cancelled > 0 && routes == 1,
            "The genuine local threat did not take over and release movement before firing.");
        combat = Discharge(combat);
        bot.Tick(.016f);
        using (var state = JsonSerializer.SerializeToDocument(bot.State))
        {
            var root = state.RootElement;
            Require(root.GetProperty("phase").GetString() == "replanning-after-combat" &&
                root.GetProperty("reference").GetString() == "Fixture.esm:000501" &&
                root.GetProperty("mode").GetString() == "travel" && root.GetProperty("active").GetBoolean() &&
                root.GetProperty("requestedDistanceMeters").GetSingle() == 1,
                "Verified combat replaced or completed the original ordinary navigation goal.");
        }
        Require(persisted == 1 && library.Feedback.Single().Success && !input.Pause,
            "Takeover death did not persist actual proof exactly once, or paused before resuming the goal.");
        Require(identities.Contains(ReactiveReferenceBot.CombatObservationIdentity),
            "Combat receipts remained dependent on unrelated travel target residency.");
        combat = combat with { Sample = combat.Sample + 1 };
        bot.Tick(.016f);
        Require(routes == 2 && input.Forward, "The original NAVM/capsule route was not reacquired after verified combat.");
        bot.Stop();

        combat = Observation();
        bot.Start(null, "combat", 1.5f); bot.Tick(.016f);
        combat = Discharge(combat); bot.Tick(.016f);
        Require(Phase(bot) == "combat-observed" && input.Pause && !input.Forward && input.Combat is null,
            "Standalone ordinary combat did not finish only on verified death with released controls and operator protection.");
    }

    private static void StopAndFailureProtection()
    {
        var combat = Observation(); SteeringIntent input = default;
        var library = new VerifiedBotSkillLibrary();
        var bot = new ReactiveReferenceBot(_ => Goal(combat), (_, end, _) => new([end], end, end, true),
            (intent, _) => input = intent, skills: library);
        bot.Start(null, "combat", 1.5f); bot.Tick(.016f);
        bot.Stop();
        Require(input == default && library.Feedback.Last() is { Success: false, FailureKind: "cancelled" },
            "Human input yield retained fire/aim or paused the user's session.");
        combat = Observation(); bot.Start(null, "combat", 1.5f); bot.Tick(.016f);
        bot.Stop(pauseAfter: true);
        Require(input.Pause && input.Combat is null && Phase(bot) == "stopped", "Explicit operator stop did not release controls and request ordinary pause.");
        combat = combat with { Paused = true };
        bot.Stop(pauseAfter: true);
        Require(!input.Pause, "Repeated stop toggled an already paused game back into combat.");

        foreach (var change in new Func<BotCombatObservation, BotCombatObservation>[]
        {
            value => value with { Paused = true }, value => value with { Loading = true },
            value => value with { Defeated = true, ModalInput = true }
        })
        {
            combat = Observation(); bot.Start(null, "combat", 1.5f); bot.Tick(.016f);
            combat = change(combat) with { Sample = 2 }; bot.Tick(.016f);
            Require(input == default && !Active(bot) && library.Feedback.Last().Success == false,
                "Pause/loading/death retained combat controls, restarted a skill or inferred victory.");
        }
        combat = Observation(); bot.Start(null, "combat", 1.5f); bot.Tick(.016f);
        combat = combat with { Sample = 2, Targets = [] }; bot.Tick(.016f);
        Require(Phase(bot) == "blocked" && input.Pause && input.Combat is null &&
            library.Feedback.Last().FailureKind == "observation-loss", "Lost target was mistaken for victory or left the operator exposed.");
        combat = Observation() with { Targets = [Observation().Targets[0] with { BodyPoint = null }] };
        bot.Start("Fixture.esm:000501", "approach", 1); bot.Tick(.016f);
        Require(Phase(bot) == "blocked" && input.Pause && library.Feedback.Last().FailureKind == "engine-owner",
            "Missing posed body owner became a navigation/combat bot error or retained movement.");
        combat = Observation() with { Targets = [] };
        bot.Start(Target, "combat", 1.5f); bot.Tick(.016f);
        Require(Phase(bot) == "blocked" && input.Combat is null, "An explicit source reference fabricated an eligible threat.");
    }

    private static void PersistenceFailureReleasesInput()
    {
        var combat = Observation(); SteeringIntent input = default;
        var bot = new ReactiveReferenceBot(_ => Goal(combat), (_, end, _) => new([end], end, end, true),
            (intent, _) => input = intent, persistSkills: () => throw new IOException("private evidence store denied"));
        bot.Start(null, "combat", 1.5f); bot.Tick(.016f);
        combat = Discharge(combat); bot.Tick(.016f);
        Require(Phase(bot) == "blocked" && !Active(bot) && input.Pause && input.Combat is null,
            "Skill evidence persistence failure retained controls or advertised a durable completed command.");
    }

    private static void OptionalCombatLimits()
    {
        var combat = Observation();
        var inputs = new List<SteeringIntent>();
        var library = new VerifiedBotSkillLibrary();
        var bot = new ReactiveReferenceBot(_ => Goal(combat), (_, end, _) => new([end], end, end, true),
            (intent, _) => inputs.Add(intent), skills: library);
        // Equality stops before either ordinary fire or reload is dispatched.
        foreach (var loaded in new[] { 0, 4 })
        {
            combat = Observation() with { Weapon = Observation().Weapon! with { Loaded = loaded } };
            bot.Start(null, "combat", 1.5f, minimumCombatHitPoints: 100);
            inputs.Clear(); bot.Tick(.125f);
            Require(!Active(bot) && inputs.Count(value => value.Pause) == 1 &&
                inputs.All(value => value.Combat is null) && !library.Feedback.Last().Success,
                "Exact health floor dispatched fire/reload, fabricated success or omitted ordinary protection.");
            var count = inputs.Count; bot.AdvanceCombatPhysics(.125f); bot.Tick(.125f);
            Require(inputs.Count == count, "Stopped physics/process callbacks sent another Pause pulse.");
        }
        combat = Observation();
        bot.Start(null, "combat", 1.5f, minimumCombatHitPoints: 90);
        inputs.Clear(); bot.Tick(.125f);
        Require(inputs.Last().Combat?.Fire == true, "Health above the optional floor could not use ordinary fire.");
        bot.AdvanceCombatPhysics(.125f);
        combat = Discharge(combat, delayed: true) with { HitPoints = 90 };
        bot.AdvanceCombatPhysics(.125f);
        bot.Tick(.125f);
        Require(!Active(bot) && inputs.Last().Pause && !library.Feedback.Last().Success &&
            library.Feedback.Last().Receipts.Count == 0,
            "A pending shot became a successful or invented receipt at the health stop.");
        using (var state = JsonSerializer.SerializeToDocument(bot.State))
        {
            var root = state.RootElement;
            var stop = root.GetProperty("combatPolicyStop");
            Require(root.GetProperty("minimumCombatHitPoints").GetSingle() == 90 &&
                stop.GetProperty("Sample").GetInt64() == combat.Sample &&
                stop.GetProperty("HitPoints").GetSingle() == 90 &&
                stop.GetProperty("SimulationSeconds").GetSingle() == .25f &&
                stop.GetProperty("Reason").GetString() == "minimum-hit-points",
                "Policy stop lost its actual source observation, elapsed time or selected floor.");
        }

        foreach (var blocked in new Func<BotCombatObservation, BotCombatObservation>[]
        {
            value => value with { Paused = true }, value => value with { Loading = true },
            value => value with { ModalInput = true }
        })
        {
            combat = blocked(Observation());
            bot.Start(null, "combat", 1.5f, maximumCombatSimulationSeconds: .25f);
            inputs.Clear(); bot.AdvanceCombatPhysics(2); bot.Tick(2);
            Require(inputs.All(value => value.Combat is null && !value.Pause),
                "Waiting for paused/loading/modal admission sent combat or toggled Pause.");
            combat = Observation() with { Sample = 2 };
            bot.Tick(.125f);
            bot.AdvanceCombatPhysics(.125f);
            Require(Active(bot) && inputs.Last().Combat?.Fire == true,
                "Paused/loading/modal admission consumed the simulation budget.");
            combat = combat with { Sample = 3 };
            bot.AdvanceCombatPhysics(.125f);
            bot.Tick(.125f);
            Require(!Active(bot) && inputs.Last().Pause && !library.Feedback.Last().Success,
                "Exact simulation boundary did not stop the pending attempt.");
            using var state = JsonSerializer.SerializeToDocument(bot.State);
            Require(state.RootElement.GetProperty("combatSimulationSeconds").GetSingle() == .25f &&
                state.RootElement.GetProperty("combatPolicyStop").GetProperty("Reason").GetString() == "simulation-time",
                "Combat simulation time included excluded admission frames or lost its stop reason.");
        }
        combat = Observation();
        bot.Start(null, "combat", 1.5f, maximumCombatSimulationSeconds: .25f);
        bot.Tick(.125f);
        bot.AdvanceCombatPhysics(.125f);
        Require(Active(bot), "A new Start inherited the previous simulation budget.");
        bot.Stop();
        combat = Observation() with { HitPoints = 89 };
        bot.Start(null, "combat", 1.5f);
        inputs.Clear(); bot.Tick(.5f);
        Require(Active(bot) && inputs.Last().Combat?.Fire == true,
            "Absent policy limits retained the previous floor/time or changed default combat admission.");
        using (var state = JsonSerializer.SerializeToDocument(bot.State))
            Require(state.RootElement.GetProperty("minimumCombatHitPoints").ValueKind == JsonValueKind.Null &&
                state.RootElement.GetProperty("maximumCombatSimulationSeconds").ValueKind == JsonValueKind.Null &&
                state.RootElement.GetProperty("combatPolicyStop").ValueKind == JsonValueKind.Null,
                "Default Start retained policy limits or an obsolete stop observation.");
        bot.Stop();
    }

    private static void CombatPhysicsClock()
    {
        var combat = Observation(); var observations = 0; var persisted = 0;
        var inputs = new List<SteeringIntent>();
        var bot = new ReactiveReferenceBot(_ => { observations++; return Goal(combat); },
            (_, end, _) => new([end], end, end, true), (intent, _) => inputs.Add(intent),
            persistSkills: () => persisted++);
        bot.Start(null, "combat", 1.5f, maximumCombatSimulationSeconds: .25f);
        bot.Tick(.016f);
        // Arbitrary process intervals must not accrue physics simulation time.
        foreach (var seconds in new[] { .01f, .02f, .4f }) bot.Tick(seconds);
        Require(Active(bot) && PhysicsSeconds(bot) == 0, "Process delta was still counted as combat physics time.");
        bot.AdvanceCombatPhysics(.125f);
        Require(Active(bot) && PhysicsSeconds(bot) == .125f, "The actual physics callback did not advance the clock.");
        bot.AdvanceCombatPhysics(10);
        combat = combat with { Sample = 0 };
        bot.AdvanceCombatPhysics(10);
        Require(PhysicsSeconds(bot) == .125f, "Duplicate/regressed physics samples advanced simulation time.");
        combat = combat with { Sample = 2 };
        // Process inspects frame 2 before its physics accounting: equality must
        // not suppress the subsequent newly accounted physics limit.
        bot.Tick(.016f);
        bot.AdvanceCombatPhysics(.125f);
        var count = inputs.Count;
        bot.AdvanceCombatPhysics(.125f); bot.Tick(.016f);
        Require(!Active(bot) && PhysicsSeconds(bot) == .25f && persisted == 1 &&
            inputs.Count(value => value.Pause) == 1 && inputs.Count == count,
            "The same process/physics sample suppressed or duplicated the exact time stop/Pause/persistence.");

        foreach (var excluded in new Func<BotCombatObservation, BotCombatObservation>[]
        {
            value => value with { Paused = true }, value => value with { Loading = true },
            value => value with { ModalInput = true }
        })
        {
            combat = Observation();
            bot.Start(null, "combat", 1.5f, maximumCombatSimulationSeconds: .25f);
            bot.Tick(.016f);
            combat = excluded(combat) with { Sample = 2 };
            bot.AdvanceCombatPhysics(10);
            Require(PhysicsSeconds(bot) == 0 && Active(bot), "Excluded source physics accrued optional simulation time.");
            combat = Observation() with { Sample = 3 };
            bot.AdvanceCombatPhysics(.125f);
            Require(PhysicsSeconds(bot) == .125f && Active(bot), "Eligible physics did not resume from the excluded source state.");
            bot.Stop();
        }
        combat = Observation();
        bot.Start(null, "combat", 1.5f);
        bot.Tick(.016f);
        var reads = observations; count = inputs.Count;
        bot.AdvanceCombatPhysics(10);
        Require(observations == reads && inputs.Count == count && PhysicsSeconds(bot) == 0,
            "Default combat acquired new physics observations/input or policy time.");
        bot.Stop();
        combat = Observation() with { Targets = [] };
        bot.Start(Target, "travel", 1.5f);
        reads = observations; count = inputs.Count;
        bot.AdvanceCombatPhysics(10);
        Require(observations == reads && inputs.Count == count && PhysicsSeconds(bot) == 0,
            "Noncombat goals acquired an optional physics owner.");
        bot.Stop();
        combat = Observation();
        bot.Start(null, "combat", 1.5f, maximumCombatSimulationSeconds: .25f);
        // A large process delta may still trigger the unchanged skill's own
        // receipt/stale bounds; it must never become physics time or a policy stop.
        bot.Tick(.016f); bot.Tick(20);
        using var state = JsonSerializer.SerializeToDocument(bot.State);
        Require(!Active(bot) && PhysicsSeconds(bot) == 0 &&
            state.RootElement.GetProperty("combatPolicyStop").ValueKind == JsonValueKind.Null,
            "An arbitrary large process interval became an optional physics stop.");
    }

    private static void CombatLimitReviewRegressions()
    {
        var failures = new List<string>();
        foreach (var timeBound in new[] { false, true })
            foreach (var (change, kind) in new (Func<BotCombatObservation, BotCombatObservation>, string)[]
            {
            (value => value with { Targets = [] }, "observation-loss"),
            (value => value with { Targets = [value.Targets[0], value.Targets[0]] }, "observation-loss"),
            (value => value with { Targets = [value.Targets[0] with { Error = "target owner" }] }, "engine-owner"),
            (value => value with { Targets = [value.Targets[0] with { Health = null }] }, "engine-owner"),
            (value => value with { Weapon = null }, "engine-owner"),
            (value => value with { Weapon = value.Weapon! with { Error = "weapon owner" } }, "engine-owner"),
            (value => value with { Weapon = value.Weapon! with { Supported = false } }, "skill-limit"),
            (value => value with { Weapon = value.Weapon! with { Ammunition = "changed" } }, "observation-loss"),
            (value => value with { Weapon = value.Weapon! with { Loaded = -1 } }, "observation-loss"),
            (value => value with { Firing = value.Firing with { Error = "firing owner" } }, "engine-owner")
            })
            {
                var combat = Observation(); var library = new VerifiedBotSkillLibrary();
                var inputs = new List<SteeringIntent>();
                var bot = new ReactiveReferenceBot(_ => Goal(combat), (_, end, _) => new([end], end, end, true),
                    (intent, _) => inputs.Add(intent), skills: library);
                bot.Start(null, "combat", 1.5f, minimumCombatHitPoints: timeBound ? null : 90,
                    maximumCombatSimulationSeconds: timeBound ? .125f : null);
                bot.Tick(.016f);
                combat = change(combat with { Sample = 2, HitPoints = 90 });
                bot.AdvanceCombatPhysics(.125f); bot.Tick(.016f);
                using var state = JsonSerializer.SerializeToDocument(bot.State);
                if (Active(bot) || library.Feedback.Last().FailureKind != kind || library.Feedback.Last().Success ||
                    library.Feedback.Last().Receipts.Count != 0 || PhysicsSeconds(bot) != 0 ||
                    state.RootElement.GetProperty("combatPolicyStop").ValueKind != JsonValueKind.Null)
                    failures.Add($"{(timeBound ? "time" : "HP")} masked {kind} source owner");
            }
        foreach (var completedShot in new[] { false, true })
        {
            var combat = Observation(); var library = new VerifiedBotSkillLibrary(); var persisted = 0;
            var inputs = new List<SteeringIntent>();
            var bot = new ReactiveReferenceBot(_ => Goal(combat), (_, end, _) => new([end], end, end, true),
                (intent, _) => inputs.Add(intent), skills: library, persistSkills: () => persisted++);
            bot.Start(null, "combat", 1.5f, minimumCombatHitPoints: 90);
            bot.Tick(.016f); // The ordinary skill has a pending shot.
            combat = combat with { Sample = 2 };
            bot.AdvanceCombatPhysics(.125f); // Early physics sees 100 HP at sample 2.
            // Later actor physics changes vitals and may complete the shot in that SAME sample.
            combat = (completedShot ? Discharge(combat) : combat) with { Sample = 2, HitPoints = 90 };
            bot.Tick(.016f);
            using var state = JsonSerializer.SerializeToDocument(bot.State);
            var stop = state.RootElement.GetProperty("combatPolicyStop");
            if (Active(bot) || stop.ValueKind != JsonValueKind.Object ||
                stop.GetProperty("HitPoints").GetSingle() != 90 ||
                library.Feedback.Last().Success || library.Feedback.Last().Receipts.Count != 0 ||
                inputs.Count(value => value.Pause) != 1 || persisted != 1 || PhysicsSeconds(bot) != .125f)
                failures.Add($"physics-first same-sample HP stop lost (completion={completedShot})");
            var inputCount = inputs.Count;
            bot.AdvanceCombatPhysics(.125f); bot.Tick(.016f);
            if (inputs.Count != inputCount || persisted != 1) failures.Add("Stopped callbacks duplicated Pause/persistence.");
        }
        Require(failures.Count == 0, "Combat review regressions: " + string.Join("; ", failures));
    }

    private static float PhysicsSeconds(ReactiveReferenceBot bot)
    {
        using var state = JsonSerializer.SerializeToDocument(bot.State);
        return state.RootElement.GetProperty("combatSimulationSeconds").GetSingle();
    }

    private static void CombatLimitAdmission()
    {
        var combat = Observation(); var inputs = 0;
        var bot = new ReactiveReferenceBot(_ => Goal(combat), (_, end, _) => new([end], end, end, true),
            (_, _) => inputs++);
        bot.Start(null, "combat", 1.5f); bot.Tick(.016f);
        var before = inputs;
        foreach (var value in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject(() => bot.Start(null, "combat", 1.5f, minimumCombatHitPoints: value));
            Reject(() => bot.Start(null, "combat", 1.5f, maximumCombatSimulationSeconds: value));
        }
        foreach (var mode in new[] { "travel", "interact", "approach", "follow", "observe", "stop" })
        {
            Reject(() => ReactiveReferenceBot.ValidateCombatLimits(mode, 90, null));
            Reject(() => ReactiveReferenceBot.ValidateCombatLimits(mode, null, 10));
            Reject(() => bot.Start(Target, mode, 1.5f, minimumCombatHitPoints: 90));
        }
        Require(inputs == before && Active(bot), "Rejected limits changed the current input/session before admission.");
        bot.Stop();
    }

    private static void CombatLimitFaultPrecedence()
    {
        foreach (var (change, kind) in new (Func<BotCombatObservation, BotCombatObservation>, string)[]
        {
            (value => value with { Defeated = true }, "safety-stop"),
            (value => value with { ExecutionFault = "source fault" }, "engine-owner"),
            (value => value with { HitPoints = float.NaN }, "engine-owner"),
            (value => value with { MaximumHitPoints = 0 }, "engine-owner"),
            (value => value with { Binding = value.Binding with { Generation = "changed" } }, "observation-loss"),
            (value => value with { Scene = "changed" }, "observation-loss"),
            (value => value with { Sample = 0 }, "observation-loss"),
            (value => value with { Sample = 1 }, "observation-loss"),
            (value => value with { HitPoints = 15 }, "safety-stop")
        })
        {
            var combat = Observation(); SteeringIntent input = default;
            var library = new VerifiedBotSkillLibrary();
            var bot = new ReactiveReferenceBot(_ => Goal(combat), (_, end, _) => new([end], end, end, true),
                (intent, _) => input = intent, skills: library);
            bot.Start(null, "combat", 1.5f, minimumCombatHitPoints: 90, maximumCombatSimulationSeconds: .25f);
            bot.Tick(.125f);
            bot.AdvanceCombatPhysics(.125f);
            combat = change(combat with { Sample = 2, HitPoints = 90 });
            bot.AdvanceCombatPhysics(.25f);
            bot.Tick(1.125f);
            // Inspect deliberate invalid source vitals without relaxing production JSON publication.
            using var state = JsonSerializer.SerializeToDocument(bot.State, new JsonSerializerOptions
            {
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
            });
            Require(!state.RootElement.GetProperty("active").GetBoolean() && input.Combat is null && !library.Feedback.Last().Success &&
                library.Feedback.Last().FailureKind == kind &&
                state.RootElement.GetProperty("combatSimulationSeconds").GetSingle() == .125f &&
                state.RootElement.GetProperty("combatPolicyStop").ValueKind == JsonValueKind.Null,
                "An optional limit masked the existing source/vitals/binding/stale/critical-health refusal.");
        }
    }

    private static void CombatLimitPersistenceOrdering()
    {
        var combat = Observation();
        var inputs = new List<SteeringIntent>();
        var persisted = 0;
        var bot = new ReactiveReferenceBot(_ => Goal(combat), (_, end, _) => new([end], end, end, true),
            (intent, _) => inputs.Add(intent), persistSkills: () =>
            {
                persisted++;
                Require(inputs.Last().Pause && inputs.Last().Combat is null,
                    "Persistence ran before ordinary stop protection released combat input.");
                throw new IOException("private evidence store denied");
            });
        bot.Start(null, "combat", 1.5f, minimumCombatHitPoints: 90);
        inputs.Clear(); bot.Tick(.125f);
        combat = combat with { Sample = 2, HitPoints = 90 };
        bot.Tick(.125f); bot.Tick(.125f);
        Require(persisted == 1 && inputs.Count(value => value.Pause) == 1 && !Active(bot),
            "Throwing persistence retried protection, retained combat or lost the single feedback attempt.");

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        combat = Observation(); inputs.Clear();
        var blocking = new ReactiveReferenceBot(_ => Goal(combat), (_, end, _) => new([end], end, end, true),
            (intent, _) => inputs.Add(intent), persistSkills: () => { entered.Set(); release.Wait(); });
        blocking.Start(null, "combat", 1.5f, maximumCombatSimulationSeconds: .25f);
        blocking.Tick(.125f);
        blocking.AdvanceCombatPhysics(.125f);
        combat = combat with { Sample = 2 };
        var tick = Task.Run(() => blocking.AdvanceCombatPhysics(.125f));
        try
        {
            Require(entered.Wait(TimeSpan.FromSeconds(5)), "Blocked persistence was not reached.");
            Require(!tick.IsCompleted && !Active(blocking) && inputs.Last().Pause &&
                inputs.Last().Combat is null && inputs.Count(value => value.Pause) == 1,
                "Blocking persistence delayed source-driven stop/input release or sent duplicate protection.");
        }
        finally { release.Set(); tick.GetAwaiter().GetResult(); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Malformed or unsupported optional combat limits were admitted.");
    }

    private static string? Phase(ReactiveReferenceBot bot)
    {
        using var state = JsonSerializer.SerializeToDocument(bot.State);
        return state.RootElement.GetProperty("phase").GetString();
    }

    private static bool Active(ReactiveReferenceBot bot)
    {
        using var state = JsonSerializer.SerializeToDocument(bot.State);
        return state.RootElement.GetProperty("active").GetBoolean();
    }
}
