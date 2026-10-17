using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutImageFloatKey(float Time, float Value);
internal readonly record struct FalloutImageColorKey(float Time, Vector4 Value);

internal sealed record FalloutImageSpaceModifier(
    FalloutFormKey Form, string EditorId, string SourceSha256, bool Animated, float Duration,
    IReadOnlyList<FalloutImageFloatKey[]> Multiply, IReadOnlyList<FalloutImageFloatKey[]> Add,
    IReadOnlyDictionary<string, FalloutImageFloatKey[]> Effects,
    FalloutImageColorKey[] Tint, FalloutImageColorKey[] Fade,
    uint RadialFlags, Vector2 RadialCenter, bool DepthUsesTarget,
    FalloutFormKey? IntroSound, FalloutFormKey? OutroSound)
{
    internal float NormalizedTime(double seconds) => Animated ? (float)Math.Clamp(seconds / Duration, 0, 1) : 0;

    internal static float Sample(IReadOnlyList<FalloutImageFloatKey> keys, float time, float neutral)
    {
        if (keys.Count == 0) return neutral;
        var right = 0;
        while (right < keys.Count && keys[right].Time <= time) right++;
        if (right == 0) return keys[0].Value;
        if (right == keys.Count) return keys[^1].Value;
        var left = keys[right - 1];
        var amount = (time - left.Time) / (keys[right].Time - left.Time);
        return left.Value + (keys[right].Value - left.Value) * amount;
    }

    internal static Vector4 Sample(IReadOnlyList<FalloutImageColorKey> keys, float time, Vector4 neutral)
    {
        if (keys.Count == 0) return neutral;
        var right = 0;
        while (right < keys.Count && keys[right].Time <= time) right++;
        if (right == 0) return keys[0].Value;
        if (right == keys.Count) return keys[^1].Value;
        var left = keys[right - 1];
        return Vector4.Lerp(left.Value, keys[right].Value, (time - left.Time) / (keys[right].Time - left.Time));
    }
}

internal static class FalloutImageSpaceModifierReader
{
    internal static readonly IReadOnlyDictionary<string, int> EffectCounts = new Dictionary<string, int>
    {
        ["BNAM"] = 180,
        ["VNAM"] = 184,
        ["RNAM"] = 188,
        ["SNAM"] = 192,
        ["UNAM"] = 196,
        ["WNAM"] = 212,
        ["XNAM"] = 216,
        ["YNAM"] = 220,
        ["NAM1"] = 228,
        ["NAM2"] = 232,
        ["NAM4"] = 240,
    };

