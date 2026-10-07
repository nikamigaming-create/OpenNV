using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class CreationAgeControlContracts
{
    internal static void Run()
    {
        var code = Enumerable.Repeat((byte)0x90, 1000).ToArray();
        var settings = new Dictionary<uint, string>();
        void Bytes(int at, params byte[] value) => value.CopyTo(code, at);
        void Word(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(at), value);
        void Call(int at, int target) { code[at] = 0xe8; Word(at + 1, target - at - 5); }
        Bytes(0, 0x55, 0x8b, 0xec);
        for (var index = 0; index < 16; index++)
        {
            var at = 32 + index * 13; var pointer = 1000 + index * 12;
            settings[(uint)pointer] = index == 0 ? "sRSMCustomize" : "sHeader" + index;
            code[at] = 0xb9; Word(at + 1, pointer); Call(at + 5, 900);
            Bytes(at + 10, 0x89, 0x45, unchecked((byte)(-100 + index * 4)));
        }
        Bytes(450, 0x8b, 0x4d, 0xf4, 0x64, 0x89, 0x0d, 0, 0, 0, 0);
        settings[6000] = "sAge";
        Require(FalloutExecutableStringTable.ReadCreationAgeControl(code, settings) is null,
            "A generic age string cannot add an undeclared creation slider.");
        settings[5000] = "sRSMAge";
        Reject(() => FalloutExecutableStringTable.ReadCreationAgeControl(code, settings));
        settings[7000] = "sRSMRandomize";
        code[250] = 0xb9; Word(251, 7000); Call(255, 900); code[260] = 0x50;
        code[300] = 0x68; Word(301, unchecked((int)0x80000001)); Bytes(305, 0x6a, 14, 0x6a, 2, 0x6a, 26, 0xb9);
        Word(312, 5000); Call(316, 900); Bytes(321, 0x50, 0x8b, 0x95); Word(324, -300);
        Bytes(328, 0x8b, 0x4a, 0x30); Call(331, 920); Bytes(336, 0x89, 0x85); Word(338, -200);
        var age = FalloutExecutableStringTable.ReadCreationAgeControl(code, settings);
        Require(age is { Setting: "sRSMAge", Minimum: 2, Maximum: 14 },
            "The admitted slider must retain the original label and interval operands.");
        Call(316, 901); Reject(() => FalloutExecutableStringTable.ReadCreationAgeControl(code, settings)); Call(316, 900);
        code[308] = 14; Reject(() => FalloutExecutableStringTable.ReadCreationAgeControl(code, settings)); code[308] = 2;
        code[330] = 0x34; Reject(() => FalloutExecutableStringTable.ReadCreationAgeControl(code, settings)); code[330] = 0x30;
        settings[5010] = "sRSMAge"; Reject(() => FalloutExecutableStringTable.ReadCreationAgeControl(code, settings)); settings.Remove(5010);
        code[450] = 0x90; Reject(() => FalloutExecutableStringTable.ReadCreationAgeControl(code, settings));
        Console.WriteLine("OPENNV_CREATION_AGE_CONTROL_PASS sourcePresence=true genericAgeIgnored=true sourceInterval=true getterAssociation=true malformedRefused=true");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid creation age declaration was admitted.");
    }
}
