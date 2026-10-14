using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock ReferencePositionQualificationLock = new();
    private static string? _qualifiedReferencePosition;

    internal static FalloutReferencePositionSource ReadReferencePosition(string path, FalloutPackageCommandFieldsSource fields)
    {
        var source = FalloutReferencePositionSource.Reviewed(fields); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("Reference position changed its current original source bytes.");
        lock (ReferencePositionQualificationLock)
        {
            if (_qualifiedReferencePosition == source.Identity) return source;
            _ = ReadPackageCommandFields(path, fields.Controls);
            _ = ReadReferenceScriptExtraInitializer(path, fields);
            var (code, image) = Load(original);
            var player = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray() is [var table] ? table :
                throw new NotSupportedException("Reference position lacks its exact selected Player class.");
            var constructor = Unique(at => Match(code, at, [0xc7, 7]) && Word(code, at + 2) == player &&
                Match(code, at + 0x5a, [0xc7, 0x87, 0xf0, 5, 0, 0, 0, 0, 0, 0]), 0xa63);
            var character = Call(constructor - 5); var actor = Call(character + 12);
            var mobile = Call(actor + 0x2b); var reference = Call(mobile + 0xa);
            var initializedActor = Call(actor + 0xe9);
            Need(initializedActor + 0x9d, [0xc7, 0x86, 0xfc, 0, 0, 0, 0, 0, 0, 0], "same existing neutral Actor life constructor word");
            Need(initializedActor + 0x312, [0xc7, 0x86, 0x9c, 1, 0, 0, 0, 0, 0, 0], "independent Actor yaw selection constructor word");
            Need(Get(0x214), [0x8b, 0x81, 0x9c, 1, 0, 0, 0xc3], "same current Actor yaw selection getter");
            Need(reference + 0x7d, [0xc7, 0x46, 0x38, 0, 0, 0x80, 0x3f], "independent positive-one reference scale constructor");
            var setter = Get(0x2a8); var basis = Call(setter + 0x24);
            Need(basis + 0x47, [0x8b, 0x45, 8, 0xf3, 0x0f, 0x7e, 0, 0x66, 0x0f, 0xd6, 0x46, 0x2c,
                0x8b, 0x40, 8, 0x89, 0x46, 0x34], "paired raw XY followed by raw Z");
            Need(basis + 0x69, [0x6a, 2, 0xff, 0x50, 0x48], "mandatory mask2 after raw stores");
            var kind = Call(basis + 0x13);
            Need(kind, [0x55, 0x8b, 0xec, 0x8b, 0x45, 8, 0x32, 0xc9, 0x85, 0xc0], "pure BaseForm kind predicate");
            Need(kind + 0x13, [0x0f, 0xb6, 0x40, 4, 0x83, 0xc0, 0xf3, 0x83, 0xf8, 0x20], "original kind table extent");
            var selectors = image.Read(Word(code, kind + 0x22), 33);
            var jumps = image.Read(Word(code, kind + 0x29), 8);
            Need(SourceOffset(code, image.CodeBase, Word(jumps, 0)), [0xb1, 1], "kind-true arm");
            Need(SourceOffset(code, image.CodeBase, Word(jumps, 4)), [0x8a, 0xc1, 0x5d, 0xc3], "kind-false return");
            for (var index = 0; index < selectors.Length; index++)
                if (selectors[index] != (FalloutReferencePositionSource.UsesWorldspacePositionList((byte)(index + 13)) ? 0 : 1))
                    throw new NotSupportedException("Reference position changed an original BaseForm kind selector.");
            var mark = Get(0x48);
            Need(mark, [0x55, 0x8b, 0xec, 0x8b, 0x45, 8, 0x6a, 0, 0x51, 0x8b, 0xd4, 0x51, 0x89, 2],
                "actual reference/mask/force-false change callback");
            var manager = Call(mark + 0x14);
            Need(manager + 0x24, [0x8a, 0x86, 0x7c, 2, 0, 0, 0xf6, 0xd0, 0xa8, 1], "current-thread flags before shared manager flags");
            Need(manager + 0x30, [0x8a, 0x87, 0x44, 2, 0, 0, 0xf6, 0xd0, 0xa8, 1], "shared manager bit-one predicate");
            Need(manager + 0x4a, [0xc1, 0xe8, 0xe, 0xa8, 1], "independent reference flag fourteen");
            var modify = Call(manager + 0x73);
            Need(modify + 0x51, [0x8d, 0x4e, 4, 0xc7, 6, 0, 0, 0, 0, 0xe8], "real change record flags-zero/data constructor");
            Need(modify + 0x77, [0x83, 0x7e, 4, 0, 0x75, 5, 0x8b, 0x45, 0xc, 9, 6], "nonnull data skips mask OR");
            var map = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x51, 0x56, 0x8b, 0xf1, 0x68, 0xbc, 0x4e, 0, 0]) &&
                Match(code, at + 0x15, [0xc7, 0x46, 4, 0xaf, 0x13, 0, 0]), 0x49);
            Need(map + 0x1c, [0xc7, 0x46, 0xc, 0, 0, 0, 0], "original zero change-map count");
            var first = Unique(at => Match(code, at, [0xc7, 0x85, 0x98, 0xfc, 0xff, 0xff, 0, 0, 0, 0x45]) &&
                Match(code, at + 10, [0xc7, 0x85, 0x9c, 0xfc, 0xff, 0xff, 0, 0, 0, 0x45]) &&
                Match(code, at + 20, [0xc7, 0x85, 0xa0, 0xfc, 0xff, 0xff, 0, 0, 0, 0]), 0x30);
            Need(first + 30, [0x8b, 0x11, 0x50, 0xff, 0x92, 0xa8, 2, 0, 0], "first supplied vector before selected position writer");
            _ = Unique(at => Match(code, at, [0xc7, 0x85, 0x98, 0xfc, 0xff, 0xff, 0, 0, 0, 0x45]) &&
                Match(code, at + 10, [0xc7, 0x85, 0x9c, 0xfc, 0xff, 0xff, 0, 0, 0, 0x45]) &&
                Match(code, at + 20, [0xc7, 0x85, 0xa0, 0xfc, 0xff, 0xff, 0, 0, 0, 0x43]), 0x20);
            image.RequireReferenceThreadFlagsLoader();
            RequireBaseAssignment();
            _qualifiedReferencePosition = source.Identity; return source;

            void RequireBaseAssignment()
            {
                var assigned = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x56, 0x8b, 0xf1, 0x57, 0x8b, 0x7d, 8, 0x57, 0x89, 0x7e, 0x1c, 0xe8]) &&
                    Match(code, at + 0x43, [0xff, 0x92, 0xf8, 1, 0, 0]), 0x4d);
                var component = Call(assigned + 0xe); var selected = Call(component + 6);
                Need(selected, [0x55, 0x8b, 0xec, 0x8b, 0x55, 8, 0x33, 0xc9], "Base destructible kind-table association");
                var npcComponent = SourceOffset(code, image.CodeBase, Word(image.Read(Word(code, selected + 0x23) + (0x2a - 0x15) * 4, 4), 0));
                Need(npcComponent, [0x8d, 0x8a, 4, 1, 0, 0], "NPC inherited destructible component");
                var npc = image.SourceClassTables(".?AVTESNPC@@", 78).ToArray() is [var npcTable] ? npcTable :
                    throw new NotSupportedException("Base assignment lacks its actual winning NPC constructor class.");
                var npcConstructor = Unique(at => Match(code, at, [0xc7, 0]) && Word(code, at + 2) == npc, 0x40);
                Need(npcConstructor - 0xb3, [0x8d, 0x8f, 4, 1, 0, 0], "NPC constructor selected destructible child");
                var constructed = Call(npcConstructor - 0xa3);
                Need(constructed + 8, [0xc7, 0x41, 4, 0, 0, 0, 0, 0xc3], "actual destructible data-null constructor");
                Need(component + 0xb, [0x83, 0xc4, 4, 0x85, 0xc0], "absent component return before data query");
                Need(component + 0x16, [0x8b, 0x50, 4, 0x85, 0xd2, 0x74], "ctor-null data predicate return");
                var flag = Call(assigned + 0x1c);
                Need(flag + 6, [0x8b, 0xc2, 0x25, 0xff, 0xff, 0xff, 0xfe], "Base flag retains unrelated reference bits");
                Need(flag + 0x1a, [0x89, 0x51, 8], "same actual reference flag word store");
                Need(Get(0xfc), [0xb0, 1, 0xc3], "selected Player Base assignment true predicate");
                Need(assigned + 0x31, [0x8b, 0x47, 0x34], "actual Base Actor flags input");
                Need(assigned + 0x38, [0xc1, 0xe8, 9, 0xf6, 0xd0, 0x24, 1], "Actor response inverted bit-nine selector");
                Need(Get(0x1f8), [0x55, 0x8b, 0xec, 0x8a, 0x45, 8, 0x88, 0x41, 0x7f, 0x5d, 0xc2, 4, 0], "independent Actor response byte store");
            }

            int Get(uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(player + slot, 4), 0));
            int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("Reference source lost an actual selected child association.");
            int Unique(Func<int, bool> predicate, int tail) => Enumerable.Range(0, code.Length - tail + 1).Where(predicate).ToArray() is [var at] ? at :
                throw new NotSupportedException("Reference position source selector is absent or ambiguous.");
            void Need(int at, byte[] expected, string role)
            { if (!Match(code, at, expected)) throw new NotSupportedException("Reference position changed its original " + role + "."); }
        }
    }
    private sealed partial class Image
    {
        internal void RequireReferenceThreadFlagsLoader()
        {
            var directory = headers.PEHeader!.ThreadLocalStorageTableDirectory;
            if (directory.Size != 24 || directory.RelativeVirtualAddress == 0)
                throw new NotSupportedException("Reference flags lack an original PE32 TLS declaration.");
            var declaration = Read(checked(Base + (uint)directory.RelativeVirtualAddress), 24);
            var first = Word(declaration, 0); var last = Word(declaration, 4);
            if (last < first || (ulong)last - first + Word(declaration, 16) < 0x280 ||
                ReadLoaderData(checked(first + 0x27c), 4).Any(value => value != 0))
                throw new NotSupportedException("Reference flags changed their original loader constructor word.");
        }
    }
}
