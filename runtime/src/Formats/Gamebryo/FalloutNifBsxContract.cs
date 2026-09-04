namespace OpenNV.Runtime.Formats.Gamebryo;

internal readonly record struct FalloutNifBsxEvidence(
    bool HasCollision,
    bool HasBlendCollision,
    bool HasConstrainedCollision,
    bool HasEditorMarker,
    bool HasExternalEmittanceShader);

internal static class FalloutNifBsxContract
{
    internal const uint Animated = 1U << 0;
    internal const uint Havok = 1U << 1;
    internal const uint Ragdoll = 1U << 2;
    internal const uint Complex = 1U << 3;
    internal const uint EditorMarkers = 1U << 5;
    internal const uint Dynamic = 1U << 6;
    internal const uint Articulated = 1U << 7;
    internal const uint ExternalEmit = 1U << 9;

    private const uint SupportedFlags = Animated | Havok | Ragdoll | Complex | EditorMarkers |
        Dynamic | Articulated | ExternalEmit;

    internal static void Validate(uint flags, FalloutNifBsxEvidence evidence)
    {
        if ((flags & ~SupportedFlags) != 0)
            throw Unsupported(flags, "contains an unimplemented flag");
        if ((flags & Havok) != 0 && !evidence.HasCollision)
            throw Unsupported(flags, "declares Havok without a decoded collision attachment");
        if ((flags & Ragdoll) != 0 && !evidence.HasBlendCollision)
            throw Unsupported(flags, "declares a ragdoll without a decoded blend-collision attachment");
        if ((flags & Complex) != 0 && !evidence.HasCollision)
            throw Unsupported(flags, "complex content has no decoded collision attachment");
        if ((flags & Dynamic) != 0 && !evidence.HasCollision)
            throw Unsupported(flags, "dynamic content has no decoded collision attachment");
        if ((flags & Articulated) != 0 && !evidence.HasConstrainedCollision)
            throw Unsupported(flags, "articulated content has no decoded constrained rigid body");
        if ((flags & EditorMarkers) != 0 && !evidence.HasEditorMarker)
            throw Unsupported(flags, "declares editor markers without the exact marker subtree");
        if ((flags & ExternalEmit) != 0 && !evidence.HasExternalEmittanceShader)
            throw Unsupported(flags, "external emission has no matching shader contract");
    }

    private static NotSupportedException Unsupported(uint flags, string detail) =>
        new($"Unsupported BSX flags 0x{flags:x8}: {detail}.");
}
