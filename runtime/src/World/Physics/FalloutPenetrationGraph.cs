namespace OpenNV.Runtime.World.Physics;

internal static class FalloutPenetrationGraph
{
    internal const int ContactConstraintType = 11, BreakableConstraintType = 12;

    internal static IEnumerable<IFalloutPenetrationScene> Walk(IReadOnlyList<IFalloutPenetrationScene?> roots)
    {
        var pending = new Stack<IFalloutPenetrationScene>();
        var visited = new HashSet<IFalloutPenetrationScene>(ReferenceEqualityComparer.Instance);
        Push(roots);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node))
                throw new InvalidDataException("Current physical scene repeats or cycles through a live object.");
            yield return node;
            Push(node.Children);
        }

        void Push(IReadOnlyList<IFalloutPenetrationScene?> children)
        {
            for (var slot = children.Count - 1; slot >= 0; slot--)
                if (children[slot] is { } child) pending.Push(child);
        }
    }

    internal static bool IsCandidateRoot(IFalloutPenetrationBody body) =>
        body.IncomingConstraints.All(constraint => constraint.DataType == ContactConstraintType) &&
        body.OutgoingConstraints.Any(constraint => constraint.DataType is { } type && type != ContactConstraintType);

    internal static (IReadOnlyList<IFalloutPenetrationBody> Bodies,
        IReadOnlyList<IFalloutPenetrationConstraint> Constraints) Collect(IFalloutPenetrationScene root)
    {
        var bodies = new List<IFalloutPenetrationBody>();
        var constraints = new List<IFalloutPenetrationConstraint>();
        foreach (var scene in Walk([root]))
        {
            if (scene.Body is not { } body) continue;
            var weapon = body.IsWeaponObject;
            var linked = new List<IFalloutPenetrationConstraint>();
            var excluded = false;
            foreach (var constraint in body.LinkedConstraints)
            {
                if (constraint?.DataType is not { } type) continue;
                if (type == BreakableConstraintType) { excluded = true; break; }
                linked.Add(constraint);
            }
            if (excluded || (weapon && linked.Count == 0)) continue;
            bodies.Add(body);
            constraints.AddRange(linked);
        }
        return (bodies.AsReadOnly(), constraints.AsReadOnly());
    }

    // A count mismatch is the source deferred result. Invalid endpoints, cycles
    // and multiple parents remain visible; no partial graph becomes a system.
    internal static FalloutPenetrationConnectedGraph? Validate(
        IReadOnlyList<IFalloutPenetrationBody> bodies, IReadOnlyList<IFalloutPenetrationConstraint> constraints)
    {
        if (bodies.Count == 0 || constraints.Count != bodies.Count - 1) return null;
        var indices = new Dictionary<IFalloutPenetrationBody, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < bodies.Count; index++)
            if (!indices.TryAdd(bodies[index], index))
                throw new InvalidDataException("Connected physical graph repeats a live body.");
        var parents = Enumerable.Repeat(-1, bodies.Count).ToArray();
        foreach (var constraint in constraints)
        {
            if (!indices.TryGetValue(constraint.First, out var first) ||
                !indices.TryGetValue(constraint.Second, out var second))
                throw new InvalidDataException("Structural constraint endpoint is absent from its current connected bodies.");
            if (parents[first] != -1)
                throw new InvalidDataException("Current connected physical body already has a structural parent.");
            parents[first] = second;
        }
        var depths = Depths(parents);
        return new(bodies, constraints, Array.AsReadOnly(parents), Array.AsReadOnly(depths));
    }

    internal static void ValidateOrder(FalloutPenetrationConnectedGraph original, FalloutPenetrationOrderedGraph ordered)
    {
        if (ordered is null || ordered.Bodies.Count != original.Bodies.Count ||
            ordered.Constraints.Count != original.Constraints.Count || ordered.Parents.Count != ordered.Bodies.Count)
            throw new InvalidDataException("Source depth ordering changed its connected physical graph extent.");
        var originals = new HashSet<IFalloutPenetrationBody>(original.Bodies, ReferenceEqualityComparer.Instance);
        foreach (var body in ordered.Bodies)
            if (!originals.Remove(body))
                throw new InvalidDataException("Source depth ordering changed or repeated a current body.");
        var constraints = new HashSet<IFalloutPenetrationConstraint>(original.Constraints, ReferenceEqualityComparer.Instance);
        foreach (var constraint in ordered.Constraints)
            if (!constraints.Remove(constraint))
                throw new InvalidDataException("Source depth ordering changed or repeated a current constraint.");
        var indices = new Dictionary<IFalloutPenetrationBody, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < ordered.Bodies.Count; index++) indices.Add(ordered.Bodies[index], index);
        var expectedParents = Enumerable.Repeat(-1, ordered.Bodies.Count).ToArray();
        foreach (var constraint in ordered.Constraints)
            expectedParents[indices[constraint.First]] = indices[constraint.Second];
        var previousDepth = -1;
        var depths = Depths(expectedParents);
        for (var index = 0; index < ordered.Bodies.Count; index++)
        {
            if (ordered.Parents[index] != expectedParents[index] || ordered.Parents[index] >= index || depths[index] < previousDepth)
                throw new InvalidDataException("Source depth ordering lost its parent or topological depth order.");
            previousDepth = depths[index];
        }
    }

    private static int[] Depths(IReadOnlyList<int> parents)
    {
        var depths = Enumerable.Repeat(-1, parents.Count).ToArray();
        var visiting = new bool[parents.Count];
        var path = new List<int>();
        for (var start = 0; start < parents.Count; start++)
        {
            if (depths[start] != -1) continue;
            path.Clear(); var current = start;
            while (current != -1)
            {
                if (current < 0 || current >= parents.Count)
                    throw new InvalidDataException("Connected physical graph has an invalid parent chain.");
                if (depths[current] != -1) break;
                if (visiting[current])
                    throw new InvalidDataException("Connected physical graph has a cyclic or invalid parent chain.");
                visiting[current] = true; path.Add(current); current = parents[current];
            }
            var depth = current == -1 ? -1 : depths[current];
            for (var index = path.Count - 1; index >= 0; index--)
            {
                depths[path[index]] = checked(++depth); visiting[path[index]] = false;
            }
        }
        return depths;
    }
}
