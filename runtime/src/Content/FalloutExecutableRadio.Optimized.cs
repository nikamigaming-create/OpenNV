namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static IReadOnlyList<FalloutRadioHudDeclaration> ReadDirectRadioHudDeclarations(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, IReadOnlyDictionary<uint, string> settings)
    {
        var declarations = new List<FalloutRadioHudDeclaration>();
        int? nameReader = null, formatter = null, queue = null;
        for (var at = 0; at + 53 <= code.Length; ++at)
        {
            // Typed GMST payload -> format argument -> one local result buffer
            // -> text/icon/duration notice arguments. The optimized consumer
            // reads the string field directly instead of calling its getter.
            if (code[at] != 0x8b || code[at + 1] != 0x35) continue;
            var field = U32(code, at + 2);
            var setting = field >= 4 ? settings.GetValueOrDefault(field - 4) : null;
            var sounds = new List<string>();
            for (var before = Math.Max(0, at - 64); before + 5 <= at; ++before)
                if (code[before] == 0x68 && literal(U32(code, before + 1)) is { } sound &&
                    sound.StartsWith("UI", StringComparison.Ordinal)) sounds.Add(sound);
            var row = code[at..];
            var boundFlow = row[6] == 0xe8 &&
                row.Slice(11, 3).SequenceEqual(new byte[] { 0x50, 0x8d, 0x45 }) && row[14] >= 0x80 &&
                row.Slice(15, 3).SequenceEqual(new byte[] { 0x56, 0x50, 0xe8 }) &&
                row.Slice(22, 3).SequenceEqual(new byte[] { 0x8b, 0x75, row[14] }) &&
                row.Slice(25, 6).SequenceEqual(new byte[] { 0x83, 0xc4, 8, 0xc7, 4, 0x24 }) &&
                row.Slice(35, 3).SequenceEqual(new byte[] { 0x6a, 0, 0x68 }) &&
                row.Slice(42, 4).SequenceEqual(new byte[] { 0x6a, 0, 0x56, 0xe8 });
            if (sounds.Count == 0)
            {
                if (boundFlow && (setting == "sRadioStationDiscovered" || setting is null &&
                    literal(U32(row, 38)) is { } missingSoundIcon &&
                    missingSoundIcon.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)))
                    throw new NotSupportedException("Owned direct radio notice source sound is absent.");
                continue;
            }
            if (setting != "sRadioStationDiscovered")
            {
                // A complete direct notice with an unowned payload cannot be
                // silently omitted while another source branch remains valid.
                if (setting is null && boundFlow && literal(U32(row, 38)) is { } path &&
                    path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("Owned direct radio notice setting field is unbound.");
                continue;
            }
            if (sounds.Count != 1 || !boundFlow)
                throw new NotSupportedException("Owned direct radio notice argument flow is unbound.");
            var cleanup = 50;
            // A scheduled byte load after the call does not change any of the
            // five notice arguments or their original stack cleanup.
            if (row.Length >= 59 && row.Slice(cleanup, 2).SequenceEqual(new byte[] { 0x8a, 0x0d })) cleanup += 6;
            if (row.Length < cleanup + 3 || !row.Slice(cleanup, 3).SequenceEqual(new byte[] { 0x83, 0xc4, 20 }))
                throw new NotSupportedException("Owned direct radio notice stack extent is unbound.");
            var icon = literal(U32(row, 38));
            var seconds = BitConverter.Int32BitsToSingle(unchecked((int)U32(row, 31)));
            if (icon is null || !icon.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || !float.IsFinite(seconds) || seconds <= 0)
                throw new NotSupportedException("Owned direct radio notice media or duration is unbound.");
            SameRadioNoticeTarget(ref nameReader, RadioNoticeCallTarget(code, at + 6));
            SameRadioNoticeTarget(ref formatter, RadioNoticeCallTarget(code, at + 17));
            SameRadioNoticeTarget(ref queue, RadioNoticeCallTarget(code, at + 45));
            declarations.Add(new(icon, seconds, sounds[0]));
        }
        return declarations;
    }

    private static int RadioNoticeCallTarget(ReadOnlySpan<byte> code, int at)
    {
        var target = (long)at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code[(at + 1)..]);
        if (target < 0 || target >= code.Length) throw new InvalidDataException("Radio notice call has no backed executable target.");
        return (int)target;
    }

    private static void SameRadioNoticeTarget(ref int? existing, int target)
    {
        if (existing is { } retained && retained != target)
            throw new NotSupportedException("Radio notice branches have different source consumers.");
        existing = target;
    }
}
