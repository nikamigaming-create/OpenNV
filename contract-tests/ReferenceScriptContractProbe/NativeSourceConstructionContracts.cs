using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class NativeSourceConstructionContracts
{
    internal static void Run()
    {
        var header = new byte[20];
        Require(FalloutNativePluginSourceDeclarations.Body([], header).Length == 0, "declared-empty absent body");
        Require(FalloutNativePluginSourceDeclarations.Body([new("SCDA", Array.Empty<byte>())], header).Length == 0, "genuine empty body");
        Refuse(() => FalloutNativePluginSourceDeclarations.Body([new("SCDA", Array.Empty<byte>()), new("SCDA", Array.Empty<byte>())], header), "duplicate body");
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 3);
        Refuse(() => FalloutNativePluginSourceDeclarations.Body([], header), "missing declared body");
        Refuse(() => FalloutNativePluginSourceDeclarations.Body([new("SCDA", new byte[2])], header), "mismatched body");
        // Declaring bytes and executing a VM are independent owners. This
        // deliberately unsupported instruction sequence remains source bytes.
        var unowned = new byte[] { 0xff, 0xf1, 0xff };
        Require(FalloutNativePluginSourceDeclarations.Body([new("SCDA", unowned)], header).AsSpan().SequenceEqual(unowned), "source publication must not execute/decode a program");
        Refuse(() => FalloutNativePluginSourceDeclarations.Body([], new byte[19]), "incomplete header");
        var positive = Reset([52, 56, 60]); var reordered = Reset([60, 52, 56]);
        foreach (var body in new[] { positive, reordered })
        {
            var clocks = Read(body); Require(clocks.Count == 3 && clocks.All(row => row.Value == 0), "source Float32 reset fields");
        }
        Refuse(() => Read(Reset([52, 56, 64])), "different field");
        Refuse(() => Read(Reset([52, 52, 60])), "duplicate field");
        Refuse(() => Read(positive[..^1]), "missing final extent");
        var branch = positive.ToArray(); branch[7] = 0x75; branch[8] = 0;
        Refuse(() => Read(branch), "control-flow prefix");
        var setter = Setter(); setter[15] = 5;
        Refuse(() => Read(positive, setter), "type setter changes another field");
        Console.WriteLine("OPENNV_NATIVE_SOURCE_CONSTRUCTION_CONTRACT_PASS");
    }
    private static IReadOnlyDictionary<int, uint> Read(byte[] body, byte[]? setter = null)
        => FalloutExecutableStringTable.ReadScriptResetClocks(body, 0x1000,
            (address, count) => address == 0x2000 && count == 22 ? setter ?? Setter() : throw new InvalidDataException("Authored source target changed."),
            address => address == 0x2000);
    private static byte[] Reset(IReadOnlyList<byte> fields)
    {
        var bytes = new List<byte> { 0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d, 0xfc };
        foreach (var field in fields) bytes.AddRange(new byte[] { 0x8b, 0x4d, 0xfc, 0xd9, 0xee, 0xd9, 0x59, field });
        bytes.AddRange(new byte[] { 0x6a, 17, 0x8b, 0x4d, 0xfc, 0xe8 });
        var target = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(target, checked(0x2000 - (0x1000 + bytes.Count + 4)));
        bytes.AddRange(target); bytes.AddRange(new byte[] { 0x8b, 0xe5, 0x5d, 0xc3 }); return bytes.ToArray();
    }
    private static byte[] Setter() => [0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d, 0xfc, 0x8b, 0x45, 0xfc, 0x8a, 0x4d, 8, 0x88, 0x48, 4, 0x8b, 0xe5, 0x5d, 0xc2, 4, 0];
    private static void Require(bool value, string label)
    { if (!value) throw new InvalidOperationException("Native source construction contract failed: " + label); }
    private static void Refuse(Action action, string label)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Native source construction contract unexpectedly admitted " + label);
    }
}
