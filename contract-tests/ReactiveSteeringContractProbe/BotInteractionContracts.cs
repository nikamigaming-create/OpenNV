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
        evidence.Begin("door", closed); evidence.End("door", closed with { RequestedOutcome = "portal:source-destination", RequestOrdinal = 1 }, true); evidence.Finish("door", true);
        Require(evidence.Observe("door", closed) == 0 &&
            evidence.Observe("door", closed with { TargetOutcome = "portal:source-destination" }) == 1,
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
        CheckBotResponse();
        Console.WriteLine("Reference bot interaction evidence: source-target results, actual book menu, unrelated stage/timer/pause/menu rejection, other-target isolation, failed prefix and deferred target UI PASS.");
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
        Require(Phase(bot) == "blocked" && activations == 1 && input == default, "No-op activation did not time out and release input.");
        paused = false; successfulMenu = true;
        bot.Start("book", "interact", 1); bot.Tick(.016f); paused = true; bot.Tick(.016f);
        Require(Phase(bot) == "interaction-observed" && activations == 2 && input == default,
            "Actual menu1060 did not end ordinary activation waiting.");
    }

    private static string Phase(ReactiveReferenceBot bot)
    {
        using var state = JsonDocument.Parse(JsonSerializer.Serialize(bot.State));
        return state.RootElement.GetProperty("phase").GetString()!;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
