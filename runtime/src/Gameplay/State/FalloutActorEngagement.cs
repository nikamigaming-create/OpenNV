using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutActorEngagement(FalloutFormKey Target, string Action = "pursue", double Seconds = 0,
    bool StartPending = true, string? Animation = null, string? AnimationHash = null,
    IReadOnlyList<float>? Position = null, IReadOnlyList<float>? Rotation = null,
    FalloutWeaponHandlingSnapshot? WeaponHandling = null)
{
    internal void Validate()
    {
        if (Target.ObjectId == 0 || string.IsNullOrWhiteSpace(Target.OwnerPlugin) ||
            Action is not ("pursue" or "attack" or "reload" or "flee" or "idle") || !double.IsFinite(Seconds) || Seconds < 0 ||
            StartPending && Seconds != 0 || (Animation is null) != (AnimationHash is null) ||
            AnimationHash is not null && (AnimationHash.Length != 64 || !AnimationHash.All(Uri.IsHexDigit)) ||
            (Position is null) != (Rotation is null) || Position is not null && (Position.Count != 3 || Position.Any(value => !float.IsFinite(value))) ||
            Rotation is not null && (Rotation.Count != 4 || Rotation.Any(value => !float.IsFinite(value)) ||
                MathF.Abs(Rotation.Sum(value => value * value) - 1) > .001f))
            throw new InvalidDataException("Saved actor engagement is invalid.");
        if (WeaponHandling is { } handling) FalloutWeaponHandling.Validate(handling);
    }

    internal FalloutActorEngagement Transition(string action) => this with
    { Action = action, Seconds = 0, StartPending = true, Animation = null, AnimationHash = null };
}
