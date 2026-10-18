namespace OpenNV.Runtime.Content;

internal static class FalloutNavigationTopology
{
    internal static int SharedVertexCount(IReadOnlyList<int> first, IReadOnlyList<int> second)
    {
        var count = 0;
        for (var index = 0; index < first.Count; ++index)
        {
            var repeated = false;
            for (var previous = 0; previous < index; ++previous)
                if (first[previous] == first[index]) { repeated = true; break; }
            if (repeated) continue;
            for (var other = 0; other < second.Count; ++other)
                if (first[index] == second[other]) { ++count; break; }
        }
        return count;
    }

    internal static bool HasInternalEdge(IReadOnlyList<int> adjacent, uint flags, int target)
    {
        for (var edge = 0; edge < adjacent.Count; ++edge)
            if (adjacent[edge] == target && (flags & (1u << edge)) == 0) return true;
        return false;
    }
}
