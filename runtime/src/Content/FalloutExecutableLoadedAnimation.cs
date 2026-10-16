using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private const int MaximumQualifiedPlayerAnimationSources = 8;
    private static readonly Lock LoadedAnimationQualificationLock = new();
    private static readonly Dictionary<string, FalloutLoadedAnimationSource> QualifiedLoadedAnimations = [];
    internal static FalloutLoadedAnimationSource ReadLoadedPlayerAnimation(string path, FalloutPlayerReturnTailSource tail)
    {
        tail.Validate(); var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != tail.Scene.Main.EngineSha256)
            throw new InvalidDataException("Loaded animation changed the selected original executable.");
        lock (LoadedAnimationQualificationLock)
        {
            if (QualifiedLoadedAnimations.TryGetValue(tail.Identity, out var qualified)) return qualified;
            _ = ReadPlayerReturnTail(path, tail.Movement, tail.Scene);
            var (code, image) = Load(bytes);
            var player = Table(".?AVPlayerCharacter@@", 191); var high = Table(".?AVHighProcess@@", 520);
            var load = Get(player, 0x1c8); var firstConstruct = Call(load + 0x39e);
            Need(load + 0x83, [0xc7, 0x80, 0x80, 2, 0, 0, 52, 0, 0, 0], "Player loader kind fifty-two");
            Need(load + 0x3ad, [0x89, 0x87, 0xec, 5, 0, 0], "first context store before inherited loader");
            Need(load + 0x165, [0x89, 0x8f, 0xf0, 5, 0, 0], "first model root store independent of resources and context");
            Need(load + 0x171, [0xf0, 0xff, 0x41, 4], "actual first model retain before animation allocations");
            Need(load + 0x383, [0x89, 0x87, 0xe8, 5, 0, 0], "actual returned first animation resource store");
            Need(load + 0x3c5, [0xc6, 0x87, 0xa8, 5, 0, 0, 1], "temporary source view one before inherited Character load");
            Need(load + 0x3d7, [0x88, 0x8f, 0xa8, 5, 0, 0], "exact saved source view restore after inherited Character load");
            Need(load + 0x4e3, [0x8b, 0x8f, 0xec, 5, 0, 0], "same constructed first context receives later first animation resource list");
            Need(load + 0x50c, [0x8b, 0x80, 0x48, 1, 0, 0], "following current weapon getter precedes setter");
            Need(load + 0x51d, [0xff, 0x90, 0x60, 1, 0, 0], "following same weapon setter child");
            Need(load + 0x52b, [0x83, 0x49, 0x30, 1], "following inherited model active flag store");
            Need(load + 0x56d, [0xff, 0x90, 0x6c, 4, 0, 0], "following actual current Common child");
            Need(load + 0x584, [0xff, 0x90, 0xc4, 1, 0, 0], "following selected Player child before controller attachment");
            Need(load + 0x60b, [0xff, 0x92, 0xd0, 0, 0, 0], "following nonnull controller resource attachment child");
            Need(load + 0x621, [0xc6, 0x87, 0xa, 7, 0, 0, 1], "following same toddler field store before actual animation reload");
            Need(load + 0x657, [0x89, 0xb7, 0x80, 2, 0, 0], "enclosing loader actual prior source kind restore");
            var character = Call(load + 0x3cc); var actor = Call(character + 0xa); var reference = Call(actor + 0xb);
            var inherited = Call(reference + 0x65f);
            Need(inherited + 0x3d4, [0xe8], "NPC current animation-resource cleanup before context allocation");
            Need(inherited + 0x3fb, [0xe8], "NPC inherited actual constructor association");
            if (Call(inherited + 0x3fb) != firstConstruct)
                throw new NotSupportedException("First/Common animation use different source constructors.");
            var publish = Call(inherited + 0x40e);
            Need(publish + 0x36, [0x8b, 0x50, 0x28, 0x8b, 0xc8, 0x83, 0xfa, 1], "actual current tier before Common publication");
            Need(publish + 0x65, [0xff, 0x92, 8, 8, 0, 0], "same current Common pointer setter virtual");
            var resourceConstruct = Call(load + 0x2bf);
            Need(resourceConstruct + 0x2b, [0x68, 0x6c, 1, 0, 0, 0x6a, 0, 0x56, 0xe8], "actual first resource buffer clear");
            Need(resourceConstruct + 0x38, [0x68, 0x40, 1, 0, 0, 0x8d, 0x86, 0x6c, 1, 0, 0], "actual twenty-slot secondary constructor clear");
            var resourceCleanup = Call(inherited + 0x3d4);
            Need(resourceCleanup + 0x17, [0x8d, 0x77, 0x34, 0xbb, 20, 0, 0, 0], "actual current resource primary slots");
            Need(resourceCleanup + 0x155, [0x83, 0xc6, 0x10, 0x83, 0xeb, 1], "bounded source resource cleanup stride and extent");
            Need(Get(player, 0x1e8), [0x80, 0xb9, 0xa8, 5, 0, 0, 0, 0x75, 0xa, 0x8b, 0x81, 0xe8, 5, 0, 0], "actual source view and resource field selector");
            var toddlerCommand = image.ScriptCommandStart("SetPCToddler", code);
            Need(toddlerCommand + 0x34, [0x83, 0x7d, 0xfc, 0], "same existing ScriptSession nonzero toddler flag law");
            var toddlerSetter = Call(toddlerCommand + 0x45);
            Need(toddlerSetter, [0x55, 0x8b, 0xec, 0x8a, 0x45, 8, 0x38, 0x81, 0xa, 7, 0, 0], "same existing toddler field comparison used by the first list");
            Need(toddlerSetter + 0x10, [0x88, 0x81, 0xa, 7, 0, 0], "same current toddler store before independent animation reload");
            var setter = Get(high, 0x808);
            Need(setter + 6, [0x83, 0xbe, 0xc0, 1, 0, 0, 0, 0x74], "selected Common animation setter old-null arm");
            var store = Call(setter + 0x26);
            Need(store + 0xa, [0x8b, 0x86, 0xc0, 1, 0, 0, 0x85, 0xc0], "same animation pointer current cell");
            Need(store + 0x14, [0x3b, 0xc7, 0x74], "same pointer skips all release stores");
            Need(store + 0x36, [0x89, 0xbe, 0xc0, 1, 0, 0], "same Common pointer publication");

            var bind = Call(load + 0x4f8);
            Need(bind + 0x6b, [0xc7, 0x80, 0x80, 2, 0, 0, 51, 0, 0, 0], "binding kind fifty-one");
            Need(bind + 0xea, [0xbf], "named-node source table receiver");
            var nodeTable = Word(code, bind + 0xeb);
            Need(bind + 0x11a, [0x81, 0xff], "inclusive named-node source table end");
            if (Word(code, bind + 0x11c) != nodeTable + 7 * 4)
                throw new NotSupportedException("Animation bind changed its eight actual nullable named-node slots.");
            Need(bind + 0x12a, [0x83, 0x60, 0x30, 0xfd], "same current source root flag clear");
            var manager = Call(bind + 0x148);
            Need(manager + 0xc, [0x6a, 10, 0x6a, 0, 0x8d, 0x4e, 0x34], "real manager source sequence capacity");
            Need(manager + 0x3f, [0xc7, 0x46, 0x54, 37, 0, 0, 0], "real manager name-map constructor");
            Need(manager + 0xad, [0x6a, 79, 0x57, 0x8b, 0xc8, 0xe8], "real palette source capacity and root");
            Need(bind + 0x1d1, [0xe8], "same real per-resource registration child");
            var registration = Call(bind + 0x1d1);
            Need(registration + 0x197, [0x8d, 4, 0xc0, 0x80, 0x3c, 0x85], "group source row extent and multiplicity field");
            var groupTable = Word(code, registration + 0x19d) - 4;
            var sourceNameReference = new byte[] { 0xff, 0x34, 0x85 }.Concat(BitConverter.GetBytes(groupTable)).ToArray();
            var matches = new List<int>();
            for (var at = 0; at <= code.Length - 0x40; at++)
                if (Match(code, at, sourceNameReference) && Match(code, at + 0x2f, [0x81, 0xf9, 183, 0, 0, 0])) matches.Add(at);
            if (matches is not [var groupNames]) throw new NotSupportedException("Animation groups lost their actual bounded name parser.");
            var parser = groupNames - 0x4de;
            Need(parser, [0x55, 0x8b, 0xec, 0x6a, 0xff, 0x68], "actual group parser entry derived from the selected group table");
            Need(parser + 0x1c0, [0x8b, 0x14, 0xb5], "movement prefix table");
            Need(parser + 0x210, [0x8b, 0x14, 0xb5], "weapon prefix table");
            var movementTable = Word(code, parser + 0x1c3); var weaponTable = Word(code, parser + 0x213);
            Need(firstConstruct + 0x11b, [0xc7, 0x86, 0xd0, 0, 0, 0, 0, 0, 0, 0], "independent source animation clock constructor zero");
            Need(firstConstruct + 0x24f, [0x0f, 0x11, 0x46, 0x4c], "same current group words constructor");
            var words = image.Read(Word(code, firstConstruct + 0x24b), 16);
            if (words.Any(value => value != byte.MaxValue))
                throw new NotSupportedException("Source current group constructor lost its all-one word sentinel.");
            // The original loader's setting descriptor selects the first model;
            // presentation's configured skeleton path cannot replace that getter.
            Need(load + 0x407, [0xff, 0x35], "winning mutable first skeleton setting read");
            var payload = Word(code, load + 0x409);
            if (ReadStaticSettings(image).SingleOrDefault(setting => setting.Descriptor + 4 == payload) is not
                { Collection: SettingCollection.Game } setting || setting.Name != FalloutLoadedAnimationSource.FirstSkeletonSetting)
                throw new NotSupportedException("Player first skeleton has no actual source setting binding.");
            var nodes = Enumerable.Range(0, 8).Select(index => Name(Word(image.Read(nodeTable + checked((uint)index * 4), 4), 0))).ToArray();
            var groups = Enumerable.Range(0, FalloutLoadedAnimationSource.GroupCount).Select(index =>
            {
                var row = image.Read(groupTable + checked((uint)index * 36), 36);
                return new FalloutLoadedAnimationGroup(checked((byte)index), Name(Word(row, 0)) ??
                    throw new InvalidDataException("Source animation group name is null."), row[4] != 0,
                    unchecked((int)Word(row, 12)), Enumerable.Range(0, 5).Select(slot => unchecked((int)Word(row, 16 + slot * 4))).ToArray());
            }).ToArray();
            var result = new FalloutLoadedAnimationSource(tail, FalloutLoadedAnimationSource.ContractDigest, nodes,
                Prefixes(movementTable, 4), Prefixes(weaponTable, 12), groups,
                Name(Word(code, resourceConstruct + 0x6f)) ?? throw new InvalidDataException("Resource root name is null."),
                Prefixes(Word(code, resourceConstruct + 0x7f), 5));
            result.Validate();
            // Evict only declaration lookup ownership. Living models retain
            // their own immutable source declarations independently.
            if (QualifiedLoadedAnimations.Count == MaximumQualifiedPlayerAnimationSources) QualifiedLoadedAnimations.Clear();
            QualifiedLoadedAnimations.Add(tail.Identity, result); return result;

            string? Name(uint pointer) => pointer == 0 ? null : image.Literal(pointer) ?? throw new InvalidDataException("Animation name has no actual source string.");
            string[] Prefixes(uint table, int count) => Enumerable.Range(0, count).Select(index =>
                Name(Word(image.Read(table + checked((uint)index * 4), 4), 0)) ?? throw new InvalidDataException("Animation prefix is null.")).ToArray();
            uint Table(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
                throw new NotSupportedException("Animation source class is absent or ambiguous: " + name);
            int Get(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
            int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("Animation source lost its actual direct child association.");
            void Need(int at, byte[] expected, string role)
            { if (!Match(code, at, expected)) throw new NotSupportedException("Loaded animation changed " + role + "."); }
        }
    }
}
