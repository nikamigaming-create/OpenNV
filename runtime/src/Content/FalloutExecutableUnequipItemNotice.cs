namespace OpenNV.Runtime.Content;

internal sealed record FalloutEquipmentItemNotice(string Format, string Icon, float Seconds);

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutEquipmentItemNotice ReadUnequipItemNotice(string path)
    {
        var (code, image) = Load(path);
        return ReadUnequipItemNotice(code, image.Literal, ControlDescriptors(code, image),
            address => BitConverter.ToSingle(image.Read(address, sizeof(float))));
    }

    internal static FalloutEquipmentItemNotice ReadEquipItemNotice(string path)
    {
        var (code, image) = Load(path);
        return ReadEquipItemNotice(code, image.Literal, ControlDescriptors(code, image),
            address => BitConverter.ToSingle(image.Read(address, sizeof(float))));
    }
    internal static FalloutEquipmentItemNotice ReadEquipItemNotice(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, IReadOnlyDictionary<uint, string> settings, Func<uint, float> scalar) =>
        ReadEquipmentItemNotice(code, literal, settings, scalar, "sEquipItemOnPlayer");
    internal static FalloutEquipmentItemNotice ReadUnequipItemNotice(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, IReadOnlyDictionary<uint, string> settings, Func<uint, float> scalar) =>
        ReadEquipmentItemNotice(code, literal, settings, scalar, "sUnequipItemOnPlayer");
    private static FalloutEquipmentItemNotice ReadEquipmentItemNotice(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, IReadOnlyDictionary<uint, string> settings, Func<uint, float> scalar, string settingName)
    {
        var found = new List<FalloutEquipmentItemNotice>();
        for (var at = 0; at + 79 <= code.Length; ++at)
        {
            var row = code[at..];
            if (row[0] != 0xb9 || row[5] != 0xe8 || row[10] != 0x50 ||
                !settings.TryGetValue(U32(row, 1), out var setting) || setting != settingName) continue;
            if (row[11] != 0x8b || row[12] is not (0x4d or 0x55) || row[14] != (row[12] == 0x4d ? 0x51 : 0x52) || row[15] != 0xe8 ||
                !row.Slice(20, 4).SequenceEqual(new byte[] { 0x83, 0xc4, 4, 0x50 }) || row[24] != 0x68 ||
                row[29] != 0x8d || row[30] is not (0x45 or 0x55) || row[32] != (row[30] == 0x45 ? 0x50 : 0x52) || row[33] != 0xe8 ||
                !row.Slice(38, 8).SequenceEqual(new byte[] { 0x83, 0xc4, 0x10, 0x6a, 0, 0x51, 0xd9, 5 }) ||
                !row.Slice(50, 6).SequenceEqual(new byte[] { 0xd9, 0x1c, 0x24, 0x6a, 0, 0x68 }) ||
                row[60] != 0x6a || row[61] != 0 || row[62] != 0x8d || row[63] != 0x4d || row[64] != row[31] ||
                row[65] != 0xe8 || row[70] != 0x50 || row[71] != 0xe8 ||
                !row.Slice(76, 3).SequenceEqual(new byte[] { 0x83, 0xc4, 0x18 })) continue;
            var format = literal(U32(row, 25)); var icon = literal(U32(row, 56)); var seconds = scalar(U32(row, 46));
            if (format != "%s %s." || icon is null || !icon.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
                !float.IsFinite(seconds) || seconds <= 0)
                throw new NotSupportedException("Owned equipment notice format/icon/lifetime is unbound.");
            found.Add(new(format, icon, seconds));
        }
        var unique = found.Distinct().ToArray();
        return unique.Length == 1 ? unique[0] : throw new NotSupportedException("Owned equipment notice is absent or ambiguous.");
    }
}
