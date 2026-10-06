using System.Numerics;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.Bots;

internal static class BotInteractionContracts
{
    internal static void Run()
    {
        var evidence = new BotInteractionEvidence();
        var closed = new BotInteractionSnapshot("unchanged", null, []);
        evidence.Begin("book", closed);
        Require(evidence.Observe("book", closed) == 0, "Admission was mistaken for a gameplay result.");
        evidence.End("book", closed, true);
        evidence.Finish("book", true);
        Require(evidence.Observe("book", closed with { Menus = [1060] }) == 0 &&
            evidence.Observe("book", closed with { TargetState = "later autonomous mutation" }) == 0,
            "A later unrelated menu or autonomous target mutation completed a no-op activation.");
        evidence.Begin("other", closed);
        evidence.End("other", closed with { Menus = [1060] }, true);
        evidence.Finish("other", true);
        Require(evidence.Observe("other", closed) == 1 && evidence.Observe("book", closed) == 0,
            "Another reference's result completed the requested interaction.");
        evidence.Begin("book", closed);
        evidence.End("book", closed with { Menus = [1060] }, true);
        Require(evidence.Observe("book", closed) == 0, "A block result escaped before the full activation finished.");
        evidence.Finish("book", true);
        Require(evidence.Observe("book", closed) == 1 && evidence.Observe("book", closed) == 1,
            "Actual synchronous book opening was lost or published repeatedly.");
        evidence.Begin("failed", closed);
        evidence.End("failed", closed with { Menus = [1060], TargetState = "changed-prefix" }, false);
        evidence.Finish("failed", false);
        Require(evidence.Observe("failed", closed with { TargetOutcome = "conversation" }) == 0,
            "A failed source activation or its later UI was reported successful.");
        evidence.Begin("actor", closed); evidence.End("actor", closed with { RequestedOutcome = "conversation" }, true); evidence.Finish("actor", true);
        Require(evidence.Observe("actor", closed) == 0 &&
            evidence.Observe("actor", closed with { TargetOutcome = "conversation" }) == 1,
            "A queued conversation was accepted before its target's real menu appeared.");
        evidence.Begin("door", closed); evidence.End("door", closed with
        {
            RequestedOutcome = "portal:source-destination", RequestOrdinal = 1,
            RequestedOutcomeKind = BotInteractionOutcomeKind.Portal
        }, true); evidence.Finish("door", true);
        Require(evidence.Observe("door", closed) == 0 &&
            evidence.Observe("door", closed with
            {
                TargetOutcome = "portal:source-destination", TargetOutcomeKind = BotInteractionOutcomeKind.Portal
            }) == 1,
            "Source portal admission and observed destination arrival were conflated.");
        evidence.Begin("item", closed); evidence.End("item", closed with { TargetState = "taken" }, true);
        evidence.Finish("item", true);
        Require(evidence.Observe("item", closed) == 1, "An actual target mutation was ignored.");
        evidence.Begin("interleaved", closed); evidence.End("interleaved", closed, true);
        var unrelatedMenu = closed with { Menus = [1060] };
        evidence.Begin("interleaved", unrelatedMenu); evidence.End("interleaved", unrelatedMenu, true);
        evidence.Finish("interleaved", true);
        Require(evidence.Observe("interleaved", unrelatedMenu) == 0, "An unrelated menu between activation blocks became a result.");
        Require(evidence.Observe("interleaved", unrelatedMenu with { TargetOutcome = "conversation" }) == 0,
            "A later same-reference conversation without an activation-owned request became a result.");
        var alreadyRequested = closed with { RequestedOutcome = "conversation" };
        evidence.Begin("old-request", alreadyRequested); evidence.End("old-request", alreadyRequested, true); evidence.Finish("old-request", true);
        Require(evidence.Observe("old-request", alreadyRequested with { TargetOutcome = "conversation" }) == 0,
            "A conversation request predating activation completed the new interaction.");
        evidence.Begin("wrong-outcome", closed); evidence.End("wrong-outcome", closed with { RequestedOutcome = "conversation" }, true);
        evidence.Finish("wrong-outcome", true);
        Require(evidence.Observe("wrong-outcome", closed with { TargetOutcome = "portal:source-destination" }) == 0,
            "A different target outcome satisfied the activation's pending request.");
        evidence.Begin("later-failure", closed); evidence.End("later-failure", unrelatedMenu, true);
        evidence.Begin("later-failure", unrelatedMenu); evidence.Finish("later-failure", false);
        Require(evidence.Observe("later-failure", unrelatedMenu) == 0, "A later block failure published an earlier activation prefix.");
        CheckSourceLinkedEffects();
        CheckBotResponse();
        CheckPortalObservation();
        CheckDefeatedObservation();
        Console.WriteLine("Reference bot interaction evidence: source-target results, actual book menu, unrelated stage/timer/pause/menu rejection, other-target isolation, failed prefix and deferred target UI PASS.");
    }

