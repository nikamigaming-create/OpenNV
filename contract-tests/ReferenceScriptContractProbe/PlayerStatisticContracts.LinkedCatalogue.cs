using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static partial class PlayerStatisticContracts
{
    private static void LinkedCatalogueConstructor(bool challenge)
    {
        // Independent input makes the receiver displacement contain E8 and
        // gives the linked constructor an unequal literal table/count. This is
        // parsed by the real catalogue reader; no machine code is executed.
        const uint array = 0x2900, table = 0x3a00, unrelated = 0x3b00, format = 0x7c00;
        const int count = 7, consumer = 256, manager = 512, entry = 900;
        var code = new byte[1400];
        var command = new List<byte> { 0x55, 0x8b, 0xec, 0xff, 0x75, 0xf4, 0xff, 0x75, 0xf0 };
        Relative(command, 0, consumer); command.AddRange([0x5d, 0xc3]); command.ToArray().CopyTo(code, 0);
        var delta = new List<byte> { 0x55, 0x8b, 0xec, 0x83, 0xf9, count, 0x8b, 0x04, 0x85 };
        Word(delta, array);
        if (challenge) { delta.AddRange([0x6a, 11]); Relative(delta, consumer, 1200); }
        delta.Add(0x68); Word(delta, 1003); Relative(delta, consumer, 1200);
        delta.AddRange([0x5d, 0xc3]); delta.ToArray().CopyTo(code, consumer);

        var body = new List<byte> { 0x55, 0x8b, 0xec, 0x83, 0xff, count, 0x8b, 0x14, 0x8d };
        Word(body, unrelated); body.AddRange([0x50, 0x8b, 0x4d, 0xe8]); Relative(body, manager, 1250);
        body.AddRange([0x8b, 0x04, 0x85]); Word(body, table);
        var argument = manager + body.Count;
        body.AddRange([0x50, 0x8b, 0x4d, 0xe8]);
        var call = manager + body.Count;
        Relative(body, manager, entry);
        body.AddRange([0x89, 0x04, 0x85]); Word(body, array); body.AddRange([0x5d, 0xc3]);
        body.ToArray().CopyTo(code, manager);
        var linked = new List<byte> { 0x55, 0x8b, 0xec, 0x68 };
        Word(linked, format); linked.AddRange([0x5d, 0xc2, 0x0c, 0]); linked.ToArray().CopyTo(code, entry);
        code[1200] = 0x55; code[1201] = 0x8b; code[1202] = 0xec; code[1203] = 0x5d; code[1204] = 0xc3;
        code[1250] = 0x55; code[1251] = 0x8b; code[1252] = 0xec; code[1253] = 0x68;
        WordAt(1254, 0x7d00); code[1258] = 0x5d; code[1259] = 0xc3;
        var words = new byte[count * 4];
        var texts = new Dictionary<uint, string> { [format] = "sLinkedAuthored%02d", [0x7d00] = "sUnrelatedAuthored%02d" };
        for (var index = 0; index < count; ++index)
        {
            var pointer = checked((uint)(0x4800 + index * 32)); texts.Add(pointer, "Independent linked row " + index);
            BinaryPrimitives.WriteUInt32LittleEndian(words.AsSpan(index * 4), pointer);
        }
        FalloutMiscellaneousStatisticCatalogue Read() => FalloutExecutableStringTable.ReadMiscellaneousStatisticCatalogue(code, 0,
            challenge ? EventSource : DirectSource, (pointer, size) => pointer == array && size == words.Length,
            (pointer, size) => pointer == table && size == words.Length,
            (pointer, size) => pointer == table && size == words.Length ? words : throw new InvalidDataException("Authored catalogue extent differs."),
            pointer => texts.GetValueOrDefault(pointer));
        var actual = Read();
        Require(actual.Names.Count == count && actual.SettingFormat == texts[format] && actual.StatsMenuId == 1003,
            "Operand E8 was treated as an instruction, or unrelated table selected a false linked formatter.");

        var saved = code.ToArray();
        code[argument] = 0x51; Reject(() => Read()); saved.CopyTo(code, 0);
        code[argument + 2] = 0x45; Reject(() => Read()); saved.CopyTo(code, 0);
        code[argument + 3] = 4; Reject(() => Read()); saved.CopyTo(code, 0);
        WordAt(call + 1, unchecked((uint)(code.Length + 32 - call - 5))); Reject(() => Read()); saved.CopyTo(code, 0);
        code[entry] = 0x90; Reject(() => Read()); saved.CopyTo(code, 0);
        code = saved.Take(entry + 11).ToArray(); Reject(() => Read()); code = saved.ToArray();
        texts[format] = "sUnowned%q"; Reject(() => Read()); texts[format] = actual.SettingFormat;
        var wide = saved.Take(argument + 2).Concat(new byte[] { 0x8d, 0xe8, 0xff, 0xff, 0xff })
            .Concat(saved.Skip(argument + 4)).ToArray();
        // Relocation is independently authored, including the final return.
        var wideCall = call + 3;
        BinaryPrimitives.WriteInt32LittleEndian(wide.AsSpan(wideCall + 1), entry + 3 - wideCall - 5);
        code = wide;
        Require(Read().SettingFormat == actual.SettingFormat, "Negative disp32 receiver operand lost the linked catalogue constructor.");

        void WordAt(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(at), value);
    }
}
