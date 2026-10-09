namespace OpenNV.Runtime.Content;

// Raw SCHR declarations are separate from command-level script classifications.
// In particular, the original magic declaration is 0x0100, not numeric API type2.
internal enum FalloutScriptSourceKind : ushort { Object = 0, Quest = 1, MagicEffect = 0x0100 }

internal static class FalloutScriptSourceKinds
{
    internal static FalloutScriptSourceKind Classify(ushort declaration) => declaration switch
    {
        0 => FalloutScriptSourceKind.Object,
        1 => FalloutScriptSourceKind.Quest,
        0x0100 => FalloutScriptSourceKind.MagicEffect,
        _ => throw new NotSupportedException($"Original SCHR declaration {declaration:x4} has no executable script-kind owner.")
    };

    internal static FalloutScriptSourceKind Classify(FalloutScriptHeader header) => Classify(header.Type);
}
