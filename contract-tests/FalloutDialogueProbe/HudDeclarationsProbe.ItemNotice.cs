using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class HudDeclarationsProbe
{
    private static void DirectItemNotice()
    {
        foreach (var origin in new uint[] { 0x400000, 0x810000 })
        foreach (var sourceSlot in new byte[] { 0xe4, 0xe8 })
        {
            var fixture = new ItemFixture(origin, sourceSlot); var actual = fixture.Read();
            Require(actual.ItemIcon == "Source/InventoryGift.dds" && actual.ItemSeconds == 4.25f &&
                actual.SingleItemFormat == "%s: %s" && actual.MultipleItemFormat == "%i %s%s: %s",
                "Optimized inventory notice replaced the source icon, duration or formats.");
            void Alter(Action<ItemFixture> change)
            {
                var changed = new ItemFixture(origin, sourceSlot); change(changed);
                try { changed.Read(); } catch (Exception error) when (error is NotSupportedException or InvalidDataException) { return; }
                throw new InvalidDataException("Malformed optimized inventory notice was admitted.");
            }
            Alter(changed => changed.Code[changed.Anchor + 13] = 0x7f);
            Alter(changed => changed.Code[changed.Anchor + 14]++);
            Alter(changed => changed.Word(changed.Anchor + 2, origin + 0x104 + 4));
            Alter(changed => changed.Word(changed.Multiple + 2, origin + 0x100 + 4));
            Alter(changed => changed.Code[changed.SourceLoad + 2]--);
            Alter(changed => changed.Code[changed.Single + 28]--);
            Alter(changed => changed.Code[changed.Single + 14]--);
            Alter(changed => changed.Code[changed.Joined + 5]--);
            Alter(changed => changed.Retarget(changed.Single + 3, ItemFixture.Formatter));
            Alter(changed => changed.Retarget(changed.Single + 21, ItemFixture.NameHelper));
            Alter(changed => changed.Code[changed.Multiple + 39] = 16);
            Alter(changed => changed.Code[changed.Icon + 7] = 0x50);
            Alter(changed => changed.Code[changed.Icon - 6] = 0x20);
            Alter(changed => changed.Word(changed.Icon - 5, BitConverter.SingleToUInt32Bits(float.NaN)));
            Alter(changed => changed.Word(changed.Icon - 5, 0));
            Alter(changed => changed.Strings[origin + 5] = "%s: %i");
            Alter(changed => changed.Strings[origin + 6] = "%s %s%s: %s");
            Alter(changed => changed.AddNotice(1000, origin + 8, 4.25f));
            // Repeated declarations of the same source effects coalesce; a
            // larger inventory-menu consumer cannot supply the notice owner.
            fixture.AddNotice(1000, origin + 7, 4.25f);
            Require(fixture.Read().ItemIcon == actual.ItemIcon, "Equivalent source notice callers became ambiguous.");
        }
        Console.WriteLine("PASS optimized inventory-notice descriptor/count branches, same source/name/formatter/output, typed queue duration/icon/text, relocation and independent transport refusals.");
    }

    internal static void Owned(string root, string campaign)
    {
        using var source = RuntimeLiveContentSource.Open(root, campaign);
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var declaration = FalloutExecutableStringTable.ReadHudMessageDeclarations(source.FalloutExecutablePath);
        var iconPath = declaration.ItemIcon.Replace('\\', '/');
        if (!iconPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase)) iconPath = "textures/" + iconPath;
        if (!source.TryRead(iconPath, null, out var icon, out var iconIdentity))
            throw new FileNotFoundException("Owned declared inventory-notice icon is absent: " + declaration.ItemIcon);
        var labels = new[] { "sAddItemtoInventory", "sRemoveItemfromInventory", "sPlural" }
            .Select(name => new { name, value = FalloutGameSettingStrings.Read(records, name) }).ToArray();
        Console.WriteLine("OPENNV_OWNED_HUD_DECLARATIONS_PASS " + JsonSerializer.Serialize(new
        {
            source.Game,
            source.StackId,
            source.SaveCompatibilityId,
            executableSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.FalloutExecutablePath))),
            declaration,
            iconPath,
            iconIdentity,
            iconSha256 = Convert.ToHexString(SHA256.HashData(icon)),
            labels,
            boundary = "declarations-and-winning-source-icon/text;ordinary-queue/pixels/audio-remain-independent",
        }));
    }

    private sealed class ItemFixture
    {
        internal const int NameHelper = 2500, Formatter = 2600, Queue = 2700;
        internal byte[] Code { get; } = Enumerable.Repeat((byte)0x90, 3000).ToArray();
        internal Dictionary<uint, string> Strings { get; } = [];
        internal int Anchor { get; private set; }
        internal int Multiple { get; private set; }
        internal int Single { get; private set; }
        internal int Joined { get; private set; }
        internal int SourceLoad { get; private set; }
        internal int Icon { get; private set; }
        private readonly uint _origin;
        private readonly byte _sourceSlot;
        private readonly Dictionary<uint, string> _settings;
        internal ItemFixture(uint origin, byte sourceSlot)
        {
            _origin = origin; _sourceSlot = sourceSlot;
            _settings = new() { [origin + 0x100] = "sAddItemtoInventory", [origin + 0x200] = "sPlural" };
            foreach (var (offset, value) in new (uint, string)[]
            {
                (1, "Messages"), (2, "template_message_icon"), (3, "template_justify_left_text"), (4, "template_message_bracket"),
                (5, "%s: %s"), (6, "%i %s%s: %s"), (7, "Source/InventoryGift.dds"), (8, "Source/Different.dds"),
            }) Strings.Add(origin + offset, value);
            Push(32, origin + 1); Bytes(40, 0xc7, 0x45, 0xf0); Word(43, 19);
            Bytes(56, 0x8d, 0x0c, 0x50, 0x51, 0x68, 0xa1, 0x0f, 0, 0);
            Bytes(75, 0x8d, 0x4c, 0, 33, 0x51, 0x68, 0xa2, 0x0f, 0, 0);
            Push(100, origin + 2); Push(110, origin + 3);
            var traits = new uint[] { 4001, 4002, 4003, 4009, 4013, 4026 };
            for (var index = 0; index < traits.Length; index++) { Bytes(120 + index * 9, 0x6a, (byte)(index + 2)); Push(122 + index * 9, traits[index]); }
            Push(190, origin + 4);
            // An unrelated menu has the same descriptor and format literals,
            // but four authored format alternatives and no notification ABI.
            Bytes(2100, 0x55, 0x8b, 0xec, 0xff, 0x35); Word(2105, origin + 0x104);
            for (var index = 0; index < 4; index++) Push(2130 + index * 8, origin + 5 + (uint)(index % 2));
            Push(2200, origin + 8); Bytes(2220, 0x8b, 0xe5, 0x5d, 0xc3);
            AddNotice(320, origin + 7, 4.25f);
        }
        internal void AddNotice(int owner, uint icon, float seconds)
        {
            const byte buffer = 0xd8;
            Bytes(owner, 0x55, 0x8b, 0xec); SourceLoad = owner + 64; Bytes(SourceLoad, 0x8b, 0x45, _sourceSlot);
            Anchor = owner + 96; Bytes(Anchor - 3, 0x83, 0x38, 1, 0xff, 0x35); Word(Anchor + 2, _origin + 0x104);
            Bytes(Anchor + 6, 0xc7, 0x45, 0xf4); Word(Anchor + 9, 3); Bytes(Anchor + 13, 0x7e, 42);
            Multiple = Anchor + 15; Single = Multiple + 42; Joined = Single + 32;
            Bytes(Multiple, 0xff, 0x35); Word(Multiple + 2, _origin + 0x204); Bytes(Multiple + 6, 0xff, 0x70, 4); Call(Multiple + 9, NameHelper);
            Bytes(Multiple + 14, 0x8b, 0x75, _sourceSlot, 0x83, 0xc4, 4, 0x50, 0xff, 0x36, 0x8d, 0x45, buffer);
            Push(Multiple + 26, _origin + 6); Bytes(Multiple + 31, 0x50); Call(Multiple + 32, Formatter);
            Bytes(Multiple + 37, 0x83, 0xc4, 24, 0xeb, 32);
            Bytes(Single, 0xff, 0x70, 4); Call(Single + 3, NameHelper); Bytes(Single + 8, 0x83, 0xc4, 4, 0x50, 0x8d, 0x45, buffer);
            Push(Single + 15, _origin + 5); Bytes(Single + 20, 0x50); Call(Single + 21, Formatter);
            Bytes(Single + 26, 0x8b, 0x75, _sourceSlot, 0x83, 0xc4, 16); Bytes(Joined, 0x8b, 0x4e, 4, 0x8b, 0x75, buffer);
            Icon = Joined + 35; Bytes(Icon - 9, 0x51, 0xc7, 4, 0x24); Word(Icon - 5, BitConverter.SingleToUInt32Bits(seconds));
            Bytes(Icon - 1, 0x50); Push(Icon, icon); Bytes(Icon + 5, 0x6a, 0, 0x56); Call(Icon + 8, Queue); Bytes(Icon + 13, 0x83, 0xc4, 20);
            Bytes(Icon + 32, 0x8b, 0xe5, 0x5d, 0xc3);
        }
        internal FalloutHudMessageDeclarations Read() => FalloutExecutableStringTable.ReadHudMessageDeclarations(Code,
            address => Strings.GetValueOrDefault(address), _settings, _ => throw new InvalidDataException("Direct duration was replaced by a constant lookup."));
        internal void Word(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Code.AsSpan(at), value);
        internal void Retarget(int call, int target) => Word(call + 1, unchecked((uint)(target - call - 5)));
        private void Bytes(int at, params byte[] bytes) => bytes.CopyTo(Code, at);
        private void Push(int at, uint value) { Bytes(at, 0x68); Word(at + 1, value); }
        private void Call(int at, int target) { Bytes(at, 0xe8); Retarget(at, target); }
    }
}
