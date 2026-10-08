using System.Buffers.Binary;
using System.Text;

internal static partial class QuestGraphAuditContracts
{
    private static byte[] SourceFixture() => Join(Header(),
        Record("SCPT", 0x100, 0, Field("SCHR", ScriptHeader()), Field("SCTX", Text(
            "begin GameMode\nif 0\nUnknownInactive\nelseif 1\nUnknownOtherInactive\nelse\nreturn\nendif\nwhile 0\nUnknownLoop\nloop\nend"))),
        Record("INFO", 0x101, 0, Field("SCHR", ScriptHeader())),
        Record("INFO", 0x102, 0, Field("SCHR", ScriptHeader(2)), Field("SCDA", [0xee, 0xff])),
        Record("FUTR", 0x103, 0, Field("SCDA", [0xab]), Field("SCTX", Text("UnknownOrphan"))),
        Record("PACK", 0x104, 0, Field("POBA", []), Field("SCHR", ScriptHeader()), Field("SCTX", Text("return")),
            Field("POEA", []), Field("SCDA", [1]), Field("SCTX", Text("UnknownAlternative"))),
        Record("INFO", 0x105, 0, Field("SCHR", ScriptHeader()), Field("SCTX", Text("UnknownRepeatedOne")), Field("SCTX", Text("UnknownRepeatedTwo"))),
        Record("INFO", 0x106, 0, Field("SCHR", ScriptHeader()), Field("SCTX", Text("if 0\nreturn"))),
        Record("INFO", 0x107, 0, Field("SCHR", ScriptHeader()), Field("SCDA", []), Field("CTDA", [1])),
        Record("SCPT", 0x108, 0, Field("SCHR", ScriptHeader()), Field("SCTX", Text("begin GameMode\nUnknownObsolete\nend"))),
        Record("QUST", 0x109, 0, Field("INDX", [10, 0]), Field("QSDT", [0]), Field("SCHR", ScriptHeader()),
            Field("SCTX", Text("UnknownStageFirst")), Field("QSDT", [0]), Field("SCTX", Text("UnknownStageSecond"))));

    private static byte[] WinnerFixture() => Join(Header("Source.esm"),
        Record("SCPT", 0x108, 0x20, Field("SCHR", ScriptHeader()), Field("SCTX", Text("begin GameMode\nUnknownDeletedWinner\nend"))));
    private static byte[] ScriptHeader(uint bytes = 0)
    {
        var result = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), bytes); return result;
    }
    private static byte[] Header(string? master = null)
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        return master is null ? Record("TES4", 0, 1, Field("HEDR", header)) :
            Record("TES4", 0, 0, Field("HEDR", header), Field("MAST", Text(master)), Field("DATA", new byte[8]));
    }
    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value + "\0");
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string signature, byte[] payload)
    {
        var result = new byte[6 + payload.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)payload.Length)); payload.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string signature, uint id, uint flags, params byte[][] fields)
    {
        var payload = Join(fields); var result = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)payload.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(20), 15);
        payload.CopyTo(result, 24); return result;
    }
}
