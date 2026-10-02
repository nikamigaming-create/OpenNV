using System.Numerics;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.Bots;

internal static class BotNavigationContracts
{
    internal static void Run()
    {
        ShortApproaches();
        ProjectedEndpoints();
        MovingTargetEndpoints();
        ClosingAndStationaryBounds();
        Console.WriteLine("Reference bot navigation: short/inside endpoints, partial corridors, projection refinement, moving-target endpoints, ray result, closing progress and stationary bounds PASS.");
    }

    private static void MovingTargetEndpoints()
    {
        var observation = Observation(1.796f);
        SteeringIntent input = default;
        var radii = new List<float>();
        var bot = new ReactiveReferenceBot(_ => observation, (_, end, radius) =>
        { radii.Add(radius); return new([end], end, end, true); }, (intent, _) => input = intent);
        bot.Start("actor", "follow", 1.5f); bot.Tick(.016f);
        observation = Move(observation, .270f) with { Target = new(0, 0, 1.986f), Aim = new(0, 1, 1.986f) };
        bot.Tick(.016f);
        Require(Phase(bot) == "replanning-moving-target" && !input.Forward,
            "A moving actor's stale endpoint incorrectly tightened source projection.");
        bot.Tick(.016f);
        Require(input.Forward && radii.Count == 2 && radii.All(radius => radius == 2),
            "The updated target did not retain a normal source route query.");
        observation = Move(observation, .320f); bot.Tick(.016f);
        Require(Phase(bot) == "following-at-distance" && !input.Forward, "Updated follow standoff did not observe arrival.");

        for (var leg = 0; leg < 8; leg++)
        {
            var start = observation.Position.Z;
            observation = observation with { Target = new(0, 0, start + 2.1f), Aim = new(0, 1, start + 2.1f) };
            bot.Tick(.016f);
            Require(input.Forward, "Follow resumed an old completed route instead of the live target.");
            observation = Move(observation, start + .5f) with
            { Target = new(0, 0, start + 2.4f), Aim = new(0, 1, start + 2.4f) };
            bot.Tick(.016f);
            Require(Phase(bot) == "replanning-moving-target", "Sub-threshold target motion lost a completed segment.");
            bot.Tick(.016f);
            observation = Move(observation, start + .75f); bot.Tick(.016f);
            Require(Phase(bot) == "following-at-distance", "Successful repeated follow legs exhausted endpoint retries.");
        }

        observation = Observation(4);
        bot = new(_ => observation, (_, end, _) => new([end], end, end, true), (intent, _) => input = intent);
        bot.Start("actor", "follow", 1.5f);
        for (var frame = 0; frame < 400; frame++)
        {
            observation = observation with { Target = new(0, 0, 4 + frame * .03f), Aim = new(0, 1, 4 + frame * .03f) };
            bot.Tick(1f / 60);
        }
        Require(Phase(bot) == "blocked" && input == default,
            "Repeated moving-target replans reset the stationary capsule bound.");
    }