    private static void CheckSourceLinkedEffects()
    {
        var control = new BotInteractionSourceState("source-control", false, false, false, false, []);
        var linked = new BotInteractionSourceState("source-door", false, false, false, false,
            [new(1, "owned-model-hash", "Close", 2, false, null)]);
        var before = new BotInteractionSnapshot(BotInteractionSourceState.StableEffects(control, linked), null, []);
        var ticking = linked with
        { Animations = linked.Animations.Select(animation => animation with { ElapsedSeconds = 9, StartPending = true }).ToArray() };
        Require(BotInteractionSourceState.StableEffects(control, ticking) == before.TargetState,
            "An animation clock or consumed start marker became a stable source activation effect.");
        var opened = linked with { DoorOpen = true, Animations = [new(1, "owned-model-hash", "Open", 0, true, null)] };
        var after = before with { TargetState = BotInteractionSourceState.StableEffects(control, opened) };
        var evidence = new BotInteractionEvidence();
        evidence.Begin("source-control", before); evidence.End("source-control", after, true); evidence.Finish("source-control", true);
        Require(evidence.Observe("source-control", after) == 1 && evidence.Observe("other-control", after) == 0,
            "A real source-linked door/sequence change was ignored or attributed to a different activator.");
        evidence.Begin("no-op", before); evidence.End("no-op", before, true); evidence.Finish("no-op", true);
        Require(evidence.Observe("no-op", after) == 0,
            "A linked target's later autonomous mutation became a no-op activation's result.");
        evidence.Begin("failed-control", before); evidence.End("failed-control", after, false); evidence.Finish("failed-control", false);
        Require(evidence.Observe("failed-control", after) == 0,
            "A failed source control published a linked effect from its consumed prefix.");
    }

    private static void CheckBotResponse()
    {
        var evidence = new BotInteractionEvidence();
        var snapshot = new BotInteractionSnapshot("unchanged", null, []);
        var paused = false; var stage = 40; var timer = 0; var successfulMenu = false; var activations = 0;
        var input = default(SteeringIntent);
        var bot = new ReactiveReferenceBot(reference => new("cell", Vector3.Zero, Vector3.UnitY, Vector3.UnitZ,
            Vector3.UnitZ, Vector3.One, reference, paused, true, true, true, null,
            evidence.Observe(reference, snapshot).ToString()), (_, end, _) => new([end], end, end, true), (intent, activate) =>
        {
            input = intent;
            if (!activate) return;
            activations++;
            evidence.Begin("book", snapshot);
            if (successfulMenu) snapshot = snapshot with { Menus = [1060] };
            evidence.End("book", snapshot, true);
            evidence.Finish("book", true);
        });
        bot.Start("book", "interact", 1); bot.Tick(.016f);
        stage++; timer++; paused = true;
        bot.Tick(.016f);
        Require(stage == 41 && timer == 1 && Phase(bot) == "awaiting-interaction", "Unrelated stage/timer/pause ended activation waiting.");
        bot.Tick(3.1f);
        Require(Phase(bot) == "awaiting-interaction" && input == default, "User pause consumed the pending activation response bound.");
        paused = false;
        bot.Tick(3.1f);
        Require(Phase(bot) == "blocked" && activations == 1 && input == default, "No-op activation did not time out and release input.");
        paused = false; successfulMenu = true;
        bot.Start("book", "interact", 1); bot.Tick(.016f); paused = true; bot.Tick(.016f);
        Require(Phase(bot) == "interaction-observed" && activations == 2 && input == default,
            "Actual menu1060 did not end ordinary activation waiting.");
    }

