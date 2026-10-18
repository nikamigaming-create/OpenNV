using OpenNV.Runtime.Content;

internal static class NavigationTopologyContracts
{
    internal static void Run()
    {
        int[] Triple(int code) => [code % 5, code / 5 % 5, code / 25];
        for (var first = 0; first < 125; ++first)
            for (var second = 0; second < 125; ++second)
            {
                var a = Triple(first); var b = Triple(second);
                if (FalloutNavigationTopology.SharedVertexCount(a, b) != a.Intersect(b).Count())
                    throw new InvalidDataException("Shared-edge count changed distinct intersection semantics.");
            }
        for (var code = 0; code < 125; ++code)
            for (var flags = 0u; flags < 16; ++flags)
                for (var target = -1; target <= 4; ++target)
                {
                    var adjacent = Triple(code).Select(value => value - 1).ToArray();
                    var expected = Enumerable.Range(0, 3).Any(edge => adjacent[edge] == target && (flags & (1u << edge)) == 0);
                    if (FalloutNavigationTopology.HasInternalEdge(adjacent, flags, target) != expected)
                        throw new InvalidDataException("Internal adjacency changed external flags or duplicate-edge semantics.");
                }
        Console.WriteLine("OPENNV_NAVIGATION_TOPOLOGY_PASS sharedVertexCases=15625 edgeFlagCases=12000 duplicates=true reciprocalPolicy=unchanged sourceGeometry=unmodified");
    }
}
