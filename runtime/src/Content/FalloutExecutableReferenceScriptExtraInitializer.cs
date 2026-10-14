using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutReferenceScriptExtraInitializerSource ReadReferenceScriptExtraInitializer(string path,
        FalloutPackageCommandFieldsSource fields)
    {
        var source = FalloutReferenceScriptExtraInitializerSource.Reviewed(fields); source.Validate();
        var original = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("Script initializer changed its selected original source image.");
        _ = ReadPackageCommandFields(path, fields.Controls);
        var (code, image) = Load(original);
        var player = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray() is [var table] ? table :
            throw new NotSupportedException("Script initialization lacks its actual selected Player class.");
        var constructor = Enumerable.Range(0, code.Length - 0xa63 + 1).Where(at => Match(code, at, [0xc7, 7]) &&
            Word(code, at + 2) == player && Match(code, at + 0x5a, [0xc7, 0x87, 0xf0, 5, 0, 0, 0, 0, 0, 0])).ToArray() is [var start] ? start :
            throw new NotSupportedException("Script initialization lost its exact Player constructor association.");
        var character = Call(constructor - 5); var actor = Call(character + 12); var initializeActor = Call(actor + 0xe9);
        Need(initializeActor + 0x26, [0x81, 0x4e, 8, 0, 0, 0x20, 0], "independent Actor construction flag");
        Need(initializeActor + 0xf4, [0xc7, 0x86, 0x3c, 1, 0, 0, 0, 0, 0, 0], "constructor-null selected Actor template");
        Need(Get(0x1a4), [0x8b, 0x81, 0x3c, 1, 0, 0, 0xc3], "pure current template getter");
        Need(constructor + 0x277, [0x6a, 0, 0x8b, 0xcf, 0xc6, 0x45, 0xfc, 0x17], "Player initialized-false operand before controller construction");
        var initialized = Call(constructor + 0x27f);
        Need(initialized, [0x55, 0x8b, 0xec, 0x8b, 0x51, 8, 0x8b, 0xc2, 0x83, 0xe0, 0xf7, 0x83, 0xca, 8,
            0x80, 0x7d, 8, 0, 0x0f, 0x44, 0xd0, 0x89, 0x51, 8, 0x5d, 0xc2, 4, 0], "actual Boolean initialized-flag writer");
        Need(constructor + 0xa2f, [0x81, 0x4f, 8, 0, 4, 0, 0], "separate Player persistence flag");
        var reference = Get(0x88);
        Need(reference + 0x2a, [0x8b, 0x47, 8, 0xc1, 0xe8, 3, 0xa8, 1, 0x0f, 0x85], "current initialized-bit early predicate");
        Need(reference + 0x68b, [0x6a, 1], "independent inherited initialized-true operand before Script arm");
        if (Call(reference + 0x68d) != initialized)
            throw new NotSupportedException("Script initializer selected another reference flags owner.");
        Need(reference + 0x840, [0x8b, 0x47, 8, 0xc1, 0xe8, 5, 0xa8, 1, 0x0f, 0x85], "separate current Script suppression predicate");
        Need(reference + 0x84e, [0x8a, 0x83, 0xa4, 2, 0, 0, 0x84, 0xc0, 0x75], "same current-thread initialized-byte query");
        Need(reference + 0x863, [0x8b, 0xb3, 0x80, 2, 0, 0, 0x89, 0x75, 0xe8], "capture actual prior thread kind");
        if (Call(reference + 0x858) != Call(reference + 0x870))
            throw new NotSupportedException("Script scope changed its actual on-demand thread initializer.");
        Need(reference + 0x875, [0xc7, 0x83, 0x80, 2, 0, 0, 0x15, 0, 0, 0, 0x6a, 0xd, 0x8d, 0x4f, 0x40], "source kind hex15 before actual Script Extra lookup");
        Need(reference + 0x890, [0x85, 0xc0, 0x74, 0xa, 0x83, 0x78, 0xc, 0, 0x0f, 0x85], "existing nonnull Script bypasses attachment");
        Need(reference + 0x8a2, [0xff, 0x90, 0xa4, 1, 0, 0, 0x85, 0xc0, 0x75, 3, 0x8b, 0x47, 0x1c], "template before BaseForm fallback");
        Need(reference + 0x8c6, [0x85, 0xc0, 0x74, 0x24, 0x8b, 0x40, 4, 0x89, 0x45, 0xe4, 0x85, 0xc0, 0x74, 0x1a,
            0x50, 0x8d, 0x4f, 0x40], "distinct component-null and Script-null predicates before attachment");
        var attach = Call(reference + 0x8d8); var factory = Call(reference + 0x8e0); var publish = Call(reference + 0x8e9);
        if (attach == factory || attach == publish || factory == publish)
            throw new NotSupportedException("Script attachment, list/local-variable factory and publication lost independent source children.");
        Need(reference + 0x8dd, [0x8b, 0x4d, 0xe4], "actual attached Script receiver before list factory");
        Need(reference + 0x8e5, [0x50, 0x8d, 0x4f, 0x40], "returned list before publication into current Extra");
        Need(reference + 0x939, [0x89, 0xb3, 0x80, 2, 0, 0], "successful Script branch restores captured source kind");
        return source;

        int Get(uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(player + slot, 4), 0));
        int Call(int at) => at >= 0 && at <= code.Length - 5 && code[at] == 0xe8 ?
            checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Script initializer lost an actual selected source child association.");
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Script initializer changed its original " + role + "."); }
    }
}
