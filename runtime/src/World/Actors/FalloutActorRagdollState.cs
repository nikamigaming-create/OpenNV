namespace OpenNV.Runtime.World.Actors;

internal sealed record FalloutRagdollBodyState(int SourceBody, float[] Transform, float[] LinearVelocity, float[] AngularVelocity, bool Sleeping);
internal sealed record FalloutRagdollCutPose(byte Part, float[][] Bones);
internal sealed record FalloutActorRagdollState(string SkeletonSha256, IReadOnlyList<FalloutRagdollBodyState> Bodies,
    IReadOnlyList<FalloutRagdollCutPose>? Cuts = null)
{
    internal void Validate()
    {
        if (SkeletonSha256 is not { Length: 64 } || !SkeletonSha256.All(Uri.IsHexDigit) || Bodies is null || Bodies.Count == 0 ||
            Bodies.Any(body => body is null || body.SourceBody < 0 || !Finite(body.Transform, 12) ||
                !Finite(body.LinearVelocity, 3) || !Finite(body.AngularVelocity, 3) || !Invertible(body.Transform)) ||
            Bodies.Select(body => body.SourceBody).Distinct().Count() != Bodies.Count)
            throw new InvalidDataException("Saved ragdoll source/body state is invalid.");
        if (Cuts is { } cuts && (cuts.Any(cut => cut is null || cut.Part > 13 || cut.Bones is not { Length: > 0 } ||
            cut.Bones.Any(bone => !Finite(bone, 12) || !Invertible(bone))) || cuts.Select(cut => cut.Part).Distinct().Count() != cuts.Count))
            throw new InvalidDataException("Saved ragdoll cut pose is invalid.");
    }

    private static bool Finite(float[]? values, int length) => values is { } && values.Length == length && values.All(float.IsFinite);

    private static bool Invertible(float[] value)
    {
        var determinant = (double)value[0] * (value[4] * (double)value[8] - value[5] * (double)value[7])
            - value[3] * (value[1] * (double)value[8] - value[2] * (double)value[7])
            + value[6] * (value[1] * (double)value[5] - value[2] * (double)value[4]);
        return double.IsFinite(determinant) && determinant > .0001;
    }
}
