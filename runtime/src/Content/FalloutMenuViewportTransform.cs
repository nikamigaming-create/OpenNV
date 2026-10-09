using System.Numerics;

namespace OpenNV.Runtime.Content;

// The renderer's pixel extent and displayed control transform are distinct.
// An input point must travel through their actual inverse, not a screen ratio.
internal sealed class FalloutMenuViewportTransform
{
    private readonly Matrix3x2 _toOwner, _toPixels;
    internal Vector2 Pixels { get; }

    internal FalloutMenuViewportTransform(Vector2 pixels, Matrix3x2 toOwner)
    {
        if (!Finite(pixels) || pixels.X <= 0 || pixels.Y <= 0 ||
            !float.IsFinite(toOwner.M11) || !float.IsFinite(toOwner.M12) ||
            !float.IsFinite(toOwner.M21) || !float.IsFinite(toOwner.M22) ||
            !float.IsFinite(toOwner.M31) || !float.IsFinite(toOwner.M32) ||
            !Matrix3x2.Invert(toOwner, out _toPixels))
            throw new InvalidDataException("Menu viewport display transform is absent, singular or non-finite.");
        if (!float.IsFinite(_toPixels.M11) || !float.IsFinite(_toPixels.M12) ||
            !float.IsFinite(_toPixels.M21) || !float.IsFinite(_toPixels.M22) ||
            !float.IsFinite(_toPixels.M31) || !float.IsFinite(_toPixels.M32))
            throw new InvalidDataException("Menu viewport inverse transform overflowed.");
        Pixels = pixels; _toOwner = toOwner;
    }

    internal Vector2 ToOwner(Vector2 pixels)
    {
        if (!Finite(pixels)) throw new InvalidDataException("Projected menu coordinate is non-finite.");
        var point = Vector2.Transform(pixels, _toOwner);
        if (!Finite(point)) throw new InvalidDataException("Displayed menu coordinate overflowed.");
        return point;
    }

    internal bool TryPixels(Vector2 owner, out Vector2 pixels)
    {
        if (!Finite(owner)) throw new InvalidDataException("Menu input coordinate is non-finite.");
        pixels = Vector2.Transform(owner, _toPixels);
        if (!Finite(pixels)) throw new InvalidDataException("Menu input inverse overflowed.");
        return pixels.X >= 0 && pixels.Y >= 0 && pixels.X < Pixels.X && pixels.Y < Pixels.Y;
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
