using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class BitmapFontEncodingProbe
{
    internal static void Run()
    {
        var source = new byte[296 + 256 * 56];
        Float(0, 20);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(8), 1);
        Encoding.ASCII.GetBytes("synthetic").CopyTo(source, 12);
        for (var slot = 0; slot < 256; ++slot)
        {
            var offset = 296 + slot * 56;
            Float(offset + 4, slot / 256f); Float(offset + 20, slot / 256f);
            Float(offset + 12, (slot + 1) / 256f); Float(offset + 28, (slot + 1) / 256f);
            Float(offset + 24, 1); Float(offset + 32, 1);
            Float(offset + 36, slot + 1); Float(offset + 40, 10);
            Float(offset + 48, 2); Float(offset + 52, 8);
        }
        // The source alphabet includes empty glyphs with authored spacing.
        var empty = 296 + 0x92 * 56;
        source.AsSpan(empty, 56).Clear(); Float(empty + 48, 2);
        var ellipsis = 296 + 0x85 * 56;
        source.AsSpan(ellipsis, 56).Clear(); Float(ellipsis + 48, 2);
        var font = FalloutBitmapFont.Read(source);
        for (var slot = 0; slot < 256; ++slot)
        {
            // NUL terminates record strings, but its font slot remains decoded.
            var value = slot == 0 ? '\0' : FalloutDialogueTopic.Text([(byte)slot, 0]).Single();
            var expected = slot is 0x91 or 0x92 ? 0x27 : slot is 0x93 or 0x94 ? 0x22 : slot;
            Require(FalloutBitmapFont.Character(slot) == value && FalloutBitmapFont.GlyphSlot(value) == expected &&
                ReferenceEquals(font.Glyph(value), font.Glyphs[expected]), "Decoded record text selected a different source glyph.");
        }
        Require(FalloutBitmapFont.GlyphSlot('\u2018') == 0x27 && FalloutBitmapFont.GlyphSlot('\u2019') == 0x27 &&
            FalloutBitmapFont.GlyphSlot('\u201c') == 0x22 && FalloutBitmapFont.GlyphSlot('\u201d') == 0x22 &&
            FalloutBitmapFont.GlyphSlot('\u20ac') == 0x80 && FalloutBitmapFont.GlyphSlot('\u0153') == 0x9c &&
            FalloutBitmapFont.GlyphSlot('\u00e9') == 0xe9 && FalloutBitmapFont.GlyphSlot('\'') == 0x27,
            "Windows-1252 punctuation or Latin text lost its byte identity.");
        var text = FalloutDialogueTopic.Text([0x49, 0x92, 0x6d, 0x20, 0x93, 0x79, 0x6f, 0x75, 0x94, 0]);
        var width = new[] { 0x49, 0x27, 0x6d, 0x20, 0x22, 0x79, 0x6f, 0x75, 0x22 }.Sum(slot => font.Glyphs[slot].Advance);
        Require(font.Measure(text) == width && ReferenceEquals(font.Glyph('\u2019'), font.Glyph('\'')) &&
            font.Glyph('\u2026').Width == 0 && font.Glyph('\u2026').Advance == 2,
            "Measurement lost source quote normalization or another empty glyph's spacing.");
        foreach (var unsupported in new[] { "\u0092", "\u0100", "e\u0301", "\ud83d\ude00" })
        {
            var rejected = false;
            try { font.Measure(unsupported); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Unicode outside the decoded source alphabet acquired a replacement glyph.");
        }
        Require(source.AsSpan(empty, 48).IndexOfAnyExcept((byte)0) < 0,
            "Font lookup mutated the original decoded source bytes.");
        Console.WriteLine("OPENNV_BITMAP_FONT_ENCODING_CONTRACT_PASS slots=256 recordText=true sourceQuotes=true emptyAdvance=true unknownRejected=true");

        void Float(int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(source.AsSpan(offset), value);
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
