using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Each source bucket has one permanent head. Prepending copies the head's old
// value into a new interior node; clearing retires interior nodes and preserves
// the head. A synchronous caller reads Next after its callbacks return.
internal sealed class FalloutChallengeBuckets
{
    internal sealed class Node(uint @event, bool active, bool head, long identity, long generation)
    {
        internal uint Event { get; } = @event;
        internal bool Active { get; } = active;
        internal bool StableHead { get; } = head;
        internal long Identity { get; } = identity;
        internal long ConstructedGeneration { get; } = generation;
        internal FalloutFormKey? Value;
        internal Node? Next;
        internal bool Retired;
    }
    private readonly Node[] _active = new Node[14], _excluded = new Node[14];
    private long _identity;
    internal FalloutChallengeBuckets()
    {
        for (uint index = 0; index < 14; ++index)
        {
            _active[index] = New(index, true, true, 0);
            _excluded[index] = New(index, false, true, 0);
        }
    }
    private Node New(uint @event, bool active, bool head, long generation) =>
        new(@event, active, head, checked(++_identity), generation);
    internal Node Head(uint @event) => _active[checked((int)@event)];
    internal static FalloutFormKey? Read(Node node) { Require(node); return node.Value; }
    internal static Node? Next(Node node) { Require(node); return node.Next; }
    private static void Require(Node node)
    {
        if (node.Retired)
            throw new NotSupportedException("source-challenge-dispatch-cursor-interior-node-retired-by-nested-bucket-mutation");
    }
    internal void Clear()
    {
        foreach (var head in _active.Concat(_excluded))
        {
            for (var next = head.Next; next is not null;)
            {
                var retired = next; next = next.Next;
                retired.Next = null; retired.Retired = true;
            }
            head.Next = null; head.Value = null;
        }
    }
    internal void Prepend(uint @event, FalloutFormKey form, bool active, long generation)
    {
        var head = (active ? _active : _excluded)[checked((int)@event)];
        if (head.Value is { } previous)
        {
            var copy = New(@event, active, false, generation);
            copy.Value = previous; copy.Next = head.Next; head.Next = copy;
        }
        head.Value = form;
    }
    internal void Unlock(uint @event, FalloutFormKey form, long generation)
    {
        var active = _active[checked((int)@event)];
        if (!Members(active).Contains(form)) Prepend(@event, form, true, generation);
        Remove(_excluded[checked((int)@event)], form);
    }
    private static void Remove(Node head, FalloutFormKey form)
    {
        Node? previous = null, node = head;
        while (node is not null && node.Value != form) { previous = node; node = node.Next; }
        if (node is null) return;
        if (node.StableHead)
        {
            var next = node.Next;
            if (next is null) { node.Value = null; return; }
            node.Value = next.Value; node.Next = next.Next;
            next.Next = null; next.Retired = true;
        }
        else
        {
            previous!.Next = node.Next; node.Next = null; node.Retired = true;
        }
    }
    private static FalloutFormKey[] Members(Node head)
    {
        var result = new List<FalloutFormKey>();
        for (Node? node = head; node is not null; node = Next(node))
            if (Read(node) is { } form) result.Add(form);
        return result.ToArray();
    }
    internal IReadOnlyList<FalloutChallengeBucket> Capture() => Enumerable.Range(0, 14)
        .Select(index => new FalloutChallengeBucket((uint)index, Members(_active[index]), Members(_excluded[index]))).ToArray();
    internal void Restore(IReadOnlyList<FalloutChallengeBucket> buckets, long generation)
    {
        Clear();
        // Reconstruct saved traversal order without invoking source rebuild,
        // unlock, reward, counter or any completed callback a second time.
        foreach (var bucket in buckets)
        {
            foreach (var form in bucket.Members.Reverse()) Prepend(bucket.Event, form, true, generation);
            foreach (var form in bucket.Excluded!.Reverse()) Prepend(bucket.Event, form, false, generation);
        }
    }
}
