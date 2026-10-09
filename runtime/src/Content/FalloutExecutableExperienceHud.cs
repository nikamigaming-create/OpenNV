using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    // These are reviewed *image* contracts, not private addresses or a guessed
    // game-name selector. No modified/unknown image inherits an admitted clock.
    // The neutral consumer is shared; optimized SSE and framed x87 transports
    // belong to different exact original compiler images.
    internal static FalloutExperienceHudDeclaration ReadExperienceHud(string path)
    {
        var original = File.ReadAllBytes(path);
        var sha = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        var optimized = sha switch
        {
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" => false,
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" => true,
            _ => throw new NotSupportedException("Experience HUD notification consumer has no reviewed selected image contract."),
        };
        var (code, image) = Load(original);
        var body = ExperienceConsumer(code, image.Literal);
        float Scalar(uint address) => BitConverter.ToSingle(image.Read(address, sizeof(float)));
        double WideScalar(uint address) => BitConverter.ToDouble(image.Read(address, sizeof(double)));
        var fade = optimized ? ExperienceImmediateDuration(body, 0x40000000, 6) : ExperienceFloatDuration(body, Scalar, 6);
        // Independent pointer sweeps, text fade, hold comparison and timestamp
        // publication are decoded from the selected consumer. A changed call,
        // branch or source constant also changes its required original image.
        var threshold = optimized ? ExperienceImmediateDuration(body, 0x3f800000, 1) : ExperienceUnitSweep(body);
        var textFade = optimized ? ExperienceImmediateDuration(body, 0x3f000000, 2) : ExperienceTextFade(body, Scalar);
        var hold = ExperienceHold(body, optimized, Scalar, WideScalar);
        var nextTimestamp = ExperienceNextTimestamp(body);
        foreach (var tile in FalloutExperienceHudSource.Tiles)
            if (!ExperienceHasLiteral(code, image.Literal, tile))
                throw new NotSupportedException($"Selected XP tile association is absent: {tile}.");
        foreach (var declaration in new[] { "+%i", "%i", "fHudOpacity:Interface", "_x_min", "_x_max", "UILevelUpText" })
            if (!ExperienceHasLiteral(code, image.Literal, declaration))
                throw new NotSupportedException($"Selected XP declaration association is absent: {declaration}.");
        var contract = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sha + "\0experience-hud-queue-tile-clock-v1\0" +
            Convert.ToHexString(SHA256.HashData(body))))).ToLowerInvariant();
        var (insetX, insetY) = ExperienceInsets(code, image.Literal);
        var result = new FalloutExperienceHudDeclaration(sha, contract, fade, fade, threshold, textFade, hold, nextTimestamp,
            insetX, insetY, "+%i", "%i", "fHudOpacity:Interface", "UIPopUpExperienceUp", "UILevelUpText");
        result.Validate(); return result;
    }

    private static byte[] ExperienceConsumer(byte[] code, Func<uint, string?> literal)
    {
        byte[]? found = null;
        for (var at = 0; at < code.Length - 5; ++at)
        {
            if (code[at] != 0x68 || literal(U32(code, at + 1)) != "UIPopUpExperienceUp") continue;
            var start = at;
            while (start > 0 && at - start < 8192 && !code.AsSpan(start, Math.Min(3, code.Length - start)).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec })) --start;
            if (!code.AsSpan(start, 3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec }))
                throw new NotSupportedException("XP notification consumer entry is unbound.");
            var end = at;
            while (end < code.Length - 2 && end - start < 16384 &&
                !(code[end] == 0xc3 && code[end + 1] == 0xcc && code[end + 2] == 0xcc)) ++end;
            if (end - start >= 16384 || end >= code.Length - 2)
                throw new NotSupportedException("XP notification consumer return is unbound.");
            var body = code.AsSpan(start, end + 1 - start).ToArray();
            if (found is not null) throw new InvalidDataException("XP notification consumer is ambiguous.");
            found = body;
        }
        return found ?? throw new NotSupportedException("XP notification consumer is absent.");
    }

    private static bool ExperienceHasLiteral(ReadOnlySpan<byte> code, Func<uint, string?> literal, string name)
    {
        for (var at = 0; at < code.Length - 5; ++at)
            if (code[at] == 0x68 && literal(U32(code, at + 1)) == name) return true;
        return false;
    }
    private static (int X, int Y) ExperienceInsets(ReadOnlySpan<byte> code, Func<uint, string?> literal)
    {
        (int X, int Y)? result = null;
        for (var at = 0; at <= code.Length - 245; ++at)
        {
            if (code[at] != 0x68 || literal(U32(code, at + 1)) != "XPMeter") continue;
            var values = new List<int>();
            for (var next = at + 5; next < at + 245; ++next)
            {
                var count = code[next] == 0x2d ? 1 : code[next] == 0x81 && (code[next + 1] & 0xf8) == 0xe8 ? 2 : 0;
                if (count == 0) continue;
                var tail = code.Slice(next + count + 4, Math.Min(48, code.Length - next - count - 4));
                if (tail.IndexOf(new byte[] { 0x68, 0xa1, 0x0f, 0x00, 0x00 }) < 0 &&
                    tail.IndexOf(new byte[] { 0x68, 0xa2, 0x0f, 0x00, 0x00 }) < 0) continue;
                var value = checked((int)U32(code, next + count));
                if (value <= 0) continue;
                if (!values.Contains(value)) values.Add(value);
            }
            if (values.Count != 2 || result is not null) throw new NotSupportedException("XP meter position transport is ambiguous.");
            result = (values[0], values[1]);
        }
        return result ?? throw new NotSupportedException("XP meter position transport is absent.");
    }
    private static float ExperienceImmediateDuration(ReadOnlySpan<byte> body, uint bits, int minimum)
    {
        var count = 0;
        for (var at = 0; at < body.Length - 8; ++at)
            if (body.Slice(at, 4).SequenceEqual(new byte[] { 0xc7, 0x44, 0x24, 0x08 }) && U32(body, at + 4) == bits) ++count;
        if (count < minimum) throw new NotSupportedException("XP optimized tile clock transport is unbound.");
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }
    private static float ExperienceFloatDuration(ReadOnlySpan<byte> body, Func<uint, float> read, int minimum)
    {
        var values = new Dictionary<float, int>();
        // PUSH mode; reserve/load duration; reserve/load destination alpha;
        // reserve zero start; source alpha trait. The receiver/callee layout is
        // additionally covered by the admitted whole image consumer contract.
        for (var at = 0; at <= body.Length - 33; ++at)
        {
            var row = body[at..];
            if (!row[..5].SequenceEqual(new byte[] { 0x6a, 0x00, 0x51, 0xd9, 0x05 }) ||
                !row.Slice(9, 6).SequenceEqual(new byte[] { 0xd9, 0x1c, 0x24, 0x51, 0xd9, 0x05 }) ||
                !row.Slice(19, 9).SequenceEqual(new byte[] { 0xd9, 0x1c, 0x24, 0x51, 0xd9, 0xee, 0xd9, 0x1c, 0x24 }) ||
                row[28] != 0x68 || U32(row, 29) != 0xfa9) continue;
            var duration = read(U32(row, 5));
            values[duration] = values.GetValueOrDefault(duration) + 1;
        }
        var admitted = values.Where(value => value.Value >= minimum).Select(value => value.Key).ToArray();
        if (admitted.Length != 1) throw new NotSupportedException("XP source alpha clocks are absent or ambiguous.");
        return admitted[0];
    }
    private static float ExperienceUnitSweep(ReadOnlySpan<byte> body)
    {
        if (body.IndexOf(new byte[] { 0x6a, 0x00, 0x51, 0xd9, 0xe8, 0xd9, 0x1c, 0x24, 0x51 }) < 0)
            throw new NotSupportedException("XP threshold sweep clock is unbound.");
        return 1;
    }
    private static float ExperienceTextFade(ReadOnlySpan<byte> body, Func<uint, float> read)
    {
        float? value = null; var count = 0;
        for (var at = 0; at <= body.Length - 28; ++at)
        {
            var row = body[at..];
            if (!row[..5].SequenceEqual(new byte[] { 0x6a, 0x00, 0x51, 0xd9, 0x05 }) ||
                !row.Slice(9, 6).SequenceEqual(new byte[] { 0xd9, 0x1c, 0x24, 0x51, 0xd9, 0x05 }) ||
                !row.Slice(19, 9).SequenceEqual(new byte[] { 0xd9, 0x1c, 0x24, 0x51, 0xd9, 0xee, 0xd9, 0x1c, 0x24 })) continue;
            var duration = read(U32(row, 5));
            // The level text's fade differs from the six meter fades.
            if (duration >= 1) continue;
            if (value is not null && value != duration) throw new InvalidDataException("XP level text clocks disagree.");
            value = duration; ++count;
        }
        if (count < 1 || value is null) throw new NotSupportedException("XP level text fade is unbound.");
        return value.Value;
    }
    private static uint ExperienceHold(ReadOnlySpan<byte> body, bool optimized, Func<uint, float> f32, Func<uint, double> f64)
    {
        double? found = null;
        for (var at = 0; at <= body.Length - 9; ++at)
        {
            double value;
            if (optimized && body.Slice(at, 3).SequenceEqual(new byte[] { 0x0f, 0x2f, 0x05 })) value = f32(U32(body, at + 3));
            else if (!optimized && body.Slice(at, 2).SequenceEqual(new byte[] { 0xdc, 0x1d })) value = f64(U32(body, at + 2));
            else continue;
            if (value != 1500) continue;
            if (found is not null && found != value) throw new InvalidDataException("XP level text hold is ambiguous.");
            found = value;
        }
        return found is { } duration ? checked((uint)duration) : throw new NotSupportedException("XP source hold comparison is unbound.");
    }
    private static uint ExperienceNextTimestamp(ReadOnlySpan<byte> body)
    {
        for (var at = 0; at <= body.Length - 11; ++at)
            if (body[at] == 0x05 && U32(body, at + 1) == 2000 && body[at + 5] == 0x50 && body[at + 6] == 0x68 && U32(body, at + 7) == 0x100d)
                return U32(body, at + 1);
        throw new NotSupportedException("XP level text next timestamp publication is unbound.");
    }
}
