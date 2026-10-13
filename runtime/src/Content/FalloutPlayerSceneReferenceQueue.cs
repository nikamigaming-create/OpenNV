namespace OpenNV.Runtime.Content;

internal interface IFalloutPlayerSceneQueuedNodes
{
    Guid Process { get; }
    string Stack { get; }
    void RequireCurrent();
    FalloutSourceSceneNode? ReadCurrentNode(FalloutFormKey reference);
    bool RetainNode(FalloutSourceSceneNode node);
    void ClearNodeFlagZero(FalloutSourceSceneNode node);
    void UpdateNode(FalloutSourceSceneNode node);
}

// The source list has a permanent inline head. Loading prepends each resolved
// reference, including duplicates; the reference destructor removes the first
// match. A consumed entry restarts at the current head after child callbacks.
internal sealed class FalloutPlayerSceneReferenceQueue
{
    private sealed class Entry(FalloutFormKey? reference, Entry? next = null)
    {
        internal FalloutFormKey? Reference = reference;
        internal Entry? Next = next;
    }
    private readonly Entry _head = new(null);
    internal IReadOnlyList<FalloutFormKey> References
    {
        get
        {
            var result = new List<FalloutFormKey>();
            for (var entry = _head; entry is not null; entry = entry.Next)
                if (entry.Reference is { } reference) result.Add(reference);
            return result;
        }
    }
    internal void Restore(IReadOnlyList<FalloutFormKey> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        foreach (var reference in references) ValidateReference(reference);
        _head.Reference = null; _head.Next = null;
        // OpenNV stores canonical current order, rather than a retail byte
        // stream. Reversing before the source prepend preserves that order.
        LoadResolvedReferences(references.Reverse().Select(reference => (FalloutFormKey?)reference));
    }
    internal void LoadResolvedReferences(IEnumerable<FalloutFormKey?> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        foreach (var reference in references)
        {
            if (reference is null) continue;
            ValidateReference(reference.Value);
            if (_head.Reference is not null) _head.Next = new(_head.Reference, _head.Next);
            _head.Reference = reference;
        }
    }
    internal bool RemoveFirst(FalloutFormKey? reference)
    {
        if (reference is null) return false;
        for (Entry? previous = null, entry = _head; entry is not null; previous = entry, entry = entry.Next)
        {
            if (entry.Reference != reference) continue;
            if (previous is not null) previous.Next = entry.Next;
            else if (_head.Next is { } next) { _head.Reference = next.Reference; _head.Next = next.Next; }
            else _head.Reference = null;
            return true;
        }
        return false;
    }
    internal void Update(IFalloutPlayerSceneQueuedNodes? consumers, Action<FalloutFormKey, string> observe)
    {
        ArgumentNullException.ThrowIfNull(observe);
        var entry = _head;
        while (entry is not null && entry.Reference is { } reference)
        {
            var current = consumers ?? throw new NotSupportedException("source-Player-queued-reference-current-scene-node-consumers-unbound");
            current.RequireCurrent();
            var node = current.ReadCurrentNode(reference); current.RequireCurrent();
            observe(reference, "node-read");
            if (node is not null)
            {
                if (node.Process != current.Process || node.Factory == Guid.Empty || node.NativeRoot == 0)
                    throw new InvalidOperationException("Queued reference returned a foreign or unpublished source node.");
                var retain = current.RetainNode(node); current.RequireCurrent();
                observe(reference, retain ? "retained" : "not-retained");
                if (retain) { entry = entry.Next; continue; }
                current.ClearNodeFlagZero(node); current.RequireCurrent(); observe(reference, "flag-zero-cleared");
                current.UpdateNode(node); current.RequireCurrent(); observe(reference, "node-updated");
            }
            // The node callback may have removed or reordered other entries.
            // Search the current list by reference, rather than a stale index.
            observe(reference, RemoveFirst(reference) ? "removed" : "already-removed");
            entry = _head;
        }
    }
    internal static void ValidateReference(FalloutFormKey reference)
    {
        if (string.IsNullOrWhiteSpace(reference.OwnerPlugin) || reference.ObjectId == 0)
            throw new InvalidDataException("Player queued reference lost its source form identity.");
    }
}
