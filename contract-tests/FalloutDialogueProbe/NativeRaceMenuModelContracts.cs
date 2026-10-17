using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class NativeRaceMenuModelContracts
{
    internal static void Run()
    {
        var code = Enumerable.Repeat((byte)0x90, 950).ToArray();
        var settings = new Dictionary<uint, string>();
        const string model = "Terminals/abcdefghijklmnopqr.nif";
        var strings = new Dictionary<uint, string> { [5000] = model, [6000] = "../unselected.nif" };
        void Bytes(int at, params byte[] value) => value.CopyTo(code, at);
        void Word(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(at), value);
        void Relative(int at, byte opcode, int target) { code[at] = opcode; Word(at + 1, target - at - 5); }
        Bytes(0, 0x6a, 0); Relative(2, 0xe8, 100); Bytes(7, 0x83, 0xc4, 4, 0x6a, 1); Relative(12, 0xe8, 100); Bytes(17, 0x83, 0xc4, 4, 0xb0, 1, 0xc3);
        Bytes(100, 0x55, 0x8b, 0xec, 0x5d); Relative(104, 0xe9, 160);
        Bytes(160, 0x55, 0x8b, 0xec, 0xff, 0x75, 8, 0x8b, 0x0d); Word(168, 7000); Relative(172, 0xe8, 240);
        Bytes(240, 0x55, 0x8b, 0xec);
        for (var index = 0; index < 16; index++)
        {
            var at = 280 + index * 8; var pointer = 1000 + index * 12;
            settings[(uint)pointer] = index == 0 ? "sRSMCustomize" : "sHeader" + index;
            code[at] = 0xa1; Word(at + 1, pointer + 4); Bytes(at + 5, 0x89, 0x45, unchecked((byte)(-100 + index * 4)));
        }
        // Mode values are source operands, independent of model names.
        Bytes(425, 0x8b, 0x45, 8); Bytes(430, 0x83, 0xe8, 6, 0x0f, 0x84); Word(435, 700 - 439);
        Bytes(439, 0x83, 0xe8, 1, 0x0f, 0x84); Word(444, 780 - 448);
        void Copy(int at, byte destination)
        {
            Bytes(at, 0x0f, 0x10, 0x05); Word(at + 3, 5000); Bytes(at + 7, 0x0f, 0x11, destination);
            Bytes(at + 10, 0x0f, 0x10, 0x05); Word(at + 13, 5016);
            Bytes(at + 17, 0x0f, 0x11, (byte)(destination + 0x40), 16, 0xa0); Word(at + 22, 5032);
            Bytes(at + 26, 0x88, (byte)(destination + 0x40), 32);
        }
        Copy(500, 7); Copy(550, 0);
        void Direct(int at, int pointer)
        {
            Bytes(at, 0x6a, 0, 0x68); Word(at + 3, pointer); Bytes(at + 7, 0x8b, 0x8d); Word(at + 9, -400);
            Bytes(at + 13, 0x81, 0xc1); Word(at + 15, 0x90); Relative(at + 19, 0xe8, 900);
        }
        Direct(700, 6000); Direct(780, 6000);
        Bytes(850, 0x8b, 0x4d, 0xf4, 0x64, 0x89, 0x0d, 0, 0, 0, 0); Bytes(900, 0x55, 0x8b, 0xec);
        string Read() => FalloutExecutableStringTable.ReadNativeRaceMenuModel(code, settings, address => strings.GetValueOrDefault(address), 0);
        Require(Read() == "meshes/" + model.ToLowerInvariant(), "Inline source copies must select only their actual command mode's model.");
        Word(513, 5017); Reject(() => Read()); Word(513, 5016);
        code[527] = 0x40; Reject(() => Read()); code[527] = 0x47;
        Word(522, 5033); Reject(() => Read()); Word(522, 5032);
        Word(105, 700 - 109); Reject(() => Read()); Word(105, 160 - 109);
        code[1] = 6; Reject(() => Read()); code[1] = 0;
        strings[5000] = "../" + new string('a', 25) + ".nif"; Reject(() => Read()); strings[5000] = model;
        code[850] = 0x90; Reject(() => Read()); code[850] = 0x8b;

        // The spilled-argument compiler mixes short and near equality jumps.
        Array.Fill(code, (byte)0x90, 425, 425);
        Bytes(430, 0x8b, 0x55, 8, 0x89, 0x95); Word(435, -200);
        Bytes(439, 0x83, 0xbd); Word(441, -200); Bytes(445, 6, 0x74, (byte)(500 - 448));
        Bytes(448, 0x83, 0xbd); Word(450, -200); Bytes(454, 7, 0x0f, 0x84); Word(457, 550 - 461);
        Relative(461, 0xe9, 640); Direct(500, 6000); Direct(550, 6000); Direct(640, 5000);
        Require(Read() == "meshes/" + model.ToLowerInvariant(), "Spilled source mode must choose the direct model consumer.");
        Word(450, -204); Reject(() => Read()); Word(450, -200);
        Word(462, 1000); Reject(() => Read()); Word(462, 640 - 466);
        settings[1000] = "sForeign"; Reject(() => Read()); settings[1000] = "sRSMCustomize";
        Console.WriteLine("OPENNV_NATIVE_RACE_MENU_MODEL_PASS commandModes=true argumentForwarding=true sourceBranches=true inlineExtent=true terminator=true directConsumer=true foreignModesIgnored=true malformedRefused=true");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid native creation model declaration was admitted.");
    }
}
