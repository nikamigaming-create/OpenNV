using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// A previously selected source IDLE can outlive its dialogue voice or script
// invocation. Its clock and residual pose remain independent of a failed PACK
// begin; restoring that pose must not repeat selection or the original result.
internal sealed record FalloutActorStoppedIdleAnimation(string Owner, FalloutActorPackageIdleAnimation Animation)
{
    internal void Validate()
    {
        if (Owner is not ("script" or "dialogue-response" or "package-event") || Animation is null)
            throw new InvalidDataException("Stopped source IDLE has an invalid independent owner.");
        Animation.Validate();
    }

    internal void Validate(FalloutPluginStack records)
    {
        Validate();
        Animation.ValidateSource(records);
    }

    internal FalloutActorStoppedIdleAnimation Copy() => this with { Animation = Animation.Copy() };
}
