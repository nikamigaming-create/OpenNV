using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPackageStart(FalloutFormKey Package, string Sha256, float DaysPassed)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Package.OwnerPlugin) || Package.ObjectId == 0 ||
            Sha256 is not { Length: 64 } || !Sha256.All(Uri.IsHexDigit) || !float.IsFinite(DaysPassed) || DaysPassed < 0)
            throw new InvalidDataException("Saved package selection time is invalid.");
    }
}
