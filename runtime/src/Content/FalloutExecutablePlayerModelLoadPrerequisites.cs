using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock PlayerModelPrerequisiteQualificationLock = new();
    private static readonly Dictionary<string, FalloutPlayerModelLoadPrerequisiteSource> QualifiedPlayerModelPrerequisites = [];

    internal static FalloutPlayerModelLoadPrerequisiteSource ReadPlayerModelLoadPrerequisites(string path,
        FalloutPlayerModelLoadContinuationSource continuation, FalloutReferencePositionSource reference)
    {
        continuation.Validate(); reference.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != continuation.Animation.Tail.Scene.Main.EngineSha256)
            throw new InvalidDataException("Player model prerequisites changed their current executable bytes.");
        var key = continuation.Identity + "\0" + reference.Identity;
        lock (PlayerModelPrerequisiteQualificationLock)
        {
            if (QualifiedPlayerModelPrerequisites.TryGetValue(key, out var cached)) return cached;
            if (ReadPlayerModelLoadContinuation(path, continuation.Animation).Identity != continuation.Identity ||
                ReadReferencePosition(path, reference.Fields).Identity != reference.Identity)
                throw new InvalidDataException("Player model prerequisites replaced their already qualified source owners.");
            var (code, image) = Load(original);
            var player = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray() is [var table] ? table :
                throw new NotSupportedException("Player model prerequisites lack their selected Player class.");
            var load = Slot(0x1c8);
            Need(load + 0x96, [0xa1], "actual shared manager global before current field read");
            var managerGlobal = Word(code, load + 0x97);
            Need(load + 0xa2, [0x8b, 0x80, 0x44, 2, 0, 0, 0xd1, 0xe8, 0xa8, 1, 0x75], "same current manager bit-one predicate");
            var skip = checked(load + 0xae + (sbyte)code[load + 0xad]);
            Need(load + 0xae, [0x80, 0x3d], "independent following global byte read");
            var characterGenerationByte = Word(code, load + 0xb0);
            Need(load + 0xb4, [0, 0x75], "current nonzero byte suppression predicate");
            if (skip != load + 0xb7 + (sbyte)code[load + 0xb6] || skip != load + 0x116)
                throw new NotSupportedException("Player loader gates changed their same inventory-skip target.");
            var mark = Slot(0x48);
            Need(mark + 0xe, [0x8b, 0x0d], "existing reference-change manager global read");
            if (Word(code, mark + 0x10) != managerGlobal)
                throw new NotSupportedException("Player loader borrowed a different manager from reference changes.");
            var change = Call(mark + 0x14);
            Need(change + 0x30, [0x8a, 0x87, 0x44, 2, 0, 0], "same reference-change manager current flags field");
            var initialFlags = Unique(at => Match(code, at, [0xc7, 0x87, 0x44, 2, 0, 0, 1, 0, 0, 0]) &&
                Match(code, at - 7, [0xc6, 0x87, 0x48, 2, 0, 0, 0xff]), 16);
            var constructor = initialFlags - 0xa7;
            Need(constructor, [0x55, 0x8b, 0xec, 0x6a, 0xff], "actual shared manager constructor");
            var constructCall = Unique(at => code[at] == 0xe8 &&
                (long)at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)) == constructor &&
                Match(code, at + 0x10, [0xa3]) && Word(code, at + 0x11) == managerGlobal, 0x20);
            Need(constructCall - 0x1f, [0x68, 0x4c, 2, 0, 0], "same global manager allocated original extent");
            if (!image.IsWritableExtent(characterGenerationByte, 1) || image.IsFileExtent(characterGenerationByte, 1) ||
                image.ReadLoaderData(characterGenerationByte, 1)[0] != FalloutPlayerModelLoadPrerequisiteSource.LoaderInitializedCharacterGenerationByte)
                throw new NotSupportedException("Character-generation global lost its real PE zero-filled constructor input.");
            var startStore = Unique(at => Match(code, at, [0xc6, 5]) && Word(code, at + 2) == characterGenerationByte && code[at + 6] == 1, 7);
            var start = startStore - 0x61;
            Need(start, [0x55, 0x8b, 0xec, 0x51, 0x56, 0x6a, 0x10], "selected deferred character-generation entry and hexadecimal conversion");
            Need(start + 0xb, [0xff, 0x35], "actual mutable character-generation INI payload");
            var settingPayload = Word(code, start + 0xd);
            Need(start + 0x16, [0x50, 0xe8], "parsed current FormID before form lookup");
            _ = Call(start + 0x11); _ = Call(start + 0x17);
            Need(start + 0x27, [0x80, 0x78, 4, 0x47], "actual selected Quest kind before cast");
            _ = Call(start + 0x40);
            Need(start + 0x4a, [0x85, 0xf6, 0x0f, 0x84], "nonnull actual Quest cast predicate");
            Need(start + 0x52, [0x6a, 0, 0xe8], "preceding startup helper actual argument");
            Need(start + 0x5f, [0x6a, 0, 0xc6, 5], "dominating byte writer precedes following Player children");
            Need(start + 0x6d, [0xc6, 0x81, 0xb9, 6, 0, 0, 0], "distinct following Player field is not this global byte");
            Need(start + 0x9e, [0x6a, 1, 0x8b, 0xce, 0xe8], "actual Quest start follows independent byte store");
            _ = Call(start + 0xa2);
            var callback = Unique(at => Match(code, at, [0xc7, 0x43, 8]) && Word(code, at + 3) == image.CodeBase + start, 14);
            Need(callback + 7, [0xc7, 0x43, 0xc, 0x80, 0, 0, 0], "actual deferred callback work declaration");
            var teardownStore = Unique(at => Match(code, at, [0xc6, 5]) && Word(code, at + 2) == characterGenerationByte && code[at + 6] == 0, 7);
            var teardown = teardownStore - 0x96;
            Need(teardown, [0x55, 0x8b, 0xec, 0x6a, 0xff], "actual LoadingMenu destruction prefix");
            Need(teardown + 0x26, [0xc7, 7], "same LoadingMenu destructor virtual table");
            var destructorTable = Word(code, teardown + 0x28);
            if (!image.SourceClassTables(".?AVLoadingMenu@@", 2).Contains(destructorTable))
                throw new NotSupportedException("Startup byte zero writer is not the actual selected LoadingMenu destructor.");
            var scalarDestructor = SourceOffset(code, image.CodeBase, Word(image.Read(destructorTable, 4), 0));
            if (Call(scalarDestructor + 0x25) != teardown)
                throw new NotSupportedException("Character-generation byte zero store lost its original destructor caller.");
            Need(load + 0xb7, [0x57, 0xe8], "reached inventory materialization child");
            _ = Call(load + 0xb8); _ = Call(load + 0xd7);
            Need(load + 0xd4, [0x6a, 0, 0x50, 0xe8], "actual bounded worn-slot query inputs");
            Need(load + 0xfd, [0x83, 0xf8, 0x14, 0x7c], "actual twenty-slot query extent");
            Need(load + 0x108, [0x6a, 0, 0x6a, 0, 0x6a, 1, 0x6a, 1, 0x57, 0xe8], "actual no-equipped-slot model-preparation inputs");
            _ = Call(load + 0x111);
            var settingName = image.Literal(Word(image.Read(settingPayload + 4, 4), 0)) ??
                throw new InvalidDataException("Character-generation source setting has no original name.");
            var settings = ReadIniStringDefaults(original, [settingName]);
            var selected = settings.Where(row => row.Declaration.Name.Equals(settingName, StringComparison.OrdinalIgnoreCase) &&
                row.Declaration.Collection == FalloutIniCollection.Main && row.Declaration.Payload == Word(image.Read(settingPayload, 4), 0)).ToArray();
            if (selected is not [var questSetting])
                throw new NotSupportedException("Character-generation byte writer lost its actual registered string setting.");
            var result = new FalloutPlayerModelLoadPrerequisiteSource(continuation, reference, questSetting,
                FalloutPlayerModelLoadPrerequisiteSource.ContractDigest);
            result.Validate(); QualifiedPlayerModelPrerequisites.Add(key, result); return result;

            int Slot(uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(player + slot, 4), 0));
            int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("Player model prerequisites lost an actual direct source child.");
            int Unique(Func<int, bool> predicate, int extent) => Enumerable.Range(8, code.Length - extent - 8).Where(predicate).ToArray() is [var at] ? at :
                throw new NotSupportedException("Player model prerequisite association is absent or ambiguous.");
            void Need(int at, byte[] expected, string role)
            { if (!Match(code, at, expected)) throw new NotSupportedException("Player model prerequisites changed " + role + "."); }
        }
    }
}
