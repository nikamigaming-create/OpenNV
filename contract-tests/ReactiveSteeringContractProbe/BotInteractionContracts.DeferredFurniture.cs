using System.Numerics;
using System.Text.Json;
using OpenNV.Runtime.Gameplay.Bots;

internal static partial class BotInteractionContracts
{
    private static void CheckDeferredFurnitureObservation()
    {
        var before = new BotInteractionSnapshot("unchanged", null, []);
        var pending = before with
        {
            RequestedOutcome = "furniture", RequestOrdinal = 1,
            RequestedOutcomeKind = BotInteractionOutcomeKind.Furniture, RequestedOutcomePending = true
        };
        var evidence = new BotInteractionEvidence();
        evidence.Begin("seat", before);
        evidence.End("seat", pending, true);
        Require(evidence.ObserveInteraction("seat", pending) is { Revision: 0, PendingFurniture: false },
            "An unfinished source activation granted a furniture wait lease.");
        evidence.Finish("seat", true);
        Require(evidence.ObserveInteraction("seat", pending) is { Revision: 0, PendingFurniture: true, SettledPortal: false } &&
            evidence.ObserveInteraction("seat", pending).RequiresNativeGeometry(false),
            "The exact dispatched furniture reservation was lost, completed early, or granted portal geometry authority.");
        foreach (var unrelated in new[]
        {
            pending with { RequestOrdinal = 2 },
            pending with { RequestedOutcomeKind = BotInteractionOutcomeKind.Other },
            pending with { RequestedOutcome = "conversation" },
            pending with { RequestedOutcomePending = false }
        })
            Require(evidence.ObserveInteraction("seat", unrelated) is { Revision: 0, PendingFurniture: false },
                "A replacement, untyped, different or retired owner prolonged the requested furniture activation.");
        Require(evidence.ObserveInteraction("other-seat", pending) is { Revision: 0, PendingFurniture: false },
            "Another reference borrowed the furniture wait lease.");
        var entering = pending with
        {
            RequestedOutcomePending = false, TargetOutcome = "furniture",
            TargetOutcomeKind = BotInteractionOutcomeKind.Furniture, TargetOutcomeOrdinal = 1
        };
        Require(evidence.ObserveInteraction("seat", entering with { TargetOutcomeOrdinal = 2 }) is
            { Revision: 0, PendingFurniture: false } &&
            evidence.ObserveInteraction("seat", entering with { TargetOutcomeKind = BotInteractionOutcomeKind.Other }) is
            { Revision: 0, PendingFurniture: false },
            "A different activation's entry or untyped outcome completed the requested reservation.");
        Require(evidence.ObserveInteraction("seat", entering) is { Revision: 1, PendingFurniture: false } &&
            evidence.ObserveInteraction("seat", entering) is { Revision: 1, PendingFurniture: false },
            "The same actual furniture owner did not complete once at observed entry.");

        foreach (var failed in new[] { false, true })
        {
            var refused = new BotInteractionEvidence();
            refused.Begin("seat", failed ? before : pending);
            refused.End("seat", pending, !failed);
            refused.Finish("seat", !failed);
            Require(refused.ObserveInteraction("seat", pending) is { Revision: 0, PendingFurniture: false } &&
                refused.ObserveInteraction("seat", entering) is { Revision: 0, PendingFurniture: false },
                "A failed or pre-existing furniture request granted wait/completion authority.");
        }
        var anonymous = new BotInteractionEvidence();
        anonymous.Begin("seat", before);
        anonymous.End("seat", pending with { RequestOrdinal = 0 }, true);
        anonymous.Finish("seat", true);
        Require(anonymous.ObserveInteraction("seat", pending with { RequestOrdinal = 0 }) is
            { Revision: 0, PendingFurniture: false } &&
            anonymous.ObserveInteraction("seat", entering with { TargetOutcomeOrdinal = 0 }) is
            { Revision: 0, PendingFurniture: false },
            "A furniture owner without the actual native publication ordinal granted authority.");

        var delayed = new DeferredFurnitureBotFixture();
        delayed.Start();
        delayed.Bot.Tick(4);
        Require(Phase(delayed.Bot) == "awaiting-interaction" && delayed.Activations == 1 && delayed.Input == default &&
            delayed.Evidence.ObserveInteraction("seat", delayed.Snapshot) is { Revision: 0, PendingFurniture: true },
            "A source furniture approach exceeding three seconds failed or became a completed interaction.");
        delayed.Paused = true;
        delayed.Bot.Tick(1000);
        delayed.Paused = false; delayed.Loading = true;
        delayed.Bot.Tick(1000);
        delayed.Loading = false;
        delayed.Bot.Tick(10);
        using (var state = JsonDocument.Parse(JsonSerializer.Serialize(delayed.Bot.State)))
            Require(Phase(delayed.Bot) == "awaiting-interaction" && delayed.Activations == 1 && delayed.Input == default &&
                state.RootElement.GetProperty("interactionPending").GetBoolean() &&
                state.RootElement.GetProperty("elapsedSeconds").GetSingle() < 15,
                "Pending wait consumed pause/loading time, lost its factual telemetry or repeated activation.");
        delayed.Enter(); delayed.Paused = true;
        delayed.Bot.Tick(.016f);
        Require(Phase(delayed.Bot) == "interaction-observed" && delayed.Activations == 1 && delayed.Input == default,
            "Actual delayed furniture entry did not complete before user-pause suspension.");

        var lost = new DeferredFurnitureBotFixture();
        lost.Start(); lost.Bot.Tick(4);
        lost.Snapshot = lost.Snapshot with { RequestedOutcomePending = false };
        lost.Bot.Tick(2.9f);
        Require(Phase(lost.Bot) == "awaiting-interaction", "Retiring a furniture reservation erased its bounded response grace.");
        lost.Bot.Tick(.2f);
        Require(Phase(lost.Bot) == "blocked" && lost.Activations == 1 && lost.Input == default &&
            DeferredFurnitureError(lost.Bot).Contains("no observed gameplay response", StringComparison.Ordinal),
            "A retired reservation waited forever, fabricated completion or repeated activation.");

        var replaced = new DeferredFurnitureBotFixture();
        replaced.Start();
        replaced.Snapshot = replaced.Snapshot with { RequestOrdinal = 2 };
        replaced.Bot.Tick(3.1f);
        Require(Phase(replaced.Bot) == "blocked" && replaced.Activations == 1 && replaced.Input == default,
            "Another activation's reservation prolonged the original requested interaction.");

        var stalled = new DeferredFurnitureBotFixture();
        stalled.Start(); stalled.Bot.Tick(181);
        Require(Phase(stalled.Bot) == "blocked" && stalled.Activations == 1 && stalled.Input == default &&
            DeferredFurnitureError(stalled.Bot).Contains("execution bound", StringComparison.Ordinal) &&
            stalled.Evidence.ObserveInteraction("seat", stalled.Snapshot).Revision == 0,
            "A genuine pending furniture owner bypassed the global bound or fabricated completion.");

        var faulted = new DeferredFurnitureBotFixture();
        const string furnitureFailure = "source furniture approach collided";
        faulted.Start(); faulted.Paused = true; faulted.Fault = furnitureFailure;
        faulted.Bot.Tick(.016f);
        Require(Phase(faulted.Bot) == "blocked" && faulted.Activations == 1 && faulted.Input == default &&
            DeferredFurnitureError(faulted.Bot).Contains(furnitureFailure, StringComparison.Ordinal),
            "Pending furniture or pause concealed a known source/native failure.");
        Console.WriteLine("Reference bot deferred furniture: exact activation/source reservation, typed ordinal isolation, delayed entry, no delivery success, pause/loading, lost/replaced/failed/old owner, global bound and fault release PASS.");
    }

