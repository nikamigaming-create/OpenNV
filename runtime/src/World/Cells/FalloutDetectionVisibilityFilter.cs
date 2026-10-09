namespace OpenNV.Runtime.World.Cells;

// Original source-filter arithmetic. Callers supply the actual published
// source filter words and tables. Godot collision layer 1 and an authored NIF
// group which has not passed runtime group assignment cannot substitute.
internal static class FalloutDetectionVisibilityFilter
{
    internal static byte Layer(uint filter) => (byte)(filter & 0x7f);
    internal static ushort Group(uint filter) => (ushort)(filter >> 16);
    private static int Part(uint filter) => (int)((filter >> 8) & 0x1f);

    internal static uint RayFilter(ushort receiverGroup) => 37U | ((uint)receiverGroup << 16);

    internal static ulong VisibilityMask(uint targetFilter)
    {
        var layer = Layer(targetFilter);
        return 0x22757UL | (layer < 64 ? 1UL << layer : 0);
    }

    internal static bool Allows(uint first, uint second, Func<byte, ulong> sourceLayerMask,
        Func<int, uint> sourcePartMask)
    {
        ArgumentNullException.ThrowIfNull(sourceLayerMask);
        ArgumentNullException.ThrowIfNull(sourcePartMask);
        var layer = Layer(first);
        if (layer != 40 && ((first | second) & 0x4000) != 0) return false;
        if (Group(first) == 0 || Group(second) == 0) return true;
        var secondLayer = Layer(second);
        if (layer == 8 && secondLayer == 8)
        {
            if (Group(first) != Group(second)) return false;
            return (sourcePartMask(Part(first)) & (1U << Part(second))) != 0;
        }
        bool MaskAllows() => secondLayer < 64 && (sourceLayerMask(layer) & (1UL << secondLayer)) != 0;
        if (Group(first) != Group(second)) return MaskAllows();
        if ((first & second & 0x8000) != 0)
            return MaskAllows() && Math.Abs(Part(first) - Part(second)) != 1;
        if (layer is not (8 or 29) || secondLayer is not (8 or 29)) return false;
        return (sourcePartMask(Part(first)) & (1U << Part(second))) != 0;
    }

    // The collector independently filters the actual root body's word. A
    // same-layer body in another target group is ignored. Equal backoff is
    // accepted; closest-hit publication still requires a strictly smaller
    // fraction than the currently retained fraction.
    internal static bool CollectorAccepts(uint targetFilter, uint hitRootFilter,
        float hitFraction, float backoffFraction)
    {
        if (!float.IsFinite(hitFraction) || hitFraction is < 0 or > 1 ||
            !float.IsFinite(backoffFraction) || backoffFraction < 0)
            throw new InvalidDataException("Detection visibility has no finite source hit/backoff fraction.");
        return (Layer(targetFilter) != Layer(hitRootFilter) || Group(targetFilter) == Group(hitRootFilter)) &&
            hitFraction >= backoffFraction;
    }
}
