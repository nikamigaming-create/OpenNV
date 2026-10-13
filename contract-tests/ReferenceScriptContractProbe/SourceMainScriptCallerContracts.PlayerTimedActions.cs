using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerTimedActions()
    {
        var actions = new FalloutPlayerTimedActions();
        Require(!actions.Capture().Allocated && actions.Find(9) is null && !actions.RemoveFirst(9, null),
            "An empty Player action query/removal manufactured a list allocation.");
        actions.Store(1, 0x40000000, null, 0);
        var first = actions.Find(1)!;
        actions.Store(2, 0x3e800000, null, 0);
        actions.Store(5, 0xbf800000, null, 0);
        actions.Store(8, 0x80000000, null, 0);
        actions.Store(1, 0x3fa00000, null, 0);
        Require(actions.Capture().Entries.Select(entry => entry.Kind).SequenceEqual(new uint[] { 1, 8, 5, 2 }) &&
            actions.Find(1)!.Identity == first.Identity && actions.Find(1)!.RemainingBits == 0x3fa00000,
            "A refreshed source action duplicated its kind, lost its item identity or appended instead of prepending.");
        actions.Store(99, 0, null, 7);
        Require(actions.Find(99) is null, "A suppressed source action acquired a list item.");
        var beforeContextFailure = actions.Capture();
        Reject(() => actions.Store(99, 0, Guid.NewGuid(), 0));
        Require(actions.Capture().Entries.SequenceEqual(beforeContextFailure.Entries),
            "A missing context acquire mutated the real action list before refusing.");
        Require(actions.Expire().Count == 0 && actions.Subtract(.5f) == 4 &&
            actions.Find(2)!.RemainingBits == 0xbe800000 && actions.Find(5)!.RemainingBits == 0xbfc00000 &&
            actions.Find(8)!.RemainingBits == 0xbf000000,
            "Action timers clamped overshoot, expired before the next frame or skipped protected-kind subtraction.");
        var expired = actions.Expire();
        Require(expired.Count == 1 && actions.Find(2) is null && actions.Find(5) is not null && actions.Find(8) is not null,
            "The next action expiry failed to remove an overshoot or removed protected kinds.");
        var unordered = Guid.NewGuid(); var negativeZero = Guid.NewGuid();
        actions.Restore(new(true, [new(unordered, 3, 0x7fc00012, null), new(negativeZero, 4, 0x80000000, null)]));
        Require(actions.Expire().SequenceEqual(new[] { negativeZero }) && actions.Find(3)!.RemainingBits == 0x7fc00012,
            "Ordered action expiry confused an unordered value with negative zero.");
        var context = Guid.NewGuid(); var otherContext = Guid.NewGuid();
        var a = new FalloutPlayerTimedAction(Guid.NewGuid(), 9, 0x3f800000, context);
        var b = new FalloutPlayerTimedAction(Guid.NewGuid(), 9, 0x40000000, otherContext);
        actions.Restore(new(true, [a, b]));
        Require(actions.Find(9) == a && !actions.RemoveFirst(9, Guid.NewGuid()) &&
            actions.RemoveFirst(9, otherContext) && actions.Find(9) == a && actions.RemoveFirst(9, null) &&
            actions.Capture() is { Allocated: true, Entries.Count: 0 },
            "Context matching, first-kind query or wildcard removal lost source list semantics.");
        var coldSource = new FalloutPlayerTimedActionsSnapshot(true, [a, b]);
        var cold = new FalloutPlayerTimedActions();
        cold.Restore(JsonSerializer.Deserialize<FalloutPlayerTimedActionsSnapshot>(JsonSerializer.Serialize(coldSource))!);
        Require(cold.Capture().Entries.SequenceEqual(coldSource.Entries), "Cold actions changed canonical order, exact Float32 bits or logical identity.");
        Reject(() => cold.Restore(new(false, [a])));
        Reject(() => cold.Restore(new(true, [a, a])));
        Require(cold.Capture().Entries.SequenceEqual(coldSource.Entries), "Invalid cold action restoration partially replaced live state.");
    }
}
