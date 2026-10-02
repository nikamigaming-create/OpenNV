using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static partial class NumericIniSettingProbe
{
    private static void Decoder()
    {
        foreach (var relocation in new uint[] { 0x100000, 0x740000 })
        {
            var image = new IniImage(relocation);
            var declarations = image.Read();
            Require(declarations.Count == 4 && declarations.Single(row => row.Name == "fSynthetic:Display").NumericDefault == 2.5 &&
                declarations.Single(row => row.Name == "iSynthetic:General").NumericDefault == -47 &&
                declarations.Single(row => row.Name == "iConstructed:General").NumericDefault == -12 &&
                declarations.Single(row => row.Name == "rSynthetic:Menu").Payload == 0x12785634,
                "Relocated constructor/inline collection associations lost payload identity.");
            var packed = image.Code.AsSpan(400, 67).ToArray();
            Require(FalloutExecutableStringTable.ReadPackedIniPayload(packed, 0xe8) == 0x12785634,
                "Packed component masks or byte order changed.");
            packed[18] = 254;
            Reject(() => FalloutExecutableStringTable.ReadPackedIniPayload(packed, 0xe8));
            Reject(() => FalloutExecutableStringTable.ReadPackedIniPayload(image.Code.AsSpan(400, 67), 0xec));
            Reject(() => FalloutExecutableStringTable.ReadPackedIniPayload(new byte[66], 0xe8));
            var wrongSingleton = new IniImage(relocation); wrongSingleton.Write(46, relocation + 0x19100);
            Reject(() => wrongSingleton.Read());
            var wrongPayload = new IniImage(relocation); wrongPayload.Write(22, relocation + 0x18f00);
            Reject(() => wrongPayload.Read());
            var wrongRegister = new IniImage(relocation); wrongRegister.Code[519] = 0x45;
            Reject(() => wrongRegister.Read());
            var duplicate = new IniImage(relocation); duplicate.Names[relocation + 0x18000] = "iSynthetic:General";
            // Collection identity is part of a declaration: equal names in
            // separate owners are legal and still select preferences first.
            Require(duplicate.Read().Count == 4, "Equal names in separate INI collections were rejected.");
            var wrongForward = new IniImage(relocation); wrongForward.Code[2613] = 0x0c;
            Reject(() => wrongForward.Read());
        }
    }

    private sealed class IniImage
    {
        internal byte[] Code { get; } = Enumerable.Repeat((byte)0xcc, 4096).ToArray();
        internal Dictionary<uint, string> Names { get; } = [];
        private readonly Dictionary<uint, byte[]> _regions = [];
        private readonly uint _base;

        internal IniImage(uint address)
        {
            _base = address;
            Inline(0, 0, "fSynthetic:Display", 0x40200000, false);
            Inline(200, 1, "iSynthetic:General", unchecked((uint)-47), false);
            Packed(400, 0xe8);
            Inline(467, 2, "rSynthetic:Menu", 0, true);
            for (var family = 0; family < 3; ++family) Factory(family);
            Constructed();
        }

        internal IReadOnlyList<FalloutIniDeclaration> Read() => FalloutExecutableStringTable.ReadIniDeclarations(Code, _base,
            address => Names.GetValueOrDefault(address), ReadBytes,
            (address, count) => address >= _base + 0x19000 && (ulong)address + (uint)count <= _base + 0x1a000,
            (address, count) => _regions.Any(region => address >= region.Key && (ulong)address + (uint)count <= (ulong)region.Key + (uint)region.Value.Length));

        private byte[] ReadBytes(uint address, int count)
        {
            foreach (var (start, bytes) in _regions)
                if (address >= start && (ulong)address + (uint)count <= (ulong)start + (uint)bytes.Length)
                    return bytes.AsSpan(checked((int)(address - start)), count).ToArray();
            throw new InvalidDataException("Synthetic INI read escaped its source region.");
        }

        internal void Write(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Code.AsSpan(at), value);
        private void Emit(int at, params byte[] values) => values.CopyTo(Code, at);
        private void Call(int at, int target) { Code[at] = 0xe8; Write(at + 1, unchecked((uint)(target - at - 5))); }
        private uint Singleton(int family) => _base + 0x19000 + (uint)family * 4;

        private void Inline(int at, int family, string name, uint payload, bool packed)
        {
            var receiver = _base + 0x19200 + (uint)family * 16;
            var nameAddress = _base + 0x18000 + (uint)family * 64;
            Names[nameAddress] = name;
            Emit(at, 0xc7, 0x05); Write(at + 2, receiver); Write(at + 6, 0);
            Emit(at + 10, 0xc7, 0x05); Write(at + 12, receiver + 8); Write(at + 16, nameAddress);
            var derived = at + 30;
            if (packed)
            {
                Emit(at + 20, 0x8b, 0x4d, 0xe8, 0x89, 0x0d); Write(at + 25, receiver + 4);
                Code[at + 29] = 0x90;
            }
            else { Emit(at + 20, 0xc7, 0x05); Write(at + 22, receiver + 4); Write(at + 26, payload); }
            Emit(derived, 0xc7, 0x05); Write(derived + 2, receiver); Write(derived + 6, Vtable(family, true));
            Call(derived + 10, 1024 + family * 256);
            var register = derived + 20;
            if (packed) { Emit(derived + 15, 0x8b, 0x15); Write(derived + 17, Singleton(family)); ++register; }
            else { Code[derived + 15] = 0xa1; Write(derived + 16, Singleton(family)); }
            Emit(register, 0x89, packed ? (byte)0x55 : (byte)0x45, 0xe0, 0x68); Write(register + 4, receiver);
            Emit(register + 8, 0x8b, 0x45, 0xe0, 0x8b, 0x10, 0x8b, 0x4d, 0xe0, 0x8b, 0x42, 0x04, 0xff, 0xd0, 0x5d, 0xc3);
        }

        private void Factory(int family)
        {
            var at = 1024 + family * 256;
            Emit(at, 0x55, 0x8b, 0xec, 0x83, 0x3d); Write(at + 5, Singleton(family));
            Emit(at + 9, 0, 0x75, 65);
            Array.Fill(Code, (byte)0x90, at + 12, 23);
            var guard = at + 35;
            Emit(guard, 0x83, 0x7d, 0xfc, 0, 0x74, 13, 0x8b, 0x4d, 0xfc);
            Call(guard + 9, 2048 + family * 128);
            Emit(guard + 14, 0x89, 0x45, 0xf8, 0xeb, 7, 0xc7, 0x45, 0xf8, 0, 0, 0, 0, 0x8b, 0x4d, 0xf8, 0x89, 0x0d);
            Write(guard + 31, Singleton(family));
            Emit(guard + 35, 0x90, 0x90, 0x90, 0x90, 0x90, 0x5d, 0xc3);
            var constructor = 2048 + family * 128;
            Emit(constructor, 0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d, 0xfc, 0x8b, 0x45, 0xfc, 0xc7, 0x00);
            Write(constructor + 12, Vtable(family, false)); Emit(constructor + 16, 0x5d, 0xc3);
        }

        private uint Vtable(int family, bool descriptor)
        {
            var index = family * 2 + (descriptor ? 0 : 1);
            var start = _base + 0x10000 + (uint)index * 1024;
            var bytes = new byte[512];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, start + 32);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), start + 64);
            var collection = new[] { "INISettingCollection", "INIPrefSettingCollection", "RendererSettingCollection" }[family];
            Encoding.ASCII.GetBytes(descriptor ? ".?AV?$SettingT@V" + collection + "@@@@\0" : ".?AV" + collection + "@@\0").CopyTo(bytes, 72);
            _regions[start] = bytes;
            return start + 4;
        }

        private void Packed(int at, byte local)
        {
            Code[at] = 0xb8; Write(at + 1, 0x112); Emit(at + 5, 0xc1, 0xe0, 24, 0x89, 0x45, local, 0xb9);
            Write(at + 12, 0x278); Emit(at + 16, 0x81, 0xe1); Write(at + 18, 255);
            Emit(at + 22, 0xc1, 0xe1, 16, 0x0b, 0x4d, local, 0x89, 0x4d, local, 0xba);
            Write(at + 32, 0x356); Emit(at + 36, 0x81, 0xe2); Write(at + 38, 255);
            Emit(at + 42, 0xc1, 0xe2, 8, 0x0b, 0x55, local, 0x89, 0x55, local, 0xb8);
            Write(at + 52, 0x434); Code[at + 56] = 0x25; Write(at + 57, 255);
            Emit(at + 61, 0x0b, 0x45, local, 0x89, 0x45, local);
        }

        private void Constructed()
        {
            var name = _base + 0x18100; Names[name] = "iConstructed:General";
            Emit(700, 0x55, 0x8b, 0xec, 0x6a, 0xf4, 0x68); Write(706, name);
            Code[710] = 0xb9; Write(711, _base + 0x19300); Call(715, 2600); Emit(720, 0x5d, 0xc3);
            Emit(2600, 0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d, 0xfc, 0x8b, 0x45, 0x0c, 0x50,
                0x8b, 0x4d, 0x08, 0x51, 0x8b, 0x4d, 0xfc);
            Call(2618, 2800); Emit(2623, 0x8b, 0x45, 0xfc, 0xc7, 0x00); Write(2628, Vtable(0, true));
            Call(2632, 2900);
            Emit(2637, 0x89, 0x45, 0xf8, 0x8b, 0x45, 0xfc, 0x50, 0x8b, 0x4d, 0xf8, 0x8b, 0x11,
                0x8b, 0x4d, 0xf8, 0x8b, 0x42, 0x04, 0xff, 0xd0, 0x5d, 0xc2, 8, 0);
            Emit(2800, 0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d, 0xfc,
                0x8b, 0x4d, 0xfc, 0x8b, 0x55, 8, 0x89, 0x51, 8,
                0x8b, 0x45, 0xfc, 0x8b, 0x4d, 12, 0x89, 0x48, 4, 0x5d, 0xc2, 8, 0);
            Emit(2900, 0x55, 0x8b, 0xec); Call(2903, 1024); Code[2908] = 0xa1; Write(2909, Singleton(0));
            Emit(2913, 0x5d, 0xc3);
        }
    }
}
