using System.Numerics;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.Bots;

internal static class ReferenceBotContracts
{
    internal static void Run()
    {
        var observation = new BotObservation("cell-a", Vector3.Zero, Vector3.UnitY, Vector3.UnitZ,
            new(0, 0, 6), new(0, 1, 6), null, false, true, true, true, null, "closed");
        SteeringIntent input = default;
        var activations = 0; var routes = 0;
        var bot = new ReactiveReferenceBot(_ => observation, (_, end) => { routes++; return [end]; },
            (intent, activate) => { input = intent; if (activate) activations++; });

        bot.Start("actor", "approach", 2.5f);
        bot.Tick(.016f);
        if (!input.Forward || routes != 1) throw new Exception("Observed approach did not produce ordinary movement.");
        observation = observation with { Target = new(1, 0, 6), Aim = new(1, 1, 6) };
        bot.Tick(.016f);
        if (routes != 2) throw new Exception("Moving reference retained a stale destination.");
        observation = observation with { Paused = true };
        bot.Tick(.016f);
        if (input.Forward) throw new Exception("Modal gameplay retained movement.");
        observation = observation with { Paused = false, Position = new(1, 0, 3.35f) };
        bot.Tick(.016f);
        if (Phase(bot) != "arrival-observed" || input.Forward)
            throw new Exception("Waypoint arrival tolerance disagrees with standoff arrival.");

        observation = observation with { Position = new(1, 0, 5), Camera = new(1, 1, 5), AimedReference = "bystander" };
        bot.Start("actor", "interact", 1.5f); bot.Tick(.016f);
        if (activations != 0) throw new Exception("Bot activated the wrong reference.");
        observation = observation with { AimedReference = "actor" };
        bot.Tick(.016f);
        if (input.YawRadians != 0 || input.PitchRadians != 0 || input.Forward || input.AimAt is not null)
            throw new Exception("Activation changed the ray after observing its correct target.");
        bot.Tick(.016f);
        if (activations != 1 || Phase(bot) != "awaiting-interaction")
            throw new Exception("Activation repeated or its receipt was mistaken for gameplay success.");
        observation = observation with { Paused = true, InteractionState = "conversation-active" };
        bot.Tick(.016f);
        if (Phase(bot) != "interaction-observed") throw new Exception("Real interaction response was ignored.");

        observation = observation with { Paused = false, Position = Vector3.Zero, Camera = Vector3.UnitY, Target = new(0, 0, 6), Aim = new(0, 1, 6) };
        bot.Start("actor", "approach", 1);
        var obstructionReleases = 0;
        for (var frame = 0; frame < 300; frame++)
        {
            bot.Tick(1f / 60);
            if (Phase(bot) != "replanning-obstruction") continue;
            obstructionReleases++;
            if (input.Forward) throw new Exception("Obstruction replan retained movement input.");
        }
        if (obstructionReleases is < 1 or > 3) throw new Exception("Obstruction retries were absent or unbounded.");
        if (Phase(bot) != "blocked" || input.Forward) throw new Exception("Blocked capsule kept receiving movement.");
        bot.Stop();
        if (input.Forward) throw new Exception("Explicit stop retained a held input.");

        var segments = 0;
        var partial = new ReactiveReferenceBot(_ => observation,
            (from, end) => { segments++; return [Vector3.Lerp(from, end, Math.Min(1, 2 / Vector3.Distance(from, end)))]; },
            (intent, _) => input = intent);
        partial.Start("actor", "travel", 1); partial.Tick(.016f);
        observation = observation with { Position = new(0, 0, 2), Camera = new(0, 1, 2) };
        partial.Tick(.016f);
        if (Phase(partial) != "replanning-segment" || input.Forward)
            throw new Exception("A verified partial route was mistaken for arrival or retained input at its boundary.");
        partial.Tick(.016f);
        if (segments != 2 || !input.Forward) throw new Exception("The next segment was not planned from observed movement.");
        partial.Stop();
        observation = observation with { Position = Vector3.Zero, Camera = Vector3.UnitY };

        observation = observation with { Resident = false, TravelReady = true };
        bot.Start("distant-door", "travel", 1); bot.Tick(.016f);
        if (!input.Forward) throw new Exception("Known exterior destination could not be approached before streaming.");
        observation = observation with { Position = observation.Target, Camera = observation.Target + Vector3.UnitY };
        bot.Tick(.016f);
        if (Phase(bot) != "waiting-for-target-residency" || input.Forward)
            throw new Exception("Unloaded destination was incorrectly accepted as a live arrival.");
        observation = observation with { Resident = true };
        bot.Tick(.016f);
        if (Phase(bot) != "arrival-observed") throw new Exception("Streamed destination did not complete travel.");
        observation = observation with { Position = Vector3.Zero, Camera = Vector3.UnitY };

        var missing = new ReactiveReferenceBot(_ => throw new KeyNotFoundException("reference unloaded"), (_, _) => [], (_, _) => { });
        missing.Start("missing", "approach", 1); missing.Tick(.016f);
        if (Phase(missing) != "blocked") throw new Exception("A missing reference escaped the failure owner.");
        var unavailableInput = new ReactiveReferenceBot(_ => observation, (_, end) => [end], (_, _) => throw new IOException("transport lost"));
        unavailableInput.Start("actor", "approach", 1); unavailableInput.Tick(.016f);
        if (Phase(unavailableInput) != "blocked") throw new Exception("Failed input release escaped the failure owner.");
        Console.WriteLine("Reference bot: moving targets, modal stop, standoff tolerance, exact-reference activation, observed completion, obstruction, missing reference and transport failure PASS.");
    }

    private static string Phase(ReactiveReferenceBot bot)
    {
        using var state = JsonDocument.Parse(JsonSerializer.Serialize(bot.State));
        return state.RootElement.GetProperty("phase").GetString()!;
    }
}
