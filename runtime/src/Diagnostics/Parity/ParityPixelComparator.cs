namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed record ParityPixelComparison(
    bool ExactBytes,
    int? FirstByteOffset,
    long DifferentPixels,
    long DifferentChannels,
    int MaximumChannelDelta);

internal static class ParityPixelComparator
{
    // Inputs are native-size readbacks with the same format. No resize, color correction,
    // registration, threshold, or perceptual metric participates in equality.
    internal static ParityPixelComparison CompareRgba8(
        int width,
        int height,
        ReadOnlySpan<byte> retail,
        ReadOnlySpan<byte> openNv) => Compare(width, height, 4, retail, openNv);

    internal static ParityPixelComparison CompareRgb8(
        int width,
        int height,
        ReadOnlySpan<byte> retail,
        ReadOnlySpan<byte> openNv) => Compare(width, height, 3, retail, openNv);

    private static ParityPixelComparison Compare(
        int width,
        int height,
        int channelsPerPixel,
        ReadOnlySpan<byte> retail,
        ReadOnlySpan<byte> openNv)
    {
        if (width <= 0 || height <= 0 ||
            (long)width * height * channelsPerPixel != retail.Length || retail.Length != openNv.Length)
            throw new InvalidDataException("Exact pixel comparison requires equal native image extents and formats.");
        int? first = null;
        long pixels = 0;
        long channels = 0;
        var maximum = 0;
        for (var offset = 0; offset < retail.Length; offset += channelsPerPixel)
        {
            var different = false;
            for (var channel = 0; channel < channelsPerPixel; channel++)
            {
                var index = offset + channel;
                var delta = Math.Abs(retail[index] - openNv[index]);
                if (delta == 0)
                    continue;
                first ??= index;
                different = true;
                channels++;
                maximum = Math.Max(maximum, delta);
            }
            if (different)
                pixels++;
        }
        return new ParityPixelComparison(first is null, first, pixels, channels, maximum);
    }
}
