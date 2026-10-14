using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock CellReferenceLinksQualificationLock = new();
    private static string? _qualifiedCellReferenceLinks;

    internal static FalloutSourceCellReferenceLinksDeclaration ReadCellReferenceLinks(string path, string executable)
    {
        var source = FalloutSourceCellReferenceLinksDeclaration.ForExecutable(executable); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != executable)
            throw new InvalidDataException("CELL links changed their selected original source image.");
        if (executable != FalloutSourceMainFamily.Fallout3) return source;
        lock (CellReferenceLinksQualificationLock)
        {
            if (_qualifiedCellReferenceLinks == source.ContractSha256) return source;
            var (code, image) = Load(original);
            var cell = image.SourceClassTables(".?AVTESObjectCELL@@", 2).ToArray() is [var table] ? table :
                throw new NotSupportedException("CELL list lacks its actual source class.");
            var constructor = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == cell &&
                Match(code, at + 0x32, [0xc7, 0x86, 0xac, 0, 0, 0, 0, 0, 0, 0]), 0x4a);
            Need(constructor + 0x32, [0xc7, 0x86, 0xac, 0, 0, 0, 0, 0, 0, 0,
                0xc7, 0x86, 0xb0, 0, 0, 0, 0, 0, 0, 0], "actual null element and next constructor fields");
            var ordinary = Unique(at => Match(code, at, [0x8b, 0x4e, 0x3c, 0x8d, 0x46, 0x3c, 0x89, 0x45, 0xf8,
                0x85, 0xc9, 0x74, 6, 0x56, 0xe8]), 0x70);
            var unlink = Call(ordinary + 0xe);
            Need(ordinary + 0x25, [0x8d, 0x45, 8, 0x50, 0x8d, 0x8f, 0xac, 0, 0, 0, 0xe8], "same ordinary CELL list insertion receiver");
            var head = Call(ordinary + 0x2f);
            Need(ordinary + 0x34, [0x8b, 6, 0x8b, 0xce, 0x57, 0xff, 0x90, 0x28, 2, 0, 0],
                "selected reference ParentCELL setter after head insertion");
            Need(head + 0x10, [0x83, 0x3e, 0, 0x74], "empty head reuses original inline element");
            Need(head + 0x2a, [0x8b, 0xe, 0x89, 0xa, 0xc7, 0x42, 4, 0, 0, 0, 0], "nonnull old head copied into genuine next node");
            Need(head + 0x35, [0x8b, 0x46, 4, 0x89, 0x42, 4, 0x89, 0x56, 4, 0x8b, 7], "old next retained before original new head store");
            Need(head + 0x55, [0x89, 6], "actual inline new element store");
            Need(unlink + 0x8f, [0x8d, 0x45, 8, 0x50, 0x8d, 0x8b, 0xac, 0, 0, 0, 0xe8], "same list selected by old-CELL removal");

            // The actual ordinary record dispatcher retains same-parent eager
            // updates and invokes this same list writer on first construction.
            var record = Unique(at => Match(code, at, [0x3b, 5]) && Match(code, at + 6,
                [0x75, 0x15, 0x8b, 0x8d, 0xd0, 0xfe, 0xff, 0xff, 0xc6, 0x85, 0xeb, 0xfe, 0xff, 0xff, 0]), 0x1a);
            Need(record + 6, [0x75, 0x15], "same source parent keeps existing eager membership");
            var eager = Unique(at => Match(code, at, [0x6a, 0, 0x57, 0xe8]) && Call(at + 3) == ordinary - 0xbd &&
                Match(code, at + 8, [0x8b, 0x0d]), 0x15);
            Need(eager, [0x6a, 0, 0x57, 0xe8], "actual first eager reference uses this CELL list writer");

            var bulk = Unique(at => Match(code, at, [0x8b, 0x47, 0xc, 0x83, 0xe8, 4, 0x74, 0xe,
                0x83, 0xe8, 1, 0x74, 9, 0x83, 0xe8, 4, 0x0f, 0x85]), 0x1f);
            Need(bulk + 0x16, [0x8b, 0xce, 0x32, 0xff, 0xe8], "bulk file traversal skips exterior blocks and temporary group nine");
            var onam = Unique(at => Match(code, at, [0x3d, 0x4f, 0x4e, 0x41, 0x4d, 0x74, 0x2c, 0x8b, 0xcb, 0xe8]), 0x10d);
            Need(onam + 0xde, [0x8b, 0xc7, 0x8b, 0xcb, 0xc1, 0xe8, 0x18, 0x40, 0x50, 0xe8],
                "TES4 ONAM adjusts the original IDs through this file's registered masters");
            Need(onam + 0xf8, [0x0f, 0xb6, 0xc0, 0x81, 0xe7, 0xff, 0xff, 0xff, 0, 0xc1, 0xe0, 0x18,
                0x0b, 0xc7, 0x53, 0x50, 0xe8], "adjusted ONAM key retains the actual declaring provider");
            var providerStore = Call(onam + 0x108);
            Need(providerStore + 0x110, [0x8b, 0x5d, 0xc, 0x89, 0x5f, 8], "later ONAM registration overwrites an existing provider entry");
            Need(providerStore + 0x12a, [0x89, 0x5f, 4, 0x8b, 0x5d, 0xc, 0x89, 0x5f, 8], "new ONAM entry preserves adjusted identity and actual file");

            var lazy = Unique(at => Match(code, at, [0x8b, 0x9f, 0x4c, 2, 0, 0, 0x57, 0x53, 0xe8]) &&
                Match(code, at + 0xd, [0x83, 0xc4, 8, 0x84, 0xc0, 0x0f, 0x84]) &&
                Match(code, at + 0x18, [0x53, 0xe8]), 0x96);
            Need(lazy - 0xf9, [0x83, 0x7e, 0xc, 9, 0x0f, 0x85, 0x48, 2, 0, 0, 0x6a, 1, 0x8b, 0xcf, 0xe8],
                "lazy loader enters its actual group-nine body");
            var provider = Call(lazy + 8);
            Need(provider + 0xb, [0xb3, 1, 0x85, 0xc9, 0x74], "missing ONAM table permits the actual current file");
            Need(provider + 0x29, [0x84, 0xc0, 0x74, 0x26, 0x83, 0x7d, 0xfc, 0, 0x74, 0x20],
                "missing ONAM entry permits the actual current file");
            Need(provider + 0x43, [0x0f, 0x44, 0x4d, 0xc, 0x33, 0xd2, 0x39, 0x4d, 0xfc, 0x0f, 0x45, 0xc2],
                "nonnull selected provider equality follows the real source file's current reader lineage");
            Need(lazy + 0x74, [0x83, 0x7d, 0xf0, 0, 0x75, 0x23], "already constructed object skips source constructor dispatch");
            Need(lazy + 0x84, [0x8b, 0x0d, 0xe4, 0x49, 0x22, 1, 0x6a, 0, 0x6a, 0, 0x6a, 0, 0x57, 0xe8],
                "selected missing object reaches the same ordinary record dispatcher");
            if (Call(lazy + 0x91) != onam - 0x2b0)
                throw new NotSupportedException("Lazy CELL construction changed its actual ordinary record dispatcher.");
            var files = Unique(at => Match(code, at, [0x56, 0x8b, 0xcf, 0xe8]) &&
                Call(at + 3) == lazy - 0x1b7 && Match(code, at + 8, [0x84, 0xc0, 0x75, 4]), 0x19);
            Need(files - 0x58, [0x50, 0x8b, 0xcf, 0xe8], "CELL selects source file by original registered ordinal");
            Need(files + 0x10, [0x8b, 0x45, 0xf8, 0x40, 0x89, 0x45, 0xf8, 0x3b, 0xc3, 0x72],
                "lazy CELL traversal increments its original file-list order");
            RequireCellEngineStaticBases(code, image);
            _qualifiedCellReferenceLinks = source.ContractSha256; return source;

            int Unique(Func<int, bool> predicate, int tail) => Enumerable.Range(0, code.Length - tail + 1).Where(predicate).ToArray() is [var at] ? at :
                throw new NotSupportedException("CELL list source association is absent or ambiguous.");
            int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("CELL list lost an actual source child.");
            void Need(int at, byte[] expected, string role)
            { if (!Match(code, at, expected)) throw new NotSupportedException("CELL links changed their " + role + "."); }
        }
    }
}
