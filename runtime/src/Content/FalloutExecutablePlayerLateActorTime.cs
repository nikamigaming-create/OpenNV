using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutPlayerLateActorTimeSource ReadPlayerLateActorTime(string path,
        FalloutPlayerFollowingContinuationSource following)
    {
        _ = ReadPlayerFollowingContinuation(path, following.Controls);
        var source = FalloutPlayerLateActorTimeSource.Reviewed(following);
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != following.Controls.Main.EngineSha256)
            throw new InvalidDataException("Late Actor-time changed its original selected image.");
        var (code, image) = Load(original);
        var player = Class(".?AVPlayerCharacter@@", 191);
        var high = Class(".?AVHighProcess@@", 460);
        var middle = Class(".?AVMiddleHighProcess@@", 460);
        var frame = Get(player, 0x2f8); var followingAt = frame + 0xca2;
        var late = Conditional(followingAt + 7);
        Need(late + 0x23f, [0x8a, 0x87, 0xaa, 5, 0, 0, 0x3a, 0x87, 0xa8, 5, 0, 0, 0x75, 0x12],
            "desired/current view comparison before independent Actor-time child");
        var actorTime = Call(late + 0x25a);
        Need(actorTime, [0x55, 0x8b, 0xec, 0x83, 0xec, 0x14, 0x57, 0x8b, 0xf9, 0x8b, 0x47, 8,
            0xc1, 0xe8, 0xb, 0xa8, 1, 0x0f, 0x85], "raw reference flag predicate");
        Need(actorTime + 0x17, [0x80, 0xbf, 0xb4, 0, 0, 0, 0, 0x0f, 0x84], "actual Actor-update byte");
        Need(actorTime + 0x24, [0x8b, 7, 0xff, 0x90, 0x40, 1, 0, 0, 0x6a, 0, 0x8b, 0xcf, 0xe8],
            "selected Mobile virtual before Player-self predicate");
        var self = Call(actorTime + 0x30);
        Need(self, [0x55, 0x8b, 0xec, 0x53, 0x56, 0x57, 0x8b, 0xf9, 0x3b, 0x3d], "actual Player singleton self comparison");
        var playerSingleton = Word(code, self + 0xa);
        var selfReturn = Conditional(self + 0xe);
        Need(selfReturn, [0x5f, 0x5e, 0x32, 0xc0, 0x5b, 0x5d, 0xc2, 4, 0], "complete Player-self false arm");
        Need(actorTime + 0x35, [0x88, 0x87, 0x51, 1, 0, 0], "returned predicate byte store");
        Need(actorTime + 0x3b, [0x83, 0x3d], "shared time-manager kind read");
        Need(actorTime + 0x41, [4, 0xf3, 0x0f, 0x10, 0x0d], "kind-four and independent current Main scalar");
        RequirePlayerSceneClockAssociation(code, image, player, playerSingleton,
            Word(code, actorTime + 0x3d), Word(code, actorTime + 0x46));
        Need(actorTime + 0x78, [0xf3, 0x0f, 0x10, 0x87, 0x48, 1, 0, 0, 0x0f, 0x57, 0xd2,
            0x0f, 0x2f, 0xc2, 0x76, 0xe], "ordered positive remaining scalar");
        Need(actorTime + 0x88, [0xf3, 0x0f, 0x5c, 0xc1, 0xf3, 0x0f, 0x11, 0x87, 0x48, 1, 0, 0], "exact Float32 subtraction and store");
        Need(actorTime + 0x96, [0xc7, 0x87, 0x44, 1, 0, 0, 0, 0, 0, 0], "nonpositive/unordered independent word-zero store");

        var constructor = Unique(at => Match(code, at, [0xc7, 7]) && Word(code, at + 2) == player &&
            Match(code, at + 0x5a, [0xc7, 0x87, 0xf0, 5, 0, 0, 0, 0, 0, 0]), 0xa63);
        var characterConstructor = Call(constructor - 5);
        Need(characterConstructor, [0x55, 0x8b, 0xec, 0x51, 0x56, 0x8b, 0xf1, 0x6a, 1, 0x89, 0x75, 0xfc, 0xe8],
            "Player Character-to-Actor constructor association");
        var actorConstructor = Call(characterConstructor + 0xc);
        var mobileConstructor = Call(actorConstructor + 0x2b);
        Need(mobileConstructor + 0x3a, [0xc7, 0x46, 0x7c, 0xff, 0, 1, 0], "packed Mobile constructor word including selector-ff");
        var actorInitialize = Call(actorConstructor + 0xe9);
        Need(actorInitialize + 0xb1, [0xc6, 0x86, 0x0c, 1, 0, 0, 0], "independent Actor AI-block byte-zero constructor");
        Need(actorInitialize + 0x1c4, [0x66, 0xc7, 0x86, 0x68, 1, 0, 0, 0, 1], "packed constructor word including date-mismatch-one");
        Need(actorInitialize + 0x326, [0xc6, 0x86, 0x51, 1, 0, 0, 1], "independent self-predicate constructor-one");
        var mobile = Get(player, 0x140);
        Need(mobile + 0x30, [0x80, 0x7f, 0x7c, 0, 0x0f, 0x85], "current Mobile zero selector");
        var one = Conditional(mobile + 0x34);
        Need(one, [0x80, 0x7f, 0x7c, 1, 0x75], "current Mobile one selector");
        var three = one + 6 + unchecked((sbyte)code[one + 5]);
        Need(three, [0x80, 0x7f, 0x7c, 3, 0x0f, 0x85], "current Mobile three selector");
        var other = Conditional(three + 4);
        Need(other, [0xc6, 0x47, 0x7c, 0xff], "other Mobile byte's genuine ff store");
        Need(other + 4, [0x8b, 0x4d, 0xf4, 0x64, 0x89, 0x0d, 0, 0, 0, 0, 0x59, 0x5f, 0x5e, 0x5b,
            0x8b, 0x4d, 0xf0, 0x33, 0xcd, 0xe8], "selected Mobile compiled exit after its real store");
        Need(other + 0x1c, [0x8b, 0xe5, 0x5d, 0xc3], "Mobile return after compiler stack check");

        Need(Get(high, 0x30), [0x8b, 0x41, 0x24, 0x25, 0xff, 1, 0, 0, 0xc3], "real packed-date getter");
        var middleConstructor = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == middle &&
            Match(code, at + 0xcc, [0xc7, 0x86, 0x14, 1, 0, 0, 0, 0, 0, 0]), 0x440);
        var middleLow = Call(middleConstructor - 5); var low = Call(middleLow + 0x29);
        Need(low + 0xb6, [0xc7, 0x46, 0x24, 0, 0, 0, 0], "distinct Low packed-date constructor-zero");
        var copy = Call(Get(high, 4) + 0x18b);
        Need(copy + 0x52, [0x8b, 0x46, 0x24, 0x8b, 0xcf, 0x89, 0x47, 0x24], "same Low-to-High packed-date copy");
        Need(actorTime + 0xbe, [0x8b, 0x4f, 0x60, 0x8b, 1, 0xff, 0x50, 0x30, 0xb9], "packed-day before calendar getter");
        var day = Call(actorTime + 0xcd);
        Need(day, [0x8b, 0x41, 8, 0x85, 0xc0, 0x74, 6, 0xf3, 0x0f, 0x2c, 0x40, 0x24, 0xc3], "current calendar Float32 truncation");
        Need(actorTime + 0xd2, [0x0f, 0xbe, 0xc8, 0x3b, 0xf1, 0x74, 0x54,
            0x80, 0xbf, 0x69, 1, 0, 0, 0, 0x75, 0x42], "caller signed-byte comparison and real prior-mismatch predicate");
        Need(actorTime + 0x124, [0xc6, 0x87, 0x69, 1, 0, 0, 1, 0xeb, 7,
            0xc6, 0x87, 0x69, 1, 0, 0, 0], "distinct mismatch and equal-day stores");
        Need(actorTime + 0x134, [0x8b, 0x47, 0x60, 0x83, 0x78, 0x28, 0, 0x75], "actual raw tier before travel predicate");
        var travel = Call(actorTime + 0x143);
        Need(travel, [0x83, 0xb9, 0xb4, 5, 0, 0, 0, 0x0f, 0x9f, 0xc0, 0xc3], "same Player signed-positive travel leaf");
        Need(actorTime + 0x1ae, [0x38, 0x87, 0x0c, 1, 0, 0, 0x0f, 0x85], "Actor AI-block byte after returned life-false");
        // This association replaces the old candidate's wrong offset. The
        // global zero branch precedes the entire nonempty AI/package body.
        Need(actorTime + 0x1ba, [0x38, 5], "same actual shared global AI byte comparison");
        var globalAi = Word(code, actorTime + 0x1bc);
        Need(actorTime + 0x1c0, [0x0f, 0x84], "global zero branch over independent AI/package/date children");
        if (image.ReadLoaderData(globalAi, 1)[0] != 0) throw new NotSupportedException("Global AI lost its exact loader-zero producer.");
        var globalWriter = Unique(at => Match(code, at, [0x38, 5]) && Word(code, at + 2) == globalAi &&
            Match(code, at + 0x12, [0x0f, 0x94, 0xc2, 0x88, 0x15]) && Word(code, at + 0x17) == globalAi, 0x7f);
        // Reuse the existing complete console CommandInfo lookup. Its table
        // helper is generic despite its older DataHandler-specific name.
        var toggle = image.DataHandlerConsoleCommandStart("ToggleAI", code);
        if (toggle + 0x60 != globalWriter)
            throw new NotSupportedException("Shared global AI store is not its actual named console command.");
        Need(toggle + 0x37, [0x8b, 0x4d, 0xfc, 0x85, 0xc9, 0x0f, 0x85], "parsed optional reference before global selection");
        Need(toggle + 0x5c, [0x85, 0xc9, 0x75], "independent RTTI-null predicate before global selection");
        // The actual store uses DL, not an immediate. Inventory both direct
        // encodings; no other selected static store may silently retain zero.
        var writes = Enumerable.Range(0, code.Length - 7).Where(at =>
            (Match(code, at, [0x88, 0x15]) || Match(code, at, [0xc6, 5])) && Word(code, at + 2) == globalAi).ToArray();
        if (writes is not [var writer] || writer != globalWriter + 0x15)
            throw new NotSupportedException("Shared global AI has an additional unowned static writer.");
        var knownWriterOperands = new HashSet<int> { globalWriter + 2, globalWriter + 0x17,
            globalWriter + 0x2e, globalWriter + 0x57, actorTime + 0x1bc };
        var readOnlyOperands = 0;
        for (var at = 2; at < code.Length - sizeof(uint); at++)
        {
            if (Word(code, at) != globalAi || knownWriterOperands.Contains(at)) continue;
            // The other selected references are absolute comparison reads of
            // this same byte. An address materialization or other shape is an
            // explicit source refusal, not an inferred null/unchanged writer.
            if (!Match(code, at - 2, [0x80, 0x3d]) || code[at + sizeof(uint)] != 0)
                throw new NotSupportedException("Shared AI acquired an unowned symbol address/consumer shape.");
            readOnlyOperands++;
        }
        if (readOnlyOperands != 3)
            throw new NotSupportedException("Shared AI changed its complete selected direct symbol-reference graph.");
        Need(globalWriter + 0x32, [0x80, 0xbe, 0x50, 2, 0, 0, 0, 0x74], "independent console branch after global store");
        Need(actorTime + 0x479, [0x83, 0x3d], "current hardware-thread-count predicate after independent death-life child");
        var hardwareThreads = Word(code, actorTime + 0x47b);
        Need(actorTime + 0x47f, [1, 0x75, 0x21], "same signed integer one predicate before synchronous Actor values");
        if (!image.IsWritableExtent(hardwareThreads, 4) || Word(image.ReadLoaderData(hardwareThreads, 4), 0) != 1 ||
            image.Literal(Word(image.Read(hardwareThreads + 4, 4), 0)) != FalloutPlayerLateActorTimeSource.HardwareThreadsSetting)
            throw new NotSupportedException("Actor-time substituted another current INI declaration.");
        Need(actorTime + 0x48b, [0x8b, 0x0d], "actual Interface singleton before pure false leaf");
        Need(Call(actorTime + 0x491), [0x32, 0xc0, 0xc3], "complete Interface false leaf");
        var values = Call(actorTime + 0x49e);
        Need(values + 0x2a, [0x8b, 0x47, 8, 0xc1, 0xe8, 0x15, 0xa8, 1, 0x0f, 0x85], "actual Actor-construction flag before values kind-fifty");
        var valuesReturn = Conditional(values + 0x32);
        Need(valuesReturn, [0x8b, 0x4d, 0xf4, 0x64, 0x89, 0x0d, 0, 0, 0, 0, 0x59, 0x5f, 0x5e, 0x5b,
            0x8b, 0xe5, 0x5d, 0xc2, 4, 0], "complete Actor-values construction-flag return before independent work");
        Need(actorTime + 0x4a3, [0x3b, 0x3d], "actual Player singleton before nonplayer correction");
        if (Word(code, actorTime + 0x4a5) != playerSingleton)
            throw new NotSupportedException("Actor-time changed its actual selected Player singleton association.");
        return source;

        uint Class(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
            throw new NotSupportedException("Late Actor-time lost its selected class: " + name);
        int Get(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Late Actor-time changed an actual direct child.");
        int Conditional(int at) => checked(at + 6 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 2)));
        int Unique(Func<int, bool> predicate, int extent)
        {
            var found = Enumerable.Range(0, code.Length - extent).Where(predicate).ToArray();
            return found is [var at] ? at : throw new NotSupportedException("Late Actor-time source association is absent or ambiguous.");
        }
        void Need(int at, byte[] bytes, string role)
        { if (!Match(code, at, bytes)) throw new NotSupportedException("Late Actor-time changed its source " + role + "."); }
    }
}
