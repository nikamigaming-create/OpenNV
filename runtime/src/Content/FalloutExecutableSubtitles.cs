namespace OpenNV.Runtime.Content;

internal sealed record FalloutHudSubtitleDeclarations(double InfoBottomInset, double Gap, double CenterDivisor,
    int ScreenDivisor, int SafeZoneScale, string TextTemplate, bool SinglePrecision = false)
{
    internal (float X, float Y, float TextX) Place(float screenWidth, float screenHeight, float safeY, float infoHeight,
        float width, float height)
    {
        if (!float.IsFinite(screenWidth) || !float.IsFinite(screenHeight) || !float.IsFinite(safeY) ||
            !float.IsFinite(infoHeight) || !float.IsFinite(width) || !float.IsFinite(height) ||
            screenWidth <= 0 || screenHeight <= 0 || width <= 0 || height <= 0 || infoHeight <= 0 || safeY < 0 ||
            !double.IsFinite(CenterDivisor) || CenterDivisor <= 0 || !double.IsFinite(InfoBottomInset) || InfoBottomInset < 0 ||
            !double.IsFinite(Gap) || Gap < 0 || SafeZoneScale <= 0 || ScreenDivisor <= 0)
            throw new InvalidDataException("Subtitle placement has invalid source dimensions.");
        var x = SinglePrecision ? (float)Math.Truncate(screenWidth / ScreenDivisor) - width * (float)(1 / CenterDivisor) :
            (float)(Math.Truncate(screenWidth / ScreenDivisor) - width / CenterDivisor);
        var y = SinglePrecision ? (float)Math.Truncate(screenHeight) - SafeZoneScale * (float)Math.Truncate(safeY) - infoHeight -
            (float)InfoBottomInset - height - (float)Gap :
            (float)(Math.Truncate(screenHeight) - SafeZoneScale * Math.Truncate(safeY) - infoHeight - InfoBottomInset - height - Gap);
        var textX = SinglePrecision ? width * (float)(1 / CenterDivisor) : (float)(width / CenterDivisor);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(textX))
            throw new InvalidDataException("Subtitle placement exceeded finite canvas coordinates.");
        return (x, y, textX);
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutHudSubtitleDeclarations ReadHudSubtitleDeclarations(string path)
    {
        var (code, image) = Load(path);
        return ReadHudSubtitleDeclarations(code, image.Literal, image.IsWritableObject,
            address => BitConverter.ToDouble(image.Read(address, 8)), address => BitConverter.ToSingle(image.Read(address, 4)));
    }

    internal static FalloutHudSubtitleDeclarations ReadHudSubtitleDeclarations(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, Func<uint, bool> writableObject, Func<uint, double> scalar,
        Func<uint, float>? scalar32 = null)
    {
        var branches = new List<(int At, string Name, uint Global, uint Member, uint Lookup, uint Child)>();
        for (var at = 0; at <= code.Length - 39; ++at)
        {
            var entry = code[at..];
            if (entry[0] != 0x68 || entry[5] != 0x68 || U32(entry, 6) != 1004 || entry[10] != 0xe8 || entry[19] != 0xe8 ||
                !entry.Slice(15, 4).SequenceEqual(new byte[] { 0x83, 0xc4, 4, 0x50 }) ||
                !entry.Slice(33, 2).SequenceEqual(new byte[] { 0x89, 0x81 })) continue;
            var globalAt = entry.Slice(24, 5).SequenceEqual(new byte[] { 0x83, 0xc4, 8, 0x8b, 0x0d }) ? 29 :
                entry.Slice(24, 2).SequenceEqual(new byte[] { 0x8b, 0x0d }) &&
                entry.Slice(30, 3).SequenceEqual(new byte[] { 0x83, 0xc4, 8 }) ? 26 : -1;
            if (globalAt < 0 || !writableObject(U32(entry, globalAt))) continue;
            if (literal(U32(entry, 1)) is not { } name) continue;
            branches.Add((at, name, U32(entry, globalAt), U32(entry, 35),
                unchecked((uint)(at + 15 + (int)U32(entry, 11))), unchecked((uint)(at + 24 + (int)U32(entry, 20)))));
        }
        var subtitles = branches.Where(branch => branch.Name == "Subtitles").ToArray();
        if (subtitles.Length != 1) throw new NotSupportedException("Owned subtitle branch association is missing or ambiguous.");
        var subtitle = subtitles[0];
        var infos = branches.Where(branch => branch.At < subtitle.At && branch.Name == "Info" &&
            branch.Global == subtitle.Global && branch.Lookup == subtitle.Lookup && branch.Child == subtitle.Child).ToArray();
        if (infos.Length != 1) throw new NotSupportedException("Owned subtitle Info placement association is unbound.");
        var info = infos[0];
        var following = branches.FirstOrDefault(branch => branch.At > subtitle.At);
        if (following.At == 0) throw new NotSupportedException("Owned subtitle declaration extent is unbound.");
        var infoCode = code[(info.At + 39)..subtitle.At];
        var subtitleCode = code[(subtitle.At + 39)..following.At];
        ReadOnlySpan<byte> Placement(ReadOnlySpan<byte> bytes, uint member)
        {
            for (var at = 0; at <= bytes.Length - 31; ++at)
            {
                var entry = bytes[at..];
                if (entry[0] == 0xa1 && U32(entry, 1) == subtitle.Global &&
                    entry.Slice(5, 6).SequenceEqual(new byte[] { 0x6a, 1, 0x51, 0xc7, 4, 0x24 }) &&
                    U32(entry, 11) == BitConverter.SingleToUInt32Bits(1) &&
                    entry.Slice(15, 2).SequenceEqual(new byte[] { 0x8b, 0x88 }) && U32(entry, 17) == member &&
                    entry[21] == 0x68 && U32(entry, 22) == 4008 && entry[26] == 0xe8) return bytes[..at];
            }
            for (var at = 0; at <= bytes.Length - 24; ++at)
            {
                var entry = bytes[at..];
                if (!entry[..3].SequenceEqual(new byte[] { 0x6a, 1, 0x68 }) || U32(entry, 3) != 4008 ||
                    entry[7] != 0x8b || entry[8] is not (0x05 or 0x0d or 0x15) || U32(entry, 9) != subtitle.Global ||
                    entry[13] != 0x8b || entry[14] != 0x88 + (entry[8] - 5) / 8 || U32(entry, 15) != member || entry[19] != 0xe8) continue;
                return bytes[..at];
            }
            throw new NotSupportedException("Owned subtitle placement extent has no associated locus setter.");
        }
        var infoPlacement = Placement(infoCode, info.Member);
        var subtitlePlacement = Placement(subtitleCode, subtitle.Member);
        var single = infoPlacement.IndexOf(new byte[] { 0xf3, 0x0f, 0x59, 0x05 }) >= 0;
        double Operand(ReadOnlySpan<byte> bytes, byte operation)
        {
            var values = new List<double>();
            for (var at = 0; at <= bytes.Length - 6; ++at)
                if (bytes[at] == 0xdc && bytes[at + 1] == operation) values.Add(scalar(U32(bytes, at + 2)));
            var unique = values.Distinct().ToArray();
            if (unique.Length != 1 || !double.IsFinite(unique[0]) || unique[0] <= 0)
                throw new NotSupportedException("Owned subtitle numeric operand is missing, invalid or ambiguous.");
            return unique[0];
        }
        double SingleOperand(ReadOnlySpan<byte> bytes, byte operation)
        {
            if (scalar32 is null) throw new NotSupportedException("Owned subtitle has no Float32 reader.");
            var values = new List<float>();
            for (var at = 0; at <= bytes.Length - 8; ++at)
                if (bytes.Slice(at, 4).SequenceEqual(new byte[] { 0xf3, 0x0f, operation, 0x05 }))
                    values.Add(scalar32(U32(bytes, at + 4)));
            var unique = values.Distinct().ToArray();
            if (unique.Length != 1 || !float.IsFinite(unique[0]) || unique[0] <= 0)
                throw new NotSupportedException("Owned subtitle Float32 operand is missing, invalid or ambiguous.");
            return unique[0];
        }
        var divisor = single ? 1 / SingleOperand(subtitlePlacement, 0x59) : Operand(subtitlePlacement, 0x35);
        if ((single ? 1 / SingleOperand(infoPlacement, 0x59) : Operand(infoPlacement, 0x35)) != divisor)
            throw new NotSupportedException("Owned subtitle centering relationships disagree.");
        if (!single && (infoPlacement.IndexOf(new byte[] { 0x99, 0x2b, 0xc2, 0xd1, 0xf8 }) < 0 ||
            subtitlePlacement.IndexOf(new byte[] { 0x99, 0x2b, 0xc2, 0xd1, 0xf8 }) < 0))
            throw new NotSupportedException("Owned subtitle integer screen centering is unbound.");
        var safeZoneScale = single ? ReadCachedSubtitleScreen(code[..info.At], subtitle.Global, infoPlacement, subtitlePlacement) : 2;
        if (!single && infoPlacement.IndexOf(new byte[] { 0xd1, 0xe1 }) < 0)
            throw new NotSupportedException("Owned subtitle safe-zone multiplication is unbound.");
        string? template = null;
        for (var at = 0; at <= subtitleCode.Length - 25; ++at)
        {
            var factory = subtitleCode[at..];
            if (single && factory[0] == 0xa1 && U32(factory, 1) == subtitle.Global &&
                factory.Slice(5, 5).SequenceEqual(new byte[] { 0x8b, 0xcf, 0x6a, 0, 0x68 }) &&
                factory.Slice(14, 2).SequenceEqual(new byte[] { 0xff, 0xb0 }) && U32(factory, 16) == subtitle.Member && factory[20] == 0xe8)
            {
                var selected = literal(U32(factory, 10));
                if (selected is null || template is not null) throw new NotSupportedException("Owned subtitle text template is missing or ambiguous.");
                template = selected; continue;
            }
            if (factory.Length < 28 || !factory[..3].SequenceEqual(new byte[] { 0x6a, 0, 0x68 }) ||
                !factory.Slice(7, 2).SequenceEqual(new byte[] { 0x8b, 0x15 }) || U32(factory, 9) != subtitle.Global ||
                !factory.Slice(13, 2).SequenceEqual(new byte[] { 0x8b, 0x82 }) || U32(factory, 15) != subtitle.Member ||
                !factory.Slice(19, 3).SequenceEqual(new byte[] { 0x50, 0x8b, 0x4d }) || factory[23] != 0xe8) continue;
            var name = literal(U32(factory, 3));
            if (name is null || template is not null) throw new NotSupportedException("Owned subtitle text template is missing or ambiguous.");
            template = name;
        }
        return new(single ? SingleOperand(infoPlacement, 0x5c) : Operand(infoPlacement, 0x25),
            single ? SingleOperand(subtitlePlacement, 0x5c) : Operand(subtitlePlacement, 0x25), divisor, 2,
            safeZoneScale,
            template ?? throw new NotSupportedException("Owned subtitle text factory is unbound."), single);
    }

    private static int ReadCachedSubtitleScreen(ReadOnlySpan<byte> prefix, uint global,
        ReadOnlySpan<byte> infoPlacement, ReadOnlySpan<byte> subtitlePlacement)
    {
        var start = prefix.LastIndexOf(new byte[] { 0x55, 0x8b, 0xec });
        if (start < 0) throw new NotSupportedException("Owned subtitle screen-cache function is unbound.");
        var body = prefix[start..];
        var widths = new List<(int At, byte Screen, byte Cache)>();
        var bottoms = new List<(int At, byte Screen, byte Cache)>();
        for (var at = 0; at <= body.Length - 39; ++at)
        {
            var row = body[at..];
            if (row[..4].SequenceEqual(new byte[] { 0xf3, 0x0f, 0x2c, 0x45 }) && row[4] >= 0x80 &&
                row.Slice(5, 2).SequenceEqual(new byte[] { 0x8b, 0x0d }) && U32(row, 7) == global &&
                row.Slice(11, 7).SequenceEqual(new byte[] { 0x99, 0x2b, 0xc2, 0x8b, 0xf0, 0x8b, 0x89 }) &&
                row.Slice(22, 7).SequenceEqual(new byte[] { 0x6a, 1, 0xd1, 0xfe, 0x68, 0xb1, 0x0f }) &&
                U32(row, 27) == 4017 && row.Slice(31, 2).SequenceEqual(new byte[] { 0x89, 0x75 }) &&
                row[33] >= 0x80 && row[34] == 0xe8)
                widths.Add((at, row[4], row[33]));
            if (at >= 6 && body.Slice(at - 6, 2).SequenceEqual(new byte[] { 0x8b, 0x0d }) && U32(body, at - 4) == global &&
                row[..7].SequenceEqual(new byte[] { 0x8d, 0x04, 0x3f, 0xf3, 0x0f, 0x2c, 0x7d }) && row[7] >= 0x80 &&
                row.Slice(8, 3).SequenceEqual(new byte[] { 0x6a, 1, 0x68 }) && U32(row, 11) == 4016 &&
                row.Slice(15, 2).SequenceEqual(new byte[] { 0x8b, 0x89 }) &&
                row.Slice(21, 2).SequenceEqual(new byte[] { 0x89, 0x45 }) &&
                row.Slice(24, 4).SequenceEqual(new byte[] { 0x2b, 0xf8, 0x89, 0x7d }) && row[28] >= 0x80 &&
                row.Slice(29, 7).SequenceEqual(new byte[] { 0x66, 0x0f, 0x6e, 0xc7, 0x0f, 0x5b, 0xc0 }))
                bottoms.Add((at, row[7], row[28]));
        }
        if (widths.Count != 1 || bottoms.Count != 1 || widths[0].Screen == bottoms[0].Screen)
            throw new NotSupportedException("Owned subtitle screen-cache association is missing or ambiguous.");
        var width = widths[0]; var bottom = bottoms[0];
        // The screen getters publish two adjacent Float32 locals before the
        // integer caches. Tie both later placements to those same owners.
        var getters = 0;
        for (var at = 0; at <= Math.Min(width.At, bottom.At) - 16; ++at)
            if (body[at] == 0xe8 && body.Slice(at + 5, 3).SequenceEqual(new byte[] { 0xd9, 0x5d, width.Screen }) &&
                body[at + 8] == 0xe8 && body.Slice(at + 13, 3).SequenceEqual(new byte[] { 0xd9, 0x5d, bottom.Screen })) ++getters;
        var widthLoad = body.LastIndexOf(new byte[] { 0x8b, 0x75, width.Cache });
        var bottomLoad = body.LastIndexOf(new byte[] { 0x8b, 0x7d, bottom.Cache });
        if (getters != 1 || widthLoad <= width.At || bottomLoad <= bottom.At ||
            infoPlacement.IndexOf(new byte[] { 0x66, 0x0f, 0x6e, 0xce }) < 0 ||
            subtitlePlacement.IndexOf(new byte[] { 0x66, 0x0f, 0x6e, 0xce }) < 0 ||
            infoPlacement.IndexOf(new byte[] { 0x66, 0x0f, 0x6e, 0xc7 }) < 0)
            throw new NotSupportedException("Owned subtitle placements have no shared integer screen-cache owners.");
        return 2;
    }
}
