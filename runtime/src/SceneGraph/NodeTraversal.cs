using Godot;

namespace OpenNV.Runtime.SceneGraph;

/// <summary>
/// Allocation-bounded, non-recursive traversal for runtime scene trees.
/// Descendants are returned in Godot child order and the root is excluded.
/// </summary>
internal static class NodeTraversal
{
    internal static IEnumerable<T> Descendants<T>(Node root, Node? excludedRoot = null)
        where T : Node
    {
        ArgumentNullException.ThrowIfNull(root);

        var pending = new Stack<Node>();
        PushChildren(root, pending);

        while (pending.TryPop(out var node))
        {
            if (ReferenceEquals(node, excludedRoot))
                continue;

            if (node is T match)
                yield return match;

            PushChildren(node, pending);
        }
    }

    private static void PushChildren(Node parent, Stack<Node> pending)
    {
        // Snapshot the same non-internal children in reverse order before
        // yielding, without allocating a native Array for every tree node.
        for (var index = parent.GetChildCount() - 1; index >= 0; index--)
            pending.Push(parent.GetChild(index));
    }

    internal static IEnumerable<T> SelfAndDescendants<T>(Node root)
        where T : Node
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root is T match)
            yield return match;
        foreach (var descendant in Descendants<T>(root))
            yield return descendant;
    }
}
