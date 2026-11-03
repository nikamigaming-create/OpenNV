namespace OpenNV.Runtime.Content;

internal sealed record FalloutQuickSaveNotice(string Setting, string Icon, float Seconds);

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutQuickSaveNotice ReadQuickSaveNotice(string path)
    {
        var (code, image) = Load(path);
        return ReadQuickSaveNotice(code, image.Literal, ControlDescriptors(code, image),
            address => BitConverter.ToSingle(image.Read(address, sizeof(float))));
    }

    internal static FalloutQuickSaveNotice ReadQuickSaveNotice(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, IReadOnlyDictionary<uint, string> settings, Func<uint, float> scalar)
    {
        var declarations = new List<FalloutQuickSaveNotice>();
        for (var at = 21; at + 19 <= code.Length; ++at)
        {
            if (code[at] != 0xb9 || code[at + 5] != 0xe8 || code[at + 10] != 0x50 || code[at + 11] != 0xe8 ||
                !settings.TryGetValue(U32(code, at + 1), out var setting) || setting != "sQuickSaving") continue;
            if (!code.Slice(at - 21, 5).SequenceEqual(new byte[] { 0x6a, 0, 0x51, 0xd9, 5 }) ||
                !code.Slice(at - 11, 5).SequenceEqual(new byte[] { 0x1c, 0x24, 0x6a, 0, 0x68 }) ||
                code[at - 12] != 0xd9 || code[at - 2] != 0x6a || code[at - 1] != 0 ||
                !code.Slice(at + 16, 3).SequenceEqual(new byte[] { 0x83, 0xc4, 0x18 })) continue;
            var icon = literal(U32(code, at - 6));
            var seconds = scalar(U32(code, at - 16));
            if (icon is null || !icon.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
                !float.IsFinite(seconds) || seconds <= 0)
                throw new NotSupportedException("Owned quicksave HUD declaration has no actual icon/lifetime.");
            declarations.Add(new(setting, icon, seconds));
        }
        var unique = declarations.Distinct().ToArray();
        return unique.Length == 1 ? unique[0] :
            throw new NotSupportedException("Owned quicksave HUD declaration is absent or ambiguous.");
    }
}
