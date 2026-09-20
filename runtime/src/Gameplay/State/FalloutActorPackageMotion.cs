using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// A package's animation clock and observed pose survive presentation eviction.
// This is separate from both ambient idles and combat engagement state.
internal sealed record FalloutActorPackageMotion(FalloutFormKey Package, string PackageSha256,
    string Animation, string AnimationSha256, double Seconds, bool StartPending,
    float[] Position, float[] Rotation)
{
    internal void Validate()
    {
        static bool Hash(string hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
        if (string.IsNullOrWhiteSpace(Package.OwnerPlugin) || Package.ObjectId == 0 || !Hash(PackageSha256) ||
            string.IsNullOrWhiteSpace(Animation) || !Hash(AnimationSha256) || !double.IsFinite(Seconds) || Seconds < 0 ||
            StartPending && Seconds != 0 || Position is not { Length: 3 } || Rotation is not { Length: 4 } ||
            Position.Concat(Rotation).Any(value => !float.IsFinite(value)) ||
            Math.Abs(Rotation.Sum(value => value * value) - 1) > .001)
            throw new InvalidDataException("Saved actor package motion is invalid.");
    }
}