    private static void ShortApproaches()
    {
        var observation = Observation(1.8f);
        SteeringIntent input = default;
        var routes = 0;
        var bot = new ReactiveReferenceBot(_ => observation, (_, end, _) =>
        { routes++; return new([new(0, 0, .248f)], end, new(0, 0, .248f), true); }, (intent, _) => input = intent);
        bot.Start("book", "approach", 1.5f); bot.Tick(.016f);
        observation = Move(observation, .08f);
        bot.Tick(.016f);
        Require(Phase(bot) == "approaching" && input.Forward && routes == 1,
            "Eight centimetres of valid short approach consumed the endpoint or failed the old segment guard.");
        observation = Move(observation, .12f); bot.Tick(.016f);
        Require(Phase(bot) == "arrival-observed" && !input.Forward, "Short approach did not observe target range.");

        observation = Observation(6);
        routes = 0;
        bot = new(_ => observation, (_, end, _) =>
        { routes++; return new([routes == 1 ? new(0, 0, .08f) : end], end, end, routes != 1); }, (intent, _) => input = intent);
        bot.Start("door", "travel", 1); bot.Tick(.016f);
        Require(input.Forward, "Short partial corridor was consumed before movement.");
        observation = Move(observation, .061f); bot.Tick(.016f);
        Require(Phase(bot) == "replanning-segment" && !input.Forward,
            "A partial corridor with observed closing distance below ten centimetres was rejected.");
        bot.Tick(.016f);
        Require(routes == 2 && input.Forward, "Short partial arrival did not request the next supported corridor.");

        observation = Observation(1);
        routes = 0;
        bot = new(_ => observation, (_, end, _) =>
        { routes++; return new([end], end, end, true); }, (intent, _) => input = intent);
        bot.Start("door", "approach", 1); bot.Tick(.016f);
        Require(Phase(bot) == "arrival-observed" && routes == 0 && !input.Forward,
            "Already-inside target range incorrectly required motion or a route.");

        observation = Observation(1.8f);
        bot = new(_ => observation, (_, end, _) => new([end], end, end, true), (intent, _) => input = intent);
        bot.Start("book", "approach", 1.5f);
        for (var frame = 0; frame < 400; frame++)
        {
            observation = Move(observation, frame * .0003f);
            bot.Tick(1f / 60);
            Require(Phase(bot) != "blocked", "Consistent slow closing below four centimetres per stall window was rejected.");
        }
        Require(Phase(bot) == "arrival-observed", "Slow ordinary approach never observed target range.");
    }

    private static void ProjectedEndpoints()
    {
        var observation = Observation(3);
        SteeringIntent input = default;
        var radii = new List<float>();
        var bot = new ReactiveReferenceBot(_ => observation, (_, end, radius) =>
        {
            radii.Add(radius);
            var endpoint = radii.Count == 1 ? new Vector3(0, 0, .25f) : end;
            return new([endpoint], end, endpoint, true, "owned-navm");
        }, (intent, _) => input = intent);
        bot.Start("door", "approach", 1); bot.Tick(.016f);
        observation = Move(observation, .20f); bot.Tick(.016f);
        Require(Phase(bot) == "replanning-endpoint" && !input.Forward,
            "Projected floor arrival was falsely accepted as the requested target range.");
        using (var state = State(bot))
        {
            var navigation = state.RootElement.GetProperty("navigation");
            Require(navigation.GetProperty("sourceIdentity").GetString() == "owned-navm" &&
                navigation.GetProperty("projectionDistanceMeters").GetSingle() > 1.7f &&
                navigation.GetProperty("projectedEndpointDistanceMeters").GetSingle() < .051f,
                "Requested/projected endpoint provenance and distance evidence were discarded.");
        }
        bot.Tick(.016f);
        Require(radii.Count == 2 && radii[1] < radii[0] && input.Forward, "Projection outside range did not tighten its source query.");
        observation = Move(observation, 2); bot.Tick(.016f);
        Require(Phase(bot) == "arrival-observed", "Closer supported projection did not observe arrival.");

        observation = Observation(3);
        var activations = 0;
        bot = new(_ => observation, (_, end, _) => new([Vector3.Zero], end, Vector3.Zero, true),
            (intent, activate) => { input = intent; if (activate) activations++; });
        bot.Start("book", "interact", 1); bot.Tick(.016f);
        Require(Phase(bot) == "aiming-at-projected-endpoint" && input.AimAt == observation.Aim && !input.Forward && activations == 0,
            "Already-inside projected floor did not aim at the actual source geometry.");
        observation = observation with { AimedReference = "book" }; bot.Tick(.016f); bot.Tick(.016f);
        Require(Phase(bot) == "awaiting-interaction" && activations == 1 && input == default,
            "Observed ray activation was repeated or confused with an observed result.");
        observation = observation with { InteractionState = "book-menu", Paused = true }; bot.Tick(.016f);
        Require(Phase(bot) == "interaction-observed", "The actual modal response was not observed after projected-floor activation.");

        observation = Observation(1) with { AimedReference = "book" };
        activations = 0;
        bot.Start("book", "interact", 1); bot.Tick(.016f); bot.Tick(3.1f);
        Require(Phase(bot) == "blocked" && activations == 1 && input == default,
            "Activation receipt without gameplay response was counted as success or retained input.");

        observation = Observation(1);
        bot = new(_ => observation, (_, end, _) => new([end], end, end, true), (intent, _) => input = intent);
        bot.Start("book", "interact", 1.5f); bot.Tick(.016f); bot.Tick(3.1f);
        Require(Phase(bot) == "replanning-endpoint" && !input.Forward,
            "Unreachable interaction inside the initial standoff never requested a closer approach.");
        bot.Tick(.016f);
        Require(Phase(bot) == "approaching" && input.Forward, "Tightened interaction standoff did not submit ordinary movement.");

        observation = Observation(0) with { Target = new(0, 5, 0), Aim = new(0, 5, 0) };
        var queries = 0;
        bot = new(_ => observation, (_, end, _) =>
        { queries++; throw new InvalidOperationException("No supported route to upper floor."); }, (intent, _) => input = intent);
        bot.Start("upstairs-door", "approach", 1); bot.Tick(.016f);
        Require(queries == 1 && Phase(bot) == "blocked" && input == default,
            "Matching horizontal coordinates on a different floor bypassed source navigation.");
    }

