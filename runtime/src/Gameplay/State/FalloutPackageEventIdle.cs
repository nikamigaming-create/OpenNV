using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPackageEventIdle(FalloutFormKey Package, string PackageSha256, string Kind,
    FalloutFormKey Idle, string IdleSha256, string Animation, string AnimationSha256,
    FalloutIdleAnimationPlaybackSnapshot Clock)
{
    internal void Validate()
    {
        static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        if (string.IsNullOrWhiteSpace(Package.OwnerPlugin) || Package.ObjectId == 0 ||
            string.IsNullOrWhiteSpace(Idle.OwnerPlugin) || Idle.ObjectId == 0 || Kind is not ("POBA" or "POEA" or "POCA") ||
            !Hash(PackageSha256) || !Hash(IdleSha256) || !Hash(AnimationSha256) || string.IsNullOrWhiteSpace(Animation) || Clock is null)
            throw new InvalidDataException("Saved package event idle has invalid source ownership.");
        Clock.Validate();
        if (Clock.Complete) throw new InvalidDataException("Saved package event idle has already released its pose owner.");
    }
}
