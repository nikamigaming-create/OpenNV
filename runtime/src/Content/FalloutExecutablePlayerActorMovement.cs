using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock PlayerActorMovementQualificationLock = new();
    private static string? _qualifiedPlayerActorMovement;
    internal static FalloutPlayerActorMovementSource ReadPlayerActorMovement(string path,
        FalloutPlayerMoverDeltaSource mover, FalloutInitialPlacementCellsSource cells,
        FalloutPlayerWorldOrientationSource orientation)
    {
        var source = FalloutPlayerActorMovementSource.Reviewed(mover, cells, orientation);
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != mover.Controller.Fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("Actor movement changed its selected original source image.");
        lock (PlayerActorMovementQualificationLock)
        {
            if (_qualifiedPlayerActorMovement == source.Identity) return source;
            Qualify();
            _qualifiedPlayerActorMovement = source.Identity;
            return source;
        }

        void Qualify()
        {
            _ = ReadPlayerMoverDelta(path, mover.Controller);
            _ = ReadInitialPlacementCells(path, cells.Reference);
            _ = ReadPlayerWorldOrientation(path, orientation.Controls);
            var (code, image) = Load(original); var player = Class(".?AVPlayerCharacter@@", 191);
            var wrapper = Get(player, 0x250); var body = Call(wrapper + 0x4a);
            Need(body + 0x4b, [0x8b, 0x80, 0, 1, 0, 0, 0xff, 0xd0], "selected Actor predicate before current process/controller");
            Need(Get(player, 0x100), [0xb0, 1, 0xc3], "same actual Player Character predicate");
            Need(body + 0x5d, [0x8b, 0x4f, 0x60, 0x85, 0xc9], "actual current Common before selected controller getter");
            Need(body + 0x66, [0x8b, 0x80, 0x88, 2, 0, 0, 0xff, 0xd0], "same actual controller slot");
            Need(body + 0x7e, [0xff, 0x90, 0x24, 2, 0, 0], "independent current reference kind query");
            Need(Get(player, 0x224), [0x0f, 0xb6, 0x41, 4, 0x83, 0xc0, 0xc3, 0x83, 0xf8, 3], "reference-byte kind range predicate");
            var playerCtor = Unique(at => Match(code, at, [0xc7, 7]) && Word(code, at + 2) == player &&
                Match(code, at + 0x5a, [0xc7, 0x87, 0xf0, 5, 0, 0, 0, 0, 0, 0]), 0xa63);
            var character = Call(playerCtor - 5);
            Need(character + 0x48, [0xc6, 0x46, 4, FalloutPlayerActorMovementSource.CharacterFormKind], "actual inherited Character kind constructor store");
            Need(playerCtor + 0x65a, [0x66, 0xc7, 0x87, 0xdc, 5, 0, 0, 0, 0], "same existing disable-word constructor");
            Need(body + 0x84, [0x8b, 0x47, 0x34, 0x83, 0xec, 8, 0xf3, 0x0f, 0x7e, 0x47, 0x2c], "raw XYZ snapshot independent from position-mode getter");
            Need(body + 0xec, [0xf3, 0x0f, 0x10, 0x57, 0x20], "raw X angle before Y and Z validation");
            if (Word(image.Read(Word(code, body + 0xf7), 4), 0) != FalloutPlayerActorMovementSource.ExceptionalAngleBits)
                throw new NotSupportedException("Actor movement changed its actual exceptional-angle sentinel.");
            Need(body + 0x1e8, [0x8b, 7, 0x8b, 0xcf], "exceptional vector reset on same reference");
            var vectorSetter = Call(body + 0x228);
            Need(vectorSetter + 0xe, [0x66, 0x0f, 0xd6, 0x46, 0x20, 0x89, 0x46, 0x28], "paired raw rotations then Z before Character normalization");
            Need(vectorSetter + 0x3a, [0x8b, 6, 0x8b, 0xce, 0x6a, 2, 0xff, 0x50, 0x48], "same reference mask2 after raw vector stores");
            Need(body + 0x22d, [0x8b, 0x0d], "current Havok skip-frame setting before ordinary body");
            var skipCell = Word(code, body + 0x22f);
            Need(body + 0x50c, [0x8b, 0x80, 0xa4, 4, 0, 0, 0xff, 0xd0], "same current Player control-disable getter");
            Need(Get(player, 0x4a4), [0x8a, 0x81, 0xdc, 5, 0, 0, 0x24, 1, 0xc3], "same existing disable-word bit-zero getter");
            Need(body + 0x521, [0x8b, 0x80, 0x14, 2, 0, 0, 0xff, 0xd0], "same current Actor status word getter");
            Need(Get(player, 0x214), [0x8b, 0x81, 0x9c, 1, 0, 0, 0xc3], "existing Actor yaw-selection DWORD authority");
            Need(body + 0x5f6, [0xa1], "actual published World root before camera translation read");
            var worldGlobal = Word(code, body + 0x5f7);
            if (worldGlobal != Word(code, playerCtor + 0x58f))
                throw new NotSupportedException("Actor movement camera changed the qualified current World root publication.");
            Need(body + 0x5fd, [0x8b, 0xb0, 0xac, 0, 0, 0], "same current World camera field");
            Need(body + 0x605, [0x8b, 0x80, 0xf4, 1, 0, 0, 0xff, 0xd0], "same actual current position getter before camera distance");
            Need(body + 0x61e, [0xf3, 0x0f, 0x5c, 0x8e, 0x8c, 0, 0, 0], "actual camera world X translation");
            Need(Get(player, 0x1f4) + 9, [0x80, 0xbe, 0x41, 1, 0, 0, 0], "existing position-mode selection byte");
            Need(body + 0x653, [0x83, 0xbe, 0xfc, 0, 0, 0, 3], "same raw neutral life word before predicates");
            Need(Get(player, 0x2e8), [0x83, 0xb9, 0xfc, 0, 0, 0, 1, 0x0f, 0x94, 0xc0, 0xc3], "same existing raw-Life-equals-one predicate");
            Need(body + 0x69d, [0x0f, 0x2f, 5], "distance comparison precedes actual Player singleton arm");
            var warpCell = Word(code, body + 0x6a0); RequireSetting(FalloutPlayerActorMovementSource.SkipFramesName, skipCell, 'i', 0);
            RequireSetting(FalloutPlayerActorMovementSource.WarpDistanceName, warpCell, 'f', 0x4ab71b00);
            Need(body + 0xa63, [0x8b, 0x86, 0x94, 2, 0, 0], "same current controller word before independent disabled flag store");
            Need(body + 0xa79, [0x89, 0x86, 0x94, 2, 0, 0], "actual disabled flag write before life true query");
            Need(body + 0xa8b, [0x6a, 1], "distinct source life true argument");
            var helper = Get(player, 0x1c4); var controllerCtor = Call(helper + 0x33e);
            var controllerTable = Word(code, controllerCtor + 0x14d);
            Need(controllerCtor + 0x210, [0xc7, 0x86, 0x90, 4, 0, 0, 0, 0, 0, 0], "independent controlled phase constructor zero");
            var controlled = Call(body + 0xd65);
            Need(controlled, [0x56, 0x8b, 0xf1, 0x8d, 0x8e, 0x60, 2, 0, 0, 0xe8], "actual same controller State context getter");
            var state = Call(controlled + 9);
            Need(state, [0x8b, 0x41, 0x10, 0xc3], "existing current State field");
            Need(controlled + 0xe, [0x83, 0xf8, 2, 0x0f, 0x94, 0xc0], "selected state-two controlled branch");
            var phase = Call(controlled + 0x57);
            Need(phase + 4, [0x83, 0xbe, 0x90, 4, 0, 0, 2, 0x0f, 0x85], "independent phase-two predicate");
            Need(phase + 0x1b5, [0x5e, 0x59, 0xc2, 4, 0], "returned phase-other child without fabricated solver");
            Need(body + 0xd77, [0xff, 0x90, 0xc0, 0, 0, 0], "ordinary arm selects the same actual controller delta virtual");
            Need(body + 0xe37, [0xe8], "mandatory following proxy position child");
            var position = Call(body + 0xe37);
            Need(position + 0xc, [0x8b, 0x86, 0x24, 4, 0, 0], "actual retained proxy before rotation-offset transform");
            Need(position + 0x1d, [0x8d, 0x86, 0, 4, 0, 0], "same retained controller offset");
            Need(position + 0x57, [0xf3, 0x0f, 0x11, 0, 0xf3, 0x0f, 0x10, 0x41, 4, 0xf3, 0x0f, 0x5c, 0x44, 0x24, 0x10], "X return after saved register pop before independent Y offset read");
            Need(body + 0xea2, [0x8b, 0x80, 0xf4, 1, 0, 0, 0xff, 0xd0], "fresh reference position before correction distance");
            Need(body + 0xf13, [0xc7, 0x45, 0xa0, 0, 0, 0, 0], "local ground zero before independent TES current CELL test");
            Need(body + 0xf1a, [0x83, 0x79, 0x34, 0], "actual current TES CELL pointer");
            Need(body + 0xf61, [0x8b, 0xc8, 0xe8], "actual following controller-reference attachment child");
            var attachments = Call(body + 0xf63);
            Need(attachments + 0x50, [0x83, 0x78, 4, 0, 0x8d, 0x48, 4], "same current pending list tail before head null predicate");
            Need(attachments + 0x5c, [0x83, 0x38, 0], "same independent pending head null predicate");
            var finalPosition = Call(body + 0x135f);
            var actorPosition = Get(player, 0x2a8);
            if (finalPosition != Call(actorPosition + 0x24))
                throw new NotSupportedException("Actor movement final store changed its existing simple base-position setter.");
            Need(wrapper + 0x5c, [0x8b, 0x86, 0x94, 2, 0, 0], "fall suffix reads the same returned controller flags");
            RequireTesLoadStores();

            void RequireTesLoadStores()
            {
                // Only these scalar children are qualified. Their enclosing
                // loading, graph and callback owners have separate return lanes.
                var interior = Unique(at => Match(code, at + 0x57, [0x8b, 0x77, 0x34, 0x8b, 0x5d, 8, 0x3b, 0xf3]) &&
                    Match(code, at + 0x1f3, [0x89, 0x5f, 0x34]), 0x1fa);
                Need(interior, [0x55, 0x8b, 0xec], "actual interior load supplied-CELL receiver");
                Need(interior + 0x28, [0x8b, 0xf9], "same TES instance receiver");
                Need(interior + 0x1f3, [0x89, 0x5f, 0x34], "actual supplied interior CELL current-field store");
                var exterior = Unique(at => Match(code, at, [0xc7, 0x47, 0x34, 0, 0, 0, 0]) &&
                    Match(code, at - 0x64, [0x8b, 0x5f, 0x34]), 8);
                Need(exterior, [0xc7, 0x47, 0x34, 0, 0, 0, 0], "distinct exterior load clears current interior CELL");
            }
            void RequireSetting(string name, uint cell, char kind, uint payload)
            {
                var rows = ReadStaticSettings(image).Where(row => row.Name == name && row.Collection == SettingCollection.Main).ToArray();
                if (rows is not [{ } row] || row.Name[0] != kind || row.Payload != payload || row.Descriptor + 4 != cell)
                    throw new NotSupportedException("Actor movement changed its actual typed INI field: " + name);
            }
            uint Class(string name, int extent) => image.SourceClassTables(name, extent).ToArray() is [var table] ? table :
                throw new NotSupportedException("Actor movement selected class is absent or ambiguous: " + name);
            int Get(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
            int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
                throw new NotSupportedException("Actor movement lost a reached source child association.");
            int Unique(Func<int, bool> predicate, int tail) => Enumerable.Range(0x80, code.Length - tail - 0x80 + 1).Where(predicate).ToArray() is [var at] ? at :
                throw new NotSupportedException("Actor movement source producer is absent or ambiguous.");
            void Need(int at, byte[] expected, string role)
            { if (!Match(code, at, expected)) throw new NotSupportedException("Actor movement changed its original " + role + "."); }
        }
    }
}
