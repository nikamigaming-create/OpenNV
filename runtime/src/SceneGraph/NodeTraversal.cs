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

        var children = root.GetChildren();
        var pending = new Stack<Node>(children.Count);
        for (var index = children.Count - 1; index >= 0; index--)
            pending.Push(children[index]);

        while (pending.TryPop(out var node))
        {
            if (ReferenceEquals(node, excludedRoot))
                continue;

            if (node is T match)
                yield return match;

            children = node.GetChildren();
            for (var index = children.Count - 1; index >= 0; index--)
                pending.Push(children[index]);
        }
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
