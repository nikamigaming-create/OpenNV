using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Overrides retain the winning placed declaration, independently of its script.
// Difficulty and access are separate: unlocking never erases the source lock.
internal sealed record FalloutReferenceLockState(string ReferenceSha256, int Level, bool Locked)
{
    internal void Validate()
    {
        if (!Hash(ReferenceSha256) || Level is < 0 or > byte.MaxValue)
            throw new InvalidDataException("Saved reference lock state is invalid.");
    }

    internal static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    internal static int Integer(double value)
    {
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value is < int.MinValue or > int.MaxValue)
            throw new InvalidDataException("Lock command requires a signed integer argument.");
        return (int)value;
    }
}

internal sealed record FalloutReferenceOwnershipOverride(string ReferenceSha256, FalloutFormKey Owner)
{
    internal void Validate()
    {
        if (!FalloutReferenceLockState.Hash(ReferenceSha256) || string.IsNullOrWhiteSpace(Owner.OwnerPlugin) ||
            Owner.ObjectId is 0 or > FalloutFormKey.ObjectIdMask)
            throw new InvalidDataException("Saved reference ownership override is invalid.");
    }
}

internal sealed record FalloutReferenceOwnership(FalloutFormKey? Owner, int? FactionRank, FalloutFormKey? Global);
