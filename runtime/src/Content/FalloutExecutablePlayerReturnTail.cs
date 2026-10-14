using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutPlayerReturnTailSource ReadPlayerReturnTail(string path,
        FalloutPlayerActorMovementSource movement, FalloutStandalonePlayerSceneSource scene)
    {
        var source = FalloutPlayerReturnTailSource.Reviewed(movement, scene); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != scene.Main.EngineSha256)
            throw new InvalidDataException("Player return tail changed its selected original source bytes.");
        _ = ReadPlayerActorMovement(path, movement.Mover, movement.Cells, movement.Orientation);
        _ = ReadPlayerSceneClock(path, scene.Main);
        _ = ReadReferenceScriptExtraInitializer(path, movement.Mover.Controller.Fields);
        var (code, image) = Load(original); var player = Table(".?AVPlayerCharacter@@", 191);
        var high = Table(".?AVHighProcess@@", 465); var middle = Table(".?AVMiddleHighProcess@@", 465);
        var frame = Get(player, 0x2f8);
        Need(frame + 0xca2, [0x80, 0xbf, 0xe5, 6, 0, 0, 0, 0x0f, 0x85], "same following-Player selected late branch");
        var late = Near(frame + 0xca9); var tail = late + 0x285;
        Need(tail - 0x16, [0x8b, 0x8f, 0x84, 1, 0, 0], "same mover before the following tail");
        Need(tail - 3, [0xff, 0x50, 0x14], "returned mover delta before death-delay read");
        Need(tail, [0xf3, 0x0f, 0x10, 5], "winning death-delay Float32 before raw Life");
        RequireSetting(Word(code, tail + 4), FalloutPlayerReturnTailSource.DeathDelayName, 0x40a00000);
        Need(tail + 0xe, [0x8b, 0x87, 0xfc, 0, 0, 0, 0x76], "same raw Life before positive-delay branch");
        Need(tail + 0x16, [0x83, 0xf8, 2, 0x74, 0x14, 0x83, 0xf8, 1, 0x74, 0xf], "raw Life two/one before living overwrite");
        Need(tail + 0x20, [0xc7, 5], "unconditional living shared-timer overwrite");
        var deathTimer = Word(code, tail + 0x22);
        if (!image.IsWritableExtent(deathTimer, 4) || Word(image.ReadLoaderData(deathTimer, 4), 0) != FalloutPlayerReturnTailSource.InitialDeathTimerBits ||
            Word(code, tail + 0x26) != FalloutPlayerReturnTailSource.InitialDeathTimerBits)
            throw new NotSupportedException("Player death timer lost its actual shared loader-minus-one and overwrite value.");
        Need(tail + 0x2f, [0xf3, 0x0f, 0x10, 5], "death-arm current timer read");
        if (Word(code, tail + 0x33) != deathTimer) throw new NotSupportedException("Player death countdown borrowed another timer.");
        Need(tail + 0x51, [0xf3, 0x0f, 0x5c, 0x43, 8], "same cached Scene argument subtraction before death-timer store");
        Need(tail + 0xfb, [0x8b, 7, 0x8b, 0xcf, 0xff, 0x90, 0xe0, 1, 0, 0], "mandatory selected Player animation call after death arms");

        var animation = Get(player, 0x1e0);
        Need(animation, [0x55, 0x8b, 0xec, 0x51, 0xfe, 5], "selected updater enters shared byte before clock");
        var bracket = Word(code, animation + 6);
        Need(animation + 0xd, [0xb9], "same current clock-manager receiver");
        var clock = Call(animation + 0x12);
        Need(clock, [0x55, 0x8b, 0xec, 0x51, 0x56, 0x8b, 0xf1, 0xc7, 0x45, 0xfc, 0, 0, 0x80, 0x3f], "actual current ratio/time-multiplier getter");
        Need(animation + 0x17, [0xd8, 0x0d], "actual cached Main timer multiplication");
        Need(animation + 0x22, [0xe8], "fresh direct Common animation query before direct first pointer");
        var wrapper = Call(animation + 0x22);
        Need(wrapper, [0x8b, 0x49, 0x60, 0x85, 0xc9, 0x74, 8, 0x8b, 1, 0xff, 0xa0, 0xb4, 1, 0, 0, 0x33, 0xc0, 0xc3], "same current Common nullable-field wrapper");
        Need(animation + 0x27, [0x85, 0xc0, 0x74, 0x1d], "independent current Common null arm");
        var contextUpdate = Call(animation + 0x43);
        Need(animation + 0x48, [0x8b, 0x8e, 0xec, 5, 0, 0, 0x85, 0xc9, 0x74, 0x1b], "independent direct first-person AnimData field");
        if (Call(animation + 0x68) != contextUpdate)
            throw new NotSupportedException("The two Player animation contexts selected different update bodies.");
        Need(animation + 0x6d, [0x8b, 0x4e, 0x60, 0x85, 0xc9, 0x74, 0x14], "fresh actual process read before pair queue");
        Need(animation + 0x82, [0xff, 0x90, 0x34, 7, 0, 0], "selected process pair-queue virtual");
        Need(animation + 0x88, [0x8b, 6, 0x8b, 0xce, 0xff, 0x90, 0x78, 1, 0, 0, 0xfe, 0x0d], "mandatory reference Extra child before shared-byte decrement");
        if (Word(code, animation + 0x94) != bracket)
            throw new NotSupportedException("Player animation did not decrement the byte it actually entered.");
        Need(animation + 0x98, [0x5e, 0x8b, 0xe5, 0x5d, 0xc3], "actual complete selected animation return");
        if (!image.IsWritableExtent(bracket, 1) || image.ReadLoaderData(bracket, 1)[0] != 0)
            throw new NotSupportedException("Shared Player/Scene bracket lost its actual loader zero.");
        // Derive the outer wrapper from the same actual selected virtual call,
        // current timer cell and bracket; do not allocate a second counter.
        var wrappers = new List<int>();
        for (var at = 0; at <= code.Length - 0x29; at++)
            if (Match(code, at, [0xfe, 5]) && Word(code, at + 2) == bracket &&
                Match(code, at + 6, [0x8b, 0x30, 0xe8]) && Call(at + 8) == clock &&
                Match(code, at + 13, [0xd8, 0x0d]) && Word(code, at + 15) == Word(code, animation + 0x19) &&
                Match(code, at + 0x1d, [0xff, 0x96, 0xf8, 2, 0, 0, 0xfe, 0x0d]) && Word(code, at + 0x25) == bracket)
                wrappers.Add(at);
        if (wrappers is not [var outer]) throw new NotSupportedException("Animation bracket is not the actual SAME outer Scene byte.");
        _ = outer;

        var queue = Get(high, 0x734);
        Need(queue + 0x28, [0x83, 0x3d], "High pair queue current clock kind before list");
        Need(queue + 0x34, [0x75, 0xa], "High kind-four setting selector");
        RequireSetting(Word(code, queue + 0x3a), FalloutPlayerReturnTailSource.PairVatsDelayName, 0x3e800000);
        RequireSetting(Word(code, queue + 0x44), FalloutPlayerReturnTailSource.PairDelayName, 0x3e800000);
        Need(queue + 0x48, [0x8d, 0xb1, 0xb0, 1, 0, 0], "actual inherited inline queue head");
        Need(queue + 0x60, [0x83, 0x7e, 4, 0, 0x8d, 0x46, 4], "queue next before head predicate");
        Need(queue + 0x6c, [0x83, 0x3e, 0, 0x0f, 0x84], "queue head-null arm after next-null");
        Need(queue + 0x22a, [0x8b, 0x4d, 0xf4, 0x64, 0x89, 0x0d, 0, 0, 0, 0, 0x59, 0x5f, 0x5e, 0x5b, 0x8b, 0xe5, 0x5d, 0xc2, 8, 0], "High empty queue complete return without node allocation");
        if (Get(middle, 0x734) != queue) throw new NotSupportedException("MiddleHigh pair queue no longer owns the same inherited implementation.");
        foreach (var name in new[] { ".?AVBaseProcess@@", ".?AVLowProcess@@", ".?AVMiddleLowProcess@@" })
            Need(Get(Table(name, 465), 0x734), [0xc2, 8, 0], "lower process pair virtual pure return");
        var constructors = new List<int>();
        for (var at = 0; at <= code.Length - 0x79; at++)
            if (Match(code, at, [0xc7, 6]) && Word(code, at + 2) == middle &&
                Match(code, at + 0x62, [0x33, 0xc0]) &&
                Match(code, at + 0x72, [0x89, 0x86, 0xb0, 1, 0, 0, 0x89, 0x86, 0xb4, 1, 0, 0])) constructors.Add(at);
        if (constructors is not [_]) throw new NotSupportedException("Animation pair queue has no unique actual inherited null/null constructor.");

        var extra = Get(player, 0x178); var lookup = Call(extra + 7);
        Need(extra, [0x53, 0x56, 0x8b, 0xf1, 0x8d, 0x4e, 0x40, 0xe8], "actual reference Extra list receiver before node query");
        Need(lookup, [0x6a, FalloutPlayerReturnTailSource.AnimationNodeExtraKind, 0xe8], "actual subtype41 source query");
        Need(lookup + 7, [0x85, 0xc0, 0x74, 4, 0x8b, 0x40, 0xc, 0xc3, 0x33, 0xc0, 0xc3], "missing Extra or null payload getter law");
        Need(extra + 0xc, [0x8b, 0xd8, 0x85, 0xdb, 0x0f, 0x84], "returned null payload skips Player view-root swap");
        Need(extra + 0xd6, [0x5e, 0x5b, 0xc3], "actual absent animation-Extra complete return");

        RequireContextConstruction(); RequireSourceExit(); return source;

        void RequireContextConstruction()
        {
            var load = Get(player, 0x1c8);
            Need(load + 0x375, [0x68, 0x30, 1, 0, 0, 0xb9], "actual first-AnimData allocation before reference load");
            Need(load + 0x39c, [0x8b, 0xc8, 0xe8], "actual returned allocation receiver before AnimData constructor");
            var construct = Call(load + 0x39e);
            Need(load + 0x3ad, [0x89, 0x87, 0xec, 5, 0, 0], "actual first pointer source store before third load");
            Need(load + 0x3c5, [0xc6, 0x87, 0xa8, 5, 0, 0, 1, 0xe8], "temporary view-one before inherited third-person loader");
            var characterLoad = Call(load + 0x3cc); var actorLoad = Call(characterLoad + 0xa);
            var referenceLoad = Call(actorLoad + 0xb);
            Need(referenceLoad, [0x55, 0x8b, 0xec, 0x6a, 0xff, 0x68], "same actual inherited reference loader");
            Need(load + 0x3dd, [0x8b, 0xcf, 0x89, 0x85, 0x94, 0xfe, 0xff, 0xff, 0xe8], "current Common context after third load returns");
            if (Call(load + 0x3e5) != wrapper) throw new NotSupportedException("3D load and frame borrow different current Common animation fields.");
            Need(load + 0x4e3, [0x8b, 0x8f, 0xec, 5, 0, 0, 0x6a, 1, 0x57], "actual stored first context before independent model/group binding");
            var bind = Call(load + 0x4f8);
            Need(bind + 0x9c, [0x89, 0x47, 4], "actual Actor publication before model root association");
            Need(bind + 0xe0, [0x89, 0x4f, 8, 0xf0, 0xff, 0x41, 4], "model-root store before acquire and independent controller construction");
            Need(construct + 0x2c, [0xc7, 0x46, 8, 0, 0, 0, 0], "real context constructor null root");
            Need(construct + 0x44, [0xc7, 0x86, 4, 1, 0, 0, 0, 0, 0, 0, 0xc7, 0x86, 8, 1, 0, 0, 0, 0, 0, 0], "independent pending source fields");
            Need(construct + 0x71, [0x68], "actual two-element retained-pair constructor association");
            var pairConstructor = SourceOffset(code, image.CodeBase, Word(code, construct + 0x72));
            Need(pairConstructor, [0xc7, 1, 0, 0, 0, 0, 0xc3], "actual retained-pair element constructor null store");
            Need(construct + 0x76, [0x6a, 2, 0x8d, 0x86, 0x20, 1, 0, 0], "actual two distinct current pair slots");
            Need(construct + 0x8a, [0xc7, 0x86, 0x28, 1, 0, 0, 0, 0, 0, 0, 0xc7, 0x86, 0x2c, 1, 0, 0, 0, 0, 0, 0], "actual timed list null/null cells");
            Need(construct + 0x1f2, [0xc7, 0x47, 4, FalloutPlayerReturnTailSource.AnimationGroupBuckets, 0, 0, 0], "actual semantic group-map bucket count");
            Need(construct + 0x1f9, [0xc7, 0x47, 0xc, 0, 0, 0, 0], "actual semantic group-map constructor entry count zero");
            Need(construct + 0x231, [0xb8, 0xff, 0, 0, 0], "actual requested-group sentinel producer");
            Need(construct + 0x297, [0x66, 0x89, 0x86, 0x16, 1, 0, 0], "actual requested-group sentinel store");
            Need(contextUpdate + 0x30, [0x83, 0xbf, 8, 1, 0, 0, 0], "pending source work before requested-group test");
            Need(contextUpdate + 0x55, [0x66, 0x39, 0x87, 0x16, 1, 0, 0], "requested-group sentinel comparison");
            Need(contextUpdate + 0x100, [0x83, 0xbf, 0x2c, 1, 0, 0, 0], "timed next-null before head-null");
            Need(Get(player, 0x100), [0xb0, 1, 0xc3], "actual selected Player Character predicate independent of Life");
            Need(contextUpdate + 0x389, [0x8b, 0x4f, 8, 0x85, 0xc9, 0x0f, 0x84], "independent actual context root-null return after pending/pair work");
        }
        void RequireSourceExit()
        {
            var exit = frame + 0x3ebb;
            // Exact actual epilogue is additionally qualified by the existing
            // ThreadContext reader, reached from the same frame/prefix family.
            Need(exit, [0x8b, 0x0d], "actual current-thread index before source epilogue");
            Need(exit + 0x1a, [0x89, 0xb7, 0x80, 2, 0, 0], "actual saved-kind restore before Player return");
            Need(exit + 0x30, [0x8b, 0xe3, 0x5b, 0xc2, 4, 0], "same complete Player virtual return after restore");
        }
        void RequireSetting(uint value, string name, uint initial)
        {
            if (ReadStaticSettings(image).SingleOrDefault(row => row.Descriptor + 4 == value) is not
                { Collection: SettingCollection.Game } setting || setting.Name != name || setting.Payload != initial)
                throw new NotSupportedException("Player tail changed its actual winning mutable GameSetting descriptor: " + name);
        }
        uint Table(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
            throw new NotSupportedException("Player tail source class is absent or ambiguous: " + name);
        int Get(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
        int Call(int at) => at >= 0 && at <= code.Length - 5 && code[at] == 0xe8 ?
            checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Player return tail lost an actual selected direct child association.");
        int Near(int at) => checked(at + 6 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 2)));
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Player return tail changed " + role + "."); }
    }
}
