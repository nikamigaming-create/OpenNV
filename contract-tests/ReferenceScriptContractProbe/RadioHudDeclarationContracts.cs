using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class RadioHudDeclarationContracts
{
    internal static void Run()
    {
        string? Literal(uint address) => address == 101 ? "UICustomRadioFound" : address == 202 ? "icons/changed-radio.dds" : null;
        var settings = new Dictionary<uint, string> { [300] = "sRadioStationDiscovered" };
        var source = Fixture();
        FalloutRadioHudDeclaration Read(byte[] code) => FalloutExecutableStringTable.ReadRadioHudDeclaration(code, Literal, settings,
            _ => throw new InvalidDataException("Immediate notice incorrectly read an x87 scalar."));
        var expected = new FalloutRadioHudDeclaration("icons/changed-radio.dds", 4.125f, "UICustomRadioFound");
        if (Read(source) != expected) throw new InvalidDataException("Optimized radio notice lost its source fields.");
        void Reject(Action<byte[]> mutate)
        {
            var changed = source.ToArray(); mutate(changed);
            try { _ = Read(changed); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
            throw new InvalidDataException("Radio notice admitted an independent source argument/consumer drift.");
        }
        Reject(code => code[64 + 23] = 0x7d); // Another register cannot replace the formatted text.
        Reject(code => code[64 + 24]--); // Different local buffer.
        Reject(code => code[64 + 43] = 1); // Nonzero queue argument.
        Reject(code => code[64 + 52] = 16); // Different stack extent.
        Reject(code => BinaryPrimitives.WriteSingleLittleEndian(code.AsSpan(64 + 31), float.NaN));
        Reject(code => BinaryPrimitives.WriteSingleLittleEndian(code.AsSpan(64 + 31), 0));
        Reject(code => BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(64 + 38), 101));
        Reject(code => BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(64 + 2), 305));
        Reject(code => BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(256 + 18), 920 - (256 + 22)));
        Reject(code => BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(256 + 46), int.MaxValue));
        Reject(code => code[256 + 58] = 16); // Scheduled load retains the five-argument cleanup.
        Reject(code => code[48] = 0); // The other valid row cannot hide this missing source sound.
        Reject(code => code[240] = 0); // Independently retain the scheduled branch denominator.
        var missing = source.ToArray(); missing[48] = missing[240] = 0;
        try { _ = Read(missing); throw new InvalidDataException("Radio notice without its source sound was admitted."); }
        catch (NotSupportedException) { }
        Console.WriteLine("OPENNV_RADIO_HUD_DECLARATION_CONTRACT_PASS directSetting=true immediateFloat=true scheduledLoad=true " +
            "sourceFields=true sharedConsumers=true localDriftRefused=true argumentDriftRefused=true invalidDurationRefused=true " +
            "sourceDriftRefused=true consumerDriftRefused=true absentSoundRefused=true oneBranchAbsentSoundRefused=true");
    }

    private static byte[] Fixture()
    {
        var code = new byte[1024];
        void Row(int at, bool scheduled)
        {
            code[at - 16] = 0x68; BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(at - 15), 101);
            var row = new byte[59];
            row[0] = 0x8b; row[1] = 0x35; BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(2), 304);
            row[6] = 0xe8;
            new byte[] { 0x50, 0x8d, 0x45, 0xa0, 0x56, 0x50, 0xe8 }.CopyTo(row, 11);
            new byte[] { 0x8b, 0x75, 0xa0, 0x83, 0xc4, 8, 0xc7, 4, 0x24 }.CopyTo(row, 22);
            BinaryPrimitives.WriteSingleLittleEndian(row.AsSpan(31), 4.125f);
            new byte[] { 0x6a, 0, 0x68 }.CopyTo(row, 35); BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(38), 202);
            new byte[] { 0x6a, 0, 0x56, 0xe8 }.CopyTo(row, 42);
            foreach (var (call, target) in new[] { (6, 900), (17, 904), (45, 908) })
                BinaryPrimitives.WriteInt32LittleEndian(row.AsSpan(call + 1), target - (at + call + 5));
            if (scheduled) { row[50] = 0x8a; row[51] = 0x0d; BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(52), 808); }
            new byte[] { 0x83, 0xc4, 20 }.CopyTo(row, scheduled ? 56 : 50);
            row.CopyTo(code, at);
        }
        Row(64, false); Row(256, true);
        return code;
    }
}
