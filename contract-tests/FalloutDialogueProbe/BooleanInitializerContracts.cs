using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class BooleanInitializerContracts
{
    internal static void Run()
    {
        var small = new byte[20];
        new byte[] { 0x55, 0x8b, 0xec, 0x6a, 1, 0x68 }.CopyTo(small, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(small.AsSpan(6), 0x100);
        small[10] = 0xb9; BinaryPrimitives.WriteUInt32LittleEndian(small.AsSpan(11), 0x300); small[15] = 0xe8;
        var full = new byte[23];
        new byte[] { 0x55, 0x8b, 0xec, 0x68 }.CopyTo(full, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(full.AsSpan(9), 0x100);
        full[8] = 0x68; full[13] = 0xb9; BinaryPrimitives.WriteUInt32LittleEndian(full.AsSpan(14), 0x300); full[18] = 0xe8;
        string? Literal(uint address) => address == 0x100 ? "bSourceFlag:SomeSection" : null;
        bool Object(uint address) => address == 0x300;
        Require(FalloutExecutableStringTable.ReadBooleanInitializers(small, Literal, Object)["bSourceFlag:SomeSection"] &&
            !FalloutExecutableStringTable.ReadBooleanInitializers(full, Literal, Object)["bSourceFlag:SomeSection"],
            "Owned Boolean name, section, or canonical payload was lost.");
        Require(FalloutExecutableStringTable.ReadBooleanInitializers(small.AsSpan(0, 19), Literal, Object).Count == 0 &&
            FalloutExecutableStringTable.ReadBooleanInitializers(full.AsSpan(0, 22), Literal, Object).Count == 0 &&
            FalloutExecutableStringTable.ReadBooleanInitializers(small, Literal, _ => false).Count == 0 &&
            FalloutExecutableStringTable.ReadBooleanInitializers(small, _ => "iOtherType", Object).Count == 0,
            "Truncated, foreign-type, or non-object Boolean initializer was admitted.");
        small[4] = 2; Reject(() => FalloutExecutableStringTable.ReadBooleanInitializers(small, Literal, Object));
        small[4] = 0xff; Reject(() => FalloutExecutableStringTable.ReadBooleanInitializers(small, Literal, Object));
        small[4] = 1;
        Require(FalloutExecutableStringTable.ReadBooleanInitializers(small, _ => "b30 Some Flag:Display", Object)["b30 Some Flag:Display"],
            "Owned Boolean names with numeric prefixes or spaces were artificially restricted.");
        Reject(() => FalloutExecutableStringTable.ReadBooleanInitializers(small.Concat(full).ToArray(), Literal, Object));
        var profile = FalloutInstallationSettings.ReadLayers([], [new("SomeSection", "bSourceFlag", "0")]);
        Require(!profile.Boolean("SomeSection", "bSourceFlag"), "Profile Boolean override was ignored.");
        Require(FalloutInstallationSettings.ReadLayers([], [new("SomeSection", "bSourceFlag", "-1")]).Boolean("SomeSection", "bSourceFlag"),
            "Numeric INI true was restricted to one spelling.");
        Reject(() => profile.Boolean("SomeSection", "bUnbound"));
        Reject(() => FalloutInstallationSettings.ReadLayers([], [new("SomeSection", "bSourceFlag", "invalid")]).Boolean("SomeSection", "bSourceFlag"));
        Console.WriteLine("OPENNV_BOOLEAN_DEFAULT_CONTRACT_PASS sourceNames=true bytePayload=true bounds=true ambiguity=true profile=true missingVisible=true");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FormatException) { return; }
        throw new InvalidOperationException("Invalid Boolean declaration was accepted.");
    }
}
