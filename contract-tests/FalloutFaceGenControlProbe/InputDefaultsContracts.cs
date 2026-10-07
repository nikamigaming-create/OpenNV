using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class InputDefaultsContracts
{
    internal static void Run()
    {
        var code = Enumerable.Repeat((byte)0x90, 1500).ToArray();
        var names = Enumerable.Range(0, 28).Select(index => index == 0 ? "Forward" : index == 27 ? "Grab" : "Control " + index).ToArray();
        var literals = names.Select((name, index) => (name, address: 0x80000u + (uint)index * 32))
            .ToDictionary(row => row.address, row => row.name); literals[0x90000] = "Controls";
        var table = names.SelectMany((_, index) => BitConverter.GetBytes(0x80000u + (uint)index * 32)).ToArray();
        void Write(int at, params byte[] bytes) => bytes.CopyTo(code, at);
        void Word(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(at), value);
        Write(150, 0x8b, 0x2d); Word(152, 0xa0000);
        Write(156, 0x81, 0xc6); Word(158, 0x480);
        code[162] = 0xbf; Word(163, 0x40000); code[167] = 0xbb; Word(168, 28);
        Write(198, 0xff, 0x37, 0x68); Word(201, 0x90000); Write(205, 0xff, 0xd5, 0x85, 0xc0);
        Write(220, 0x8b, 0xc8, 0x88, 0x86, 0x38, 0, 0, 0, 0xc1, 0xe9, 0x10,
            0x83, 0xc4, 0x0c, 0x88, 0x4e, 0xe4, 0x8b, 0xc8, 0xc1, 0xe9, 8, 0x88, 0x0e);
        Write(600, 0x8d, 0x86); Word(602, 0x480); code[606] = 0xb9; Word(607, 28);
        Write(615, 0xc6, 0x40, 0xe4, 0xff, 0x8d, 0x40, 1, 0xc6, 0x40, 0xff, 0xff,
            0xc6, 0x40, 0x1b, 0xff, 0xc6, 0x40, 0x37, 0xff, 0x83, 0xe9, 1, 0x75, 0xe8);
        Write(639, 0xf6, 0x46, 4, 1, 0xc7, 0x86); Word(645, 0x464); Word(649, 0x201e1f11);
        Write(653, 0x66, 0xc7, 0x86); Word(656, 0x47d); Write(660, 0x43, 0x2c);
        Write(662, 0xc6, 0x86); Word(664, 0x47f); code[668] = 44;
        Write(669, 0x88, 0x8e); Word(671, 0x484);
        Write(675, 0xc6, 0x86); Word(677, 0x486); code[681] = 1;
        Write(682, 0x74, 12);
        IReadOnlyDictionary<string, (byte Keyboard, byte Mouse)> Read(byte[] bytes) =>
            FalloutExecutableStringTable.ReadControlDefaults(bytes, address => literals.GetValueOrDefault(address),
                (address, size) => address == 0x40000 && size == 112 ? table : throw new InvalidDataException("Unexpected table extent."),
                address => address == 0xa0000 ? "GetPrivateProfileStringA" : null);
        var defaults = Read(code);
        Require(defaults.Count == 28 && defaults["Forward"] == (17, 255) && defaults["Control 1"] == (31, 255) &&
            defaults["Control 4"] == (255, 0) && defaults["Control 6"] == (255, 1) && defaults["Grab"] == (44, 255),
            "Source input default bytes, sparse sentinel or lane order changed.");
        var shortCode = code.ToArray(); shortCode.AsSpan(220, 24).Fill(0x90);
        new byte[] { 0x8b, 0xc8, 0x88, 0x46, 0x38, 0xc1, 0xe9, 0x10,
            0x83, 0xc4, 0x0c, 0x88, 0x4e, 0xe4, 0x8b, 0xc8, 0xc1, 0xe9, 8, 0x88, 0x0e }.CopyTo(shortCode, 220);
        Require(Read(shortCode).SequenceEqual(defaults), "Short profile displacement changed the control byte lanes.");
        void Reject(Action action)
        {
            try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
            throw new InvalidDataException("Malformed input source was admitted.");
        }
        var invalid = code.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(168), 27); Reject(() => Read(invalid));
        invalid = code.ToArray(); invalid[233] = 15; Reject(() => Read(invalid));
        invalid = code.ToArray(); invalid[637] = 0x74; Reject(() => Read(invalid));
        invalid = code.ToArray(); code.AsSpan(600, 84).CopyTo(invalid.AsSpan(950)); Reject(() => Read(invalid));
        Console.WriteLine("OPENNV_INPUT_DEFAULTS_CONTRACT_PASS sourceNames=true sourceBytes=true sparse=true byteLanes=true malformedRefused=true ambiguousRefused=true");
    }
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
}
