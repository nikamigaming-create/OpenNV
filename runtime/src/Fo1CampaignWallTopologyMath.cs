using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime;

internal static class Fo1CampaignWallTopologyMath
{
    internal static string OccupiedHexSha256(IReadOnlyCollection<Fo1CampaignWallCell> cells)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> encoded = stackalloc byte[sizeof(int)];
        foreach (var tile in cells.Select(row => row.Tile).Order())
        {
            BinaryPrimitives.WriteInt32BigEndian(encoded, tile);
            hash.AppendData(encoded);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static Fo1CampaignWallTopologyCoverage Analyze(
        IReadOnlyCollection<Fo1CampaignWallCell> cells,
        IReadOnlyList<int> floorIds,
        int defaultTileId)
    {
        if (floorIds.Count != Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight)
            throw new InvalidOperationException("Fallout wall topology has an invalid floor grid.");
        var occupied = cells.Select(row => row.Tile).ToHashSet();
        if (occupied.Count != cells.Count)
            throw new InvalidOperationException("Fallout wall topology has duplicate occupied hexes.");

        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        var components = 0;
        var largestComponent = 0;
        var isolatedHexes = 0;
        foreach (var start in occupied)
        {
            if (!visited.Add(start))
                continue;
            components++;
            queue.Enqueue(start);
            var componentSize = 0;
            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                componentSize++;
                for (var edge = 0; edge < Fo1HexMath.DirectionCount; edge++)
                {
                    var neighbor = Fo1HexMath.NeighborAcrossEdge(tile, edge);
                    if (neighbor >= 0 && occupied.Contains(neighbor) && visited.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }
            largestComponent = Math.Max(largestComponent, componentSize);
            if (componentSize == 1)
                isolatedHexes++;
        }

        var boundaryEdges = 0;
        var floorFacingEdges = 0;
        var voidFacingEdges = 0;
        foreach (var tile in occupied)
        {
            for (var edge = 0; edge < Fo1HexMath.DirectionCount; edge++)
            {
                var neighbor = Fo1HexMath.NeighborAcrossEdge(tile, edge);
                if (neighbor >= 0 && occupied.Contains(neighbor))
                    continue;
                boundaryEdges++;
                if (neighbor >= 0 && floorIds[Fo1HexMath.FloorIndex(neighbor)] != defaultTileId)
                    floorFacingEdges++;
                else
                    voidFacingEdges++;
            }
        }
        var blockingHexes = cells.Count(
            row => row.SourceObjects.Any(source => source.Blocking));
        return new Fo1CampaignWallTopologyCoverage(
            occupied.Count,
            blockingHexes,
            occupied.Count - blockingHexes,
            components,
            largestComponent,
            isolatedHexes,
            boundaryEdges,
            floorFacingEdges,
            voidFacingEdges);
    }
}
