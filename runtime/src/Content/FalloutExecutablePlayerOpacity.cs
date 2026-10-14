using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutPlayerOpacitySource ReadPlayerOpacity(string path, FalloutPlayerMovementControlSource controls)
    {
        var source = FalloutPlayerOpacitySource.Reviewed(controls); var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != controls.Main.EngineSha256)
            throw new InvalidDataException("Player opacity changed its selected owned executable.");
        var (code, image) = Load(bytes);
        var player = Class(".?AVPlayerCharacter@@", 191); var high = Class(".?AVHighProcess@@", 460);
        var middle = Class(".?AVMiddleHighProcess@@", 460);
        // Index all relevant primary table stores once. Constructor selection
        // then examines the actual field stores, never a global RTTI count alone.
        var tables = new HashSet<uint> { high, middle };
        var stores = tables.ToDictionary(table => table, _ => new List<int>());
        for (var at = 0; at <= code.Length - 6; at++)
            if (code[at] == 0xc7 && code[at + 1] == 6 && tables.Contains(Word(code, at + 2))) stores[Word(code, at + 2)].Add(at);
        var highCtor = One(stores[high].Where(at => at <= code.Length - 0x398 &&
            Match(code, at + 0x370, [0xc7, 0x86, 0xdc, 3, 0, 0, 1, 0, 0, 0]) &&
            Match(code, at + 0x37a, [0xc7, 0x86, 0xe0, 3, 0, 0, 0, 0, 0, 0]) &&
            Match(code, at + 0x384, [0xc7, 0x86, 0xa8, 2, 0, 0, 0, 0, 0x80, 0x3f]) &&
            Match(code, at + 0x38e, [0xc7, 0x86, 0xe4, 3, 0, 0, 0, 0, 0, 0])), "High fade constructor");
        var middleCtor = One(stores[middle].Where(at => Contains(at, 0x340, [0xc7, 0x86, 0x70, 1, 0, 0, 0, 0, 0x80, 0x3f])), "inherited opacity constructor");
        if (Call(highCtor - 5) != middleCtor - 0x32 ||
            !Match(image.Read(Slot(high, 0x608), 7), 0, [0x8b, 0x81, 0xdc, 3, 0, 0, 0xc3]) ||
            Slot(high, 0x5a4) != Slot(middle, 0x5a4) || !Match(image.Read(Slot(high, 0x5a4), 7), 0, [0xd9, 0x81, 0x70, 1, 0, 0, 0xc3]))
            throw new NotSupportedException("Player opacity lost actual process inheritance or independent state/base getters.");
        var setter = Offset(Slot(player, 0x410));
        Need(setter, [0x55, 0x8b, 0xec, 0xf3, 0x0f, 0x10, 0x45, 8, 0x56, 0x51, 0x8b, 0xf1, 0xf3, 0x0f, 0x11, 4, 0x24, 0xe8], "Player opacity setter");
        Need(setter + 0x16, [0x8b, 0x86, 0xf0, 5, 0, 0, 0x5e, 0x85, 0xc0, 0x74, 0x14], "current first-person field");
        var effective = Call(setter + 0x11);
        Need(effective, [0x55, 0x8b, 0xec, 0x8b, 0x41, 0x60, 0x85, 0xc0, 0x74, 0x13, 0x83, 0x78, 0x28, 0, 0x75, 0x0d,
            0xf3, 0x0f, 0x10, 0x45, 8, 0xf3, 0x0f, 0x11, 0x80, 0xa8, 2, 0, 0, 0x5d, 0xc2, 4, 0], "High effective-alpha store-before-child");
        var alpha = Call(setter + 0x2d);
        Need(alpha, [0x53, 0x8b, 0x5c, 0x24, 8, 0x85, 0xdb, 0x0f, 0x84], "scene alpha null-root arm");
        Need(alpha + 0xd, [0x8b, 0x43, 0x20, 0x85, 0xc0, 0x0f, 0x84], "scene nullable bound arm");
        Need(alpha + 0x18, [0xf3, 0x0f, 0x10, 0x40, 0x0c, 0x0f, 0x2e, 5], "scene ordered-zero radius");
        RequireWord(Word(code, alpha + 0x20), 0, "source scene zero");
        Need(alpha + 0x40, [0x8b, 0x83, 0xa0, 0, 0, 0, 0x8b, 0x3c, 0xb0], "current ordered child array");
        Need(alpha + 0x68, [0x8b, 0x80, 0xa8, 0, 0, 0, 0x85, 0xc0], "current slot-three shader");
        Need(alpha + 0x6e, [0x85, 0xc0, 0x74, 0x12, 0x33, 0xc9, 0x83, 0x78, 0x1c, 0xff, 0x0f, 0x44, 0xc1,
            0x85, 0xc0, 0x74, 5, 0xf3, 0x0f, 0x11, 0x40, 0x2c], "invalid shader before raw alpha store");
        if (Call(alpha + 0x92) != alpha) throw new NotSupportedException("Scene alpha lost its current-node recursion.");
        byte[] fadeSignature = [0x55, 0x8b, 0xec, 0x56, 0x8b, 0xf1, 0x8b, 0x86, 0xdc, 3, 0, 0, 0x85, 0xc0, 0x75, 0x2a];
        var fade = Unique(at => Match(code, at, fadeSignature), 0x294, "High fade update");
        Need(fade + 0x21, [0x8b, 0x4d, 8, 0xc7, 0x86, 0xe0, 3, 0, 0, 0, 0, 0x80, 0x3f, 0xe8], "zero-state one store before opacity");
        var opacity = Call(fade + 0x2e);
        if (Call(fade + 0x25f) != opacity) throw new NotSupportedException("High fade changed its common Actor opacity child.");
        var settings = ReadStaticSettings(image);
        var playerSeconds = Setting(FalloutPlayerOpacitySource.PlayerFadeSeconds, FalloutPlayerOpacitySource.PlayerFadeDefaultBits);
        var actorSeconds = Setting(FalloutPlayerOpacitySource.ActorFadeSeconds, FalloutPlayerOpacitySource.ActorFadeDefaultBits);
        Need(fade + 0x40, [0xf3, 0x0f, 0x10, 5], "actual cached Main timer");
        Need(fade + 0x50, [0xf3, 0x0f, 0x5e, 5], "Player teleport setting division");
        Need(fade + 0x5a, [0xf3, 0x0f, 0x5e, 5], "Actor teleport setting division");
        if (Word(code, fade + 0x54) != playerSeconds || Word(code, fade + 0x5e) != actorSeconds)
            throw new NotSupportedException("Fade timer changed its distinct winning typed setting operands.");
        Need(fade + 0x230, [0xf3, 0x0f, 0x58, 0x86, 0xe0, 3, 0, 0], "source fade addition");
        Need(fade + 0x23f, [0xf3, 0x0f, 0x11, 0x86, 0xe0, 3, 0, 0, 0x76, 0x14], "store before strict expiry");
        Need(opacity + 6, [0x80, 0x3d], "independent extra override global byte");
        RequireZero(Word(code, opacity + 8), 1, "override byte"); RequireZero(Word(code, opacity + 0x15), 4, "override mode");
        Need(opacity + 0x98, [0xf3, 0x0f, 0x59, 0x45, 0xfc], "base-opacity times fade");
        Need(opacity + 0xa5, [0xf3, 0x0f, 0x59, 5], "independent Player visibility alpha");
        RequireWord(Word(code, opacity + 0xa9), 0x3f800000, "Player visibility alpha loader");
        Need(opacity + 0xb7, [0xff, 0x90, 0x10, 4, 0, 0], "actual Player opacity virtual dispatch");
        byte[] mergeSignature = [0x0f, 0xb6, 0x4d, 0xd6, 0x84, 0xc0, 0xba, 1, 0, 0, 0, 0x0f, 0x45, 0xca, 0x80, 0x3d];
        var merge = Unique(at => Match(code, at, mergeSignature), 0x48, "returned-control local merge");
        Need(merge - 0x780, [0xc6, 0x45, 0xd6, 0], "actual earlier Boolean local constructor");
        Need(merge + 0x15, [0x89, 0x4d, 0xdc, 0x75, 9, 0x80, 0x3d], "merged word before second independent gate");
        RequireZero(Word(code, merge + 0x10), 1, "first teleport gate"); RequireZero(Word(code, merge + 0x1c), 1, "second teleport gate");
        Need(merge + 0x28, [0x8b, 0x80, 8, 6, 0, 0, 0xff, 0xd0, 0x85, 0xc0, 0x0f, 0x84], "lazy state query after nonzero gate");
        if (Call(merge + 0x3c) != fade) throw new NotSupportedException("Returned controls lost their actual High fade child.");
        RequireLogicalSceneConstruction(code, image);
        source.Validate(); return source;

        uint Class(string name, int slots) => One(image.SourceClassTables(name, slots), "opacity class " + name);
        uint Slot(uint table, uint slot) => Word(image.Read(table + slot, 4), 0);
        int Offset(uint address) => SourceOffset(code, image.CodeBase, address);
        int Call(int at) => at >= 0 && at <= code.Length - 5 && code[at] == 0xe8 ?
            checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) : throw new NotSupportedException("Opacity source lost a linked current child.");
        void Need(int at, byte[] pattern, string name)
        { if (at < 0 || !Match(code, at, pattern)) throw new NotSupportedException("Opacity source changed " + name + "."); }
        void RequireZero(uint address, int count, string name)
        { if (!image.IsWritableExtent(address, count) || image.ReadLoaderData(address, count).Any(value => value != 0)) throw new NotSupportedException("Opacity source lost its independent loader-zero " + name + "."); }
        void RequireWord(uint address, uint value, string name)
        { if (Word(image.ReadLoaderData(address, 4), 0) != value) throw new NotSupportedException("Opacity source changed " + name + "."); }
        uint Setting(string name, uint initial)
        {
            var row = One(settings.Where(setting => setting.Name == name && setting.Collection == SettingCollection.Game && setting.Payload == initial), "typed teleport GMST " + name);
            return row.Descriptor + 4;
        }
        bool Contains(int at, int count, byte[] pattern) => at >= 0 && at <= code.Length - count &&
            Enumerable.Range(at, count - pattern.Length + 1).Any(offset => Match(code, offset, pattern));
        int Unique(Func<int, bool> predicate, int tail, string name) => One(Enumerable.Range(0, code.Length - tail + 1).Where(predicate), name);
        static T One<T>(IEnumerable<T> values, string name)
        { var rows = values.Take(2).ToArray(); return rows.Length == 1 ? rows[0] : throw new NotSupportedException("Opacity source lacks a unique " + name + "."); }
    }

    private static void RequireLogicalSceneConstruction(byte[] code, Image image)
    {
        var factoryIndex = new[] { "NiNode", "NiTriShape", "NiTriStrips", "BSShaderProperty", "BSShaderPPLightingProperty", "BSShaderNoLightingProperty" }
            .ToDictionary(name => name, _ => new HashSet<uint>(), StringComparer.Ordinal);
        for (var at = 0; at <= code.Length - 11; at++)
            if (code[at] == 0x68 && code[at + 5] == 0x68 && code[at + 10] == 0xe8 &&
                image.Literal(Word(code, at + 6)) is { } registeredName && factoryIndex.TryGetValue(registeredName, out var registry))
                registry.Add(Word(code, at + 1));

        // The selected stream factories construct these exact primary classes.
        // Node, geometry and AVObject have different table extents; the data
        // following their last methods must not be mistaken for another slot.
        var nodeFactory = Factory("NiNode");
        Need(nodeFactory, [0x68, 0xac, 0, 0, 0, 0xe8], "node allocation");
        Need(nodeFactory + 0xf, [0x74, 0x0a, 0x6a, 0, 0x8b, 0xc8, 0xe8], "node factory constructor arm");
        var nodeCtor = Child(nodeFactory + 0x15);
        Need(nodeCtor, [0x55, 0x8b, 0xec, 0x56, 0x8b, 0xf1, 0xe8], "node inherited constructor");
        Need(nodeCtor + 0xb, [0x0f, 0xb7, 0x45, 8, 0x33, 0xc9, 0xc7, 6], "node primary table store");
        var node = Word(code, nodeCtor + 0x13);
        Table(node, "NiNode", 64);
        var avCtor = Child(nodeCtor + 6);
        Need(avCtor, [0x56, 0x8b, 0xf1, 0xe8], "AVObject inherited constructor");
        Need(avCtor + 8, [0xc7, 6], "AVObject primary table store");
        var av = Word(code, avCtor + 0xa);
        Table(av, "NiAVObject", 55);
        Need(avCtor + 0x4e, [0xc7, 0x46, 0x20, 0, 0, 0, 0], "AVObject nullable bound constructor");

        int? triBasedCtor = null;
        foreach (var name in new[] { "NiTriShape", "NiTriStrips" })
        {
            var factory = Factory(name);
            Need(factory, [0x56, 0x68, 0xc4, 0, 0, 0, 0xe8], "geometry allocation");
            Need(factory + 0x12, [0x74, 0x11, 0x8b, 0xce, 0xe8], "geometry factory constructor arm");
            Need(factory + 0x1b, [0xc7, 6], "geometry factory primary table store");
            Table(Word(code, factory + 0x1d), name, 61);
            var inherited = Child(factory + 0x16);
            if (triBasedCtor is { } previous && inherited != previous)
                throw new NotSupportedException("Logical scene geometry factories changed their shared inherited constructor.");
            triBasedCtor = inherited;
        }
        var triBased = triBasedCtor ?? throw new NotSupportedException("Logical scene has no selected geometry constructor.");
        Need(triBased, [0x56, 0x8b, 0xf1, 0xe8], "triangle geometry inherited constructor");
        Need(triBased + 8, [0xc7, 6], "triangle geometry primary table store");
        Table(Word(code, triBased + 0xa), "NiTriBasedGeom", 61);
        var geometryCtor = Child(triBased + 3);
        Need(geometryCtor, [0x56, 0x8b, 0xf1, 0xe8], "geometry inherited constructor");
        Need(geometryCtor + 8, [0x8d, 0x8e, 0x9c, 0, 0, 0, 0xc7, 6], "geometry primary table store");
        if (Child(geometryCtor + 3) != avCtor)
            throw new NotSupportedException("Logical scene node and geometry lost their same-source AVObject constructor.");
        var geometry = Word(code, geometryCtor + 0x10);
        Table(geometry, "NiGeometry", 60);
        var identity = Word(image.Read(node + 0xc, 4), 0); var absent = Word(image.Read(node + 0x18, 4), 0);
        if (Word(image.Read(geometry + 0x18, 4), 0) != identity || Word(image.Read(geometry + 0xc, 4), 0) != absent ||
            !Match(image.Read(identity, 3), 0, [0x8b, 0xc1, 0xc3]) || !Match(image.Read(absent, 3), 0, [0x33, 0xc0, 0xc3]))
            throw new NotSupportedException("Scene alpha lost its exact current node/geometry casts.");
        var load = SourceOffset(code, image.CodeBase, Word(image.Read(av + 0x4c, 4), 0));
        if (!Match(code, load + 0x30d, [0x8b, 7, 0x83, 0xe0, 0xef, 0x0d, 0x0e, 0, 8, 0, 0x89, 7]))
            throw new NotSupportedException("Logical NIF load lost its retained active/update flags.");
        var constructors = new Dictionary<string, int>();
        foreach (var name in new[] { "BSShaderProperty", "BSShaderPPLightingProperty", "BSShaderNoLightingProperty" })
        {
            var factory = Factory(name);
            var atCall = factory + (name == "BSShaderProperty" ? 0x3d : 0x40);
            constructors.Add(name, Child(atCall));
        }
        var baseCtor = constructors["BSShaderProperty"];
        if (!Match(code, baseCtor + 0x12, [0xc7, 0x46, 0x1c, 0xff, 0xff, 0xff, 0xff]) ||
            !Match(code, baseCtor + 0x3a, [0xc7, 0x46, 0x2c, 0, 0, 0x80, 0x3f]) ||
            !Match(code, constructors["BSShaderPPLightingProperty"] + 0x279, [0xc7, 0x47, 0x1c, 7, 0, 0, 0]) ||
            !Match(code, constructors["BSShaderNoLightingProperty"] + 0x40, [0xc7, 0x46, 0x1c, 0x14, 0, 0, 0]))
            throw new NotSupportedException("Logical shader changed its independent runtime kind/alpha constructors.");
        if (FalloutNifSceneMath.MergeCenterEpsilonBits != 0x358637bd)
            throw new InvalidDataException("Logical bound math changed its reviewed source merge threshold.");

        int Factory(string name)
        {
            var factories = factoryIndex[name];
            if (factories.Count != 1) throw new NotSupportedException("Logical scene lost its exact selected source factory: " + name);
            return SourceOffset(code, image.CodeBase, factories.Single());
        }
        int Child(int at)
        {
            if (at < 0 || at > code.Length - 5 || code[at] != 0xe8)
                throw new NotSupportedException("Logical scene lost a linked constructor child.");
            return SourceOffset(code, image.CodeBase, checked(image.CodeBase + (uint)(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)))));
        }
        void Need(int at, byte[] pattern, string name)
        {
            if (at < 0 || !Match(code, at, pattern)) throw new NotSupportedException("Logical scene changed its source " + name + ".");
        }
        void Table(uint table, string name, int slots)
        {
            if (image.SourceClassTables(".?AV" + name + "@@", slots).Count(candidate => candidate == table) != 1 ||
                image.IsExecutableExtent(Word(image.Read(checked(table + (uint)(slots * 4)), 4), 0)))
                throw new NotSupportedException("Logical scene constructor-selected table has no exact source class/extent association: " + name);
        }
    }
}
