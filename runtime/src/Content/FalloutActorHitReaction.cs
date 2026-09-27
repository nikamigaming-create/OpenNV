namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorHitReaction(FalloutFormKey Idle, string IdleSha256, byte Part,
    FalloutActorAnimationSnapshot Animation, float[] Position, float[] Rotation)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Idle.OwnerPlugin) || Idle.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            IdleSha256 is not { Length: 64 } || !IdleSha256.All(Uri.IsHexDigit) || Part > 14 ||
            Animation is null || Position is not { Length: 3 } || Rotation is not { Length: 4 } ||
            Position.Any(value => !float.IsFinite(value)) || Rotation.Any(value => !float.IsFinite(value)) ||
            Math.Abs(Rotation.Sum(value => value * value) - 1) > .001)
            throw new InvalidDataException("Saved actor hit reaction is invalid.");
        FalloutActorAnimationState.Validate(Animation);
    }

    internal FalloutActorHitReaction Copy() => this with { Position = (float[])Position.Clone(), Rotation = (float[])Rotation.Clone() };
}
