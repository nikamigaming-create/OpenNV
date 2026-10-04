using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static class TagSkillMenuContracts
{
    internal static void Run()
    {
        Declarations();
        var directory = Path.Combine(Path.GetTempPath(), "opennv-tag-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var basePath = Path.Combine(directory, "Base.esm"); var patchPath = Path.Combine(directory, "Patch.esp");
            File.WriteAllBytes(basePath, Join(Header(), Skill(1, "First", "A skill", "Base description", "base.dds"),
                Skill(2, "Second", "B skill", "Second description", "second.dds"), Skill(3, "Third", "C skill", "Third description", null),
                Record("GMST", 4, Field("EDID", Text("fAVDTagSkillBonus")), Field("DATA", BitConverter.GetBytes(17.25f))),
                Record("AVIF", 5, Field("EDID", Text("Bad")), Field("FULL", Text("Bad")), Field("DESC", Text("One")), Field("DESC", Text("Two"))),
                Record("MISC", 6, Field("EDID", Text("WrongType")), Field("FULL", Text("Wrong type")))));
            File.WriteAllBytes(patchPath, Join(Header("Base.esm"), Skill(1, "Renamed", "A patched skill", "Winning description", "patch.dds")));
            using var records = FalloutPluginStack.Load(directory, ["Base.esm", "Patch.esp"]);
            var first = new FalloutNativeSkillIdentity(1, "Renamed", "A patched skill");
            var second = new FalloutNativeSkillIdentity(2, "Second", "B skill");
            var third = new FalloutNativeSkillIdentity(3, "Third", "C skill");
            var contract = new FalloutNativeTagSkillContract([first, second, third], 2, 80, 85, 90);
            var live = new Dictionary<uint, float> { [1] = 29.75f, [2] = 12.9f, [3] = 95 };
            var current = new[] { first };
            var draft = new FalloutTagSkillMenuSelection(records, contract, current, skill => live[skill.RuntimeFormId]);
            Check(draft.Choices[0] is { Description: "Winning description", Icon: "patch.dds" } && draft.Choices[2].Icon is null,
                "Tag choices ignored winning AVIF identity, description or optional icon.");
            Check(draft.Selected.SequenceEqual(current) && draft.Value(first) == 29 && !draft.Complete,
                "Opening an incomplete draft changed tags or doubled the existing tag bonus.");
            Reject(() => draft.Submit());
            Check(draft.Toggle(first) && draft.Value(first) == 11 && current.Length == 1,
                "Removing a draft tag changed accepted tags or kept the source bonus.");
            Check(draft.Toggle(second) && draft.Value(second) == 29, "Draft tag value ignored the winning bonus or integer conversion.");
            live[2] = 20.9f;
            Check(draft.Value(second) == 37, "Displayed skill value retained stale player state.");
            Check(draft.Toggle(third) && draft.Value(third) == 100 && draft.Complete && !draft.Toggle(first) && draft.Selected.Count == 2,
                "Tag draft exceeded its source count or skill cap.");
            Check(draft.Submit().SequenceEqual(new[] { second, third }), "Tag acceptance changed source identities or ordering.");
            draft.Reset();
            Check(draft.Selected.Count == 0 && draft.Value(third) == 95 && draft.Value(first) == 11 && current.SequenceEqual(new[] { first }),
                "Reset committed tags or retained a draft value.");
            records.NumericSettings.Set("fAVDTagSkillBonus", 9);
            draft.Toggle(second);
            Check(draft.Value(second) == 29, "Tag draft ignored a live numeric-setting mutation.");
            live[2] = float.NaN; Reject(() => draft.Value(second));
            live[2] = -5; Check(draft.Value(second) == 4, "Tag display changed the source clamp order.");
            Reject(() => draft.Toggle(first with { EditorId = "Foreign" }));
            Reject(() => new FalloutTagSkillMenuSelection(records, contract, [first, first], _ => 0));
            var reducing = new FalloutTagSkillMenuSelection(records, contract, [first, second, third], _ => 0);
            Reject(() => reducing.Submit());
            Check(reducing.Toggle(third) && reducing.Submit().Count == 2, "A smaller source menu count could not reduce existing tags.");
            var replacing = new FalloutTagSkillMenuSelection(records, contract, [first], skill => skill == first ? 30 : 10, false);
            Check(replacing.Selected.Count == 0 && replacing.Value(first) == 21 && replacing.Toggle(second) && replacing.Value(second) == 19,
                "Hidden initial tags retained their draft marker or doubled the accepted bonus.");
            Check(FalloutTagSkillMenuRequest.Read(["3"]) == new FalloutTagSkillMenuRequest(3, true) &&
                FalloutTagSkillMenuRequest.Read(["4", "1"]) == new FalloutTagSkillMenuRequest(4, true) &&
                FalloutTagSkillMenuRequest.Read(["2", "0"]) == new FalloutTagSkillMenuRequest(2, false),
                "Source total count or optional initial-selection default changed.");
            foreach (var command in new string[][] { [], ["0"], ["5"], ["3.5"], ["3", "2"], ["3", "1", "0"] })
                Reject(() => FalloutTagSkillMenuRequest.Read(command));
            Reject(() => new FalloutTagSkillMenuSelection(records, contract with { RequiredCount = 0 }, [], _ => 0));
            Reject(() => new FalloutTagSkillMenuSelection(records, contract with { Skills = [first with { DisplayName = "Stale" }, second, third] }, [], _ => 0));
            Reject(() => new FalloutTagSkillMenuSelection(records, contract with { Skills = [new(5, "Bad", "Bad"), second, third] }, [], _ => 0));
            Reject(() => new FalloutTagSkillMenuSelection(records, contract with { Skills = [new(6, "WrongType", "Wrong type"), second, third] }, [], _ => 0));
            Check(FalloutTagSkillMenuSelection.FormatCounts("Chosen %d of %d", 2, 5) == "Chosen 2 of 5" &&
                FalloutTagSkillMenuSelection.FormatCounts("Remaining %d", 1) == "Remaining 1", "Tag count formatting changed owned strings.");
            foreach (var format in new[] { "%f", "%d %d", "missing", "%", "%% %d" })
                Reject(() => FalloutTagSkillMenuSelection.FormatCounts(format, 2));
            Console.WriteLine("OPENNV_TAG_MENU_CONTRACT_PASS ownedDeclarations=true winningAvif=true draft=true limit=true reset=true sourceBonus=true liveValues=true invalid=true");
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static void Declarations()
    {
        var code = new byte[256];
        code[0] = 0x68; UInt(code, 1, 0x1001);
        new byte[] { 0x6a, 1, 0x51, 0xd9, 0x05 }.CopyTo(code, 12); UInt(code, 17, 0x1002);
        new byte[] { 0xd9, 0x1c, 0x24, 0x68 }.CopyTo(code, 21); UInt(code, 25, 4002);
        new byte[] { 0x8b, 0x55, 0xf0, 0x8b, 0x4a, 0x34 }.CopyTo(code, 29);
        code[80] = 0xb9; UInt(code, 81, 0x2001); code[85] = 0xe8;
        code[112] = 0xb9; UInt(code, 113, 0x2002); code[117] = 0xe8;
        new byte[] { 0x83, 0xbd, 0x80, 0xff, 0xff, 0xff, 1, 0x74, 0x19, 0x68 }.CopyTo(code, 150); UInt(code, 160, 0x1003);
        var settings = new Dictionary<uint, string> { [0x2001] = "sSkillsTitle", [0x2002] = "sSkillsCount" };
        string? Literal(uint address) => address switch { 0x1001 => "CGM_SelectItemTemplate", 0x1003 => "plural", _ => null };
        var scalar = 137.5f;
        FalloutTagMenuDeclarations Read(byte[] bytes) => FalloutExecutableStringTable.ReadTagMenuDeclarations(bytes, Literal, settings, address =>
            address == 0x1002 ? scalar : throw new InvalidDataException("Foreign scalar."));
        Check(Read(code) == new FalloutTagMenuDeclarations(137.5f, "plural"), "Tag declaration reader fitted source placement or plural text.");
        scalar = 267; Check(Read(code).ListY == 267, "Tag placement ignored source scalar changes.");
        var malformed = (byte[])code.Clone(); UInt(malformed, 25, 4001); Reject(() => Read(malformed));
        malformed = (byte[])code.Clone(); malformed[150] = 0; Reject(() => Read(malformed));
        malformed = (byte[])code.Clone(); code.AsSpan(0, 36).CopyTo(malformed.AsSpan(40)); Reject(() => Read(malformed));
        scalar = float.NaN; Reject(() => Read(code));
        scalar = 137;
        Reject(() => FalloutExecutableStringTable.ReadTagMenuDeclarations(code, Literal,
            new Dictionary<uint, string> { [0x2001] = "sSkillsTitle" }, _ => scalar));
    }
    private static byte[] Header(string? master = null)
    {
        var data = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(data, 1.34f);
        return master is null ? Record("TES4", 0, Field("HEDR", data)) :
            Record("TES4", 0, Field("HEDR", data), Field("MAST", Text(master)), Field("DATA", new byte[8]));
    }
    private static byte[] Skill(uint id, string editor, string name, string description, string? icon) =>
        Record("AVIF", id, Field("EDID", Text(editor)), Field("FULL", Text(name)), Field("DESC", Text(description)),
            icon is null ? [] : Field("ICON", Text(icon)));
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static void UInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)data.Length); UInt(bytes, 12, id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported tag menu input was accepted.");
    }
}
