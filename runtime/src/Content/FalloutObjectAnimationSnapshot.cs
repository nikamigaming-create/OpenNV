namespace OpenNV.Runtime.Content;

internal sealed record FalloutObjectAnimationSnapshot(int Controller, string Sha256, string Sequence,
    double ElapsedSeconds, bool StartPending, string? PendingSequence = null)
{
    internal void Validate()
    {
        if (Controller < 0 || Sha256 is not { Length: 64 } || !Sha256.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(Sequence) || !double.IsFinite(ElapsedSeconds) || ElapsedSeconds < 0 ||
            PendingSequence is not null && string.IsNullOrWhiteSpace(PendingSequence))
            throw new InvalidDataException("Saved object animation state is invalid.");
    }
}
