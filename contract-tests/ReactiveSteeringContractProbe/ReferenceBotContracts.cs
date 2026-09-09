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
        bot.Tick(.016f); bot.Tick(.016f);
        if (activations != 1 || Phase(bot) != "awaiting-interaction")
            throw new Exception("Activation repeated or its receipt was mistaken for gameplay success.");
        observation = observation with { Paused = true, InteractionState = "conversation-active" };
        bot.Tick(.016f);
        if (Phase(bot) != "interaction-observed") throw new Exception("Real interaction response was ignored.");

        observation = observation with { Paused = false, Position = Vector3.Zero, Camera = Vector3.UnitY, Target = new(0, 0, 6), Aim = new(0, 1, 6) };
        bot.Start("actor", "approach", 1);
        for (var frame = 0; frame < 90; frame++) bot.Tick(1f / 60);
        if (Phase(bot) != "blocked" || input.Forward) throw new Exception("Blocked capsule kept receiving movement.");
        bot.Stop();
        if (input.Forward) throw new Exception("Explicit stop retained a held input.");

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
