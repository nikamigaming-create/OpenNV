namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerPointOfViewFields(byte[] code, Image image, int setter)
    {
        // This branch borrows the same first-person field as the source model
        // getter, and writes a different byte from that getter's view selector.
        if (!Match(code, setter + 0x4c, [0xa8, 0x10, 0x74, 0x25, 0x83, 0xbe, 0xf0, 5, 0, 0, 0,
                0xc6, 0x86, 0xaa, 5, 0, 0, 0, 0x74, 0x0b, 0x6a, 0, 0x8b, 0xce, 0xe8]) ||
            !Match(code, setter + 0x69, [0xeb, 0x0a, 0xc7, 5]) || Word(code, setter + 0x71) != 0)
            throw new NotSupportedException("Player POV lost current first-person selection before its independent camera byte/scalar stores.");
        var scalar = Word(code, setter + 0x6d);
        if (!image.IsWritableExtent(scalar, 4))
            throw new NotSupportedException("Player POV shared Float32 state has no actual mutable source owner.");
        var players = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray();
        if (players is not [var player]) throw new NotSupportedException("Player POV lacks its exact source Player class.");
        var model = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x1d0, 4), 0));
        if (!Match(code, model, [0x56, 0x8b, 0xf1, 0x80, 0xbe, 0xa8, 5, 0, 0, 0, 0x75, 0x0a]) ||
            !Match(code, model + 0xc, [0x8b, 0x86, 0xf0, 5, 0, 0, 0x85, 0xc0]))
            throw new NotSupportedException("POV current field differs from the genuine first-person model getter.");
        var constructors = 0;
        for (var at = 0; at <= code.Length - 0x64a; ++at)
            if (Match(code, at, [0xc7, 7]) && Word(code, at + 2) == player &&
                Match(code, at + 0x5a, [0xc7, 0x87, 0xf0, 5, 0, 0, 0, 0, 0, 0]) &&
                Match(code, at + 0x54a, [0xc7, 0x87, 0xa8, 5, 0, 0, 0, 0, 0, 0]) &&
                Match(code, at + 0x640, [0xc7, 5]) && Word(code, at + 0x642) == scalar && Word(code, at + 0x646) == 0)
                constructors++;
        if (constructors != 1)
            throw new NotSupportedException("Player POV fields lack their unique actual null/zero source constructor.");
    }
}
