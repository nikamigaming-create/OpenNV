using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock InitialPlacementQualificationLock = new();
    private static string? _qualifiedInitialPlacement;
    internal static FalloutInitialPlacementCellsSource ReadInitialPlacementCells(string path, FalloutReferencePositionSource reference)
    {
        reference.Validate(); var source = FalloutInitialPlacementCellsSource.Reviewed(reference);
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != reference.Fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("Initial TES fields changed current original source bytes.");
        lock (InitialPlacementQualificationLock)
        {
            if (_qualifiedInitialPlacement == source.Identity) return source;
            _ = ReadReferencePosition(path, reference.Fields);
            _ = ReadPlayerWeaponContinuation(path, reference.Fields.Controls);
            var (code, image) = Load(original);
            var tes = Class(".?AVTES@@", 1); var grid = Class(".?AVGridCellArray@@", 8);
            var constructed = Unique(at => Match(code, at, [0xc7, 3]) && Word(code, at + 2) == tes, 0xd4);
            Need(constructed + 0xc2, [0x68, 0xc4, 0, 0, 0, 0x6a, 0, 0x53], "actual TES field zero initialization");
            var gridConstructed = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == grid &&
                Match(code, at + 0xd, [0xc7, 0x46, 0x24, 0, 0, 0, 0]), 0x80);
            Need(gridConstructed + 0x1d, [0x83, 0xf8, 5], "unsigned minimum grid diameter");
            Need(gridConstructed + 0x29, [0xa8, 1], "original odd grid diameter predicate");
            Need(gridConstructed + 0x2d, [0x40, 0xa3], "original normalized Main setting store");
            var ini = Word(code, gridConstructed + 0x15);
            if (Word(code, gridConstructed + 0x2f) != ini) throw new NotSupportedException("Grid normalization changed the actual Main INI value cell.");
            Need(gridConstructed + 0x33, [0x89, 0x46, 0xc], "same current grid diameter field");
            Need(gridConstructed + 0x5a, [0x89, 0x46, 0x10, 0xc1, 0xe1, 2, 0x51, 0x6a, 0, 0x50, 0xe8], "actual diameter-squared zero slots");
            var selected = Unique(at => Match(code, at, [0x8b, 0x71, 0x34, 0x66, 0x0f, 0xd6, 0x45, 0xb0,
                0x89, 0x45, 0xb8, 0x85, 0xf6, 0x75, 0x17, 0xa1]) && Word(code, at + 16) == ini, 0x64);
            Need(selected + 0x14, [0x8b, 0x49, 8, 0xd1, 0xe8, 0x50, 0x50, 0xe8], "current TES CELL else actual center grid");
            var getter = Call(selected + 0x1b);
            Need(getter, [0x55, 0x8b, 0xec, 0x8b, 0x51, 0xc, 0x39, 0x55, 8], "current grid unsigned bounds");
            Need(getter + 0x12, [0x0f, 0xaf, 0x55, 8, 3, 0xd0, 0x8b, 0x41, 0x10, 0x8d, 4, 0x90], "same row-major CELL pointer slot");
            Need(selected + 0x20, [0x8b, 0x30, 0x85, 0xf6, 0x74, 0xf], "genuine null current CELL/grid branch");
            Need(selected - 0xd, [0xf3, 0x0f, 0x7e, 5], "shared original zero-vector read");
            var center = Word(code, selected - 9);
            if (!image.IsWritableExtent(center, 12) || image.ReadLoaderData(center, 12).Any(value => value != 0) ||
                !Match(code, selected - 5, [0xa1]) || Word(code, selected - 4) != center + 8)
                throw new NotSupportedException("Initial yaw changed its actual shared source zero-vector Z cell.");
            Need(selected + 0x3b, [0xf3, 0x0f, 0x10, 0x45, 0xb8], "selected yaw argument before position setter");
            Need(selected + 0x48, [0xff, 0x90, 0xc4, 2, 0, 0], "same selected Actor yaw virtual");
            Need(selected + 0x5a, [0xff, 0x90, 0xa8, 2, 0, 0], "same selected position virtual after yaw");
            RequireYaw(); _qualifiedInitialPlacement = source.Identity; return source;

            void RequireYaw()
            {
                var player = Class(".?AVPlayerCharacter@@", 191);
                var yaw = Get(player, 0x2c4);
                Need(yaw, [0x55, 0x8b, 0xec, 0x8b, 0x81, 0xfc, 0, 0, 0, 0x85, 0xc0, 0x74, 5, 0x83, 0xf8, 4], "same existing neutral Actor life gate");
                var actor = Call(yaw + 0x1d);
                Need(actor + 6, [0x83, 0x7e, 0x60, 0], "current Common existence before activity predicate");
                Need(Get(player, 0x230), [0x8b, 0x49, 0x60, 0x85, 0xc9, 0x74, 0xf, 0x8b, 1, 0xff, 0x90, 8, 4, 0, 0], "same current Common activity getter");
                Need(actor + 0x28, [0x83, 0xc0, 0xfc, 0x83, 0xf8, 6], "raw Actor yaw selection word range");
                Need(actor + 0x87, [0x8b, 0x4e, 0x1c, 0xe8], "current Base before CREA turn selector");
                var creature = Call(actor + 0x8a);
                Need(creature, [0x80, 0x79, 4, 0x2b], "actual NPC bypass of CREA turn delta");
                Need(actor + 0xf8, [0xe8], "same raw reference Z setter");
            }
            uint Class(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
                throw new NotSupportedException("Initial placement lost its selected " + name + " class.");
            int Get(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
            int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("Initial placement lost an actual selected child association.");
            int Unique(Func<int, bool> test, int tail) => Enumerable.Range(0, code.Length - tail + 1).Where(test).ToArray() is [var at] ? at :
                throw new NotSupportedException("Initial placement source selector is absent or ambiguous.");
            void Need(int at, byte[] expected, string role)
            { if (!Match(code, at, expected)) throw new NotSupportedException("Initial placement source changed its " + role + "."); }
        }
    }
}
