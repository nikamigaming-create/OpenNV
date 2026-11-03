namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompassHudDeclaration(float EdgeFadeStartFraction, float LandmarkBlinkSquaredLimit);

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutCompassHudDeclaration ReadCompassHudDeclaration(string path)
    {
        var (code, image) = Load(path);
        return ReadCompassHudDeclaration(code, image.Literal, address => BitConverter.ToDouble(image.Read(address, sizeof(double))));
    }
    internal static FalloutCompassHudDeclaration ReadCompassHudDeclaration(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, Func<uint, double> scalar)
    {
        var fractions = new List<float>(); var limits = new List<float>();
        for (var at = 0; at + 5 <= code.Length; at++)
        {
            if (code[at] != 0x68 || literal(U32(code, at + 1)) is not { } name ||
                !name.Replace('\\', '/').EndsWith("/glow_hud_compass_landmark.dds", StringComparison.OrdinalIgnoreCase)) continue;
            for (var next = Math.Max(0, at - 2048); next + 14 <= at; next++)
                if (code[next] == 0xdb && (code[next + 1] & 0xc7) == 0x81 && U32(code, next + 2) == 0x214 &&
                    code[next + 6] == 0xdc && code[next + 7] == 0x0d && code[next + 12] == 0xdc && code[next + 13] == 0xc0)
                    fractions.Add(checked((float)scalar(U32(code, next + 8))));
            for (var next = at + 5; next + 6 < Math.Min(code.Length, at + 512); next++)
                if (code[next] == 0xdc && code[next + 1] == 0x1d && scalar(U32(code, next + 2)) is var value && value > 0)
                    limits.Add(checked((float)value));
        }
        if (fractions.Count != 1 || limits.Count != 1 || !float.IsFinite(fractions[0]) || fractions[0] is <= 0 or >= 1 ||
            !float.IsFinite(limits[0]) || limits[0] <= 0)
            throw new NotSupportedException("Owned compass edge/blink declarations are absent or ambiguous.");
        return new(fractions[0], limits[0]);
    }
}
