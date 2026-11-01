using System.Numerics;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutVatsLabelSample(byte Part, Vector2? Projected, Vector2 Previous);
internal sealed record FalloutVatsLabelPlacement(Vector2 Position, int Justify);

// Source part-family displacement, viewport clamp and bounded pair separation.
// Inputs are actual projected target-node points, not hand-placed anatomy.
internal static class FalloutVatsLabelLayout
{
    internal static Vector2 ReferenceSize(Vector2 physical)
    {
        if (!float.IsFinite(physical.X) || !float.IsFinite(physical.Y) || physical.X <= 0 || physical.Y <= 0)
            throw new InvalidDataException("VATS physical viewport is invalid.");
        return physical.X > physical.Y ? new((float)((double)physical.X / physical.Y * 960), 960) :
            physical.Y > physical.X ? new(1280, (float)((double)physical.Y / physical.X * 1280)) : new(1280, 960);
    }

    internal static IReadOnlyList<FalloutVatsLabelPlacement> Calculate(Vector2 screen, Vector2 crop,
        float actionPointHeight, IReadOnlyList<FalloutVatsLabelSample> samples)
    {
        if (!float.IsFinite(screen.X) || !float.IsFinite(screen.Y) || screen.X <= 0 || screen.Y <= 0 ||
            !float.IsFinite(crop.X) || !float.IsFinite(crop.Y) || !float.IsFinite(actionPointHeight))
            throw new InvalidDataException("VATS label viewport inputs are invalid.");
        var minimum = new Vector2((int)(crop.X * 2 + 100), (int)(crop.Y * 2 + 100));
        var maximum = new Vector2((int)(screen.X - minimum.X - 50), (int)(screen.Y - crop.Y * 2 - actionPointHeight + 50));
        if (minimum.X > maximum.X || minimum.Y > maximum.Y) throw new NotSupportedException("VATS label viewport has no source clamp region.");
        var middle = new Vector2((int)(screen.X / 2), (int)(screen.Y / 2));
        var positions = samples.Select(sample => sample.Previous).ToArray();
        var justify = new int[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            justify[i] = sample.Part switch { 0 => 6, 1 or 2 or 13 => 5, 5 or 6 or 10 or 11 or 12 => 4, _ => 1 };
            if (sample.Projected is not { } point) continue;
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) throw new InvalidDataException("VATS target projection is non-finite.");
            if (sample.Part is 1 or 2) point.Y += point.Y > middle.Y ? 100 : -100;
            else if (sample.Part is >= 3 and <= 12 or 14)
            {
                point.X += point.X > middle.X ? 100 : -100;
                point.Y += point.Y > middle.Y ? 50 : -50;
            }
            positions[i] = Vector2.Clamp(new((int)point.X, (int)point.Y), minimum, maximum);
            if (sample.Part is >= 3 and <= 12 or 14) justify[i] = positions[i].X < middle.X ? 4 : 1;
        }
        var halfSeparation = MathF.Sqrt(150 * 150 + 100 * 100) / 2;
        for (var pass = 0; pass < 31; pass++)
        {
            var changed = false;
            for (var i = 0; i < positions.Length; i++)
                for (var j = i + 1; j < positions.Length; j++)
                {
                    var first = positions[i]; var second = positions[j];
                    if (MathF.Abs(first.X - second.X) >= 150 || MathF.Abs(first.Y - second.Y) >= 100) continue;
                    if (first == second) second.X += 1;
                    var midpoint = Vector2.Min(first, second) + Vector2.Abs(first - second) / 2;
                    var a = first - midpoint; var b = second - midpoint;
                    var aLength = a.Length(); var bLength = b.Length();
                    if (aLength <= 1e-6f || bLength <= 1e-6f)
                        throw new NotSupportedException("VATS label separation reached an unowned degenerate native division.");
                    positions[i] = Vector2.Clamp(midpoint + a / aLength * halfSeparation, minimum, maximum);
                    positions[j] = Vector2.Clamp(midpoint + b / bLength * halfSeparation, minimum, maximum);
                    changed = true;
                }
            if (!changed) break;
        }
        return positions.Select((position, i) => new FalloutVatsLabelPlacement(position, justify[i])).ToArray();
    }
}