    // Authored adapter facts exercise the living shared observer and bot. They
    // do not establish owned NIF, collision, native input or retail acceptance.
    private sealed class DeferredFurnitureBotFixture
    {
        internal readonly BotInteractionEvidence Evidence = new();
        internal BotInteractionSnapshot Snapshot = new("unchanged", null, []);
        internal readonly ReactiveReferenceBot Bot;
        internal SteeringIntent Input;
        internal int Activations;
        internal bool Paused, Loading;
        internal string? Fault;

        internal DeferredFurnitureBotFixture()
        {
            Bot = new(reference =>
            {
                var observation = Evidence.ObserveInteraction(reference, Snapshot);
                return new("cell", Vector3.Zero, Vector3.UnitY, Vector3.UnitZ,
                    Vector3.UnitZ, Vector3.One, reference, Paused, Snapshot.RequestedOutcome is null, true, true, null,
                    observation.Revision.ToString(), Loading: Loading, ExecutionFault: Fault,
                    InteractionPending: observation.PendingFurniture);
            }, (_, end, _) => new([end], end, end, true), (intent, activate) =>
            {
                Input = intent;
                if (!activate) return;
                Activations++;
                Evidence.Begin("seat", Snapshot);
                Snapshot = Snapshot with
                {
                    RequestedOutcome = "furniture", RequestOrdinal = Activations,
                    RequestedOutcomeKind = BotInteractionOutcomeKind.Furniture, RequestedOutcomePending = true
                };
                Evidence.End("seat", Snapshot, true);
                Evidence.Finish("seat", true);
            });
        }

        internal void Start()
        {
            Bot.Start("seat", "interact", 1, combatTakeover: false, pauseAfter: false);
            Bot.Tick(.016f);
            Require(Phase(Bot) == "awaiting-interaction" && Activations == 1,
                "Furniture fixture did not issue the ordinary exact-reference activation once.");
        }

        internal void Enter() => Snapshot = Snapshot with
        {
            RequestedOutcomePending = false, TargetOutcome = "furniture",
            TargetOutcomeKind = BotInteractionOutcomeKind.Furniture, TargetOutcomeOrdinal = Snapshot.RequestOrdinal
        };
    }

    private static string DeferredFurnitureError(ReactiveReferenceBot bot)
    {
        using var state = JsonDocument.Parse(JsonSerializer.Serialize(bot.State));
        return state.RootElement.GetProperty("error").GetString() ?? "";
    }
}
