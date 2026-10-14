using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static partial class ConditionGlobalContracts
{
    private const string MasterA = "ConditionA.esm", MasterB = "ConditionB.esm", PatchName = "ConditionPatch.esp";
    private const ushort GlobalValueFunction = 74, UnknownFunction = 0x7fff;
    private const uint FunctionRaw = 0x0000_0900, ComparisonRaw = 0x0100_0900;
    private const uint NullRaw = 0, MissingRaw = 0x0200_0999, NonGlobalRaw = 0x0200_0a00,
        DeletedRaw = 0x0000_0901, UndeclaredRaw = 0x0300_0900;
    private static readonly string[] InputOrder = [MasterA, MasterB, PatchName];
    private static readonly FalloutFormKey ComparisonGlobal = new(MasterA, 0x900), FunctionGlobal = new(MasterB, 0x900),
        Quest = new(PatchName, 0xa00), Info = new(PatchName, 0xb00), Message = new(PatchName, 0xb01), Speaker = new(PatchName, 0xc00),
        Package = new(PatchName, 0xd00), FallbackPackage = new(PatchName, 0xd01), Idle = new(PatchName, 0xe00);

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = System.IO.Directory.CreateTempSubdirectory("opennv-condition-global-");
        internal string Directory => _directory.FullName;
        internal FalloutPluginStack Records { get; }
        internal Fixture()
        {
            try
            {
                File.WriteAllBytes(Path.Combine(Directory, MasterA), Join(Header(), Global(0x900, 3)));
                File.WriteAllBytes(Path.Combine(Directory, MasterB), Join(Header(), Global(0x900, 7), Global(0x901, 1)));
                File.WriteAllBytes(Path.Combine(Directory, PatchName), Join(
                    Header(MasterB, MasterA), Global(ComparisonRaw, 5), RecordFlags("GLOB", DeletedRaw, FalloutPluginRecord.DeletedFlag),
                    Record("QUST", 0x0200_0a00, Field("DATA", [1, 50]), Field("CTDA", GlobalBytes()),
                        Field("INDX", BitConverter.GetBytes((short)1)), Field("QSDT", [0]), Field("CTDA", GlobalBytes()),
                        Field("SCTX", Text("set-comparison")), Field("QSDT", [0]), Field("CTDA", GlobalBytes()),
                        Field("SCTX", Text("stale")), Field("QSDT", [0]), Field("SCTX", Text("after"))),
                    Record("INFO", 0x0200_0b00, Field("CTDA", GlobalBytes())),
                    Record("MESG", 0x0200_0b01, Field("FULL", Text("Authored title")), Field("DESC", Text("Authored message")),
                        Field("INAM", U32(0)), Field("DNAM", U32(1)), Field("ITXT", Text("Conditional")),
                        Field("CTDA", GlobalBytes()), Field("ITXT", Text("Always"))),
                    Record("NPC_", 0x0200_0c00, Field("ACBS", new byte[24]), Field("PKID", U32(0x0200_0d00)), Field("PKID", U32(0x0200_0d01))),
                    Record("PACK", 0x0200_0d00, Field("PSDT", [255, 255, 0, 255, 0, 0, 0, 0]), Field("CTDA", GlobalBytes())),
                    Record("PACK", 0x0200_0d01, Field("PSDT", [255, 255, 0, 255, 0, 0, 0, 0])),
                    Record("IDLE", 0x0200_0e00, Field("ANAM", new byte[8]), Field("CTDA", GlobalBytes()))));
                Records = FalloutPluginStack.Load(Directory, InputOrder);
            }
            catch { _directory.Delete(true); throw; }
        }
        public void Dispose() { Records.Dispose(); _directory.Delete(true); }
    }

    private static byte[] Header(params string[] masters) => Record("TES4", 0,
        new[] { Field("HEDR", new byte[12]) }.Concat(masters.SelectMany(name =>
            new[] { Field("MAST", Text(name)), Field("DATA", new byte[8]) })).ToArray());
    private static byte[] Global(uint raw, float value) => Record("GLOB", raw,
        Field("FNAM", [(byte)'l']), Field("FLTV", BitConverter.GetBytes(value)));
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint raw, params byte[][] fields) => RecordFlags(name, raw, 0, fields);
    private static byte[] RecordFlags(string name, uint raw, uint flags, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        U32((uint)data.Length).CopyTo(bytes, 4); U32(flags).CopyTo(bytes, 8); U32(raw).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
}
