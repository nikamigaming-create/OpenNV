using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static class StaticExecutableSettingsContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-static-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var origin in new uint[] { 0x230000, 0x670000 })
            {
                var image = new Fixture(origin);
                var rows = FalloutExecutableStringTable.ReadStaticSettings(image.Bytes);
                Require(rows.Count == 8 && rows.Single(row => row.Name == "iSynthetic").Payload == unchecked((uint)-47),
                    "Static descriptors lost their relocated integer payloads.");
                var path = Path.Combine(directory, "synthetic.exe");
                File.WriteAllBytes(path, image.Bytes);
                Require(FalloutExecutableStringTable.Read(path)["sSynthetic"] == "Synthetic\nvalue" &&
                    FalloutExecutableStringTable.ReadFloatDefaults(path, gameSettingsOnly: true)["fSynthetic"] == 2.5f &&
                    FalloutExecutableStringTable.ReadIntegerDefaults(path, gameSettingsOnly: true)["iSynthetic"] == unchecked((uint)-47) &&
                    FalloutExecutableStringTable.ReadBooleanDefaults(path)["bSynthetic:General"],
                    "Static typed defaults lost their source values.");
                Require(FalloutExecutableStringTable.ReadFloatDefaults(path, gameSettingsOnly: true).Count == 1,
                    "INI float was admitted as a game setting.");
                var ini = FalloutExecutableStringTable.ReadIniDeclarations(image.Bytes);
                Require(ini.Count == 4 && ini.Single(row => row.Name == "fPreference:General").Collection == FalloutIniCollection.Prefs &&
                    ini.Single(row => row.Name == "fRenderer:Display").Collection == FalloutIniCollection.Renderer,
                    "Static INI ownership was inferred from the section instead of its declared collection.");

                var duplicate = new Fixture(origin); duplicate.Row(8, "fsynthetic", 0x40000000, 0);
                Reject(() => FalloutExecutableStringTable.ReadStaticSettings(duplicate.Bytes));
                var nonfinite = new Fixture(origin); nonfinite.Write(0xc10, 0x7fc00000);
                Reject(() => FalloutExecutableStringTable.ReadStaticSettings(nonfinite.Bytes));
                var boolean = new Fixture(origin); boolean.Write(0xc28, 2);
                Reject(() => FalloutExecutableStringTable.ReadStaticSettings(boolean.Bytes));
                var equalNames = new Fixture(origin); equalNames.Row(8, "fSetting:General", 0x40800000, 2);
                Require(FalloutExecutableStringTable.ReadIniDeclarations(equalNames.Bytes).Count == 5,
                    "Equal names in independent INI collections were rejected.");

                var invalidVtable = new Fixture(origin); invalidVtable.Write(0x440, origin + 0x3000);
                Require(FalloutExecutableStringTable.ReadStaticSettings(invalidVtable.Bytes).All(row => row.Collection != FalloutExecutableStringTable.SettingCollection.Game),
                    "A writable non-code pointer became an admitted virtual method.");
                var invalidLocator = new Fixture(origin); invalidLocator.Write(0x500, 1);
                Require(FalloutExecutableStringTable.ReadStaticSettings(invalidLocator.Bytes).All(row => row.Collection != FalloutExecutableStringTable.SettingCollection.Game),
                    "An unsupported locator version was admitted.");
                var missingType = new Fixture(origin); missingType.Write(0x50c, origin + 0x3ff8);
                Require(FalloutExecutableStringTable.ReadStaticSettings(missingType.Bytes).All(row => row.Collection != FalloutExecutableStringTable.SettingCollection.Game),
                    "An unbacked type descriptor was admitted.");
                var readonlyRows = new Fixture(origin); readonlyRows.Write(0x1ec, 0x40000040);
                Require(FalloutExecutableStringTable.ReadStaticSettings(readonlyRows.Bytes).Count == 0,
                    "A read-only table became mutable setting storage.");
                var virtualOnly = new Fixture(origin); virtualOnly.Write(0x1d0, 8);
                Require(FalloutExecutableStringTable.ReadStaticSettings(virtualOnly.Bytes).Count == 0,
                    "A partial descriptor escaped its virtual extent.");
                var badString = new Fixture(origin); badString.Write(0xc04, origin + 0x3100);
                File.WriteAllBytes(path, badString.Bytes);
                Reject(() => FalloutExecutableStringTable.Read(path));
                var truncated = image.Bytes[..0xc08];
                Reject(() => FalloutExecutableStringTable.ReadStaticSettings(truncated));
                var wrongMachine = new Fixture(origin); wrongMachine.Short(0x84, 0x8664);
                Reject(() => FalloutExecutableStringTable.ReadStaticSettings(wrongMachine.Bytes));

                // A constructor-owned default remains authoritative even if
                // its pre-construction storage contains a different value.
                image.EmitFloatConstructor("fConstructed", 7.5f);
                File.WriteAllBytes(path, image.Bytes);
                var floats = FalloutExecutableStringTable.ReadFloatDefaults(path);
                Require(floats.Count == 1 && floats["fConstructed"] == 7.5f,
                    "Static storage replaced an admitted constructor default.");
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("OPENNV_STATIC_EXECUTABLE_SETTINGS_PASS relocated=true typedCollections=true malformedRejected=true constructorPrecedence=true synthetic=true");
    }

    private sealed class Fixture
    {
        internal byte[] Bytes { get; } = new byte[0x1600];
        private readonly uint _origin;
        private int _literal = 0x740;
        internal Fixture(uint origin)
        {
            _origin = origin;
            Short(0, 0x5a4d); Write(0x3c, 0x80); Write(0x80, 0x4550);
            Short(0x84, 0x14c); Short(0x86, 3); Short(0x94, 0xe0); Short(0x96, 0x102);
            Short(0x98, 0x10b); Write(0xb4, origin); Write(0xb8, 0x1000); Write(0xbc, 0x200);
            Write(0xd0, 0x4000); Write(0xd4, 0x200); Short(0xdc, 3); Write(0xf4, 16);
            Section(0x178, ".text", 0x1000, 0x200, 0x200, 0x60000020);
            Section(0x1a0, ".rdata", 0x2000, 0x400, 0x800, 0x40000040);
            Section(0x1c8, ".data", 0x3000, 0xc00, 0xa00, 0xc0000040);
            Bytes[0x200] = 0xc3; Bytes[0x201] = 0xc3;
            string[] families = ["Game", "INI", "INIPref", "Renderer", "Blend", "Reg"];
            for (var index = 0; index < families.Length; ++index)
            {
                var vtable = 0x440 + index * 16; var locator = 0x500 + index * 24; var type = 0x1000 + index * 80;
                Write(vtable - 4, Address(locator)); Write(vtable, origin + 0x1000); Write(vtable + 4, origin + 0x1001);
                Write(locator + 12, Address(type));
                Encoding.ASCII.GetBytes(".?AV?$SettingT@V" + families[index] + "SettingCollection@@@@\0").CopyTo(Bytes, type + 8);
            }
            Row(0, "sSynthetic", Literal("Synthetic\nvalue"), 0);
            Row(1, "fSynthetic", 0x40200000, 0);
            Row(2, "iSynthetic", unchecked((uint)-47), 0);
            Row(3, "bSynthetic:General", 1, 1);
            Row(4, "fSetting:General", 0x40400000, 1);
            Row(5, "fPreference:General", 0x40800000, 2);
            Row(6, "fRenderer:Display", 0x40a00000, 3);
            Row(7, "iRegistry", 9, 5);
        }
        internal void Row(int index, string name, uint payload, int family)
        {
            var at = 0xc00 + index * 12;
            Write(at, Address(0x440 + family * 16)); Write(at + 4, payload); Write(at + 8, Literal(name));
        }
        internal void EmitFloatConstructor(string name, float value)
        {
            byte[] code = [0x55, 0x8b, 0xec, 0x51, 0xd9, 0x05, 0, 0, 0, 0, 0xd9, 0x1c, 0x24, 0x68, 0, 0, 0, 0, 0xb9, 0, 0, 0, 0, 0xe8, 0, 0, 0, 0];
            code.CopyTo(Bytes, 0x200);
            Write(0x700, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
            Write(0x206, Address(0x700)); Write(0x20e, Literal(name)); Write(0x213, _origin + 0x3100);
        }
        private uint Literal(string text)
        {
            var at = _literal; var encoded = Encoding.ASCII.GetBytes(text + '\0'); encoded.CopyTo(Bytes, at);
            _literal += encoded.Length; return Address(at);
        }
        private uint Address(int at) => _origin + (uint)(at < 0xc00 ? 0x2000 + at - 0x400 : 0x3000 + at - 0xc00);
        private void Section(int at, string name, uint rva, uint raw, uint count, uint flags)
        {
            Encoding.ASCII.GetBytes(name).CopyTo(Bytes, at); Write(at + 8, count); Write(at + 12, rva);
            Write(at + 16, count); Write(at + 20, raw); Write(at + 36, flags);
        }
        internal void Write(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(at), value);
        internal void Short(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(at), value);
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or BadImageFormatException) { return; }
        throw new InvalidOperationException("Malformed executable settings were accepted.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
