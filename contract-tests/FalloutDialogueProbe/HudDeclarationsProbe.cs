using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static partial class HudDeclarationsProbe
{
    internal static void Run()
    {
        var tile = FalloutMenuXml.Parse(Encoding.UTF8.GetBytes("<image name=\"synthetic\"><systemcolor</systemcolor><width>11</width></image>"));
        Require(tile.Element("image")!.Element("systemcolor")!.Value == "" && tile.Element("image")!.Element("width")!.Value == "11",
            "Admitted empty source property changed adjacent content.");
        var failed = false;
        try { FalloutMenuXml.Parse(Encoding.UTF8.GetBytes("<image><wrong</different></image>")); }
        catch (System.Xml.XmlException) { failed = true; }
        Require(failed, "Unrelated malformed source markup was silently repaired.");

        var code = Enumerable.Repeat((byte)0x90, 1200).ToArray();
        var literals = new Dictionary<uint, string>
        {
            [1] = "Messages", [2] = "template_message_icon", [3] = "template_justify_left_text", [4] = "template_message_bracket",
            [5] = "%s %s", [6] = "%i %s%s %s", [7] = "synthetic-item.dds", [8] = "synthetic-unrelated.dds",
        };
        void Write(int at, params byte[] value) => value.CopyTo(code, at);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(at), value);
        void Push(int at, uint value) { code[at] = 0x68; U32(at + 1, value); }
        Push(32, 1);
        Write(40, 0xc7, 0x45, 0xf0); U32(43, 19);
        Write(56, 0x8d, 0x0c, 0x50, 0x51, 0x68, 0xa1, 0x0f, 0, 0);
        Write(75, 0x8d, 0x4c, 0x00, 33, 0x51, 0x68, 0xa2, 0x0f, 0, 0);
        Push(100, 2); Push(110, 3);
        var traits = new[] { 4001, 4002, 4003, 4009, 4013, 4026 };
        for (var index = 0; index < traits.Length; index++)
        {
            var at = 120 + index * 9;
            Write(at, 0x6a, (byte)(index + 2)); Push(at + 2, (uint)traits[index]);
        }
        Push(190, 4);
        void Declaration(int at, uint icon, bool unrelated)
        {
            Write(at, 0x55, 0x8b, 0xec);
            code[at + 10] = 0xb9; U32(at + 11, 100); Write(at + 15, 0xe8, 0, 0, 0, 0);
            Push(at + 40, 5); Push(at + 50, 6);
            if (unrelated) { Push(at + 60, 5); Push(at + 70, 6); }
            Write(at + 80, 0xd9, 0x05); U32(at + 82, 200);
            Push(at + 92, icon);
            Write(at + 115, 0x8b, 0xe5, 0x5d, 0xc3);
        }
        Declaration(300, 7, false);
        Declaration(550, 8, true);
        var declaration = FalloutExecutableStringTable.ReadHudMessageDeclarations(code,
            address => literals.GetValueOrDefault(address), new Dictionary<uint, string> { [100] = "sAddItemtoInventory" },
            address => address == 200 ? 3.75f : throw new InvalidDataException("Unexpected scalar."));
        Require(declaration.XInset == 19 && declaration.YInset == 33 && declaration.ItemSeconds == 3.75 &&
            declaration.ItemIcon == "synthetic-item.dds" && declaration.TextTraits[4026] == 7,
            "HUD declaration replaced source values or admitted unrelated repeated notification branches.");
        Declaration(800, 8, false);
        failed = false;
        try
        {
            FalloutExecutableStringTable.ReadHudMessageDeclarations(code, address => literals.GetValueOrDefault(address),
                new Dictionary<uint, string> { [100] = "sAddItemtoInventory" }, _ => 3.75f);
        }
        catch (NotSupportedException) { failed = true; }
        Require(failed, "Conflicting inventory notice declarations were admitted.");
        Require(FalloutExecutableStringTable.ReadHudMessageLayout(code, address => literals.GetValueOrDefault(address)).XInset == 19,
            "An unrelated notice ambiguity prevented binding the HUD layout.");
        ScalarLayout();
        DirectItemNotice();
    }

    private static void ScalarLayout()
    {
        var code = Enumerable.Repeat((byte)0x90, 600).ToArray();
        var names = new Dictionary<uint, string> { [1] = "Messages", [2] = "template_message_icon",
            [3] = "template_justify_left_text", [4] = "template_message_bracket" };
        void Word(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(at), value);
        void Push(int at, uint value) { code[at] = 0x68; Word(at + 1, value); }
        Push(32, 1); Push(120, 2); Push(140, 3); Push(360, 4);
        void Placement(int at, byte local, sbyte inset, uint trait)
        {
            new byte[] { 0x8b, 0x45, local, 0x83, 0xc0, unchecked((byte)inset), 0x51, 0x66, 0x0f, 0x6e,
                0xc0, 0x0f, 0x5b, 0xc0, 0xf3, 0x0f, 0x11, 0x04, 0x24, 0x68 }.CopyTo(code, at);
            Word(at + 20, trait); code[at + 24] = 0xe8; Word(at + 25, (uint)(500 - at - 29));
        }
        Placement(50, 0xc0, -7, 4001); Placement(80, 0xc4, 29, 4002);
        var traits = new[] { 4001, 4002, 4003, 4009, 4013, 4026 };
        for (var index = 0; index < traits.Length; ++index)
        {
            var at = 150 + 32 * index;
            new byte[] { 0x6a, 1, 0x51, 0xc7, 4, 0x24 }.CopyTo(code, at);
            Word(at + 6, BitConverter.SingleToUInt32Bits(index + 1));
            new byte[] { 0x8b, 0x48, 4 }.CopyTo(code, at + 10); Push(at + 13, (uint)traits[index]);
            code[at + 18] = 0xe8; Word(at + 19, (uint)(500 - at - 23));
        }
        FalloutHudMessageLayout Read(byte[] bytes) => FalloutExecutableStringTable.ReadHudMessageLayout(bytes,
            address => names.GetValueOrDefault(address));
        var layout = Read(code);
        Require(layout.SafeZoneScale == 1 && layout.XInset == -7 && layout.YInset == 29 && layout.TextTraits[4026] == 6,
            "HUD scalar placement lost its source inset, conversion or text values.");
        void Reject(byte[] invalid)
        {
            try { Read(invalid); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
            throw new InvalidDataException("Malformed HUD scalar declaration was admitted.");
        }
        var invalid = code.ToArray(); invalid[105]++; Reject(invalid);
        invalid = code.ToArray(); invalid[82]++; Reject(invalid);
        foreach (var value in new[] { float.NaN, .5f })
        { invalid = code.ToArray(); BinaryPrimitives.WriteSingleLittleEndian(invalid.AsSpan(156), value); Reject(invalid); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