    private static void CheckPortalObservation()
    {
        // Synthetic adapter state: the native destination producer supplies
        // TargetOutcome only after loading and collision residency settle.
        // A deliberately unavailable old model detects an accidental query.
        var evidence = new BotInteractionEvidence();
        var closed = new BotInteractionSnapshot("unchanged", null, []);
        var snapshot = closed;
        var loading = false; var missingOldModel = false; string? executionFault = null;
        var geometryQueries = 0; var activations = 0; var input = default(SteeringIntent);
        BotObservation Observe(string reference)
        {
            if (executionFault is not null)
                return new("destination", Vector3.Zero, Vector3.UnitY, Vector3.UnitZ,
                    new(float.NaN, 0, 0), Vector3.Zero, null, false, true, true, false, null, "0",
                    Loading: loading, ExecutionFault: executionFault);
            var interaction = evidence.ObserveInteraction(reference, snapshot);
            if (interaction.RequiresNativeGeometry(loading))
            {
                geometryQueries++;
                if (missingOldModel) throw new NotSupportedException("old model has no matching native pickable surface");
            }
            return new(snapshot.TargetOutcome is null ? "source" : "destination", Vector3.Zero, Vector3.UnitY, Vector3.UnitZ,
                Vector3.UnitZ, Vector3.One, loading || missingOldModel ? null : reference, false, true, true,
                !loading && !missingOldModel, null, interaction.Revision.ToString(), Loading: loading);
        }
        var bot = new ReactiveReferenceBot(Observe, (_, end, _) => new([end], end, end, true), (intent, activate) =>
        {
            input = intent;
            if (!activate) return;
            activations++;
            evidence.Begin("door", snapshot);
            snapshot = snapshot with { RequestedOutcome = "portal:destination", RequestOrdinal = activations,
                RequestedOutcomeKind = BotInteractionOutcomeKind.Portal };
            evidence.End("door", snapshot, true); evidence.Finish("door", true);
        });
        bot.Start("door", "interact", 1); bot.Tick(.016f);
        Require(Phase(bot) == "awaiting-interaction" && activations == 1 && geometryQueries == 1,
            "Portal fixture did not use ordinary exact-reference activation before its request.");
        loading = missingOldModel = true;
        bot.Tick(1000);
        Require(Phase(bot) == "awaiting-interaction" && input == default && geometryQueries == 1 &&
            evidence.ObserveInteraction("door", snapshot) is { Revision: 0, SettledPortal: false },
            "Loading queried a retired model, consumed the response bound or completed a requested portal.");
        var wrong = snapshot with { TargetOutcome = "portal:wrong", TargetOutcomeKind = BotInteractionOutcomeKind.Portal };
        Require(evidence.ObserveInteraction("door", wrong) is { Revision: 0, SettledPortal: false } &&
            evidence.ObserveInteraction("door", wrong).RequiresNativeGeometry(false),
            "A different settled destination suppressed geometry or completed the requested portal.");
        loading = false;
        snapshot = snapshot with { TargetOutcome = "portal:destination", TargetOutcomeKind = BotInteractionOutcomeKind.Portal };
        bot.Tick(.016f);
        Require(Phase(bot) == "interaction-observed" && activations == 1 && input == default && geometryQueries == 1 &&
            evidence.ObserveInteraction("door", snapshot) is { Revision: 1, SettledPortal: true } &&
            evidence.ObserveInteraction("door", snapshot) is { Revision: 1, SettledPortal: true },
            "Settled source portal queried its retired model or published/completed more than once.");
        Require(evidence.ObserveInteraction("door", closed).RequiresNativeGeometry(false),
            "A historical portal receipt suppressed geometry after leaving its destination.");

        foreach (var failed in new[] { false, true })
        {
            var refused = new BotInteractionEvidence();
            var before = failed ? closed : snapshot with { TargetOutcome = null, TargetOutcomeKind = BotInteractionOutcomeKind.Other };
            refused.Begin("door", before);
            refused.End("door", snapshot with { TargetOutcome = null, TargetOutcomeKind = BotInteractionOutcomeKind.Other }, !failed);
            refused.Finish("door", !failed);
            var observed = refused.ObserveInteraction("door", snapshot);
            Require(observed is { Revision: 0, SettledPortal: false } && observed.RequiresNativeGeometry(false),
                "A failed or pre-existing portal request suppressed native geometry.");
        }
        var untyped = new BotInteractionEvidence();
        untyped.Begin("door", closed);
        untyped.End("door", snapshot with { TargetOutcome = null, TargetOutcomeKind = BotInteractionOutcomeKind.Other,
            RequestedOutcomeKind = BotInteractionOutcomeKind.Other }, true);
        untyped.Finish("door", true);
        Require(untyped.ObserveInteraction("door", snapshot with { TargetOutcomeKind = BotInteractionOutcomeKind.Other }) is
            { Revision: 1, SettledPortal: false }, "An arbitrary portal-prefixed token granted settled portal authority.");

        bot.Start("door", "interact", 1); loading = true; executionFault = "source suffix failed";
        bot.Tick(.016f);
        using (var state = JsonDocument.Parse(JsonSerializer.Serialize(bot.State)))
            Require(Phase(bot) == "blocked" && input == default && geometryQueries == 1 &&
                state.RootElement.GetProperty("error").GetString()!.Contains(executionFault, StringComparison.Ordinal),
                "Loading, stale geometry or an earlier portal result concealed a known source execution fault.");
        Console.WriteLine("Reference bot portal observation: ordinary activation, loading suspension, exact typed settled destination, retired geometry, duplicate/wrong/failed/old request and source fault PASS.");
    }

