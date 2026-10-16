using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock PlayerModelContinuationQualificationLock = new();
    private static readonly Dictionary<string, FalloutPlayerModelLoadContinuationSource> QualifiedPlayerModelContinuations = [];
    internal static FalloutPlayerModelLoadContinuationSource ReadPlayerModelLoadContinuation(string path,
        FalloutLoadedAnimationSource animation)
    {
        animation.Validate(); var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != animation.Tail.Scene.Main.EngineSha256)
            throw new InvalidDataException("Player model continuation changed its selected executable.");
        lock (PlayerModelContinuationQualificationLock)
        {
            if (QualifiedPlayerModelContinuations.TryGetValue(animation.Identity, out var qualified)) return qualified;
            if (ReadLoadedPlayerAnimation(path, animation.Tail).Identity != animation.Identity)
                throw new InvalidDataException("Player model continuation changed its qualified preceding loader.");
            var (code, image) = Load(bytes);
            var player = Table(".?AVPlayerCharacter@@", 191); var high = Table(".?AVHighProcess@@", 520);
            var middle = Table(".?AVMiddleHighProcess@@", 520); var load = Slot(player, 0x1c8);
            Need(load + 0x139, [0x81, 0x49, 0x30, 0, 0, 0x20, 0], "first returned source root independent flag");
            Need(load + 0x165, [0x89, 0x8f, 0xf0, 5, 0, 0], "independent current Player first model pointer store");
            Need(load + 0x171, [0xf0, 0xff, 0x41, 4], "actual current first model field retain");
            var firstNames = new[] { 0x17f, 0x18d, 0x1a8 }.Select(at =>
            {
                Need(load + at, [0xff, 0x35], "actual first named query source input");
                return InternedName(Word(code, load + at + 2));
            }).ToArray();
            Need(load + 0x234, [0x8b, 0x80, 0x94, 0, 0, 0, 0x89, 0x87, 0xf4, 5, 0, 0], "current named first camera world-height field store");
            Need(load + 0x240, [0x0f, 0x10, 5], "selected original identity matrix input");
            var identity = image.Read(Word(code, load + 0x243), 36);
            if (!Enumerable.Range(0, 9).Select(index => Word(identity, index * 4)).SequenceEqual(
                    new uint[] { 0x3f800000, 0, 0, 0, 0x3f800000, 0, 0, 0, 0x3f800000 }))
                throw new NotSupportedException("First model rotation changed its actual source identity matrix.");
            var matrixCopy = Call(load + 0x275);
            Need(matrixCopy, [0x55, 0x8b, 0xec, 0x8b, 0x4d, 0xc, 0x8b, 0x45, 8, 0x0f, 0x10, 1, 0x0f, 0x11, 0, 0x0f, 0x10, 0x41, 0x10], "actual 36-byte copy does not read reference angles");
            Need(load + 0x288, [0x0f, 0x11, 0x41, 0x34], "same first model local rotation publication");
            _ = Call(load + 0x228);
            var initialControllers = QualifyInitialModelTransformControllers(code);
            var transformTable = Table(".?AVNiTransformController@@", 42);
            if (Enumerable.Range(0, 42).Count(index => Slot(transformTable, checked((uint)index * 4)) == initialControllers.Update) != 1 ||
                Slot(transformTable, 0xa4) != initialControllers.Clock)
                throw new NotSupportedException("Initial model update/clock lost their actual NiTransformController dispatch.");
            Need(load + 0x500, [0x8b, 0xcf, 0x6a, 0], "current process weapon zero-argument caller");
            Need(load + 0x50c, [0x8b, 0x80, 0x48, 1, 0, 0, 0xff, 0xd0, 0x50], "getter returns the exact setter input");
            Need(load + 0x51d, [0xff, 0x90, 0x60, 1, 0, 0], "same process selected weapon setter");
            var weapon = Slot(high, 0x160);
            if (weapon != Slot(middle, 0x160)) throw new NotSupportedException("Weapon setter changed its shared High/MiddleHigh body.");
            Need(weapon + 0x63, [0xc7, 0x87, 0x80, 2, 0, 0, 50, 0, 0, 0], "weapon setter actual thread kind");
            Need(weapon + 0x6d, [0x8b, 0x83, 0x14, 1, 0, 0], "same current weapon requery");
            Need(weapon + 0x7d, [0x3b, 0xc7, 0x0f, 0x84], "weapon equality skips replacement only");
            if (weapon + 0x85 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(weapon + 0x81)) != weapon + 0x2f6)
                throw new NotSupportedException("Equal weapon changed its mandatory tail target.");
            Need(weapon + 0x2f6, [0x8b, 0x8b, 0xc0, 1, 0, 0], "weapon equality still reads actual Common AnimData");
            Need(weapon + 0x300, [0x83, 0x7d, 0xc, 0, 0x74], "zero argument takes scalar/global tail");
            Need(weapon + 0x36e, [0xc7, 0x83, 0x2c, 1, 0, 0, 0, 0, 0, 0, 0xc7, 0x83, 0x30, 1, 0, 0, 0, 0, 0, 0], "same Common weapon scalar stores");
            Need(weapon + 0x38c, [0xa1], "current Player weapon global read before dominating same-process clear");
            Need(weapon + 0x393, [0x39, 0x59, 0x60, 0x0f, 0x44, 0xc2, 0xa3], "actual Player Common pointer association");
            Need(weapon + 0x3a2, [0x6a, 0xff, 0xff, 0x90, 0x74, 7, 0, 0], "selected signed selection reset follows global");
            Need(weapon + 0x3bd, [0x89, 0xb7, 0x80, 2, 0, 0], "weapon scope restores actual prior kind");
            Need(Slot(high, 0x774), [0x55, 0x8b, 0xec, 0x8b, 0x45, 8, 0x89, 0x81, 0x44, 2, 0, 0, 0x5d, 0xc2, 4, 0], "same current Common selection cell");
            var constructors = new List<int>();
            for (var at = 0; at <= code.Length - 0x3fe; ++at)
                if (Match(code, at, [0xc7, 6]) && Word(code, at + 2) == middle &&
                    Match(code, at + 0xcc, [0xc7, 0x86, 0x14, 1, 0, 0, 0, 0, 0, 0])) constructors.Add(at);
            if (constructors is not [var constructor]) throw new NotSupportedException("Model-load fields have no actual unique process constructor.");
            Need(constructor + 0xe0, [0xc7, 0x86, 0x30, 1, 0, 0, 0, 0, 0, 0], "independent weapon scalar constructor");
            Need(constructor + 0xea, [0xc7, 0x86, 0x2c, 1, 0, 0, 0, 0, 0, 0], "independent weapon word constructor");
            Need(constructor + 0x3f4, [0xc7, 0x86, 0x44, 2, 0, 0, 0xff, 0xff, 0xff, 0xff], "actual selection constructor sentinel");
            Need(load + 0x52b, [0x83, 0x49, 0x30, 1, 0xff, 0x35], "same returned third root active flag before named lookup");
            Need(load + 0x535, [0x8b, 0x80, 0x9c, 0, 0, 0, 0xff, 0xd0], "actual returned-root name virtual");
            var cameraName = InternedName(Word(code, load + 0x531));
            Need(load + 0x543, [0xa3], "actual camera-node global publication");
            Need(load + 0x548, [0x85, 0xc0, 0x75, 0x15], "missing named camera reaches its diagnostic child");
            _ = Call(load + 0x54e); _ = Call(load + 0x559);
            var magic = Call(load + 0x563);
            Need(magic + 0x27, [0x8b, 0x73, 0x1c, 0x81, 0xc6, 0x80, 0, 0, 0], "actual current Base inline spell list");
            Need(magic + 0x65, [0xff, 0x90, 0x78, 3, 0, 0], "current race virtual before second read");
            Need(magic + 0x73, [0xff, 0x90, 0x78, 3, 0, 0], "repeated actual nonnull race virtual");
            Need(Slot(player, 0x378), [0x8b, 0x41, 0x1c, 0x8b, 0x80, 0x10, 1, 0, 0, 0xc3], "same current Base race field");
            Need(magic + 0x7b, [0x83, 0xc6, 0x30], "current race inline spell list");
            var worn = Call(magic + 0xca);
            Need(worn + 0x63, [0xc7, 0x83, 0x80, 2, 0, 0, 54, 0, 0, 0], "worn-entry actual nested thread kind");
            Need(magic + 0xc0, [0x6a, 0, 0xff, 0xb6], "actual five worn slot input table");
            var slots = Word(code, magic + 0xc4);
            for (var index = 0; index < 5; ++index)
                if (Word(image.Read(slots + checked((uint)index * 4), 4), 0) != index)
                    throw new NotSupportedException("Model reset changed its source worn slot order.");
            Need(magic + 0x123, [0x83, 0xc6, 4, 0x83, 0xfe, 0x14], "bounded five-slot loop");
            Need(magic + 0x12b, [0x6a, 0x13, 0xe8], "reserved form lookup before genuine SpellItem cast");
            Need(Slot(high, 0x46c), [0xc6, 0x81, 0x8c, 1, 0, 0, 0, 0xc3], "same existing scene mask clear");
            var select = Call(load + 0x57b); var named = Call(select + 0x45);
            Need(select + 0x3d, [0x6a, 1, 0x68], "first root actual palette-first name query");
            var skin = Name(Word(code, select + 0x40));
            Need(select + 0x7b, [0x6a, 3, 0x68], "fallback prefix length");
            var prefix = Name(Word(code, select + 0x7e));
            Need(select + 0x8b, [0x85, 0xc0, 0x74], "matching BIP prefix is excluded");
            Need(named + 0x3c, [0xe8], "actual manager query before source palette");
            Need(named + 0x5c, [0x8b, 0x4b, 0x78], "same current controller-manager palette pointer");
            Need(named + 0x65, [0xff, 0x90, 0x8c, 0, 0, 0], "palette lookup before ordinary named virtual fallback");
            Need(named + 0x82, [0xff, 0x90, 0x9c, 0, 0, 0], "same root named fallback");
            var bind = Call(load + 0x4f8); var manager = Call(bind + 0x148);
            var palette = Call(manager + 0xb2); var buildPalette = Call(palette + 0x52);
            Need(palette + 0x4f, [0x89, 0x46, 0x18], "palette stores the actual manager root before building names");
            Need(buildPalette + 0x22, [0x8b, 7, 0x56, 0x51, 0x8b, 0xcf, 0xff, 0x90, 0x90, 0, 0, 0], "palette inserts its actual named root");
            var insertChild = Call(buildPalette + 0x5a);
            Need(insertChild + 0x1c, [0x8b, 3, 0x56, 0x51, 0x8b, 0xcb, 0xff, 0x90, 0x90, 0, 0, 0], "palette inserts each actual child before recursion");
            if (Call(insertChild + 0x4d) != insertChild) throw new NotSupportedException("Object palette changed its source preorder traversal.");
            Need(Slot(high, 0x4f4) + 0xa, [0x8b, 0x8f, 0x20, 2, 0, 0], "actual post-action shared shader field");
            Need(Slot(high, 0x4f4) + 0x27, [0x89, 0xb7, 0x20, 2, 0, 0], "same shared shader publication");
            Need(Slot(high, 0x4f4) + 0x31, [0xf0, 0xff, 0x46, 4], "actual shared property retain");
            var result = new FalloutPlayerModelLoadContinuationSource(animation, FalloutPlayerModelLoadContinuationSource.ContractDigest,
                cameraName, skin, prefix)
            { FirstNodeNames = firstNames }; result.Validate();
            if (QualifiedPlayerModelContinuations.Count == MaximumQualifiedPlayerAnimationSources) QualifiedPlayerModelContinuations.Clear();
            QualifiedPlayerModelContinuations.Add(animation.Identity, result); return result;

            string Name(uint value) => image.Literal(value) ?? throw new InvalidDataException("Player loader name has no original string.");
            string InternedName(uint global)
            {
                var nameStores = new List<int>();
                for (var at = 0x21; at < code.Length - 6; ++at)
                    if (Match(code, at, [0x89, 0x35]) && Word(code, at + 2) == global && code[at - 0x21] == 0x68 && code[at - 0x1c] == 0xe8)
                        nameStores.Add(at);
                if (nameStores is not [var store]) throw new NotSupportedException("Named query lost its actual interned-name constructor.");
                return Name(Word(code, store - 0x20));
            }
            uint Table(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
                throw new NotSupportedException("Player model continuation class is absent or ambiguous: " + name);
            int Slot(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
            int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("Player model continuation lost an actual direct child.");
            void Need(int at, byte[] expected, string role)
            { if (!Match(code, at, expected)) throw new NotSupportedException("Player model continuation changed " + role + "."); }
        }
    }

    private static (int Update, int Clock) QualifyInitialModelTransformControllers(byte[] code)
    {
        var update = Find([0x55, 0x8b, 0xec, 0x83, 0xec, 0x2c, 0x56, 0x8b, 0xf1, 0x0f, 0xb7, 0x46, 8]);
        Need(update + 0x31, [0x8a, 0xc1, 0xc0, 0xe8, 3, 0xa8, 1], "active flag before clock update");
        Need(update + 0x51, [0xc0, 0xe9, 6, 0x8b, 0xc2, 0xf6, 0xc1, 1], "compute-scaled-time flag");
        Need(update + 0x96, [0x25, 0x7f, 0xff, 0, 0, 0x66, 0x89, 0x46, 8], "source forced-update clear after clock");
        Need(update + 0xbe, [0x8b, 0x4e, 0x34, 0x85, 0xc9], "nullable interpolator after clock fields");
        Need(update + 0x112, [0x84, 0xc0, 0x0f, 0x84], "sampler return before source local pose stores");
        Need(update + 0x27f, [0xf3, 0x0f, 0x11, 0x41, 0x64], "absolute scale publication");
        _ = Find([0xc7, 0x46, 0x1c, 0xff, 0xff, 0x7f, 0xff,
            0xc7, 0x46, 0x20, 0xff, 0xff, 0x7f, 0xff, 0xc7, 0x46, 0x24, 0, 0, 0, 0,
            0xc7, 0x46, 0x28, 0xff, 0xff, 0x7f, 0xff, 0xc7, 0x46, 0x2c, 0, 0, 0, 0]);
        var clock = Find([0xf3, 0x0f, 0x11, 0x46, 0x20, 0xf3, 0x0f, 0x58, 0xcb,
            0xf3, 0x0f, 0x11, 0x65, 8, 0xf3, 0x0f, 0x10, 0x87, 0x94, 2, 0, 0]) - 0x88;
        Need(clock, [0x55, 0x8b, 0xec, 0xf3, 0x0f, 0x10, 0x15], "same scaled-time body");
        Need(clock + 0x219, [0x0f, 0x2f, 0xcc, 0x76, 5], "upper-first clamp before lower bound");
        return (update, clock);
        int Find(byte[] pattern)
        {
            var found = -1;
            for (var at = 0; at <= code.Length - pattern.Length; ++at)
                if (Match(code, at, pattern))
                { if (found >= 0) throw new NotSupportedException("Initial model controller source association is ambiguous."); found = at; }
            return found >= 0 ? found : throw new NotSupportedException("Initial model controller source association is absent.");
        }
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Initial model controller changed " + role + "."); }
    }
}
