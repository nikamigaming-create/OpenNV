using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorResidualBonePose(int Index, string Name,
    IReadOnlyList<float>? Position = null, IReadOnlyList<float>? Rotation = null, IReadOnlyList<float>? Scale = null)
{
    internal FalloutNifTransformComponents Components =>
        (Position is null ? FalloutNifTransformComponents.None : FalloutNifTransformComponents.Translation) |
        (Rotation is null ? FalloutNifTransformComponents.None : FalloutNifTransformComponents.Rotation) |
        (Scale is null ? FalloutNifTransformComponents.None : FalloutNifTransformComponents.Scale);

    internal void Validate()
    {
        static bool Invalid(IReadOnlyList<float>? values, int extent) => values is not null &&
            (values.Count != extent || values.Any(value => !float.IsFinite(value)));
        if (Index < 0 || string.IsNullOrWhiteSpace(Name) || Components == FalloutNifTransformComponents.None ||
            Invalid(Position, 3) || Invalid(Rotation, 4) || Invalid(Scale, 3) ||
            Rotation is not null && Math.Abs(Rotation.Sum(value => value * value) - 1) > .001f ||
            Scale?.Any(value => value <= 0) == true)
            throw new InvalidDataException("Saved residual bone components are invalid.");
    }

    internal FalloutActorResidualBonePose Copy() => this with
    { Position = Position?.ToArray(), Rotation = Rotation?.ToArray(), Scale = Scale?.ToArray() };
}

// Components absent from the selected source layers still retain native pose
// history. Store components directly, avoiding matrix decomposition on reentry.
internal sealed record FalloutActorResidualPose(string SkeletonResource, string SkeletonSha256,
    IReadOnlyList<FalloutActorResidualBonePose> Bones)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(SkeletonResource) || !FalloutActorFurnitureContinuation.ValidHash(SkeletonSha256) ||
            Bones is null) throw new InvalidDataException("Saved residual pose has no source skeleton.");
        var previous = -1; var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bone in Bones)
        {
            if (bone is null || bone.Index <= previous || !names.Add(bone.Name))
                throw new InvalidDataException("Saved residual bones are duplicated or unordered.");
            bone.Validate(); previous = bone.Index;
        }
    }

    internal void ValidateBinding(string resource, string sha256,
        IReadOnlyList<(string Name, FalloutNifTransformComponents Covered)> source)
    {
        Validate();
        if (!SkeletonResource.Equals(resource, StringComparison.OrdinalIgnoreCase) ||
            !SkeletonSha256.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved residual pose differs from its owned skeleton.");
        var cursor = 0;
        for (var index = 0; index < source.Count; ++index)
        {
            var (name, covered) = source[index];
            if ((covered & ~FalloutNifTransformComponents.All) != 0)
                throw new InvalidDataException("Source pose coverage has invalid components.");
            var missing = FalloutNifTransformComponents.All & ~covered;
            if (missing == FalloutNifTransformComponents.None) continue;
            if (cursor == Bones.Count || Bones[cursor] is not { } bone || bone.Index != index ||
                bone.Name != name || bone.Components != missing)
                throw new InvalidDataException("Saved residual pose differs from its source layer coverage.");
            cursor++;
        }
        if (cursor != Bones.Count)
            throw new InvalidDataException("Saved residual pose has an unbound source bone.");
    }

    internal FalloutActorResidualPose Copy() => this with { Bones = Bones.Select(bone => bone.Copy()).ToArray() };
}