    private static void CheckDefeatedObservation()
    {
        var defeated = false;
        var outcome = "0";
        var input = default(SteeringIntent);
        var activations = 0;
        var routes = 0;
        var bot = new ReactiveReferenceBot(reference => new("cell", Vector3.Zero, Vector3.UnitY, Vector3.UnitZ,
            Vector3.UnitZ, Vector3.One, reference, defeated, true, true, true, null, outcome,
            Defeated: defeated), (_, end, _) => { routes++; return new([end], end, end, true); },
            (intent, activate) => { input = intent; if (activate) activations++; });
        bot.Start("door", "interact", 1); bot.Tick(.016f);
        Require(activations == 1 && Phase(bot) == "awaiting-interaction", "Defeat fixture did not issue ordinary activation.");
        defeated = true; outcome = "death-menu"; bot.Tick(.016f);
        using (var state = JsonDocument.Parse(JsonSerializer.Serialize(bot.State)))
            Require(Phase(bot) == "blocked" && !state.RootElement.GetProperty("active").GetBoolean() &&
                state.RootElement.GetProperty("error").GetString()!.Contains("Player died", StringComparison.Ordinal) &&
                input == default && activations == 1 && routes == 0,
                "Death menu was mistaken for an interaction or left a paused goal/input active.");
        bot.Start("door", "approach", 1); bot.Tick(1000);
        Require(Phase(bot) == "blocked" && input == default && routes == 0,
            "A new ordinary goal on a defeated player entered navigation or an indefinite pause.");
        Console.WriteLine("Reference bot defeat: paused death menu refuses interaction and new routes, releases input, and reports recovery PASS.");
    }

    private static string Phase(ReactiveReferenceBot bot)
    {
        using var state = JsonDocument.Parse(JsonSerializer.Serialize(bot.State));
        return state.RootElement.GetProperty("phase").GetString()!;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
