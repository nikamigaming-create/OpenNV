namespace OpenNV.Runtime.Content;

// OpenNV scheduling policy. This predicts resource demand, never player state,
// source CELL identity or collision residency. A pending grid remains hidden
// until the player actually enters its center cell.
internal static class FalloutExteriorStreamTarget
{
    internal static (int X, int Y) Predict(float xMeters, float yMeters, float vx, float vy, float units)
    {
        if (!float.IsFinite(xMeters) || !float.IsFinite(yMeters) || !float.IsFinite(vx) || !float.IsFinite(vy) ||
            !float.IsFinite(units) || units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
        var width = 4096 * units;
        var x = (int)MathF.Floor(xMeters / width); var y = (int)MathF.Floor(yMeters / width);
        const float leadSeconds = 6;
        return (Math.Clamp((int)MathF.Floor((xMeters + vx * leadSeconds) / width), x - 1, x + 1),
            Math.Clamp((int)MathF.Floor((yMeters + vy * leadSeconds) / width), y - 1, y + 1));
    }
}
