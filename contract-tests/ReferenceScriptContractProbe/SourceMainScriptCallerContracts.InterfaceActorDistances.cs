using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunInterfaceActorDistances()
    {
        var list = new FalloutInterfaceActorDistanceList();
        FalloutFormKey Actor(uint id) => new("UnrelatedActors.esm", id);
        var values = new Dictionary<FalloutFormKey, float>();
        float? Value(FalloutFormKey key) => values[key];
        list.Sort(20, _ => throw new InvalidOperationException("Empty source actor list polled a distance."));
        Require(list.Capture() is { Count: 0, Sequence: 0 }, "Empty sort invented actor data or an array store.");
        Require(!list.Add(null, 20, _ => throw new InvalidOperationException("Null actor queried a context.")) &&
            !list.Add(Actor(1), 20, _ => null), "Nullable context branch manufactured an actor entry.");
        for (uint id = 1; id <= 35; ++id) { values.Add(Actor(id), 36 - id); list.Add(Actor(id), 20, Value); }
        var filled = list.Capture();
        Require(filled.Count == 20 && filled.Slots[0] == Actor(35) && filled.Slots[19] == Actor(16) &&
            filled.Slots[20] == Actor(15) && !list.Add(Actor(20), 20, Value),
            "Ordered actor insertion lost the winning limit, duplicate guard or inactive full-tail state.");
        var serialized = JsonSerializer.Deserialize<FalloutInterfaceActorDistanceSnapshot>(JsonSerializer.Serialize(filled))!;
        var cold = new FalloutInterfaceActorDistanceList(); cold.Restore(serialized);
        Require(cold.Capture().Slots.SequenceEqual(filled.Slots) && cold.Capture().Count == filled.Count,
            "Cold actor-list restoration lost inactive slots or replayed a producer.");
        Require(cold.Remove(Actor(23)) && !cold.Remove(Actor(23)) && cold.Capture().Count == 19 && cold.Capture().Slots[19] is null,
            "Actor removal lost first-match/full-tail movement or cleared a different count slot.");
        var queryOrder = new List<FalloutFormKey>();
        cold.Sort(7, key => { queryOrder.Add(key); return key.ObjectId % 7; });
        var sorted = cold.Capture();
        Require(sorted.Count == 7 && sorted.Slots.Take(7).Select(key => key!.Value.ObjectId % 7).SequenceEqual(
                sorted.Slots.Take(7).Select(key => key!.Value.ObjectId % 7).Order()) &&
            queryOrder[0] == Actor(31) && queryOrder[1] == Actor(35),
            "Actor sort lost source Shell gaps, candidate-first distance order or post-sort truncation.");
        var wide = new FalloutInterfaceActorDistanceList();
        for (uint id = 1; id <= 65; ++id) { values.TryAdd(Actor(id), id); wide.Add(Actor(id), 80, Value); }
        Require(wide.Capture().Count == 50, "A large setting override exceeded the source inline extent.");
        var failed = new FalloutInterfaceActorDistanceList(); failed.Restore(filled);
        var calls = 0;
        Reject(() => failed.Sort(20, key => ++calls == 3 ? throw new NotSupportedException("actual-distance-query-failure") : (float)key.ObjectId));
        Require(failed.Capture().Failure == "actual-distance-query-failure" && failed.Capture().Sequence > filled.Sequence &&
            failed.Capture().Slots[0] == Actor(31) && failed.SaveBlocker is not null && calls == 3,
            "Failed actor sort discarded its committed prefix or retried a source query.");
        var reentrant = new FalloutInterfaceActorDistanceList(); reentrant.Restore(filled);
        Reject(() => reentrant.Sort(20, _ => { try { reentrant.Clear(); } catch (InvalidOperationException) { } return 1; }));
        Require(reentrant.SaveBlocker is not null, "Actor-distance callback swallowed an actual mutation refusal.");
        var unordered = new FalloutInterfaceActorDistanceList();
        values[Actor(100)] = 0; values[Actor(101)] = 0;
        unordered.Add(Actor(100), 2, Value); unordered.Add(Actor(101), 2, Value);
        unordered.Sort(2, _ => float.NaN);
        Require(unordered.Capture().Slots[0] == Actor(100), "Unordered distances took an ordered shift branch.");
        Reject(() => FalloutInterfaceActorDistanceList.Validate(filled with { Slots = filled.Slots.Take(20).ToArray() }));
        Reject(() => FalloutInterfaceActorDistanceList.Validate(filled with { Count = 51 }));
        Console.WriteLine("OPENNV_INTERFACE_ACTOR_DISTANCE_LIST_PASS sourceExtent=true fullInactiveState=true nonemptyInsertSortRemove=true settingOverrides=true currentFailurePrefix=true coldNoReplay=true sourceProducerBinding=UNOWNED");
    }
}
