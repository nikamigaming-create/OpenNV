namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static uint RequirePlayerSceneScalarStores(byte[] code, Image image, int caller)
    {
        var start = caller + 0xd3;
        if (!Match(code, start, [0xf3, 0x0f, 0x10, 0x87, 0x48, 1, 0, 0, 0x0f, 0x2f, 5]) ||
            Word(image.Read(Word(code, start + 0xb), 4), 0) != 0 ||
            !Match(code, start + 0x15, [0x8b, 0x87, 0xec, 5, 0, 0]) ||
            !Match(code, start + 0x3d, [0xc7, 0x87, 0xa8, 7, 0, 0, 0, 0, 0, 0, 0xc7, 0x87, 0xac, 7, 0, 0, 0, 0, 0, 0]) ||
            !Match(code, start + 0x51, [0x76, 0x12, 0xf3, 0x0f, 0x5c, 5]) ||
            !Match(code, start + 0x5b, [0xf3, 0x0f, 0x11, 0x87, 0x48, 1, 0, 0, 0xeb, 0x0a, 0xc7, 0x87, 0x44, 1, 0, 0, 0, 0, 0, 0]))
            throw new NotSupportedException("Player scalar update changed its distinct fields, ordered-positive comparison or unclamped subtraction.");
        var player = Class(".?AVPlayerCharacter@@", 191);
        if (Word(image.Read(player + 0x2f8, 4), 0) != image.CodeBase + caller - 0xf4)
            throw new NotSupportedException("Player scalar update is not the actual selected Player scene virtual.");
        var constructors = new List<int>();
        for (var at = 5; at <= code.Length - 0xa2f; ++at)
            if (Match(code, at, [0xc7, 7]) && Word(code, at + 2) == player && code[at - 5] == 0xe8 &&
                Match(code, at + 0xa1b, [0xc7, 0x87, 0xa8, 7, 0, 0, 0, 0, 0, 0, 0xc7, 0x87, 0xac, 7, 0, 0, 0, 0, 0, 0]) &&
                Match(code, at + 0x5bf, [0xc7, 0x87, 0xec, 5, 0, 0, 0, 0, 0, 0])) constructors.Add(at);
        if (constructors.Count != 1) throw new NotSupportedException("Player scalar fields have no unique actual constructor.");
        var character = Call(constructors[0] - 5);
        if (!Match(code, character + 0xc, [0xe8]) || !Match(code, character + 0x11, [0xc7, 6]) ||
            Word(code, character + 0x13) != Class(".?AVCharacter@@", 1))
            throw new NotSupportedException("Player scalar constructor lost its actual Character parent.");
        var actor = Call(character + 0xc);
        if (!Match(code, actor + 0x6f, [0xc7, 3]) || Word(code, actor + 0x71) != Class(".?AVActor@@", 1) ||
            code[actor + 0xe9] != 0xe8)
            throw new NotSupportedException("Player scalar constructor lost its actual Actor initializer.");
        var initialize = Call(actor + 0xe9);
        if (!Match(code, initialize + 0x205, [0xc6, 0x86, 0x41, 1, 0, 0, 0]))
            throw new NotSupportedException("Actor movement branch lost its own independent constructor-zero byte.");
        if (!Match(code, initialize + 0x29c, [0xc7, 0x86, 0x44, 1, 0, 0, 0, 0, 0, 0, 0xc7, 0x86, 0x48, 1, 0, 0, 0, 0, 0, 0]))
            throw new NotSupportedException("Actor scene scalar fields changed their independent constructor-zero stores.");
        RequirePlayerSceneReferenceQueue(code, image, start + 0x6f);
        RequirePlayerTimedActions(code, image, caller, constructors[0], Word(code, start + 0x57));
        RequirePlayerPostActionFields(code, image, caller, constructors[0]);
        return Word(code, start + 0x57);

        uint Class(string name, int slots)
        {
            var tables = image.SourceClassTables(name, slots).ToArray();
            return tables.Length == 1 ? tables[0] : throw new NotSupportedException("Player scalar class declaration is absent or ambiguous: " + name);
        }
        int Call(int at) => checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
    }

    private static void RequirePlayerScalarTimerAssociation(byte[] code, Image image, uint scaledTimer)
    {
        var tables = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray();
        if (tables.Length != 1) throw new NotSupportedException("Player scalar timer has no unique source class.");
        var method = SourceOffset(code, image.CodeBase, Word(image.Read(tables[0] + 0x2f8, 4), 0));
        if (RequirePlayerSceneScalarStores(code, image, method + 0xf4) != scaledTimer || !image.IsWritableExtent(scaledTimer, 4))
            throw new NotSupportedException("Player scalar subtraction lost its actual Main scaled-timer producer.");
    }
}
