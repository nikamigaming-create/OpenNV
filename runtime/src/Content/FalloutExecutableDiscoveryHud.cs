namespace OpenNV.Runtime.Content;

internal sealed record FalloutDiscoveryHudDeclaration(string PrefixSetting, string SoundEditorId,
    int HeaderFont, int NameFont, int SafeZoneScale, int XInset, int YInset,
    float HeaderGap, float LineGap, int WrapAt, int NameCapacity, float FadeOutAlphaMinimum);

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutDiscoveryHudDeclaration ReadDiscoveryHudDeclaration(string path)
    {
        var (code, image) = Load(path);
        return ReadDiscoveryHudDeclaration(code, image.Literal, ControlDescriptors(code, image),
            address => BitConverter.ToDouble(image.Read(address, sizeof(double))));
    }

    // Read compiler-emitted declarations. Values remain owned inputs; matching
    // only a localized string is insufficient to admit a different UI branch.
    internal static FalloutDiscoveryHudDeclaration ReadDiscoveryHudDeclaration(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, IReadOnlyDictionary<uint, string> settings, Func<uint, double> scalar)
    {
        var notices = new List<string>();
        var delayAt = -1;
        var rootAt = -1;
        var fadeOutAt = -1;
        for (var at = 0; at + 10 <= code.Length; at++)
        {
            if (code[at] == 0xb9 && code[at + 5] == 0xe8 && settings.TryGetValue(U32(code, at + 1), out var setting))
            {
                if (setting == "fQuestCinematicCharacterFadeOut" && fadeOutAt < 0) fadeOutAt = at;
                if (setting == "fQuestCinematicCharacterFadeInDelay" && delayAt < 0) delayAt = at;
                if (setting != "sDiscoveredText") continue;
                for (var before = Math.Max(0, at - 32); before + 13 < at; before++)
                    if (code[before] == 0x68 && literal(U32(code, before + 1)) is { } sound &&
                        sound.StartsWith("UI", StringComparison.Ordinal) &&
                        code.Slice(before + 5, 8).SequenceEqual(new byte[] { 0x6a, 0xff, 0x6a, 0xff, 0x6a, 0, 0x6a, 1 }))
                        notices.Add(sound);
            }
            if (code[at] == 0x68 && literal(U32(code, at + 1)) == "QuestReminder")
            {
                if (rootAt >= 0) throw new NotSupportedException("Cinematic reminder root is ambiguous.");
                rootAt = at;
            }
        }
        if (notices.Count != 1 || delayAt < 0 || rootAt < 10 || fadeOutAt < 0)
            throw new NotSupportedException("Owned discovery reminder declaration is unbound.");
        // Default fonts are assignments consumed by a font-trait setter. The
        // two text rows precede the per-character delay declaration.
        var fonts = new List<(int At, int Value)>();
        for (var at = Math.Max(0, delayAt - 4096); at + 7 < delayAt; at++)
        {
            if (code[at] != 0xc7 || code[at + 1] != 0x45) continue;
            var local = code[at + 2];
            for (var next = at + 7; next + 10 <= Math.Min(delayAt, at + 512); next++)
                if (code[next] == 0x8b && (code[next + 1] & 0xc7) == 0x45 && code[next + 2] == local &&
                    code[next + 3] == 0x50 + ((code[next + 1] >> 3) & 7) && code[next + 4] == 0x68 && U32(code, next + 5) == 4025)
                { fonts.Add((at, checked((int)U32(code, at + 3)))); break; }
        }
        if (fonts.Count != 2 || fonts.Any(row => row.Value <= 0))
            throw new NotSupportedException("Cinematic reminder row fonts are unbound.");
        var gaps = new List<float>();
        var wrap = new List<int>();
        for (var at = fonts[0].At; at + 22 < delayAt; at++)
        {
            if (code[at] == 0xdc && code[at + 1] == 0x05)
            {
                var value = scalar(U32(code, at + 2));
                if (!double.IsFinite(value) || value < 0 || value > float.MaxValue)
                    throw new InvalidDataException("Cinematic row spacing is invalid.");
                gaps.Add((float)value);
            }
            if (code[at] == 0xc7 && code[at + 1] == 0x85 && code[at + 10] == 0x8d && code[at + 11] == 0x85 &&
                code[at + 16] == 0x50 && code[at + 17] == 0xe8)
                wrap.Add(checked((int)U32(code, at + 6)));
        }
        var fadeOutMinimum = new List<float>();
        for (var at = Math.Max(0, fadeOutAt - 64); at + 6 < fadeOutAt; at++)
            if (code[at] == 0xdc && code[at + 1] == 0x1d)
            {
                var value = scalar(U32(code, at + 2));
                if (!double.IsFinite(value) || value is < 0 or > 255) throw new InvalidDataException("Cinematic fade-out alpha is invalid.");
                fadeOutMinimum.Add((float)value);
            }
        var x = new List<int>(); var capacity = new List<int>();
        for (var at = rootAt + 5; at + 7 < Math.Min(code.Length, rootAt + 256); at++)
        {
            if (code[at] == 0x8d && code[at + 1] == 0x44 && code[at + 2] == 0x12) x.Add(unchecked((sbyte)code[at + 3]));
            if (code[at] == 0x83 && code[at + 1] == 0xbd && code[at + 7] == 0x7d) capacity.Add(code[at + 6] - 1);
        }
        if (fadeOutMinimum.Count != 1 || gaps.Count != 2 || wrap.Count != 1 || wrap[0] <= 0 || x.Count != 1 || capacity.Count != 1 || capacity[0] <= 0 ||
            !code.Slice(rootAt - 10, 3).SequenceEqual(new byte[] { 0x8d, 0x94, 0x09 }))
            throw new NotSupportedException("Cinematic reminder geometry is unbound.");
        return new("sDiscoveredText", notices[0], fonts[0].Value, fonts[1].Value, 2,
            x[0], checked((int)U32(code, rootAt - 7)), gaps[0], gaps[1], wrap[0], capacity[0], fadeOutMinimum[0]);
    }
}
