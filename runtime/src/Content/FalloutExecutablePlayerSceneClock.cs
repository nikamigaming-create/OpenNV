using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    // Scene construction and the late Actor-time borrower use this same byte
    // qualification. Similar names or constant callback results are not joins.
    internal static FalloutStandalonePlayerSceneSource ReadPlayerSceneClock(string path, FalloutMainScriptCallerSource main)
    {
        main.Validate(); var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != main.EngineSha256)
            throw new InvalidDataException("Player scene clock changed its actual source image.");
        if (!FalloutSourceMainFamily.IsFallout3(main.EngineSha256))
            throw new NotSupportedException("Player scene clock selected source family is unqualified.");
        var (code, image) = Load(original);
        var tables = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray();
        if (tables is not [var player]) throw new NotSupportedException("Player scene clock lost its selected actual class.");
        var frame = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x2f8, 4), 0));
        var following = frame + 0xca2;
        if (!Match(code, following, [0x80, 0xbf, 0xe5, 6, 0, 0, 0, 0x0f, 0x85]))
            throw new NotSupportedException("Player scene clock lost the selected following frame branch.");
        var late = checked(following + 13 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(following + 9)));
        if (!Match(code, late + 0x25a, [0xe8])) throw new NotSupportedException("Player scene clock lost the Actor-time child.");
        var actorTime = checked(late + 0x25f + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(late + 0x25b)));
        if (!Match(code, actorTime + 0x3b, [0x83, 0x3d]) ||
            !Match(code, actorTime + 0x41, [4, 0xf3, 0x0f, 0x10, 0x0d]) || code[actorTime + 0x30] != 0xe8)
            throw new NotSupportedException("Player scene clock changed the actual Actor-time fields or singleton predicate.");
        var self = checked(actorTime + 0x35 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(actorTime + 0x31)));
        if (!Match(code, self + 8, [0x3b, 0x3d])) throw new NotSupportedException("Player scene clock lost its actual singleton association.");
        RequirePlayerSceneClockAssociation(code, image, player, Word(code, self + 0xa),
            Word(code, actorTime + 0x3d), Word(code, actorTime + 0x46));
        return FalloutStandalonePlayerSceneSource.Read(main);
    }

    private static void RequirePlayerSceneClockAssociation(byte[] code, Image image, uint player,
        uint singleton, uint actorKind, uint actorTimer)
    {
        int? selected = null;
        for (var at = 0x126; at < code.Length - 0x33; at++)
        {
            if (!Match(code, at, [0xa1]) || Word(code, at + 1) != singleton ||
                !Match(code, at + 5, [0xb9]) || !Match(code, at + 10, [0xfe, 5]) ||
                !Match(code, at + 16, [0x8b, 0x30, 0xe8]) ||
                !Match(code, at + 23, [0xd8, 0x0d]) || Word(code, at + 25) != actorTimer ||
                !Match(code, at + 29, [0x51, 0x8b, 0x0d]) || Word(code, at + 32) != singleton ||
                !Match(code, at + 36, [0xd9, 0x1c, 0x24, 0xff, 0x96, 0xf8, 2, 0, 0, 0xfe, 0x0d]) ||
                Word(code, at + 47) != Word(code, at + 12)) continue;
            if (selected is not null) throw new NotSupportedException("Player scene clock wrapper is ambiguous.");
            selected = at;
        }
        if (selected is not { } clock) throw new NotSupportedException("Player scene clock lost its actual wrapper/store/return order.");
        // This is the selected Main Tab/Alt prologue and independent scene
        // mode branch, not an arbitrary matching ratio consumer elsewhere.
        Need(clock - 0x126, [0x55, 0x8b, 0xec, 0x83, 0xec, 0x14, 0x53, 0x56, 0x8b, 0x35], "actual Main wrapper");
        Need(clock - 0x115, [0x6a, 9, 0xff, 0xd6, 0x66, 0x0f, 0xba, 0xe0, 0xf], "Main first key/high-bit predicate");
        Need(clock - 0x10a, [0x6a, 0x12, 0xff, 0xd6, 0x66, 0x0f, 0xba, 0xe0, 0xf], "Main second key/high-bit predicate");
        Need(clock - 0x49, [0x80, 0x7b, 6, 0, 0x8b, 0x0d], "actual scene mode before Player model getter");
        if (Word(code, clock - 0x43) != singleton ||
            !Match(code, clock - 10, [0xff, 0x90, 0xd0, 1, 0, 0, 0x85, 0xc0, 0x74]))
            throw new NotSupportedException("Player scene clock changed the model-gated selected Player object.");
        var manager = Word(code, clock + 6);
        if (checked(manager + 8) != actorKind || !image.IsWritableExtent(actorKind, 4) ||
            Word(image.ReadLoaderData(actorKind, 4), 0) != 0 ||
            image.ReadLoaderData(Word(code, clock + 12), 1)[0] != 0)
            throw new NotSupportedException("Scene and Actor-time do not read the same actual manager kind and shared bracket constructor.");
        var ratio = checked(clock + 23 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(clock + 19)));
        Need(ratio, [0x55, 0x8b, 0xec, 0x51, 0x56, 0x8b, 0xf1, 0xc7, 0x45, 0xfc, 0, 0, 0x80, 0x3f,
            0x83, 0x7e, 8, 4, 0x75], "same manager kind query before ratio branch");
        Need(ratio + 0x61, [0xd9, 5], "actual Float32 ratio numerator");
        Need(ratio + 0x67, [0xd8, 0x35], "actual Float32 ratio denominator");
        Need(ratio + 0x6d, [0x5e, 0xd8, 0x4d, 0xfc, 0x8b, 0xe5, 0x5d, 0xc3], "source scalar multiplication and complete unit-kind return");
        foreach (var field in new[] { Word(code, ratio + 0x63), Word(code, ratio + 0x69) })
            if (!image.IsWritableExtent(field, 4) || Word(image.ReadLoaderData(field, 4), 0) != FalloutStandalonePlayerSceneSource.One)
                throw new NotSupportedException("Player scene clock ratio lost its own independent loader-one field.");
        if (Word(image.Read(player + 0x2f8, 4), 0) == 0)
            throw new NotSupportedException("Player scene clock lost its actual selected virtual body.");
        void Need(int at, byte[] bytes, string role)
        { if (!Match(code, at, bytes)) throw new NotSupportedException("Player scene clock changed " + role + "."); }
    }
}
