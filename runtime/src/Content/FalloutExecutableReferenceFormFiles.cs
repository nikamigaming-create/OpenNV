using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock ReferenceFormFileQualificationLock = new();
    private static string? _qualifiedReferenceFormFiles;
    internal static FalloutReferenceFormFileSource ReadReferenceFormFileAssociation(string path, FalloutReferencePositionSource reference)
    {
        reference.Validate(); var source = FalloutReferenceFormFileSource.Reviewed(reference);
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != reference.Fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("Reference file association changed current original source bytes.");
        lock (ReferenceFormFileQualificationLock)
        {
            if (_qualifiedReferenceFormFiles == source.Identity) return source;
            _ = ReadReferencePosition(path, reference.Fields);
            var (code, image) = Load(original);
            var player = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray() is [var table] ? table :
                throw new NotSupportedException("Reference form files lost their selected Player class.");
            var constructed = Unique(at => Match(code, at, [0xc7, 7]) && Word(code, at + 2) == player &&
                Match(code, at + 0x5a, [0xc7, 0x87, 0xf0, 5, 0, 0, 0, 0, 0, 0]), 0xa63);
            var character = Call(constructed - 5); var actor = Call(character + 12);
            var inherited = Call(Call(actor + 0x2b) + 0xa); var form = Call(inherited + 0x2a);
            Need(form + 0x36, [0xc7, 0x43, 0x10, 0, 0, 0, 0, 0xc7, 0x43, 0x14, 0, 0, 0, 0], "actual empty Form list constructor");
            Need(form + 0x298, [0x8b, 0x80, 0x98, 1, 0, 0, 0x85, 0xc0, 0x74, 8], "nonnull active-file-only constructor append");
            var setter = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x2a8, 4), 0));
            var predicate = Call(Call(setter + 0x24) + 9); var file = Call(predicate + 6);
            Need(predicate, [0x56, 0x57, 0x6a, 0xff, 0x8b, 0xf1, 0xe8], "current form-file minus-one before persistence predicate");
            Need(predicate + 0x4d, [0x8b, 0x46, 8, 0xc1, 0xe8, 0xa], "absent-file actual reference flag fallback");
            Need(file, [0x55, 0x8b, 0xec, 0x57, 0x33, 0xc0, 0x33, 0xff, 0x83, 0xc1, 0x10], "actual association list getter");
            _ = Unique(at => Match(code, at, [0x33, 0xc0]) && Match(code, at + 0x6b,
                [0x89, 0x86, 0xe8, 3, 0, 0, 0x89, 0x86, 0, 4, 0, 0]), 0x77);
            _ = Unique(at => Match(code, at, [0x33, 0xc9]) && Match(code, at + 0x66,
                [0x89, 0x8e, 0x98, 1, 0, 0, 0x89, 0x8e, 0xa4, 5, 0, 0]), 0x73);
            _ = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x8b, 0x91, 0xe8, 3, 0, 0, 0x8b, 0xc2,
                0x83, 0xc8, 4, 0x83, 0xe2, 0xf3, 0x80, 0x7d, 8, 0, 0x0f, 0x45, 0xd0]), 0x20);
            _ = Unique(at => Match(code, at, [0x8a, 0x81, 0xe8, 3, 0, 0, 0xc0, 0xe8, 3, 0x24, 1, 0xc3]), 12);
            _qualifiedReferenceFormFiles = source.Identity; return source;

            int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("Reference form-file source lost an actual selected child.");
            int Unique(Func<int, bool> test, int tail) => Enumerable.Range(0, code.Length - tail + 1).Where(test).ToArray() is [var at] ? at :
                throw new NotSupportedException("Reference form-file selector is missing or ambiguous.");
            void Need(int at, byte[] expected, string role)
            { if (!Match(code, at, expected)) throw new NotSupportedException("Reference form-file source changed its " + role + "."); }
        }
    }
}
