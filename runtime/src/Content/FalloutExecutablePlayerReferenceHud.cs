using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutPlayerReferenceHudSource ReadPlayerReferenceHud(string path, FalloutMainScriptCallerSource main)
    {
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != main.EngineSha256)
            throw new InvalidDataException("Player HUD declaration changed its selected executable.");
        var source = FalloutPlayerReferenceHudSource.Reviewed(main);
        var (code, image) = Load(bytes);
        var found = new List<int>();
        for (var at = 0; at <= code.Length - 0x2d9; ++at)
            if (Match(code, at, [0x55, 0x8b, 0xec, 0x51, 0x8b, 0x0d]) &&
                Match(code, at + 10, [0x53, 0x56, 0x8b, 0x75, 8, 0xc6, 0x45, 0xff, 0, 0x85, 0xf6, 0x0f, 0x84]) &&
                Match(code, at + 0x23, [0x80, 0x3d]) && Match(code, at + 0x30, [0x80, 0x7d, 0x0c, 0, 0x0f, 0x84]))
                found.Add(at);
        if (found.Count != 1) throw new NotSupportedException("Player HUD reference consumer is absent or ambiguous.");
        var start = found[0];
        if (!Match(code, start + 0x242, [0x8b, 0x89, 0x20, 1, 0, 0, 0x68, 0xf4, 0x0f, 0, 0, 0xe8]) ||
            !Match(code, start + 0x255, [0xf3, 0x0f, 0x10, 0x45, 8, 0x0f, 0x2e, 5]) ||
            !Match(code, start + 0x261, [0x9f, 0xf6, 0xc4, 0x44, 0x7a, 0x5d]) ||
            Word(image.Read(Word(code, start + 0x25d), 4), 0) != 0x3f800000 ||
            !Match(code, start + 0x2c4, [0x8a, 0x45, 0x0c, 0x5e, 0xc6, 5]) || code[start + 0x2ce] != 0 ||
            !Match(code, start + 0x2cf, [0xa2]) || !Match(code, start + 0x2d4, [0x5b, 0x8b, 0xe5, 0x5d, 0xc3]))
            throw new NotSupportedException("Player HUD clear comparison or independent flag stores changed.");
        var manager = Word(code, start + 6);
        var hides = new[] { 0x267, 0x284, 0x2a4 };
        for (var index = 0; index < hides.Length; ++index)
        {
            var hide = start + hides[index];
            if (!Match(code, hide, [0x6a, 1, 0x51, 0x8b, 0x0d]) || Word(code, hide + 5) != manager ||
                !Match(code, hide + 9, [0xc7, 4, 0x24, 0, 0, 0, 0, 0x68, 0xa3, 0x0f, 0, 0]))
                throw new NotSupportedException("Player HUD hide setter lost its genuine manager or visible trait.");
        }
        if (image.ReadLoaderData(Word(code, start + 0x25), 1)[0] != source.InitialSuppressed ||
            image.ReadLoaderData(Word(code, start + 0x2ca), 1)[0] != source.InitialHostile ||
            image.ReadLoaderData(Word(code, start + 0x2d0), 1)[0] != source.InitialArgument)
            throw new NotSupportedException("Player HUD independent loaded bytes changed.");
        // Bind the same menu member used above to the source-owned branch and
        // three actual template factories. Definitions are not live UI tiles.
        var branch = new List<int>();
        for (var at = 0; at <= code.Length - 40; ++at)
            if (code[at] == 0x68 && image.Literal(Word(code, at + 1)) == source.Branch &&
                Match(code, at + 5, [0x68, 0xec, 3, 0, 0, 0xe8]) &&
                Match(code, at + 24, [0x8b, 0x0d]) && Word(code, at + 26) == manager &&
                Match(code, at + 30, [0x83, 0xc4, 8, 0x89, 0x81, 0x20, 1, 0, 0])) branch.Add(at);
        if (branch.Count != 1) throw new NotSupportedException("Player HUD actual branch member is unbound.");
        var constructor = branch[0];
        var templates = new[] { source.BracketTemplate, source.MeterTemplate, source.NameTemplate };
        var offsets = new[] { 0x107, 0x162, 0x21f };
        for (var index = 0; index < templates.Length; ++index)
        {
            var at = constructor + offsets[index];
            if (code[at] != 0x68 || image.Literal(Word(code, at + 1)) != templates[index])
                throw new NotSupportedException("Player HUD template field association changed.");
        }
        if (!Match(code, constructor + 0xe4, [0xc7, 4, 0x24, 0, 0, 0, 0x40]) ||
            !Match(code, constructor + 0xf1, [0x68, 0xf4, 0x0f, 0, 0, 0xe8]))
            throw new NotSupportedException("Player HUD constructor color trait changed.");
        var caller = RequirePlayerActorScalarReset(code, image, start);
        RequirePlayerThreadContext(code, image, caller, main);
        RequirePlayerActorProcessQueries(code, image, caller);
        source.Validate(); return source;
    }
    private static int RequirePlayerActorScalarReset(byte[] code, Image image, int hud)
    {
        int? reset = null, caller = null;
        for (var at = 0; at <= code.Length - 0x32; ++at)
        {
            if (code[at] != 0xe8 || checked((long)image.CodeBase + at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) != image.CodeBase + hud ||
                !Match(code, at + 5, [0x8b, 0x8f, 0xf4, 8, 0, 0, 0x83, 0xc4, 0x0c, 0x85, 0xc9, 0x74, 0x19]) ||
                !Match(code, at + 0x21, [0xc7, 0x87, 0xf4, 8, 0, 0, 0, 0, 0, 0, 0x8b, 0xcf, 0xe8])) continue;
            if (reset is not null) throw new InvalidDataException("Player Actor scalar-reset caller is ambiguous.");
            reset = checked(at + 0x32 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 0x2e)));
            caller = at;
        }
        if (reset is not { } body || body < 0 || body > code.Length - 41)
            throw new NotSupportedException("Player Actor scalar reset has no actual HUD-to-release source caller.");
        for (var index = 0; index < 4; ++index)
            if (!Match(code, body + index * 10, [0xc7, 0x81]) || Word(code, body + index * 10 + 2) != 0x16c + index * 4 ||
                Word(code, body + index * 10 + 6) != FalloutStandalonePlayerSceneSource.ActorSceneResetBits)
                throw new NotSupportedException("Player Actor scene reset changed a distinct original scalar store.");
        if (code[body + 40] != 0xc3) throw new NotSupportedException("Player Actor scalar reset has an unowned trailing child.");
        return caller!.Value;
    }
}
