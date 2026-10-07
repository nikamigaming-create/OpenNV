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

                foreach (var receiver in new[] { 3, 6, 7 })
                {
                    var inline = new Fixture(origin); inline.EmitInlineString("sInline", "Owned inline value", receiver);
                    File.WriteAllBytes(path, inline.Bytes);
                    var strings = FalloutExecutableStringTable.Read(path);
                    Require(strings.Count == 2 && strings["sInline"] == "Owned inline value" && strings["sSynthetic"] == "Synthetic\nvalue",
                        "An inline allocation lost its receiver/value or unrelated static defaults.");
                }
                var first = new Fixture(origin); first.EmitInlineString("sFirst", "First allocation", first: true);
                File.WriteAllBytes(path, first.Bytes);
                Require(FalloutExecutableStringTable.Read(path)["sFirst"] == "First allocation",
                    "The first inline allocation lost its exception-frame owner.");
                first.Bytes[0x240 - 36 + 30] = 0x65; File.WriteAllBytes(path, first.Bytes);
                Require(!FalloutExecutableStringTable.Read(path).ContainsKey("sFirst"),
                    "A foreign exception frame admitted an unowned first allocation.");
                first = new Fixture(origin); first.EmitInlineString("sFirst", "First allocation", first: true);
                first.Bytes[0x240 - 18] = 0x53; File.WriteAllBytes(path, first.Bytes);
                Require(!FalloutExecutableStringTable.Read(path).ContainsKey("sFirst"),
                    "An unrelated saved receiver became the first constructor owner.");
                foreach (var (offset, replacement) in new (int, byte)[]
                {
                    (1, 16), (26, 0xf8), (38, 0xff), (40, 38), (43, 4), (50, 8), (70, 0x57),
                    (75, 0xe8), (79, 3), (81, 0xff), (83, 16), (97, 0x3d)
                })
                {
                    var malformed = new Fixture(origin); malformed.EmitInlineString("sInline", "Ignored");
                    malformed.Bytes[0x240 + offset] = replacement; File.WriteAllBytes(path, malformed.Bytes);
                    Require(!FalloutExecutableStringTable.Read(path).ContainsKey("sInline"),
                        "A foreign receiver, extent, branch or registration became an inline setting.");
                }
                var foreignCollection = new Fixture(origin); foreignCollection.EmitInlineString("sInline", "Ignored");
                foreignCollection.Write(0x240 + 61, foreignCollection.Address(0x450)); File.WriteAllBytes(path, foreignCollection.Bytes);
                Require(!FalloutExecutableStringTable.Read(path).ContainsKey("sInline"), "An INI descriptor became a game string.");
                var unownedCall = new Fixture(origin); unownedCall.EmitInlineString("sInline", "Ignored");
                unownedCall.Write(0x240 + 21, 0x2000); File.WriteAllBytes(path, unownedCall.Bytes);
                Require(!FalloutExecutableStringTable.Read(path).ContainsKey("sInline"), "A non-code allocation target became a constructor.");
                var duplicateInline = new Fixture(origin); duplicateInline.EmitInlineString("sInline", "First");
                duplicateInline.EmitInlineString("sInline", "Second", at: 0x2c0); File.WriteAllBytes(path, duplicateInline.Bytes);
                Reject(() => FalloutExecutableStringTable.Read(path));
                var initialized = new Fixture(origin); initialized.Write(0xc04, origin + 0x3100);
                initialized.EmitInlineString("sSynthetic", "Constructed value"); File.WriteAllBytes(path, initialized.Bytes);
                Require(FalloutExecutableStringTable.Read(path)["sSynthetic"] == "Constructed value",
                    "Pre-construction static storage replaced the actual inline default.");
                var missingInline = new Fixture(origin); missingInline.EmitInlineString("sInline", "Source value");
                missingInline.Write(0x240 + 51, origin + 0x3100); File.WriteAllBytes(path, missingInline.Bytes);
                Reject(() => FalloutExecutableStringTable.Read(path));
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("OPENNV_STATIC_EXECUTABLE_SETTINGS_PASS relocated=true typedCollections=true malformedRejected=true " +
            "constructorPrecedence=true inlineAllocation=true receiverAndBranches=true staticRemainder=true synthetic=true");
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
        internal void EmitInlineString(string name, string value, int receiver = 6, int at = 0x240, bool first = false)
        {
            var row = new byte[102];
            void BytesAt(int offset, params byte[] bytes) => bytes.CopyTo(row, offset);
            void Dword(int offset, uint payload) => BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(offset), payload);
            var absolute = (byte)(5 | receiver << 3); var same = (byte)(0xc0 | receiver << 3 | receiver);
            BytesAt(0, 0x6a, 12, 0xb9); Dword(3, _origin + 0x3180);
            BytesAt(7, 0xc7, 0x45, 0xfc, 0xff, 0xff, 0xff, 0xff);
            BytesAt(14, 0x89, absolute); Dword(16, _origin + 0x31e0);
            row[20] = 0xe8; Dword(21, unchecked((uint)(0x200 - at - 25)));
            BytesAt(25, 0x8b, (byte)(0xc0 | receiver << 3), 0x89, (byte)(0x45 | receiver << 3), 0xf0);
            BytesAt(30, 0xc7, 0x45, 0xfc); Dword(33, 2);
            BytesAt(37, 0x85, same, 0x74, 39, 0xc7, (byte)(0x40 | receiver), 8); Dword(44, Literal(name));
            BytesAt(48, 0xc7, (byte)(0x40 | receiver), 4); Dword(51, Literal(value));
            BytesAt(55, 0xc6, 0x45, 0xfc, 3, 0xc7, (byte)receiver); Dword(61, Address(0x440));
            row[65] = 0xe8; Dword(66, unchecked((uint)(0x201 - at - 70)));
            BytesAt(70, (byte)(0x50 + receiver), 0x8b, 0xc8, 0x8b, 0x10, 0xff, 0x52, 4, 0xeb, 2, 0x33, same, 0x6a, 12, 0xb9);
            Dword(85, _origin + 0x3180); BytesAt(89, 0xc7, 0x45, 0xfc, 0xff, 0xff, 0xff, 0xff);
            BytesAt(96, 0x89, absolute); Dword(98, _origin + 0x31f0);
            if (first)
            {
                byte[] frame = [0x55, 0x8b, 0xec, 0x6a, 0xff, 0x68, 0, 0, 0, 0, 0x64, 0xa1, 0, 0, 0, 0, 0x50, 0x51,
                    (byte)(0x50 + receiver), 0xa1, 0, 0, 0, 0, 0x33, 0xc5, 0x50, 0x8d, 0x45, 0xf4, 0x64, 0xa3, 0, 0, 0, 0];
                frame.CopyTo(Bytes, at - frame.Length); Write(at - 30, _origin + 0x1000); Write(at - 16, _origin + 0x3190);
                row = row[..7].Concat(row[20..]).ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(8), unchecked((uint)(0x200 - at - 12)));
                BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(53), unchecked((uint)(0x201 - at - 57)));
            }
            row.CopyTo(Bytes, at);
        }
        private uint Literal(string text)
        {
            var at = _literal; var encoded = Encoding.ASCII.GetBytes(text + '\0'); encoded.CopyTo(Bytes, at);
            _literal += encoded.Length; return Address(at);
        }
        internal uint Address(int at) => _origin + (uint)(at < 0xc00 ? 0x2000 + at - 0x400 : 0x3000 + at - 0xc00);
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
