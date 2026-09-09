namespace OpenNV.Runtime.Campaigns.Classic;

/// <summary>One affine mapping between original MAP screen pixels and the regular hex world.</summary>
internal static class ClassicMapProjection
{
    internal static (double X, double Y) HexScreen(int tile)
    {
        var x = tile % 200; var y = tile / 200;
        return (4816 - 32 * ((x + 1) / 2) - 16 * (x / 2) + 16 * y, 12 * (x / 2) + 12 * y + 11);
    }

    internal static (double X, double Y) FloorScreen(int index)
    {
        // MAP square columns and object hex columns have the same order.
        // The negative screen-space X basis below already projects that
        // order; reversing the index here mirrors the floor under the map.
        var x = index % 100; var y = index / 100;
        return (4752 + 32 * y - 48 * x, 24 * y + 12 * x);
    }

    internal static (double X, double Z) World(double screenX, double screenY)
    {
        var u = screenX - 4816; var v = screenY - 11;
        return ((4 * v - 3 * u) / (64 * Math.Sqrt(3)), (u + 4 * v) / 64);
    }

    internal static (double X, double Z) FloorCenter(int index)
    {
        var screen = FloorScreen(index);
        return World(screen.X + 40, screen.Y + 18);
    }

    // The four tips of an 80x36 floor FRM are (48,0), (80,24),
    // (32,36), (0,12). Its unequal screen-space axes become orthogonal
    // world axes. A symmetric-diamond resample shears every source floor.
    internal static (double X, double Y) FloorSample(double u, double v) => (48 - 48 * u + 32 * v, 12 * u + 24 * v);
}
