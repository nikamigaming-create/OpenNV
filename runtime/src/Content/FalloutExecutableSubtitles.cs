namespace OpenNV.Runtime.Content;

internal sealed record FalloutHudSubtitleDeclarations(double InfoBottomInset, double Gap, double CenterDivisor,
    int ScreenDivisor, int SafeZoneScale, string TextTemplate)
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
        var x = (float)(Math.Truncate(screenWidth / ScreenDivisor) - width / CenterDivisor);
        var y = (float)(Math.Truncate(screenHeight) - SafeZoneScale * Math.Truncate(safeY) - infoHeight - InfoBottomInset - height - Gap);
        var textX = (float)(width / CenterDivisor);
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
            address => BitConverter.ToDouble(image.Read(address, 8)));
    }

    internal static FalloutHudSubtitleDeclarations ReadHudSubtitleDeclarations(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, Func<uint, bool> writableObject, Func<uint, double> scalar)
    {
        var branches = new List<(int At, string Name, uint Global, uint Member, uint Lookup, uint Child)>();
        for (var at = 0; at <= code.Length - 39; ++at)
        {
            var entry = code[at..];
            if (entry[0] != 0x68 || entry[5] != 0x68 || U32(entry, 6) != 1004 || entry[10] != 0xe8 || entry[19] != 0xe8 ||
                !entry.Slice(15, 4).SequenceEqual(new byte[] { 0x83, 0xc4, 4, 0x50 }) ||
                !entry.Slice(24, 5).SequenceEqual(new byte[] { 0x83, 0xc4, 8, 0x8b, 0x0d }) ||
                !entry.Slice(33, 2).SequenceEqual(new byte[] { 0x89, 0x81 }) || !writableObject(U32(entry, 29))) continue;
            if (literal(U32(entry, 1)) is not { } name) continue;
            branches.Add((at, name, U32(entry, 29), U32(entry, 35),
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
        var divisor = Operand(subtitlePlacement, 0x35);
        if (Operand(infoPlacement, 0x35) != divisor) throw new NotSupportedException("Owned subtitle centering relationships disagree.");
        if (infoPlacement.IndexOf(new byte[] { 0x99, 0x2b, 0xc2, 0xd1, 0xf8 }) < 0 ||
            subtitlePlacement.IndexOf(new byte[] { 0x99, 0x2b, 0xc2, 0xd1, 0xf8 }) < 0)
            throw new NotSupportedException("Owned subtitle integer screen centering is unbound.");
        if (infoPlacement.IndexOf(new byte[] { 0xd1, 0xe1 }) < 0)
            throw new NotSupportedException("Owned subtitle safe-zone multiplication is unbound.");
        string? template = null;
        for (var at = 0; at <= subtitleCode.Length - 28; ++at)
        {
            var factory = subtitleCode[at..];
            if (!factory[..3].SequenceEqual(new byte[] { 0x6a, 0, 0x68 }) ||
                !factory.Slice(7, 2).SequenceEqual(new byte[] { 0x8b, 0x15 }) || U32(factory, 9) != subtitle.Global ||
                !factory.Slice(13, 2).SequenceEqual(new byte[] { 0x8b, 0x82 }) || U32(factory, 15) != subtitle.Member ||
                !factory.Slice(19, 3).SequenceEqual(new byte[] { 0x50, 0x8b, 0x4d }) || factory[23] != 0xe8) continue;
            var name = literal(U32(factory, 3));
            if (name is null || template is not null) throw new NotSupportedException("Owned subtitle text template is missing or ambiguous.");
            template = name;
        }
        return new(Operand(infoPlacement, 0x25), Operand(subtitlePlacement, 0x25), divisor, 2, 2,
            template ?? throw new NotSupportedException("Owned subtitle text factory is unbound."));
    }
}
