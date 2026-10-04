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
        var bot = new ReactiveReferenceBot(_ => observation, (_, end, _) => { routes++; return new([end], end, end, true); },
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
            (from, end, _) => { segments++; return new([Vector3.Lerp(from, end, Math.Min(1, 2 / Vector3.Distance(from, end)))], end, end, false); },
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

        var missing = new ReactiveReferenceBot(_ => throw new KeyNotFoundException("reference unloaded"), (_, end, _) => new([], end, end, true), (_, _) => { });
        missing.Start("missing", "approach", 1); missing.Tick(.016f);
        if (Phase(missing) != "blocked") throw new Exception("A missing reference escaped the failure owner.");
        var unavailableInput = new ReactiveReferenceBot(_ => observation, (_, end, _) => new([end], end, end, true), (_, _) => throw new IOException("transport lost"));
        unavailableInput.Start("actor", "approach", 1); unavailableInput.Tick(.016f);
        if (Phase(unavailableInput) != "blocked") throw new Exception("Failed input release escaped the failure owner.");
        CheckControlProgress();
        Console.WriteLine("Reference bot: moving targets, modal stop, standoff tolerance, exact-reference activation, observed completion, obstruction, missing reference and transport failure PASS.");
    }

    private static void CheckControlProgress()
    {
        var observation = new BotObservation("cell", Vector3.Zero, Vector3.UnitY, Vector3.UnitZ,
            new(0, 0, 6), new(0, 1, 6), null, false, false, false, true, null, "idle");
        SteeringIntent input = default;
        var releases = 0;
        var bot = new ReactiveReferenceBot(_ => observation, (_, end, _) => new([end], end, end, true),
            (intent, _) => { input = intent; if (!intent.Forward) releases++; });
        bot.Start("target", "approach", 1);
        for (var second = 0; second <= ReactiveReferenceBot.ControlWaitLimitSeconds; second++)
        {
            // Moving cameras and ambient speech/activity are observations, not
            // quest or player-control progress in a locked cinematic loop.
            observation = observation with { Camera = new(0, 1 + second % 3, 0), InteractionState = "radio-" + second };
            bot.Tick(1);
        }
        if (Phase(bot) != "blocked" || input.Forward || releases == 0 || !Error(bot).Contains("source movement is disabled", StringComparison.Ordinal))
            throw new Exception("A locked camera/audio loop did not stop with a visible reason and released input.");

        observation = observation with { Camera = Vector3.UnitY, InteractionState = "idle" };
        bot.Start("target", "approach", 1); bot.Tick(25);
        observation = observation with { Paused = true };
        bot.Tick(1000);
        observation = observation with { Paused = false, Loading = true };
        bot.Tick(1000);
        using (var state = State(bot))
            if (state.RootElement.GetProperty("elapsedSeconds").GetSingle() != 25 ||
                state.RootElement.GetProperty("controlWaitSeconds").GetSingle() != 25)
                throw new Exception("User pause or loading consumed the bot execution/progress bound.");
        observation = observation with { Loading = false };
        bot.Tick(6);
        if (Phase(bot) != "blocked") throw new Exception("Suspending the clock erased its previous stalled-control interval.");

        bot.Start("target", "approach", 1); bot.Tick(25);
        foreach (var update in new Func<BotObservation, BotObservation>[]
        {
            value => value with { ProgressRevision = value.ProgressRevision + 1 },
            value => value with { ActiveMenus = "1036" },
            value => value with { ControlMask = 4 },
            value => value with { Scene = "other-cell" },
        })
        {
            observation = update(observation);
            bot.Tick(1); bot.Tick(24);
            if (Phase(bot) != "waiting-for-player-control")
                throw new Exception("Actual quest/menu/control/scene progress did not renew the control wait.");
        }
        observation = observation with { MovementEnabled = true, LookingEnabled = true };
        bot.Tick(.016f);
        if (!input.Forward) throw new Exception("Restored controls did not resume ordinary navigation.");
        observation = observation with { ModalInput = true };
        bot.Tick(.016f);
        if (input.Forward) throw new Exception("Unpaused modal input retained movement.");
        bot.Tick(ReactiveReferenceBot.ControlWaitLimitSeconds);
        if (Phase(bot) != "blocked" || !Error(bot).Contains("modal input is held", StringComparison.Ordinal))
            throw new Exception("An unpaused modal lock was mistaken for an indefinitely suspended user pause.");

        observation = observation with { ModalInput = false, Paused = true, ExecutionFault = "source command failed" };
        bot.Start("target", "approach", 1); bot.Tick(.016f);
        if (Phase(bot) != "blocked" || input.Forward || !Error(bot).Contains("source command failed", StringComparison.Ordinal))
            throw new Exception("An explicit execution fault waited for a timeout or retained input.");
        var cancellationFault = false;
        observation = observation with { Paused = false, ExecutionFault = null };
        var failedCancellation = new ReactiveReferenceBot(_ => observation, (_, end, _) => new([end], end, end, true),
            (intent, _) => input = intent, cancelRoute: () =>
            {
                if (cancellationFault) throw new IOException("route owner retired");
            });
        failedCancellation.Start("target", "approach", 1); failedCancellation.Tick(.016f);
        if (!input.Forward) throw new Exception("Cancellation failure fixture did not own movement input.");
        observation = observation with { ExecutionFault = "source command failed" };
        cancellationFault = true;
        failedCancellation.Tick(.016f);
        if (Phase(failedCancellation) != "blocked" || input.Forward || !Error(failedCancellation).Contains("route owner retired", StringComparison.Ordinal))
            throw new Exception("Failed route cancellation prevented known-fault input release.");
        Console.WriteLine("Reference bot: semantic control progress, camera/audio loop rejection, pause/loading suspension, modal lock and immediate fault release PASS.");
    }

    private static JsonDocument State(ReactiveReferenceBot bot) => JsonDocument.Parse(JsonSerializer.Serialize(bot.State));
    private static string Error(ReactiveReferenceBot bot)
    {
        using var state = State(bot);
        return state.RootElement.GetProperty("error").GetString() ?? "";
    }

    private static string Phase(ReactiveReferenceBot bot)
    {
        using var state = JsonDocument.Parse(JsonSerializer.Serialize(bot.State));
        return state.RootElement.GetProperty("phase").GetString()!;
    }
}
