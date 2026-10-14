using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutPlayerWeaponContinuationSource ReadPlayerWeaponContinuation(string path, FalloutPlayerMovementControlSource controls)
    {
        var source = FalloutPlayerWeaponContinuationSource.Reviewed(controls); source.Validate(); var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != controls.Main.EngineSha256)
            throw new InvalidDataException("Weapon continuation changed its selected original executable.");
        var (code, image) = Load(original); var player = Class(".?AVPlayerCharacter@@", 191);
        var high = Class(".?AVHighProcess@@", 460); var middle = Class(".?AVMiddleHighProcess@@", 460);
        foreach (var table in new[] { high, middle })
        {
            Need(Get(table, 0x6d4), [0x8a, 0x81, 0xd9, 1, 0, 0, 0xc3], "weapon-action getter");
            Need(Get(table, 0x6d8), [0x55, 0x8b, 0xec, 0x8a, 0x45, 8, 0x88, 0x81, 0xd9, 1, 0, 0, 0x5d, 0xc2, 4, 0], "pure weapon-action byte setter");
            Need(Get(table, 0x408), [0x0f, 0xbe, 0x81, 0x3c, 1, 0, 0, 0xc3], "independent signed activity getter");
            Need(Get(table, 0x148), [0x8b, 0x81, 0x14, 1, 0, 0, 0xc3], "actual nullable weapon entry");
        }
        var ctor = Unique(at => Match(code, at, [0xc7, 7]) && Word(code, at + 2) == player &&
            Match(code, at + 0x5a, [0xc7, 0x87, 0xf0, 5, 0, 0, 0, 0, 0, 0]), 0xa63);
        Need(ctor + 0xa56, [0x8b, 0x4f, 0x60, 0x6a, 0, 0x8b, 1, 0xff, 0x90, 0xd8, 6, 0, 0], "actual Player constructor-zero writer");
        var middleCtor = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == middle &&
            Match(code, at + 0xcc, [0xc7, 0x86, 0x14, 1, 0, 0, 0, 0, 0, 0]), 0x440);
        Need(middleCtor + 0x1b7, [0x66, 0xc7, 0x86, 0x3c, 1, 0, 0, 0, 0], "activity constructor word");
        Need(middleCtor + 0x84, [0x6a, 0x44], "actual cached-value context allocation");
        var cacheCtor = Call(middleCtor + 0xa1);
        Need(middleCtor + 0xb5, [0x89, 0x46, 0x2c], "constructed process context publication");
        Need(cacheCtor, [0xc7, 1, 0, 0, 0x80, 0xbf, 0x8b, 0xc1], "context first scalar");
        for (var index = 1; index < FalloutPlayerWeaponContinuationSource.CachedScalarCount; ++index)
            Need(cacheCtor + 8 + (index - 1) * 7, [0xc7, 0x41, checked((byte)(index * 4)), 0, 0, 0x80, 0xbf], "context independent scalar");
        Need(cacheCtor + 0x4e, [0xc7, 0x41, 0x2c, 0xff, 0xff, 0xff, 0xff, 0xc7, 0x41, 0x30, 0xff, 0xff, 0xff, 0xff,
            0xc6, 0x41, 0x3c, 0], "context independent words and byte");
        Need(cacheCtor + 0x60, [0xc7, 0x41, 0x34, 0, 0, 0, 0, 0xc7, 0x41, 0x38, 0, 0, 0, 0,
            0xc7, 0x41, 0x40, 0, 0, 0, 0, 0xc3], "context flags and pointers");
        var status = Get(player, 0x234);
        Need(status, [0x55, 0x8b, 0xec, 0x51, 0x8b, 0x51, 0x60, 0x85, 0xd2, 0x74], "Actor status nullable process");
        Need(status + 0xb, [0x8b, 0x52, 0x2c, 0x85, 0xd2, 0x74], "actual cached-value context");
        Need(status + 0x12, [0x8b, 0x42, 0x40, 0x81, 0xc1, 0xa0, 0, 0, 0, 0xc1, 0xe8, 5, 0xf6, 0xd0, 0xa8, 1], "independent cache flag test");
        Need(ctor + 0x2b, [0xc7, 0x87, 0xa0, 0, 0, 0], "Player secondary current status table");
        var statusValue = SourceOffset(code, image.CodeBase, Word(image.Read(Word(code, ctor + 0x31) + 0x18, 4), 0));
        Need(statusValue, [0x55, 0x8b, 0xec, 0x51, 0x56, 0x8b, 0xf1, 0x8b, 0x46, 0xfc, 0x8d, 0x4e, 0xfc,
            0x6a, 0x2f, 0x8b, 0x40, 0x0c, 0xff, 0xd0], "current actor-value forty-seven virtual");
        Need(statusValue + 0x29, [0xf3, 0x0f, 0x10, 0x45, 0xfc, 0xf3, 0x0f, 0x11, 0x40, 0x18,
            0x8b, 0x41, 0x2c, 0x83, 0x48, 0x40, 0x20], "returned scalar before cache mask store");
        var scalar = ReadDefaultStatusActorValues(path).Values.Single(value => value.Index == FalloutPlayerWeaponContinuationSource.ParalysisActorValue);
        if (scalar.Name != "Paralysis") throw new NotSupportedException("Following status selects a foreign current actor-value descriptor.");
        return source;

        uint Class(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
            throw new NotSupportedException("Weapon continuation lost its actual unique class: " + name);
        int Get(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
        int Unique(Func<int, bool> predicate, int tail)
        { var found = Enumerable.Range(0, code.Length - tail + 1).Where(predicate).ToArray(); return found is [var at] ? at : throw new NotSupportedException("Weapon continuation constructor is absent or ambiguous."); }
        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Weapon continuation lost a real constructor child.");
        void Need(int at, byte[] bytes, string role)
        { if (!Match(code, at, bytes)) throw new NotSupportedException("Weapon continuation changed its source " + role + "."); }
    }
}
