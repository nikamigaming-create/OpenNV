namespace OpenNV.Runtime.Content;

internal sealed record FalloutActorAnimationSnapshot(string Resource, string Sha256, double ElapsedSeconds, bool StartPending);

// Reference state outlives its loaded skin. Reassembly binds the same resource
// and resumes its clock without replaying already crossed sounds or events.
internal sealed class FalloutActorAnimationState
{
    internal string Resource { get; private set; } = "";
    internal string Sha256 { get; private set; } = "";
    internal double ElapsedSeconds { get; private set; }
    internal bool StartPending { get; private set; } = true;

    internal static void Validate(FalloutActorAnimationSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Resource) || snapshot.Sha256 is not { Length: 64 } ||
            !snapshot.Sha256.All(Uri.IsHexDigit) || !double.IsFinite(snapshot.ElapsedSeconds) || snapshot.ElapsedSeconds < 0 ||
            snapshot.StartPending && snapshot.ElapsedSeconds != 0)
            throw new InvalidDataException("Saved actor animation clock is invalid.");
    }

    internal void Bind(string resource, string sha256)
    {
        Validate(new(resource, sha256, 0, true));
        if (Resource.Length != 0 && (!Resource.Equals(resource, StringComparison.OrdinalIgnoreCase) ||
            !Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Saved actor animation differs from the winning source; state migration is unresolved.");
        Resource = resource; Sha256 = sha256;
    }

    internal void Advance(double delta)
    {
        if (Resource.Length == 0 || !double.IsFinite(delta) || delta < 0 || !double.IsFinite(ElapsedSeconds + delta))
            throw new InvalidDataException("Actor animation has an invalid advancement.");
        ElapsedSeconds += delta; StartPending = false;
    }

    internal FalloutActorAnimationSnapshot? Capture() => Resource.Length == 0 ? null : new(Resource, Sha256, ElapsedSeconds, StartPending);
    internal void Restore(FalloutActorAnimationSnapshot snapshot)
    {
        Validate(snapshot); Bind(snapshot.Resource, snapshot.Sha256);
        ElapsedSeconds = snapshot.ElapsedSeconds; StartPending = snapshot.StartPending;
    }
}
