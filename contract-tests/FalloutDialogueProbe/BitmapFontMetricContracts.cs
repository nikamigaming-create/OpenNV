using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class BitmapFontMetricContracts
{
    internal static void Run()
    {
        var bytes = Source();
        var digest = SHA256.HashData(bytes);
        var font = FalloutBitmapFont.Read(bytes);
        var control = font.Glyph('\u007f');
        Require(control.Width == 3 && control.Height == 6 && control.LeftBearing == 1 &&
            control.RightBearing == -9 && control.Advance == -5 && control.Ascent == 4,
            "Signed source bearings or glyph extent were replaced or dropped.");
        Require(font.Glyph('X').Advance == -5 && font.Measure("AA\u007fAA") == 15 && font.Measure("AXA") == 5,
            "Source measurement clamped or skipped a signed glyph advance.");
        Require(font.Glyph('\t').Advance == 1 && font.Glyph('\t').Width == 0 && font.Glyph(' ').Advance == 4,
            "Empty source glyphs lost their independently authored spacing.");
        Require(font.Glyphs.Count == 256 && SHA256.HashData(bytes).AsSpan().SequenceEqual(digest),
            "Font decoding omitted entries or changed source bytes.");

        Reject(bytes, 127, 36, -1); // Negative drawable width remains invalid.
        Reject(bytes, 127, 40, -1); // Negative drawable height remains invalid.
        Reject(bytes, 127, 44, float.NaN);
        Reject(bytes, 127, 48, float.PositiveInfinity);
        var overflow = bytes.ToArray();
        Write(overflow, 127, 36, float.MaxValue);
        Write(overflow, 127, 44, float.MaxValue);
        Refused(overflow);
        var atlas = bytes.ToArray();
        Write(atlas, 127, 12, 1.125f); Write(atlas, 127, 28, 1.125f);
        Refused(atlas);
        Console.WriteLine("OPENNV_BITMAP_FONT_SIGNED_METRICS_PASS sourceEntries=256 signedAdvance=true exactMeasurement=true emptySpacing=true invalidMetricsRefused=true sourceReadonly=true");
    }

    private static byte[] Source()
    {
        var result = new byte[296 + 256 * 56];
        BinaryPrimitives.WriteSingleLittleEndian(result, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), 1);
        Encoding.ASCII.GetBytes("authored-metrics").CopyTo(result, 12);
        for (var slot = 0; slot < 256; ++slot)
        {
            Write(result, slot, 12, .5f); Write(result, slot, 28, .5f);
            Write(result, slot, 24, .5f); Write(result, slot, 32, .5f);
            Write(result, slot, 36, 5); Write(result, slot, 40, 7); Write(result, slot, 52, 6);
        }
        Write(result, 127, 36, 3); Write(result, 127, 40, 6);
        Write(result, 127, 44, 1); Write(result, 127, 48, -9); Write(result, 127, 52, 4);
        Write(result, 'X', 36, 4); Write(result, 'X', 44, -2); Write(result, 'X', 48, -7);
        foreach (var slot in new[] { 9, 32 })
        {
            result.AsSpan(296 + slot * 56, 56).Clear();
            Write(result, slot, 44, slot == 9 ? 2 : 0); Write(result, slot, 48, slot == 9 ? -1 : 4);
        }
        return result;
    }
    private static void Write(byte[] bytes, int slot, int metric, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(296 + slot * 56 + metric), value);
    private static void Reject(byte[] source, int slot, int metric, float value)
    {
        var bytes = source.ToArray(); Write(bytes, slot, metric, value); Refused(bytes);
    }
    private static void Refused(byte[] bytes)
    {
        try { _ = FalloutBitmapFont.Read(bytes); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid source font metrics or atlas extent were admitted.");
    }
    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
