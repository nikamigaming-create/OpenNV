using System.Text.Json;

namespace OpenNV.Runtime.Gameplay.Bots;

internal sealed record BotInteractionAnimationState(int Controller, string Source, string Sequence,
    double ElapsedSeconds, bool StartPending, string? PendingSequence);

internal sealed record BotInteractionSourceState(string Reference, bool Taken, bool DoorOpen, bool Deleted,
    bool Destroyed, IReadOnlyList<BotInteractionAnimationState> Animations)
{
    internal static string StableEffects(BotInteractionSourceState target, BotInteractionSourceState? linked)
    {
        static object Project(BotInteractionSourceState state) => new
        {
            state.Reference,
            state.Taken,
            state.DoorOpen,
            state.Deleted,
            state.Destroyed,
            animations = state.Animations.OrderBy(animation => animation.Controller)
                .ThenBy(animation => animation.Source, StringComparer.Ordinal).Select(animation => new
                { animation.Controller, animation.Source, animation.Sequence, animation.PendingSequence }).ToArray()
        };
        return JsonSerializer.Serialize(new { target = Project(target), linked = linked is null ? null : Project(linked) });
    }
}

// Stable effects, not script clocks, stage changes, input receipts or pauses.
internal sealed record BotInteractionSnapshot(string TargetState, string? TargetOutcome, IReadOnlyList<uint> Menus,
    string? RequestedOutcome = null, long RequestOrdinal = 0,
    BotInteractionOutcomeKind TargetOutcomeKind = BotInteractionOutcomeKind.Other,
    BotInteractionOutcomeKind RequestedOutcomeKind = BotInteractionOutcomeKind.Other,
    bool RequestedOutcomePending = false, long TargetOutcomeOrdinal = 0);

internal enum BotInteractionOutcomeKind { Other, Portal, Furniture }

internal readonly record struct BotInteractionObservation(long Revision, bool SettledPortal, bool PendingFurniture = false)
{
    // Loading suspends native queries; a consumed portal can complete without
    // observing its retired source-cell model. Neither grants activation.
    internal bool RequiresNativeGeometry(bool loading) => !loading && !SettledPortal;
}

internal sealed class BotInteractionEvidence
{
    private sealed record Pending(BotInteractionSnapshot Before, BotInteractionSnapshot? BlockBefore,
        bool Result, bool Dispatched, string? DeferredOutcome = null,
        BotInteractionOutcomeKind DeferredOutcomeKind = BotInteractionOutcomeKind.Other,
        string? ResultPortalOutcome = null, long DeferredRequestOrdinal = 0);
    private sealed record Completion(long Revision, string? PortalOutcome);
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Completion> _outcomes = new(StringComparer.Ordinal);

    internal void Begin(string reference, BotInteractionSnapshot before) =>
        _pending[reference] = _pending.TryGetValue(reference, out var prior) && !prior.Dispatched
            ? prior with { BlockBefore = before } : new(before, before, false, false);

    internal void End(string reference, BotInteractionSnapshot after, bool successful)
    {
        if (!_pending.TryGetValue(reference, out var pending)) return;
        if (!successful) { _pending.Remove(reference); return; }
        var before = pending.BlockBefore ?? throw new InvalidOperationException("Activation observation has no current block.");
        var newOutcome = NewTargetOutcome(before, after);
        var newRequest = after.RequestedOutcome is not null &&
            (after.RequestedOutcome != before.RequestedOutcome || after.RequestOrdinal != before.RequestOrdinal ||
                after.RequestedOutcomeKind != before.RequestedOutcomeKind);
        _pending[reference] = pending with
        {
            BlockBefore = null,
            Result = pending.Result || after.TargetState != before.TargetState || newOutcome ||
                after.Menus.Except(before.Menus).Any(),
            DeferredOutcome = newRequest ? after.RequestedOutcome : pending.DeferredOutcome,
            DeferredOutcomeKind = newRequest ? after.RequestedOutcomeKind : pending.DeferredOutcomeKind,
            DeferredRequestOrdinal = newRequest ? after.RequestOrdinal : pending.DeferredRequestOrdinal,
            ResultPortalOutcome = newOutcome && after.TargetOutcomeKind == BotInteractionOutcomeKind.Portal
                ? after.TargetOutcome : pending.ResultPortalOutcome
        };
    }

    internal void Finish(string reference, bool successful)
    {
        if (!_pending.TryGetValue(reference, out var pending)) return;
        if (!successful || pending.BlockBefore is not null) { _pending.Remove(reference); return; }
        if (pending.Result) Complete(reference, pending.ResultPortalOutcome);
        else _pending[reference] = pending with { Dispatched = true };
    }

    internal long Observe(string reference, BotInteractionSnapshot current)
        => ObserveInteraction(reference, current).Revision;

    internal BotInteractionObservation ObserveInteraction(string reference, BotInteractionSnapshot current)
    {
        // Delayed proof requires the actual reference's conversation,
        // furniture, container or source portal destination. A later unrelated
        // menu or autonomous target mutation is not an activation result.
        if (_pending.TryGetValue(reference, out var pending) && pending.Dispatched && pending.DeferredOutcome is not null &&
            current.TargetOutcome == pending.DeferredOutcome && current.TargetOutcomeKind == pending.DeferredOutcomeKind &&
            NewTargetOutcome(pending.Before, current) &&
            (pending.DeferredOutcomeKind != BotInteractionOutcomeKind.Furniture ||
                pending.DeferredRequestOrdinal > 0 && current.TargetOutcomeOrdinal == pending.DeferredRequestOrdinal))
            Complete(reference, current.TargetOutcomeKind == BotInteractionOutcomeKind.Portal ? current.TargetOutcome : null);
        // A reservation belongs to the deferred activation; it is progress
        // that permits waiting, never a completed interaction. Do not borrow a
        // different reference, old request, or replacement activation's lease.
        var pendingFurniture = _pending.TryGetValue(reference, out pending) && pending.Dispatched &&
            pending.DeferredOutcomeKind == BotInteractionOutcomeKind.Furniture && pending.DeferredOutcome is not null &&
            pending.DeferredRequestOrdinal > 0 && current.RequestedOutcomePending &&
            current.RequestedOutcome == pending.DeferredOutcome && current.RequestedOutcomeKind == pending.DeferredOutcomeKind &&
            current.RequestOrdinal == pending.DeferredRequestOrdinal;
        var completion = _outcomes.GetValueOrDefault(reference);
        return new(completion?.Revision ?? 0, completion?.PortalOutcome is { } portal &&
            current.TargetOutcomeKind == BotInteractionOutcomeKind.Portal && current.TargetOutcome == portal, pendingFurniture);
    }

    internal void Forget(string reference) => _pending.Remove(reference);
    private static bool NewTargetOutcome(BotInteractionSnapshot before, BotInteractionSnapshot after) =>
        after.TargetOutcome is not null && after.TargetOutcome != before.TargetOutcome;
    private void Complete(string reference, string? portalOutcome)
    {
        _outcomes[reference] = new(checked((_outcomes.GetValueOrDefault(reference)?.Revision ?? 0) + 1), portalOutcome);
        _pending.Remove(reference);
    }
}
