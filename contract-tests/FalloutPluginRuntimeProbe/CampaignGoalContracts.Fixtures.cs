using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static partial class CampaignGoalContracts
{
    private const string BasePlugin = "Base.esm";
    private const string OtherPlugin = "Other.esm";
    private const string OverridePlugin = "Override.esp";
    private static FalloutFormKey Key(uint id) => new(BasePlugin, id);

    private static void WithPlugins((string Name, byte[] Bytes)[] plugins, Action<FalloutPluginStack> inspect)
    {
        var directory = Path.Combine("contract-tests", "generated", "campaign-goals-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var plugin in plugins) File.WriteAllBytes(Path.Combine(directory, plugin.Name), plugin.Bytes);
            using var records = FalloutPluginStack.Load(directory, plugins.Select(plugin => plugin.Name).ToArray());
            inspect(records);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static (string Name, byte[] Bytes)[] Layers(byte[]? winningQuest = null) =>
    [
        (OtherPlugin, Join(Header(), Record("DOOR", 0x100), Cell(0x500),
            Group(0x500, Reference(0x900, 0x100)))),
        (BasePlugin, Join(Header(), World(), Quest(
            Field("QOBJ", BitConverter.GetBytes(17u)), Field("NNAM", Text("Losing description")),
            Field("QSTA", Target(0x230))))),
        (OverridePlugin, Join(Header(BasePlugin, OtherPlugin), winningQuest ?? Quest(
            Field("CTDA", Condition(28, 14)),
            Field("INDX", BitConverter.GetBytes((short)10)), Field("QSDT", [0]),
            Field("CTDA", Condition(24, 524)),
            Field("QOBJ", BitConverter.GetBytes(17u)), Field("NNAM", Text("Winning description")),
            Field("QSTA", Target(0x210, 1, [0xa1, 0xb2, 0xc3])),
            Field("CTDA", Condition(20, 1, 0x0100_0900, flags: 1)),
            Field("CTDA", Condition(24, 524, runOn: 2)),
            Field("QSTA", Target(0x210)),
            Field("CTDA", Condition(28, 1, 0x211, runOn: 2, reference: 0x0100_0900)),
            Field("QOBJ", BitConverter.GetBytes(3u)), Field("NNAM", Text("Second objective")),
            Field("QSTA", Target(0x0200_0600))),
            Group(0x300, Reference(0x210, 0x100, teleport: 0x220, flags: 0x800,
                extra: Join(Field("XESP", Join(BitConverter.GetBytes(0x250u), BitConverter.GetBytes(1u))),
                    Field("XLOC", new byte[20]))),
                Record("REFR", 0x212, 0x20)),
            Group(0x301, Reference(0x220, 0x100)),
            Group(0x302, Record("ACHR", 0x0200_0600, 0, Field("NAME", BitConverter.GetBytes(0x102u)),
                Field("DATA", new byte[24])))))
    ];

    private static byte[] World() => Join(
        Record("DOOR", 0x100), Record("STAT", 0x101), Record("NPC_", 0x102),
        Cell(0x300), Group(0x300,
            Reference(0x210, 0x100, teleport: 0x230),
            Reference(0x211, 0x100, teleport: 0x240),
            Reference(0x212, 0x100, teleport: 0x230),
            Reference(0x250, 0x101, flags: 0x800)),
        Cell(0x301), Group(0x301, Reference(0x221, 0x100, teleport: 0x230),
            Reference(0x222, 0x100, teleport: 0x210)),
        Cell(0x302), Group(0x302, Reference(0x230, 0x100)),
        Cell(0x303), Group(0x303, Reference(0x220, 0x100), Reference(0x240, 0x100),
            Reference(0x241, 0x100, teleport: 0x230)),
        Cell(0x304));

    private static byte[] Quest(params byte[][] fields) => Record("QUST", 0x400, 0,
        [Field("EDID", Text("SourceCampaign")), .. fields]);

    private static byte[] PortalFixture(byte[] source, byte[]? destination = null, byte[]? cells = null) => Join(
        Header(), Record("DOOR", 0x100), Record("STAT", 0x101),
        cells ?? Cell(0x300), Group(0x300, source),
        Cell(0x301), Group(0x301, destination ?? Reference(0x220, 0x100)));

    private static byte[] Reference(uint id, uint basis, uint? teleport = null, uint flags = 0, byte[]? extra = null) =>
        Record("REFR", id, flags,
            Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[24]),
            teleport is { } target ? Field("XTEL", Teleport(target)) : [],
            extra ?? []);

    private static byte[] Target(uint id, byte flags = 0, byte[]? unused = null) =>
        Join(BitConverter.GetBytes(id), [flags], unused ?? new byte[3]);

    private static byte[] Teleport(uint id, uint flags = 0)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, id);
        for (var index = 0; index < 6; index++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4 + index * 4), index * 1.25f - 2.5f);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), flags);
        return bytes;
    }

    private static byte[] Condition(int extent, ushort function, uint argument = 0, uint runOn = 0,
        uint reference = 0, byte flags = 0)
    {
        var bytes = new byte[extent];
        bytes[0] = flags;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), function);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), argument);
        if (extent >= 24) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), runOn);
        if (extent >= 28) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), reference);
        return bytes;
    }

    private static byte[] Header(params string[] masters)
    {
        var data = new byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(data, 1.34f);
        return Record("TES4", 0, 0, [Field("HEDR", data), .. masters.SelectMany(master => new[]
        {
            Field("MAST", Text(master)), Field("DATA", new byte[8])
        })]);
    }

    private static byte[] Cell(uint id) => Record("CELL", id, 0, Field("DATA", [1]));
    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();

    private static byte[] Field(string signature, byte[] payload)
    {
        var bytes = new byte[6 + payload.Length];
        Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)payload.Length));
        payload.CopyTo(bytes, 6);
        return bytes;
    }

    private static byte[] Record(string signature, uint id, uint flags = 0, params byte[][] fields)
    {
        var payload = Join(fields);
        var bytes = new byte[24 + payload.Length];
        Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 15);
        payload.CopyTo(bytes, 24);
        return bytes;
    }

    private static byte[] Group(uint cell, params byte[][] records)
    {
        var payload = Join(records);
        var bytes = new byte[24 + payload.Length];
        Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), cell);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 6);
        payload.CopyTo(bytes, 24);
        return bytes;
    }
}
