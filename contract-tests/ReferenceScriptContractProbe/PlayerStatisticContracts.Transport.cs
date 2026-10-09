using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static partial class PlayerStatisticContracts
{
    // These are independently authored x86 compiler envelopes and arbitrary
    // literal/data identities. They are parsed, never executed or retail bytes.
    private static void TransportCatalogue(bool challenge)
    {
        var count = challenge ? 3 : 5;
        const uint array = 0x2200, table = 0x3300, format = 0x7700;
        var code = new byte[1024];
        var command = new List<byte> { 0x55, 0x8b, 0xec, 0xff, 0x75, 0xf4, 0xff, 0x75, 0xf0 };
        Relative(command, 0, 256); command.AddRange([0x5d, 0xc3]); command.ToArray().CopyTo(code, 0);
        var delta = new List<byte> { 0x55, 0x8b, 0xec, 0x83, 0xf9, (byte)count, 0x8b, 0x04, 0x85 };
        Word(delta, array);
        if (challenge) { delta.AddRange([0x6a, 11]); Relative(delta, 256, 800); }
        delta.Add(0x68); Word(delta, 1003); Relative(delta, 256, 800); delta.AddRange([0x5d, 0xc3]);
        delta.ToArray().CopyTo(code, 256);
        var constructor = new List<byte> { 0x55, 0x8b, 0xec, 0x83, 0xff, (byte)count, 0x68 };
        Word(constructor, format); constructor.AddRange([0x8b, 0x14, 0x8d]); Word(constructor, table);
        constructor.AddRange([0x89, 0x14, 0x8d]); Word(constructor, array); constructor.AddRange([0x5d, 0xc3]);
        constructor.ToArray().CopyTo(code, 512);
        code[800] = 0x55; code[801] = 0x8b; code[802] = 0xec; code[803] = 0x5d; code[804] = 0xc3;
        var words = new byte[count * 4]; var texts = new Dictionary<uint, string> { [format] = "sFixtureStatistic%02d" };
        for (var index = 0; index < count; index++)
        {
            var pointer = (uint)(0x4400 + index * 256); texts.Add(pointer, "Independent source counter " + index);
            BinaryPrimitives.WriteUInt32LittleEndian(words.AsSpan(index * 4), pointer);
        }
        FalloutMiscellaneousStatisticCatalogue Read() => FalloutExecutableStringTable.ReadMiscellaneousStatisticCatalogue(code, 0,
            challenge ? EventSource : DirectSource, (pointer, size) => pointer == array && size == count * 4,
            (pointer, size) => pointer == table && size == words.Length,
            (pointer, size) => pointer == table && size == words.Length ? words : throw new InvalidDataException("Authored data extent differs."),
            pointer => texts.GetValueOrDefault(pointer));
        var read = Read();
        Require(read.Names.Count == count && read.Names[0] == texts[0x4400] && read.StatsMenuId == 1003 &&
            FalloutMiscellaneousStatisticSource.SettingName(read.SettingFormat, 1) == "sFixtureStatistic01",
            "Selected source transport replaced unequal counts, literal order, setting association or menu selector.");
        var saved = words.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(words.AsSpan(4), 0x4400);
        Reject(() => Read()); saved.CopyTo(words, 0);
        texts[format] = "sFixture%q"; Reject(() => Read()); texts[format] = read.SettingFormat;
        var bodyCount = code[261]; code[261] = (byte)(count + 1); Reject(() => Read()); code[261] = bodyCount;
        Reject(() => FalloutExecutableStringTable.ReadMiscellaneousStatisticCatalogue(code, 0, new('0', 64),
            (_, _) => true, (_, _) => true, (_, _) => words, pointer => texts.GetValueOrDefault(pointer)));
    }
    private static void Word(List<byte> output, uint value)
    {
        var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); output.AddRange(bytes);
    }
    private static void Relative(List<byte> output, int entry, int target)
    {
        var call = entry + output.Count; output.Add(0xe8); Word(output, unchecked((uint)(target - call - 5)));
    }
}
