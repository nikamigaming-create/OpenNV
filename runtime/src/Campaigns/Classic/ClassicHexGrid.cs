namespace OpenNV.Runtime.Campaigns.Classic;

/// <summary>Classic MAP coordinates and shortest legal hex paths, independent of the renderer.</summary>
internal static class ClassicHexGrid
{
    internal const int Width = 200, Count = Width * Width;

    internal static int Neighbor(int tile, int rotation)
    {
        if (tile is < 0 or >= Count || rotation is < 0 or >= 6) throw new ArgumentOutOfRangeException(nameof(tile));
        var x = tile % Width; var y = tile / Width; var odd = (x & 1) != 0;
        var (dx, dy) = rotation switch
        {
            0 => (-1, odd ? -1 : 0),
            1 => (-1, odd ? 0 : 1),
            2 => (0, 1),
            3 => (1, odd ? 0 : 1),
            4 => (1, odd ? -1 : 0),
            _ => (0, -1),
        };
        x += dx; y += dy;
        return x is < 0 or >= Width || y is < 0 or >= Width ? -1 : y * Width + x;
    }

    internal static IEnumerable<int> Neighbors(int tile) => Enumerable.Range(0, 6).Select(rotation => Neighbor(tile, rotation)).Where(value => value >= 0);
    internal static int Rotation(int from, int to) => Enumerable.Range(0, 6).Single(rotation => Neighbor(from, rotation) == to);
    internal static int FloorIndex(int tile) => (tile / Width / 2) * 100 + tile % Width / 2;

    internal static int Distance(int first, int second)
    {
        static (int Q, int R) Axial(int tile) { var x = tile % Width; return (x, tile / Width - (x + (x & 1)) / 2); }
        var a = Axial(first); var b = Axial(second); var q = a.Q - b.Q; var r = a.R - b.R;
        return Math.Max(Math.Abs(q), Math.Max(Math.Abs(r), Math.Abs(q + r)));
    }

    // Results exclude the occupied start and include the destination. Equal-cost
    // ties preserve source rotation order, so save/replay never depends on hashes.
    internal static int[] Path(int start, int destination, IReadOnlySet<int> walkable)
    {
        if (!walkable.Contains(start) || !walkable.Contains(destination) || start == destination) return [];
        var queue = new PriorityQueue<int, (int Score, int Order)>();
        var previous = new Dictionary<int, int>(); var costs = new Dictionary<int, int> { [start] = 0 }; var order = 0;
        queue.Enqueue(start, (Distance(start, destination), order++));
        while (queue.TryDequeue(out var current, out _))
        {
            if (current == destination)
            {
                var path = new List<int>();
                while (current != start) { path.Add(current); current = previous[current]; }
                path.Reverse(); return path.ToArray();
            }
            foreach (var next in Neighbors(current))
            {
                var cost = costs[current] + 1;
                if (!walkable.Contains(next) || costs.TryGetValue(next, out var known) && known <= cost) continue;
                costs[next] = cost; previous[next] = current;
                queue.Enqueue(next, (cost + Distance(next, destination), order++));
            }
        }
        return [];
    }
}
