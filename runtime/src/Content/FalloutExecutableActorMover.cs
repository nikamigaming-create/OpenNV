using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutActorMoverSource ReadActorMover(string path, FalloutPlayerMovementControlSource controls)
    {
        var source = FalloutActorMoverSource.Reviewed(controls); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != controls.Main.EngineSha256)
            throw new InvalidDataException("Actor mover changed its selected original executable.");
        _ = ReadPackageAssignment(path, FalloutPackageCommandFieldsSource.Reviewed(controls));
        var (code, image) = Load(original);
        var playerMover = Class(".?AVPlayerMover@@", 10);
        var player = Class(".?AVPlayerCharacter@@", 191);
        var derivedStore = Unique(at => Match(code, at, [0xc7, 6]) && Word(code, at + 2) == playerMover &&
            Match(code, at + 0x23, [0xc7, 0x86, 0x94, 0, 0, 0, 0, 0, 0, 0]) &&
            Match(code, at + 0x2d, [0xc7, 0x86, 0x9c, 0, 0, 0, 0, 0, 0x80, 0xbf]) &&
            Match(code, at + 6, [0xf3, 0x0f, 0x7e, 5]) &&
            Match(code, at + 0xe, [0x66, 0x0f, 0xd6, 0x86, 0x88, 0, 0, 0, 0xa1]) &&
            Match(code, at + 0x37, [0xc7, 0x86, 0x98, 0, 0, 0, 0, 0, 0, 0]) &&
            Match(code, at + 0x41, [0x5e, 0x8b, 0xe5, 0x5d, 0xc2, 4, 0]), 0x4a);
        var derived = derivedStore - 0x12;
        Need(derived, [0x55, 0x8b, 0xec, 0x51, 0x56, 0xff, 0x75, 8, 0x8b, 0xf1, 0x89, 0x75, 0xfc, 0xe8],
            "actual inherited constructor call before PlayerMover fields");
        var inherited = Call(derived + 0xd);
        Need(inherited + 0x2b, [0xc7, 6], "selected ActorMover base constructor vtable store");
        if (!image.SourceClassTables(".?AVActorMover@@", 10).Contains(Word(code, inherited + 0x2d)))
            throw new NotSupportedException("Player mover did not call its selected actual ActorMover constructor.");
        Need(inherited + 0x31, [0xf3, 0x0f, 0x7e, 5], "inherited vector XY source read");
        Need(inherited + 0x39, [0x66, 0x0f, 0xd6, 0x46, 4, 0xa1], "inherited vector XY store before Z read");
        Need(inherited + 0x43, [0x89, 0x46, 0xc, 0xf3, 0x0f, 0x7e, 5], "inherited Z store before separate second vector read");
        Need(inherited + 0x4e, [0x66, 0x0f, 0xd6, 0x46, 0x10, 0xa1], "second inherited vector XY then source Z read");
        Need(inherited + 0x58, [0x89, 0x46, 0x18], "second inherited vector Z store");
        var vector = Word(code, inherited + 0x35);
        if (Word(code, inherited + 0x3f) != vector + 8 || Word(code, inherited + 0x4a) != vector ||
            Word(code, inherited + 0x54) != vector + 8 || image.ReadLoaderData(vector, 12).Any(value => value != 0))
            throw new NotSupportedException("Actor mover default vectors lost their same selected positive-zero source.");
        Need(inherited + 0xfd, [0xc7, 0x46, 0x34, 1, 1, 0, 0, 0xc7, 0x46, 0x70, 0, 0, 0, 0],
            "inherited flags101 before independent reset-byte-containing zero word");
        Need(derivedStore + 6, [0xf3, 0x0f, 0x7e, 5], "distinct derived vector source read");
        Need(derivedStore + 0xe, [0x66, 0x0f, 0xd6, 0x86, 0x88, 0, 0, 0, 0xa1], "distinct derived XY store then Z source read");
        Need(derivedStore + 0x1b, [0x89, 0x86, 0x90, 0, 0, 0], "derived Z store");
        Need(derivedStore + 0x37, [0xc7, 0x86, 0x98, 0, 0, 0, 0, 0, 0, 0], "derived secondary word constructor zero");
        if (Word(code, derivedStore + 0xa) != vector || Word(code, derivedStore + 0x17) != vector + 8)
            throw new NotSupportedException("PlayerMover derived vector borrowed another default source.");
        Need(Get(playerMover, 0x20), [0x8b, 0x81, 0x94, 0, 0, 0, 0xc3], "separate current movement word getter");
        _ = Unique(at => Match(code, at, [0x8b, 0x8f, 0x84, 1, 0, 0, 0x85, 0xc9, 0x74, 6,
            0x8b, 1, 0x6a, 1, 0xff, 0x10, 0x68, 0xa0, 0, 0, 0, 0xb9]) &&
            Match(code, at + 0x2a, [0x57, 0x8b, 0xc8, 0xe8]) && Call(at + 0x2d) == derived &&
            Match(code, at + 0x32, [0xeb, 2, 0x33, 0xc0, 0x89, 0x87, 0x84, 1, 0, 0]), 0x3c);

        var assignment = Get(player, 0x2f4);
        Need(assignment, [0x55, 0x8b, 0xec, 0x57, 0x8b, 0xf9, 0x83, 0x7f, 0x60, 0, 0x0f, 0x84], "actual selected Actor assignment process guard");
        Need(assignment + 0x192, [0x8b, 0x4f, 0x60, 0x57, 0x53, 0x8b, 1, 0xff, 0x90, 0x2c, 2, 0, 0], "Current state setter before shared reset joins");
        Need(assignment + 0x20f, [0x57, 0x53, 0xff, 0x90, 0xc, 2, 0, 0], "Primary state setter before its distinct children");
        Need(assignment + 0x248, [0x8b, 0x4f, 0x60, 0x6a, 0, 0x8b, 1, 0xff, 0x90, 0x2c, 1, 0, 0,
            0x8b, 0x4f, 0x60, 0x8b, 1, 0xff, 0x90, 0x40, 2, 0, 0], "linked-reference null setter before selected High reset");
        Need(assignment + 0x260, [0x80, 0xbf, 0xf8, 0, 0, 0, 0, 0x75, 0x17, 0x8b, 0x4f, 0x1c, 0xe8],
            "independent Actor byte before BaseForm predicate");
        Need(assignment + 0x271, [0x84, 0xc0, 0x75, 0xb, 0x8b, 0x8f, 0x84, 1, 0, 0, 0xe8], "same actual mover field after returned BaseForm predicate");
        var reset = Call(assignment + 0x27b);
        Need(reset, [0xf3, 0x0f, 0x7e, 5], "reset default-vector XY source read");
        Need(reset + 8, [0x66, 0x0f, 0xd6, 0x41, 4, 0xa1], "reset XY store before independent Z source read");
        Need(reset + 0x12, [0x89, 0x41, 0xc, 0xc6, 0x41, 0x73, 0, 0xc3], "reset Z store then distinct byte zero and pure return");
        if (Word(code, reset + 4) != vector || Word(code, reset + 0xe) != vector + 8)
            throw new NotSupportedException("Actor package reset borrowed a different default vector producer.");
        RequirePlayerFrameStores();
        return source;

        void RequirePlayerFrameStores()
        {
            var frame = Get(player, 0x2f8);
            Need(frame + 0xca2, [0x80, 0xbf, 0xe5, 6, 0, 0, 0, 0x0f, 0x85], "actual following first-byte late-tail branch");
            var late = checked(frame + 0xca2 + 13 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(frame + 0xca2 + 9)));
            Need(late + 0x24, [0x8b, 0x8f, 0x84, 1, 0, 0, 0x0f, 0xb7, 0x45, 0xec, 0x50, 0x8b, 0x11, 0xff, 0x52, 0xc],
                "same actual mover receiver and existing frame-local unsigned-short flag argument");
            var setter = Get(playerMover, 0xc);
            Need(setter, [0x55, 0x8b, 0xec, 0x8b, 0x55, 8, 0x8b, 0x81, 0x94, 0, 0, 0,
                0x56, 0x57, 0x8b, 0xf2, 0x8b, 0xfa, 0x25, 0, 4, 0, 0, 0x81, 0xe6, 0, 4, 0, 0,
                0x83, 0xe7, 0x30, 0x74, 0x16], "current/requested turning masks before zero-direction skip");
            Need(setter + 0x38, [0x89, 0x91, 0x94, 0, 0, 0, 0x3b, 0xc6, 0x0f, 0x84],
                "complete flag-word store before turning-change predicate");
            var returned = checked(setter + 0x46 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(setter + 0x42)));
            Need(returned, [0x5f, 0x5e, 0x5d, 0xc2, 4, 0], "zero-word flags setter's complete pure return");
            var supplied = late + 0x25f;
            Need(supplied, [0x8b, 0x8f, 0x84, 1, 0, 0, 0x68], "same frame mover and separate default-vector argument");
            if (Word(code, supplied + 7) != vector)
                throw new NotSupportedException("Player frame vector writer borrowed a different default source.");
            var copied = Call(supplied + 0xb);
            Need(copied, [0x55, 0x8b, 0xec, 0x8b, 0x45, 8, 0xf3, 0x0f, 0x7e, 0,
                0x66, 0x0f, 0xd6, 0x81, 0x88, 0, 0, 0, 0x8b, 0x40, 8,
                0x89, 0x81, 0x90, 0, 0, 0, 0x5d, 0xc2, 4, 0], "complete paired-XY/then-Z frame helper without following virtual");
            Need(supplied + 0x10, [0x8b, 0x8f, 0x84, 1, 0, 0, 0xf3, 0x0f, 0x10, 0x43, 8, 0x51,
                0xf3, 0x0f, 0x11, 4, 0x24, 0x8b, 1, 0xff, 0x50, 0x14], "independent mandatory mover delta virtual after returned vector helper");
        }

        uint Class(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
            throw new NotSupportedException("Actor mover lacks a unique selected class: " + name);
        int Get(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
        int Call(int at) => at >= 0 && at + 5 <= code.Length && code[at] == 0xe8 ?
            checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Actor mover lost an exact selected source child.");
        int Unique(Func<int, bool> predicate, int tail) => Enumerable.Range(0, code.Length - tail + 1).Where(predicate).ToArray() is [var at] ? at :
            throw new NotSupportedException("Actor mover constructor/publication source is absent or ambiguous.");
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Actor mover changed its original " + role + "."); }
    }
}
