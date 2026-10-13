namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerIdleAnimation(byte[] code, Image image, int caller,
        int middleConstructor, int highConstructor, uint mainTimer)
    {
        var high = Table(".?AVHighProcess@@", 460);
        RequirePlayerIdlePackages(code, image, middleConstructor);
        var idle = Offset(Slot(high, 0xd4));
        if (!Match(code, caller + 0x74e, [0x8b, 0x4f, 0x60, 0x57, 0x8b, 1, 0xff, 0x90, 0xd4, 0, 0, 0]) ||
            !Match(code, idle + 0xd, [0x8b, 0xcf, 0x8b, 7, 0xff, 0x90, 0xd0, 1, 0, 0, 0x85, 0xc0, 0x0f, 0x84]) ||
            checked(idle + 0x1f + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(idle + 0x1b))) != idle + 0x1d2 ||
            !Match(code, idle + 0x24, [0xff, 0x90, 0x78, 2, 0, 0]) ||
            !Match(code, idle + 0x30, [0xff, 0x92, 0xe4, 1, 0, 0]) ||
            !Match(code, idle + 0x85, [0xff, 0x90, 0xd0, 1, 0, 0]) ||
            !Match(code, idle + 0x90, [0x8b, 0x40, 0x20, 0x85, 0xc0, 0x0f, 0x45, 0xc8, 0x83, 0xbe, 0x48, 3, 0, 0, 0]) ||
            !Match(code, idle + 0x9f, [0xf3, 0x0f, 0x10, 0x41, 0x0c, 0xf3, 0x0f, 0x59, 5]) ||
            Word(image.Read(Word(code, idle + 0xa8), 4), 0) != 0x3c800000 ||
            !Match(code, idle + 0xb7, [0x6a, 1, 0x6a, 0, 0xff, 0x35]) ||
            !Match(code, idle + 0xcd, [0xf3, 0x0f, 0x59, 0x0d]) ||
            !Match(code, idle + 0xdd, [0x0f, 0x2f, 0xc1, 0x0f, 0x87]) ||
            !Match(code, idle + 0x17d, [0x0f, 0x2e, 0xc2, 0x9f, 0xf6, 0xc4, 0x44, 0x7b, 0x33]) ||
            !Match(code, idle + 0x18a, [0x6a, 1, 0x6a, 0, 0x6a, 1, 0x6a, 2, 0x6a, 0, 0x57, 0xff, 0x50, 0x44]) ||
            !Match(code, idle + 0x1b9, [0xf3, 0x0f, 0x5c, 0x0d]) || Word(code, idle + 0x1bd) != mainTimer ||
            !Match(code, idle + 0x1d2, [0x5f, 0x5e, 0x8b, 0xe5, 0x5d, 0xc2, 4, 0]))
            throw new NotSupportedException("High idle update changed its actual Player call, selected model/null return, bound/distance or independent timer branches.");
        var settings = ReadStaticSettings(image).ToArray();
        RequireSetting(Word(code, idle + 0xd1), "fAIIdleAnimationDistance");
        RequireSetting(Word(code, idle + 0x179), "fAIIdleWaitTime");
        if (Word(code, idle + 0x19e) != Word(code, idle + 0x179) ||
            Word(image.ReadLoaderData(Word(code, idle + 0x8c) + 12, 4), 0) != 0)
            throw new NotSupportedException("High idle reset did not reread its winning setting or its loader-default bound changed.");
        foreach (var name in new[] { ".?AVLowProcess@@", ".?AVMiddleLowProcess@@", ".?AVMiddleHighProcess@@" })
            if (!Match(image.Read(Slot(Table(name, 460), 0xd4), 3), 0, [0xc2, 4, 0]))
                throw new NotSupportedException("Lower idle virtual gained a source consumer.");
        if (!Match(image.Read(Slot(high, 0x330), 7), 0, [0xd9, 0x81, 0x2c, 3, 0, 0, 0xc3]) ||
            !Match(image.Read(Slot(high, 0x334), 20), 0, [0x55, 0x8b, 0xec, 0xf3, 0x0f, 0x10, 0x45, 8, 0xf3, 0x0f, 0x11, 0x81, 0x2c, 3, 0, 0, 0x5d, 0xc2, 4, 0]) ||
            !Match(image.Read(Slot(high, 0x58c), 7), 0, [0x8a, 0x81, 0x9d, 1, 0, 0, 0xc3]) ||
            !Match(image.Read(Slot(high, 0x3e0), 8), 0, [0x0f, 0xbf, 0x81, 0xe4, 2, 0, 0, 0xc3]) ||
            !Match(code, middleConstructor + 0x365, [0x66, 0xc7, 0x86, 0x9c, 1, 0, 0, 0, 0]) ||
            !Match(code, highConstructor + 0x189, [0x66, 0x89, 0xbe, 0xe4, 2, 0, 0]) ||
            !Match(code, highConstructor + 0x214, [0xc7, 0x86, 0x2c, 3, 0, 0, 0, 0, 0x80, 0x3f]) ||
            !Match(code, highConstructor + 0x531, [0xc7, 0x86, 0x48, 3, 0, 0, 0, 0, 0, 0]))
            throw new NotSupportedException("High idle getters/setters lost their separate flag, signed action, wait or current-idle constructor fields.");
        var player = Table(".?AVPlayerCharacter@@", 191);
        var model = Offset(Slot(player, 0x1d0));
        var animation = Offset(Slot(player, 0x1e4));
        if (!Match(code, model, [0x56, 0x8b, 0xf1, 0x80, 0xbe, 0xa8, 5, 0, 0, 0, 0x75, 0x0a]) ||
            !Match(code, model + 0xc, [0x8b, 0x86, 0xf0, 5, 0, 0, 0x85, 0xc0]) ||
            !Match(code, animation, [0x8b, 0x81, 0xfc, 5, 0, 0, 0x85, 0xc0, 0x75]) ||
            !Match(code, animation + 0xa, [0x38, 0x81, 0xa8, 5, 0, 0, 0x75, 0x0a]) ||
            !Match(code, animation + 0x12, [0x8b, 0x81, 0xec, 5, 0, 0, 0x85, 0xc0]))
            throw new NotSupportedException("Player idle model and animation getters lost their independent view/root/override/context selectors.");
        var cache = image.PlayerWorkingContextLoaderCells(Word(code, model + 0x18));
        if (cache.Model != 0 || cache.Reference != 0 ||
            !Match(code, model + 0x34, [0x3b, 0xb7, 0x4c, 2, 0, 0, 0x75, 0x17]) ||
            !Match(code, model + 0x4a, [0x8b, 0x87, 0x48, 2, 0, 0]) ||
            !Match(code, model + 0x53, [0x8b, 0x46, 0x5c, 0x85, 0xc0, 0x74, 6, 0x8b, 0x40, 0x14]))
            throw new NotSupportedException("Player fallback model lost its independent current-thread cache or loaded-reference-root getter.");
        var block = Offset(image.CodeBase + (uint)Child(idle + 0x60));
        var allow = Child(idle + 0x6c);
        if (!Match(code, block, [0x8b, 0x81, 0x18, 1, 0, 0, 0x85, 0xc0, 0x74, 0x1d]) ||
            !Match(code, block + 0x11, [0x83, 0x78, 8, 3, 0x75, 0x2e, 0x32, 0xc0, 0xc3]) ||
            !Match(code, block + 0x1a, [0x8b, 0x42, 0x44, 0x83, 0xe8, 2, 0x74, 0x23, 0x83, 0xe8, 3, 0x74, 0x1e]) ||
            !Match(code, block + 0x27, [0x8b, 0x81, 0x1c, 1, 0, 0, 0x85, 0xc0]) ||
            !Match(code, allow, [0x83, 0xb9, 0x1c, 1, 0, 0, 0, 0x75, 0x19, 0x8b, 0x81, 0x18, 1, 0, 0, 0x85, 0xc0, 0x74, 0x0c,
                0x83, 0x78, 0x18, 0, 0x74, 9, 0x83, 0x78, 8, 3, 0x75, 3, 0xb0, 1, 0xc3, 0x32, 0xc0, 0xc3]))
            throw new NotSupportedException("Source animation context changed its independent pending-action predicates.");
        var factories = new List<int>();
        for (var at = 0; at <= code.Length - 0x40; ++at)
            if (Match(code, at, [0x33, 0xc0, 0x68, 0x30, 1, 0, 0, 0xb9]) &&
                Match(code, at + 0xc, [0xc6, 0x45, 0xfc, 0, 0x89, 0x87, 0xe8, 5, 0, 0, 0xe8]) &&
                Match(code, at + 0x1b, [0x89, 0x85, 0x8c, 0xfe, 0xff, 0xff, 0xc6, 0x45, 0xfc, 5, 0x85, 0xc0, 0x74, 9,
                    0x8b, 0xc8, 0xe8]) &&
                Match(code, at + 0x30, [0xeb, 2, 0x33, 0xc0, 0xff, 0xb5, 0x9c, 0xfe, 0xff, 0xff, 0x89, 0x87, 0xec, 5, 0, 0]))
                factories.Add(Child(at + 0x2b));
        if (factories.Count != 1 ||
            !Match(code, factories[0] + 0x58, [0xc7, 0x86, 0x18, 1, 0, 0, 0, 0, 0, 0]) ||
            !Match(code, factories[0] + 0x62, [0xc7, 0x86, 0x1c, 1, 0, 0, 0, 0, 0, 0]))
            throw new NotSupportedException("Player first-person animation factory lost its independent null pending-action constructor fields.");
        var statePredicate = Child(idle + 0x13c);
        if (!Match(code, idle + 0x13a, [0x8b, 0xcf, 0xe8]) ||
            !Match(code, statePredicate, [0x53, 0x57, 0x8b, 0xf9, 0x32, 0xdb, 0x8b, 0x4f, 0x60, 0x85, 0xc9, 0x74, 0x47,
                0x8b, 1, 0x56, 0xff, 0x90, 0x28, 2, 0, 0, 0x8b, 0xf0, 0x85, 0xf6, 0x74, 0x32, 0x80, 0x7e, 0x20, 3]) ||
            !Match(code, statePredicate + 0x4e, [0x5e, 0x5f, 0x8a, 0xc3, 0x5b, 0xc3]) ||
            !Match(code, statePredicate + 0x54, [0x5f, 0x8a, 0xc3, 0x5b, 0xc3]))
            throw new NotSupportedException("Actor idle state predicate changed its independent current-package null/kind-three branches.");
        RequirePlayerIdleClassPredicate(code, image, idle);
        void RequireSetting(uint address, string name)
        {
            if (settings.SingleOrDefault(row => row.Descriptor + 4 == address) is not { Collection: SettingCollection.Game } row || row.Name != name)
                throw new NotSupportedException("High idle setting changed its selected source Game owner: " + name);
        }
        uint Table(string name, int slots)
        {
            var values = image.SourceClassTables(name, slots).ToArray();
            return values.Length == 1 ? values[0] : throw new NotSupportedException("Idle update source class is absent or ambiguous: " + name);
        }
        uint Slot(uint table, uint slot) => Word(image.Read(table + slot, 4), 0);
        int Offset(uint address) => SourceOffset(code, image.CodeBase, address);
        int Child(int at) => checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
    }
}
