namespace OpenNV.Runtime.Gameplay.State;

internal readonly record struct FalloutStandaloneMenuControlResult(uint Word, bool Deferred, bool QueriedMessage);

// These are the actual UInt32 transport operations. The physical pressed-bit
// producer and BGSMessage lookup are independent inputs; no Godot key, action
// map or empty source form is substituted for either of them.
internal static class FalloutStandaloneMenuInput
{
    internal const int Count = 29;
    internal const uint DialogControl = 17;
    internal static uint ConstructorWord(uint actualFactoryWord) => actualFactoryWord & 0xfc;
    internal static FalloutStandaloneMenuControlResult Bind(uint currentWord, uint control, uint menu,
        uint argument, bool? actualMessageHasFlagTwo)
    {
        if (control >= Count) return new(currentWord, false, false);
        var word = (currentWord & 0xff) | unchecked(argument << 8);
        var validMenu = unchecked(menu - 1001) <= 59;
        var deferred = false;
        if (validMenu)
        {
            word = ((unchecked(menu * 4 + 0x5c) ^ word) & 0xfc) ^ word;
            if (actualMessageHasFlagTwo is null)
                throw new NotSupportedException("source-FO3-control-BGSMessage-lookup-and-winning-flags-unowned");
            deferred = actualMessageHasFlagTwo.Value && (word & 2) == 0;
        }
        word = (word & ~1u) | (deferred ? 1u : 0u);
        return new(word, deferred, validMenu);
    }
}
