using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static readonly Lock PlayerMoverDeltaQualificationLock = new();
    private static string? _qualifiedPlayerMoverDelta;
    internal static FalloutPlayerMoverDeltaSource ReadPlayerMoverDelta(string path, FalloutEarlyControllerSource controller)
    {
        var source = FalloutPlayerMoverDeltaSource.Reviewed(controller); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != controller.Fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("Mover delta changed its selected original source image.");
        lock (PlayerMoverDeltaQualificationLock)
        {
            if (_qualifiedPlayerMoverDelta == source.Identity) return source;
            Qualify();
            _qualifiedPlayerMoverDelta = source.Identity;
            return source;
        }

        void Qualify()
        {
            _ = ReadActorMover(path, controller.Fields.Controls);
            _ = ReadEarlyController(path, controller.Fields, controller.Thread);
            var (code, image) = Load(original);
            var player = Class(".?AVPlayerCharacter@@", 191); var mover = Class(".?AVPlayerMover@@", 10);
            var high = Class(".?AVHighProcess@@", 460);
            var frame = Get(player, 0x2f8); var delta = Get(mover, 0x14);
            var late = checked(frame + 0xca2 + 13 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(frame + 0xca2 + 9)));
            Need(late + 0x26f, [0x8b, 0x8f, 0x84, 1, 0, 0, 0xf3, 0x0f, 0x10, 0x43, 8, 0x51,
            0xf3, 0x0f, 0x11, 4, 0x24, 0x8b, 1, 0xff, 0x50, 0x14], "same returned-vector receiver and actual scene delta argument");
            Need(delta + 0x20, [0x8b, 0x0d], "direct current Player before first Common animation getter");
            var globalPlayer = Word(code, delta + 0x22); var animation = Call(delta + 0x2c);
            Need(animation, [0x55, 0x8b, 0xec, 0x80, 0x7d, 8, 0, 0x74, 0xa,
            0x8b, 0x81, 0xec, 5, 0, 0, 0x5d, 0xc2, 4, 0, 0xe8], "direct first/Common animation argument arms");
            Need(animation + 0x13, [0xe8], "direct Common animation wrapper arm");
            var commonAnimation = Call(animation + 0x13);
            Need(commonAnimation, [0x8b, 0x49, 0x60, 0x85, 0xc9, 0x74, 8, 0x8b, 1, 0xff, 0xa0, 0xb4, 1, 0, 0, 0x33, 0xc0, 0xc3], "same mutable Common animation pointer");
            Need(delta + 0x39, [0x80, 0xb9, 0xa8, 5, 0, 0, 0, 0x75, 0x1d, 0x6a, 1], "current view before first animation read");
            if (Call(delta + 0x44) != animation) throw new NotSupportedException("Mover delta changed its distinct direct animation getters.");
            var movementOverride = Call(delta + 0x64);
            Need(movementOverride, [0x55, 0x8b, 0xec, 0x83, 0xec, 0x14, 0x53, 0x56, 0x8b, 0xf1, 0x32, 0xdb, 0x83, 0x7e, 0x60, 0], "actual independent movement-override process predicate");
            Need(movementOverride + 0x18, [0x8b, 0x80, 0x30, 2, 0, 0, 0xff, 0xd0], "activity predicate before selected six comparison");
            Need(movementOverride + 0x1e, [0xff, 0xd0, 0x84, 0xc0, 0x74, 0x10], "returned process activity branch");
            Need(movementOverride + 0x4b, [0x8b, 0x92, 0xf0, 6, 0, 0, 0xff, 0xd2, 0x84, 0xc0], "separate special-motion current byte");
            Need(Get(high, 0x6f0), [0x8a, 0x81, 0xb1, 3, 0, 0, 0xc3], "selected High special-motion getter");
            Need(Get(high, 0x2d4), [0x8a, 0x81, 0x5c, 3, 0, 0, 0xc3], "separate High dialog-motion getter");
            var highStore = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == high &&
                Match(code, at + 0x653, [0xc7, 0x46, 0x28, 0, 0, 0, 0]), 0x680);
            Need(highStore + 0x480, [0x66, 0xc7, 0x86, 0xb0, 3, 0, 0, 0, 0], "special byte inside independent High constructor word");
            Need(highStore + 0x21e, [0xc6, 0x86, 0x5c, 3, 0, 0, 0], "independent dialog-motion constructor byte");
            var movement = Get(player, 0x250);
            Need(movement, [0x55, 0x8b, 0xec, 0x83, 0xec, 0x18, 0x57, 0x8b, 0xf9, 0xe8], "actual selected Actor movement child");
            var menuPredicate = Call(movement + 9); var firstPredicate = Call(menuPredicate + 3);
            Need(firstPredicate, [0xa1], "same actual Interface pointer in independent Dialog predicate");
            Need(firstPredicate + 5, [0x85, 0xc0, 0x74, 0xb, 0x80, 0x38, 0, 0x74, 6, 0xa0], "manager/enabled before shared dialog lifecycle byte");
            Need(menuPredicate + 0x13, [0x8b, 1, 0x8b, 0x80, 0xd4, 2, 0, 0, 0xff, 0xd0], "fresh process dialog byte after true predicate");
            Need(movement + 0x2f, [0x33, 0xc0, 0x5f, 0x8b, 0xe5, 0x5d, 0xc2, 0xc, 0], "source blocked zero-word pure movement return");
            Need(movement + 0x29, [0x83, 0x7f, 0x5c, 0, 0x75, 9], "raw nullable reference-data test distinct from its model child");
            Need(delta + 0x330, [0x8b, 0x41, 8, 0xc1, 0xe8, 4, 0xa8, 1], "fresh direct-position source flag after vector/turning arms");
            Need(delta + 0x36c, [0x8b, 0x31, 0xe8], "ordinary current mover word before Actor delta child");
            Need(delta + 0x38a, [0xff, 0x96, 0x50, 2, 0, 0], "mandatory selected Actor movement virtual");
            Need(delta + 0x396, [0x8b, 0x71, 0x3c, 0x85, 0xf6, 0x74], "fresh ParentCELL only after movement child");
            Need(delta + 0x408, [0xf3, 0x0f, 0x11, 0x4c, 0x24, 0x24, 0x8d, 0x54, 0x24, 0x1c, 0x8b, 1, 0x52, 0xff, 0x90, 0xa8, 2, 0, 0], "direct/water path enters real Actor position setter");
            QualifyReferenceData(); QualifyController(); QualifySpeed(); QualifyMoverValues();

            void QualifyReferenceData()
            {
                var setter = Unique(at => Match(code, at, [0x55, 0x8b, 0xec, 0x56, 0x8b, 0x75, 8, 0x57, 0x8b, 0xf9, 0x85, 0xf6, 0x74, 0x2f, 0xe8]) &&
                    Match(code, at + 0x30, [0x89, 0x77, 0x14, 0xf0, 0xff, 0x46, 4]), 0x45);
                var ensure = Call(setter + 0xe);
                Need(ensure, [0x55, 0x8b, 0xec, 0x51, 0x56, 0x8b, 0xf1, 0x83, 0x7e, 0x5c, 0], "actual lazy nullable data getter/allocator");
                Need(ensure + 0x11, [0x6a, 0x1c], "real independent data allocation extent");
                Need(ensure + 0x24, [0xc7, 0x40, 0x14, 0, 0, 0, 0, 0xc7, 0x40, 0x18, 0, 0, 0, 0], "two model-child constructor null stores");
                Need(ensure + 0x36, [0x89, 0x46, 0x5c, 0xc7, 0, 0, 0, 0, 0, 0x8b, 0x46, 0x5c, 0xc7, 0x40, 4, 0, 0, 0, 0], "actual pointer publication before first and second word stores");
                Need(ensure + 0x49, [0x8b, 0x4e, 0x3c, 0x85, 0xc9, 0x74, 0x15, 0xf6, 0x41, 0x24, 2], "fresh current CELL before independent water predicate");
                Need(ensure + 0x7d, [0xc7, 0x40, 0xc, 0, 0, 0x80, 0xbf, 0x8b, 0x46, 0x5c, 0xc7, 0x40, 0x10, 0, 0, 0, 0], "minus-one scalar before separate third word zero");
                var release = Call(setter + 0x3d);
                Need(release + 0x27, [0x8b, 0x73, 0x5c, 0x85, 0xf6], "null setter reads data before independently releasing children");
                Need(release + 0xc0, [0xc7, 0x43, 0x5c, 0, 0, 0, 0], "data retirement precedes original reference pointer-null store");
                _ = Unique(at => Match(code, at, [0x8b, 0xcf, 0xe8]) && Call(at + 2) == ensure && Match(code, at + 7, [0x8b, 0xcf, 0xe8]), 0x20);
            }
            void QualifyController()
            {
                var helper = Get(player, 0x1c4); var constructor = Call(helper + 0x33e);
                var table = Word(code, constructor + 0x14d); var update = Get(table, 0xc0);
                Need(constructor + 0x1d5, [0xc7, 0x86, 0xd8, 4, 0, 0, 0, 0, 0, 0, 0xc7, 0x86, 0xdc, 4, 0, 0, 0, 0, 0, 0], "distinct release queue constructor head/tail null");
                Need(update + 0x46, [0x81, 0xa7, 0x94, 2, 0, 0, 0x3f, 0xff, 0xff, 0xff], "real transient-flag clear before any delta child");
                Need(update + 0x113, [0x8b, 0x8f, 0xc0, 4, 0, 0, 0x85, 0xc9], "fresh second-body before conditional release");
                Need(update + 0x12c, [0x8b, 0x8f, 0xc8, 4, 0, 0, 0xc7, 0x87, 0xc4, 4, 0, 0, 0, 0, 0, 0], "word-zero store before independent object release");
                Need(update + 0x16d, [0xf3, 0x0f, 0x5e, 5], "current Player speed cache denominator");
                Need(update + 0x17b, [0xf3, 0x0f, 0x11, 0x87, 0xf4, 3, 0, 0, 0xff, 0xd0], "scalar store before actual world getter call");
                Need(update + 0x19c, [0x0f, 0x83], "ordered small-delta branch after world getter");
                Need(update + 0x1a2, [0x85, 0xc0, 0x0f, 0x84], "actual current-null world terminal return");
                var world = Get(table, 0x98);
                Need(world, [0x85, 0xc9, 0x74, 0x14, 0x8b, 0x49, 8, 0x85, 0xc9, 0x74, 0xd, 0xe8], "same real base proxy before its current-world lookup");
                Need(world + 0x10, [0x85, 0xc0, 0x74, 4, 0x8b, 0x40, 8, 0xc3, 0x33, 0xc0, 0xc3], "pure returned current-world pointer/null arms");
            }
            void QualifySpeed()
            {
                var helper = Get(player, 0x1c4); var constructor = Call(helper + 0x33e);
                var table = Word(code, constructor + 0x14d); var update = Get(table, 0xc0);
                var cache = Word(code, update + 0x171);
                if (!image.IsWritableExtent(cache, 4) || image.ReadLoaderData(cache, 4).Any(value => value != 0))
                    throw new NotSupportedException("Player speed cache lost its independent loader declaration.");
                var producer = Unique(at => Match(code, at, [0x6a, 0, 0x6a, 0, 0x6a, 1, 0x6a, 0, 0x51, 0xc7, 4, 0x24, 0, 0, 0, 0, 0x50, 0xe8]) &&
                    Match(code, at + 0x16, [0xd9, 0x1d]) && Word(code, at + 0x18) == cache, 0x20);
                var wrapper = Call(producer + 0x11);
                Need(wrapper + 0x20, [0xe8], "real ordered loaded-Player speed scalar producer");
                var body = Call(wrapper + 0x20);
                Need(body + 0x1f, [0x6a, 0, 0x6a, 0x1e, 0x56, 0xe8], "right-leg mutable AV before left leg");
                Need(body + 0x2d, [0x6a, 0, 0x6a, 0x1d, 0x56], "independent left-leg AV query");
                Need(body + 0x8c, [0x8b, 6, 0x8b, 0xce, 0x6a, 0x48, 0xff, 0x50, 8], "integer current IgnoreCrippledLegs query");
                Need(body + 0xa6, [0x8b, 6, 0x8b, 0xce, 0x6a, 0x15], "float current speed multiplier query");
                var owner = Class(".?AVPlayerCharacter@@", 4, 0x9c); var integer = Get(owner, 8);
                Need(integer + 0x4d, [0xf3, 0x0f, 0x2c, 0x4d, 8, 0x3, 0xc1], "integer getter separately truncates a real Float32 pool");
                if (Word(code, delta + 0x22) != globalPlayer) throw new NotSupportedException("Mover speed producer changed actual Player identity association.");
            }
            void QualifyMoverValues()
            {
                // Reuse the qualified current/base dispatch and same three Player
                // modifier-pool constructors. These four descriptors have distinct
                // type/formula/callback declarations from the status-only defaults.
                _ = ReadDefaultStatusActorValues(path);
                var declarations = new Dictionary<int, uint[]>(); int? registration = null;
                string[] names = ["SpeedMult", "LeftMobilityCondition", "RightMobilityCondition", "IgnoreCrippledLimbs"];
                int[] ids = [21, 29, 30, 72]; uint[] kinds = [1, 4, 4, 7]; uint[] masks = [0x801, 0x821, 0x821, 1];
                for (var at = 0; at <= code.Length - 21; ++at)
                {
                    if (!Match(code, at, [0x6a, 0])) continue;
                    var cursor = at; var args = new uint[8]; var complete = true;
                    for (var index = 0; index < args.Length; ++index)
                        if (!Push(ref cursor, out args[index])) { complete = false; break; }
                    if (!complete || cursor > code.Length - 5 || code[cursor] != 0xe8 || args[7] > int.MaxValue) continue;
                    var selected = Array.IndexOf(ids, (int)args[7]);
                    if (selected < 0 || image.Literal(args[6]) != names[selected]) continue;
                    var target = Call(cursor);
                    if (args[0] != 0 || args[2] != 0 || args[4] != masks[selected] || args[5] != kinds[selected] ||
                        (selected == 0 ? args[1] != 0 || args[3] != 0 : args[1] == 0 || (selected == 3 ? args[3] != 0 : args[3] == 0)) ||
                        registration is { } previous && previous != target || !declarations.TryAdd(ids[selected], args))
                        throw new NotSupportedException("Mover values changed their selected descriptor type/formula/callback writer.");
                    registration = target;
                }
                if (declarations.Count != ids.Length || declarations[29][3] != declarations[30][3] ||
                    declarations[29][1] != declarations[30][1])
                    throw new NotSupportedException("Mover values omitted or duplicated their real limb/ignore descriptors.");
                var formula = SourceOffset(code, image.CodeBase, declarations[29][3]);
                Need(formula, [0x55, 0x8b, 0xec, 0x51, 0xd9, 5], "mutable body-condition formula before Float32 store/return");
                Need(formula + 0xa, [0xd9, 0x5d, 0xfc, 0xd9, 0x45, 0xfc, 0x8b, 0xe5, 0x5d, 0xc3], "same actual body-condition Float32 result");
                if (ReadStaticSettings(image).Single(row => row.Name == "fAVDDefaultBodyPartCondition" && row.Collection == SettingCollection.Game)
                    is not { } setting || setting.Descriptor + 4 != Word(code, formula + 6))
                    throw new NotSupportedException("Mover body-condition formula changed its mutable GameSetting slot.");
                var npc = Class(".?AVTESNPC@@", 78); var npcValues = Class(".?AVTESNPC@@", 3, 0x100);
                var integer = Get(npcValues, 8); var basis = Call(integer + 0x56);
                Need(basis + 0x131, [0x0f, 0xb7, 0x91, 0x42, 0xff, 0xff, 0xff, 0x8b, 0xc2, 0x5d, 0xc2, 4, 0], "NPC unsigned speed word in real ACBS field");
                var cases = image.Read(Word(code, basis + 0x14), 58); var targets = Word(code, basis + 0x1b);
                if (SourceOffset(code, image.CodeBase, Word(image.Read(targets + cases[21] * 4U, 4), 0)) != basis + 0x131)
                    throw new NotSupportedException("NPC speed descriptor changed its selected source dispatch.");
                var loader = Get(npc, 0x20);
                Need(loader + 0x64e, [0x3d, 0x41, 0x43, 0x42, 0x53], "NPC loader ACBS dispatch");
                Need(loader + 0x766, [0x8d, 0x47, 0x34, 0x8b, 0xce, 0x6a, 0x18, 0x50, 0xe8], "full ACBS payload into same current ActorBaseData fields");
            }
            bool Push(ref int at, out uint value)
            {
                value = 0;
                if (at <= code.Length - 2 && code[at] == 0x6a)
                { value = unchecked((uint)(sbyte)code[at + 1]); at += 2; return true; }
                if (at <= code.Length - 5 && code[at] == 0x68)
                { value = Word(code, at + 1); at += 5; return true; }
                return false;
            }
            uint Class(string name, int count, uint displacement = 0) => image.SourceClassTables(name, count, displacement).ToArray() is [var table] ? table :
                throw new NotSupportedException("Mover delta selected class is absent or ambiguous: " + name);
            int Get(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
            int Call(int at) => at >= 0 && at <= code.Length - 5 && code[at] == 0xe8 ?
                checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) : throw new NotSupportedException("Mover delta lost a selected direct source child.");
            int Unique(Func<int, bool> predicate, int extent) => Enumerable.Range(0, code.Length - extent + 1).Where(predicate).ToArray() is [var at] ? at :
                throw new NotSupportedException("Mover delta source producer is absent or ambiguous.");
            void Need(int at, byte[] bytes, string role)
            { if (!Match(code, at, bytes)) throw new NotSupportedException("Mover delta changed its original " + role + "."); }
        }
    }
}
