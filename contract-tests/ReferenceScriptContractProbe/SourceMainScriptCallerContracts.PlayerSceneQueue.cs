using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerSceneReferenceQueue()
    {
        var first = new FalloutFormKey("QueueFixture.esm", 1);
        var second = new FalloutFormKey("QueueFixture.esm", 2);
        var third = new FalloutFormKey("QueueFixture.esm", 3);
        var queue = new FalloutPlayerSceneReferenceQueue();
        queue.Update(null, (_, _) => throw new InvalidOperationException("An empty constructor queried a scene child."));
        queue.LoadResolvedReferences([first, null, second, third]);
        Require(queue.References.SequenceEqual([third, second, first]), "Source resolved load appended rather than prepending independent references.");
        queue.Restore([]);
        queue.LoadResolvedReferences([first, null, second, first]);
        Require(queue.References.SequenceEqual([first, second, first]), "Resolved source load lost prepend order, null omission or duplicates.");
        Require(queue.RemoveFirst(first) && queue.References.SequenceEqual([second, first]) &&
            !queue.RemoveFirst(third) && !queue.RemoveFirst(null), "Reference destruction did not remove only the first matching entry.");
        var cold = new FalloutPlayerSceneReferenceQueue();
        cold.Restore(JsonSerializer.Deserialize<FalloutFormKey[]>(JsonSerializer.Serialize(queue.References))!);
        Require(cold.References.SequenceEqual(queue.References), "Cold queue restoration reversed its canonical current order.");
        Reject(() => cold.Update(null, (_, _) => { }));
        Require(cold.References.SequenceEqual([second, first]), "An unbound current-node child silently discarded queued source references.");
        var events = new List<(FalloutFormKey Reference, string Operation)>();
        var nodes = new AuthoredQueuedSceneNodes();
        nodes.Read = reference => reference == second ? new(nodes.Process, Guid.NewGuid(), 27) : null;
        nodes.Retain = _ => true;
        cold.Update(nodes, (reference, operation) => events.Add((reference, operation)));
        Require(cold.References.SequenceEqual([second]) && events.SequenceEqual(new[]
        { (second, "node-read"), (second, "retained"), (first, "node-read"), (first, "removed"),
            (second, "node-read"), (second, "retained") }) && nodes.FlagStores == 0 && nodes.Updates == 0,
            "Retained nodes did not advance independently, or null-node removal failed to restart the current head.");
        nodes.Retain = _ => false;
        nodes.Update = _ => { cold.RemoveFirst(second); cold.LoadResolvedReferences([third]); };
        nodes.Read = reference => reference == second ? new(nodes.Process, Guid.NewGuid(), 27) : null;
        events.Clear(); cold.Update(nodes, (reference, operation) => events.Add((reference, operation)));
        Require(cold.References.Count == 0 && nodes.FlagStores == 1 && nodes.Updates == 1 &&
            events.Select(item => item.Operation).SequenceEqual(["node-read", "not-retained", "flag-zero-cleared", "node-updated",
                "already-removed", "node-read", "removed"]),
            "Queue consumers lost clear-before-update order or searched stale membership after the actual callback.");
        queue.Restore([first, first]); nodes.Read = _ => null;
        var reads = 0;
        queue.Update(nodes, (_, operation) => { if (operation == "node-read") ++reads; });
        Require(reads == 2 && queue.References.Count == 0, "Duplicate queued references were collapsed before their independent getters.");
        queue.Restore([first]); nodes.Read = _ => new(Guid.NewGuid(), Guid.NewGuid(), 3);
        Reject(() => queue.Update(nodes, (_, _) => { }));
        Require(queue.References.SequenceEqual([first]), "A foreign process node consumed a legitimate queued reference.");
        Reject(() => queue.Restore([new("", 1)]));
        Require(queue.References.SequenceEqual([first]), "Invalid cold queue input partially overwrote the current queue.");
    }

    private sealed class AuthoredQueuedSceneNodes : IFalloutPlayerSceneQueuedNodes
    {
        public Guid Process { get; } = Guid.NewGuid();
        public string Stack => "explicit-source-node-fixture";
        internal Func<FalloutFormKey, FalloutSourceSceneNode?> Read = _ => null;
        internal Func<FalloutSourceSceneNode, bool> Retain = _ => false;
        internal Action<FalloutSourceSceneNode> Update = _ => { };
        internal int FlagStores, Updates;
        public void RequireCurrent() { }
        public FalloutSourceSceneNode? ReadCurrentNode(FalloutFormKey reference) => Read(reference);
        public bool RetainNode(FalloutSourceSceneNode node) => Retain(node);
        public void ClearNodeFlagZero(FalloutSourceSceneNode node) => ++FlagStores;
        public void UpdateNode(FalloutSourceSceneNode node)
        {
            Require(FlagStores > Updates, "Scene node updated before its flag-zero store.");
            ++Updates; Update(node);
        }
    }
}