    private static void ClosingAndStationaryBounds()
    {
        var observation = Observation(6);
        SteeringIntent input = default;
        var routes = 0;
        var bot = new ReactiveReferenceBot(_ => observation, (from, end, _) =>
        { routes++; return new([from], end, end, false); }, (intent, _) => input = intent);
        bot.Start("door", "travel", 1);
        for (var frame = 0; frame < 10; frame++) bot.Tick(.016f);
        Require(Phase(bot) == "blocked" && routes == 4 && input == default,
            "Repeated zero-length partial segments became an unbounded stationary replan loop.");
        using (var state = State(bot))
            Require(state.RootElement.GetProperty("reference").GetString() == "door" &&
                !state.RootElement.GetProperty("active").GetBoolean() && state.RootElement.GetProperty("navigation").ValueKind == JsonValueKind.Object,
                "Failure discarded the requested goal or last endpoint evidence.");

        bot = new(_ => observation, (_, end, _) => new([end], end, end, true), (intent, _) => input = intent);
        bot.Start("door", "travel", 1);
        for (var frame = 0; frame < 200; frame++)
        {
            observation = Move(observation, -frame * .05f);
            bot.Tick(1f / 60);
        }
        Require(Phase(bot) == "blocked" && input == default,
            "Motion away from the waypoint repeatedly reset the no-closing progress bound.");

        observation = Observation(6);
        routes = 0;
        bot = new(_ => observation, (_, end, _) =>
        { routes++; return new([Vector3.Zero], end, Vector3.Zero, true); }, (intent, _) => input = intent);
        bot.Start("book", "interact", 1);
        for (var frame = 0; frame < 900; frame++) bot.Tick(1f / 60);
        Require(Phase(bot) == "blocked" && routes == 4 && input == default,
            "An unreachable final projection allowed endless aim/closing retries.");
    }

    private static BotObservation Observation(float target) => new("cell", Vector3.Zero, Vector3.UnitY, Vector3.UnitZ,
        new(0, 0, target), new(0, 1, target), null, false, true, true, true, null, "unchanged");
    private static BotObservation Move(BotObservation state, float z) => state with { Position = new(0, 0, z), Camera = new(0, 1, z) };
    private static JsonDocument State(ReactiveReferenceBot bot) => JsonDocument.Parse(JsonSerializer.Serialize(bot.State, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    private static string Phase(ReactiveReferenceBot bot) { using var state = State(bot); return state.RootElement.GetProperty("phase").GetString()!; }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
