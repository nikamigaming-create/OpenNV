namespace OpenNV.Runtime.Content;

internal enum FalloutSourceSkyImageSlot { CurrentPrimary, CurrentSecondary, PreviousPrimary, PreviousSecondary }
internal enum FalloutSourceSkyImageProgramKind { Null, AnonymousEngineDefault, WinningImad }
internal sealed record FalloutSourceSkyImageModifier(Guid Identity, Guid Sky, FalloutSourceSkyImageSlot Slot,
    int? ManagerOrdinal, uint Flags, byte EnabledByte, uint WeightBits,
    FalloutSourceSkyImageProgramKind Program, FalloutFormKey? Form, string? ProgramSha256, long Changed);
internal sealed record FalloutSourceSkyImageContribution(FalloutSourceSkyImageModifier Instance,
    FalloutImageSpaceModifier? Source, string? Failure);

// Native/managed instance identities are never used as Bethesda FormIDs. The
// default program is the selected original anonymous object's real neutral
// curve construction; a missing winning IMAD never becomes that fallback.
internal static class FalloutSourceSkyImageProgram
{
    internal static string DefaultIdentity(FalloutSkyTransferDeclaration source)
    { source.Validate(); return FalloutSkyTransferDeclaration.Hash(source.Contract + "\0anonymous-two-knot-program"); }
    internal static void Validate(FalloutSourceSkyImageModifier instance, FalloutSkyTransferDeclaration source,
        FalloutPluginStack records)
    {
        if (instance.Identity == Guid.Empty || instance.Sky == Guid.Empty || instance.Changed < 1 ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(instance.WeightBits)) ||
            instance.ManagerOrdinal is { } ordinal && (ordinal is < 0 or > 3) || instance.Flags != 1 || instance.EnabledByte != 1)
            throw new InvalidDataException("Sky image instance omitted its actual field/manager lifetime.");
        switch (instance.Program)
        {
            case FalloutSourceSkyImageProgramKind.Null:
                if (instance.Form is not null || instance.ProgramSha256 is not null) throw new InvalidDataException("Null Sky image instance has a fabricated program.");
                break;
            case FalloutSourceSkyImageProgramKind.AnonymousEngineDefault:
                if (instance.Form is not null || instance.ProgramSha256 != DefaultIdentity(source))
                    throw new InvalidDataException("Anonymous Sky modifier was promoted to a record or changed its constructor.");
                break;
            case FalloutSourceSkyImageProgramKind.WinningImad:
                if (instance.Form is not { } form || FalloutImageSpaceModifierReader.Read(records.GetEffective(form)).SourceSha256 != instance.ProgramSha256)
                    throw new InvalidDataException("Sky image instance changed its exact winning IMAD payload.");
                break;
            default: throw new InvalidDataException("Sky image instance has an unknown program disposition.");
        }
    }
}
