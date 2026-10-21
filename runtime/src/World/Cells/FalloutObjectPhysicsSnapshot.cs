namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutObjectPhysicsSnapshot(int Body, string Sha256,
    IReadOnlyList<float> Transform, IReadOnlyList<float> LinearVelocity,
    IReadOnlyList<float> AngularVelocity, bool Sleeping, ulong? WindRandomState)
{
    internal void Validate()
    {
        if (Body < 0 || Sha256 is not { Length: 64 } || !Sha256.All(Uri.IsHexDigit) ||
            Transform is not { Count: 12 } || LinearVelocity is not { Count: 3 } || AngularVelocity is not { Count: 3 } ||
            Transform.Concat(LinearVelocity).Concat(AngularVelocity).Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Saved object physics identity or state is invalid.");
        var determinant = (double)Transform[0] * (Transform[4] * (double)Transform[8] - Transform[5] * (double)Transform[7]) -
            Transform[3] * (Transform[1] * (double)Transform[8] - Transform[2] * (double)Transform[7]) +
            Transform[6] * (Transform[1] * (double)Transform[5] - Transform[2] * (double)Transform[4]);
        if (!double.IsFinite(determinant) || determinant <= 0)
            throw new InvalidDataException("Saved object physics transform is singular or reflected.");
    }

    internal FalloutObjectPhysicsSnapshot Copy() => this with
    {
        Transform = Transform.ToArray(),
        LinearVelocity = LinearVelocity.ToArray(),
        AngularVelocity = AngularVelocity.ToArray()
    };

    internal static void ValidateSet(IReadOnlyList<FalloutObjectPhysicsSnapshot> bodies)
    {
        var seen = new HashSet<(string, int)>();
        foreach (var body in bodies)
        {
            if (body is null) throw new InvalidDataException("Saved object physics body is absent.");
            body.Validate();
            if (!seen.Add((body.Sha256.ToLowerInvariant(), body.Body)))
                throw new InvalidDataException("Saved object physics body is duplicated.");
        }
    }
}
