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
        Console.WriteLine("Reference bot combat integration: existing navigation goals/NAVM cancellation and resumption, standalone commands, death/pause/loading/lost-target release, safe operator pause, human yield and evidence-store failure PASS.");
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
