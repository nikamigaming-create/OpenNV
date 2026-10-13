namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    // The control call connects each mutable setting payload to the selected
    // image's own typed descriptor. Its INI section alone never selects the
    // collection, and a source default is not the current layered value.
    private static void RequirePlayerControlConfiguration(byte[] code, Image image, int ordinary, int mouse)
    {
        var read = ordinary + 0xf3;
        if (!Match(code, mouse + 0x23, [0x8b, 0x4c, 0x24, 0x38, 0x8b, 0xd0, 0x89, 0x54, 0x24, 0x10, 0xe9]) ||
            Jump(mouse + 0x2d) != read || !Match(code, read, [0x80, 0x3d]) ||
            !Match(code, read + 6, [0, 0x74, 2, 0xf7, 0xd9, 0x8a, 0x87, 0xdc, 5, 0, 0, 0x24, 2,
                0x88, 0x44, 0x24, 0x0f, 0x74, 8, 0x33, 0xf6, 0x33, 0xd2, 0x89, 0x54, 0x24, 0x10,
                0x33, 0xc0, 0x66, 0x0f, 0x6e, 0xc2, 0x38, 0x44, 0x24, 0x0f, 0x0f, 0x5b, 0xc0,
                0x0f, 0x44, 0xc1, 0x89, 0x44, 0x24, 0x18, 0x0f, 0x2e, 5]) ||
            Word(image.Read(Word(code, read + 0x38), 4), 0) != 0 ||
            !Match(code, read + 0x3c, [0x9f, 0xf6, 0xc4, 0x44, 0x0f, 0x8b]))
            throw new NotSupportedException("Player controls changed invert-Y, independent disable-bit suppression or ordered zero-wheel routing.");
        var descriptors = ReadStaticSettings(image);
        RequireSetting(Word(code, read + 2), FalloutPlayerControlConfiguration.InvertYName, SettingCollection.Prefs, 0);
        var fov = checked(read + 0x46 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(read + 0x42)));
        if (!Match(code, fov, [0xa1]) || !Match(code, fov + 5, [0xf3, 0x0f, 0x10, 5]) ||
            !Match(code, fov + 0xd, [0xf3, 0x0f, 0x10, 0x88, 0xbc, 0, 0, 0, 0x0f, 0x2f, 0xc1,
                0x76, 0x0a, 0xf3, 0x0f, 0x5e, 0xc8, 0xf3, 0x0f, 0x11, 0x4c, 0x24, 0x2c,
                0x8b, 7, 0x8b, 0xcf, 0x6a, 0, 0x8b, 0x80, 0xbc, 2, 0, 0, 0xff, 0xd0, 0xd9, 0x1d]))
            throw new NotSupportedException("Player mouse scaling lost the actual published world-projection FOV before its independent yaw getter.");
        RequireSetting(Word(code, fov + 9), FalloutPlayerControlConfiguration.WorldFovName, SettingCollection.Main, 0x42960000);
        RequirePlayerControlOrientation(code, image, fov, ordinary);
        RequirePlayerControlFlagWriters(code, image);

        void RequireSetting(uint payload, string name, SettingCollection collection, uint initial)
        {
            var matches = descriptors.Where(row => row.Descriptor + 4 == payload).ToArray();
            if (matches is not [{ Name: var actual, Collection: var owner, Payload: var value }] ||
                actual != name || owner != collection || value != initial || !image.IsWritableExtent(payload, 4))
                throw new NotSupportedException("Player controls have no exact independently typed setting producer: " + name);
        }
        int Jump(int at) => checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
    }

    private static void RequirePlayerControlFlagWriters(byte[] code, Image image)
    {
        var disabled = Handler("DisablePlayerControls"); var enabled = Handler("EnablePlayerControls");
        if (!Match(code, disabled, [0x55, 0x8b, 0xec, 0x83, 0xec, 0x1c]) ||
            !Match(code, enabled, [0x55, 0x8b, 0xec, 0x83, 0xec, 0x1c]))
            throw new NotSupportedException("Player control command lost its actual seven-argument handler.");
        var mask = Call(disabled + 0xb6);
        if (Call(enabled + 0xc6) != mask || !Match(code, disabled + 0xb3, [0x56, 0x6a, 1]) ||
            !Match(code, enabled + 0xc3, [0x56, 0x6a, 0]) ||
            !Match(code, mask, [0x55, 0x8b, 0xec, 0x80, 0x7d, 8, 0, 0x8a, 0x45, 0x0c,
                0x8a, 0x91, 0xdc, 5, 0, 0, 0x74, 0x11, 0x0a, 0xd0, 0x88, 0x55, 8, 0xff, 0x75, 8, 0xe8]) ||
            !Match(code, mask + 0x23, [0xf6, 0xd0, 0x22, 0xc2, 0x88, 0x45, 8, 0xff, 0x75, 8, 0xe8]) ||
            Call(mask + 0x1a) != Call(mask + 0x2d))
            throw new NotSupportedException("Player disable word lost its actual add/remove mask writer and shared complete-byte setter.");
        var setter = Call(mask + 0x1a);
        if (!Match(code, setter, [0x55, 0x8b, 0xec, 0x8a, 0x45, 8, 0x56, 0x8b, 0xf1,
                0x88, 0x86, 0xdc, 5, 0, 0, 0x3c, 0xff, 0x74, 8, 0x24, 0x7f,
                0x88, 0x86, 0xdc, 5, 0, 0, 0xa8, 1, 0xb9, 1, 0, 0, 0,
                0xb8, 0x0c, 0, 0, 0, 0x0f, 0x44, 0xc1, 0x50, 0xe8]))
            throw new NotSupportedException("Player flag setter changed its ordered stores, full-disable exception or mandatory input-type child.");
        foreach (var command in new[] { disabled + 0x6c, enabled + 0x7c })
        {
            if (!Match(code, command, [0x39, 0x75, 0xfc, 0xb8, 1, 0, 0, 0, 0x0f, 0x45, 0xf0]))
                throw new NotSupportedException("Player control command changed its first argument's source mask.");
            foreach (var (at, local, bit) in new[] { (11, 0xf8, 4), (20, 0xf4, 8), (29, 0xf0, 0x10), (38, 0xec, 2), (47, 0xe8, 0x20), (56, 0xe4, 0x40) })
                if (!Match(code, command + at, [0x83, 0x7d, (byte)local, 0, 0x74, 3, 0x83, 0xce, (byte)bit]))
                    throw new NotSupportedException("Player control command changed an independent argument's disable bit.");
        }
        int Call(int at)
        {
            if (code[at] != 0xe8) throw new NotSupportedException("Player control flag writer lost its actual direct child.");
            return checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
        }
        int Handler(string name)
        {
            var handlers = new List<int>();
            foreach (var (_, data) in image.StaticDescriptorSections())
                for (var at = 0; at <= data.Length - 40; at += 4)
                    if (image.Literal(Word(data, at)) == name && Word(data, at + 0x10) == 0x00070000 &&
                        image.IsExecutableExtent(Word(data, at + 0x18)))
                        handlers.Add(SourceOffset(code, image.CodeBase, Word(data, at + 0x18)));
            return handlers.Count == 1 ? handlers[0] : throw new NotSupportedException("Player control command has no unique source handler: " + name);
        }
    }
}
