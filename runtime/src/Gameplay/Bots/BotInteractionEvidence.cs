namespace OpenNV.Runtime.Gameplay.Bots;

// Stable effects, not script clocks, stage changes, input receipts or pauses.
internal sealed record BotInteractionSnapshot(string TargetState, string? TargetOutcome, IReadOnlyList<uint> Menus,
    string? RequestedOutcome = null, long RequestOrdinal = 0);

internal sealed class BotInteractionEvidence
{
    private sealed record Pending(BotInteractionSnapshot Before, BotInteractionSnapshot? BlockBefore,
        bool Result, bool Dispatched, string? DeferredOutcome = null);
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _outcomes = new(StringComparer.Ordinal);

    internal void Begin(string reference, BotInteractionSnapshot before) =>
        _pending[reference] = _pending.TryGetValue(reference, out var prior) && !prior.Dispatched
            ? prior with { BlockBefore = before } : new(before, before, false, false);

    internal void End(string reference, BotInteractionSnapshot after, bool successful)
    {
        if (!_pending.TryGetValue(reference, out var pending)) return;
        if (!successful) { _pending.Remove(reference); return; }
        var before = pending.BlockBefore ?? throw new InvalidOperationException("Activation observation has no current block.");
        _pending[reference] = pending with
        {
            BlockBefore = null,
            Result = pending.Result || after.TargetState != before.TargetState || NewTargetOutcome(before, after) ||
                after.Menus.Except(before.Menus).Any(),
            DeferredOutcome = after.RequestedOutcome is not null &&
                (after.RequestedOutcome != before.RequestedOutcome || after.RequestOrdinal != before.RequestOrdinal)
                ? after.RequestedOutcome : pending.DeferredOutcome
        };
    }

    internal void Finish(string reference, bool successful)
    {
        if (!_pending.TryGetValue(reference, out var pending)) return;
        if (!successful || pending.BlockBefore is not null) { _pending.Remove(reference); return; }
        if (pending.Result) Complete(reference);
        else _pending[reference] = pending with { Dispatched = true };
    }

    internal long Observe(string reference, BotInteractionSnapshot current)
    {
        // Delayed proof requires the actual reference's conversation,
        // furniture, container or source portal destination. A later unrelated
        // menu or autonomous target mutation is not an activation result.
        if (_pending.TryGetValue(reference, out var pending) && pending.Dispatched && pending.DeferredOutcome is not null &&
            current.TargetOutcome == pending.DeferredOutcome && NewTargetOutcome(pending.Before, current))
            Complete(reference);
        return _outcomes.GetValueOrDefault(reference);
    }

    internal void Forget(string reference) => _pending.Remove(reference);
    private static bool NewTargetOutcome(BotInteractionSnapshot before, BotInteractionSnapshot after) =>
        after.TargetOutcome is not null && after.TargetOutcome != before.TargetOutcome;
    private void Complete(string reference)
    {
        _outcomes[reference] = checked(_outcomes.GetValueOrDefault(reference) + 1);
        _pending.Remove(reference);
    }
}