    internal static FalloutImageSpaceModifier Read(FalloutPluginRecord record)
    {
        if (record.Signature != "IMAD") throw new InvalidDataException("Image-space modifier target is not IMAD.");
        var fields = record.ReadSubrecords().ToArray();
        if (fields.GroupBy(field => field.Signature).Any(group => group.Count() != 1))
            throw new InvalidDataException($"IMAD {record.FormKey} has duplicate channels.");
        var headers = fields.Where(field => field.Signature == "DNAM").ToArray();
        if (headers.Length != 1) throw new InvalidDataException($"IMAD {record.FormKey} requires one DNAM.");
        var data = headers[0].Data;
        if (data.Length is not (236 or 240 or 244))
            throw new NotSupportedException($"IMAD {record.FormKey} has an unbound {data.Length}-byte DNAM layout.");
        // Owned older records end before the appended fade/motion key counts.
        // Their omitted channels must also be absent; Channel validates that.
        var header = new byte[244];
        data.Span.CopyTo(header);
        uint Count(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset));
        var flags = Count(0);
        var duration = BinaryPrimitives.ReadSingleLittleEndian(header.AsSpan(4));
        if ((flags & ~1u) != 0 || !float.IsFinite(duration) || duration < 0 || ((flags & 1) != 0 && duration <= 0))
            throw new NotSupportedException($"IMAD {record.FormKey} has invalid or unbound animation flags/duration.");
        var admitted = new HashSet<string>(EffectCounts.Keys, StringComparer.Ordinal)
            { "EDID", "DNAM", "TNAM", "NAM3", "RDSD", "RDSI" };
        ReadOnlyMemory<byte> Channel(string name, int countOffset, int stride)
        {
            admitted.Add(name);
            var matches = fields.Where(field => field.Signature == name).ToArray();
            var count = Count(countOffset);
            if (count > int.MaxValue / stride || (matches.Length == 0 ? 0 : matches[0].Data.Length) != (long)count * stride)
                throw new InvalidDataException($"IMAD {record.FormKey} {name} extent disagrees with its DNAM count.");
            return matches.Length == 0 ? ReadOnlyMemory<byte>.Empty : matches[0].Data;
        }
        var multiply = new List<FalloutImageFloatKey[]>();
        var add = new List<FalloutImageFloatKey[]>();
        for (var index = 0; index < 21; index++)
        {
            multiply.Add(FloatCurve(Channel($"{index}IAD", 8 + index * 8, 8).Span));
            add.Add(FloatCurve(Channel($"{64 + index}IAD", 12 + index * 8, 8).Span));
        }
        var effects = EffectCounts.ToDictionary(pair => pair.Key,
            pair => FloatCurve(Channel(pair.Key, pair.Value, 8).Span), StringComparer.Ordinal);
        var tint = ColorCurve(Channel("TNAM", 176, 20).Span);
        var fade = ColorCurve(Channel("NAM3", 236, 20).Span);
        var radialFlags = Count(200);
        var center = new Vector2(BinaryPrimitives.ReadSingleLittleEndian(header.AsSpan(204)),
            BinaryPrimitives.ReadSingleLittleEndian(header.AsSpan(208)));
        if ((radialFlags & ~1u) != 0 || header[224] > 1 || !float.IsFinite(center.X) || !float.IsFinite(center.Y))
            throw new NotSupportedException($"IMAD {record.FormKey} has unbound target/center fields.");
        FalloutFormKey? Sound(string name)
        {
            var found = fields.Where(field => field.Signature == name).ToArray();
            if (found.Length == 0) return null;
            if (found[0].Data.Length != 4) throw new InvalidDataException($"IMAD {name} has invalid extent.");
            return record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(found[0].Data.Span));
        }
        if (fields.Any(field => !admitted.Contains(field.Signature)))
            throw new NotSupportedException($"IMAD {record.FormKey} contains unbound fields: " +
                string.Join(',', fields.Where(field => !admitted.Contains(field.Signature)).Select(field => field.Signature)));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var field in fields)
        {
            hash.AppendData(System.Text.Encoding.ASCII.GetBytes(field.Signature));
            hash.AppendData(BitConverter.GetBytes(field.Data.Length));
            hash.AppendData(field.Data.Span);
        }
        var editor = fields.SingleOrDefault(field => field.Signature == "EDID");
        return new(record.FormKey, System.Text.Encoding.Latin1.GetString(editor.Data.Span).TrimEnd('\0'),
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), (flags & 1) != 0, duration,
            multiply, add, effects, tint, fade, radialFlags, center, header[224] != 0, Sound("RDSD"), Sound("RDSI"));
    }

    internal static FalloutImageFloatKey[] FloatCurve(ReadOnlySpan<byte> data)
    {
        if (data.Length % 8 != 0) throw new InvalidDataException("IMAD scalar curve has a partial key.");
        var keys = new FalloutImageFloatKey[data.Length / 8];
        for (var index = 0; index < keys.Length; index++)
        {
            var time = BinaryPrimitives.ReadSingleLittleEndian(data[(index * 8)..]);
            var value = BinaryPrimitives.ReadSingleLittleEndian(data[(index * 8 + 4)..]);
            CheckTime(time, index == 0 ? 0 : keys[index - 1].Time);
            if (!float.IsFinite(value)) throw new InvalidDataException("IMAD curve has a non-finite value.");
            keys[index] = new(time, value);
        }
        return keys;
    }

    internal static FalloutImageColorKey[] ColorCurve(ReadOnlySpan<byte> data)
    {
        if (data.Length % 20 != 0) throw new InvalidDataException("IMAD color curve has a partial key.");
        var keys = new FalloutImageColorKey[data.Length / 20];
        for (var index = 0; index < keys.Length; index++)
        {
            var key = data[(index * 20)..];
            var time = BinaryPrimitives.ReadSingleLittleEndian(key);
            var value = new Vector4(BinaryPrimitives.ReadSingleLittleEndian(key[4..]), BinaryPrimitives.ReadSingleLittleEndian(key[8..]),
                BinaryPrimitives.ReadSingleLittleEndian(key[12..]), BinaryPrimitives.ReadSingleLittleEndian(key[16..]));
            CheckTime(time, index == 0 ? 0 : keys[index - 1].Time);
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
                throw new InvalidDataException("IMAD color curve has a non-finite value.");
            keys[index] = new(time, value);
        }
        return keys;
    }

    private static void CheckTime(float time, float previous)
    {
        // Source files also contain knots beyond the normalized playback end.
        // Preserve them; duration controls sampling, not the format reader.
        if (!float.IsFinite(time) || time < previous)
            throw new InvalidDataException("IMAD curve keys must have finite, ordered, nonnegative times.");
    }
}
