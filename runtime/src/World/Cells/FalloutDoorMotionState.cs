using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// The selected source controller owns the clock and pose. This state retains
// the gameplay target and its completion independently of presentation lifetime.
internal sealed record FalloutDoorMotionState(int Controller, string Sha256, bool Open, bool Moving, string? ScriptSequence = null)
{
    internal int OpenState => Open ? Moving ? 2 : 1 : Moving ? 4 : 3;

    internal void Validate(bool open, IReadOnlyList<FalloutObjectAnimationSnapshot>? animations)
    {
        if (Controller < 0 || Sha256 is not { Length: 64 } || !Sha256.All(Uri.IsHexDigit) || Open != open)
            throw new InvalidDataException("Saved door motion has invalid source identity or target.");
        var clock = animations?.SingleOrDefault(state => state.Controller == Controller &&
            state.Sha256.Equals(Sha256, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidDataException("Saved door motion has no retained source animation clock.");
        if (ScriptSequence is not null && (string.IsNullOrWhiteSpace(ScriptSequence) || Moving) ||
            !clock.Sequence.Equals(ScriptSequence ?? (Open ? "Open" : "Close"), StringComparison.OrdinalIgnoreCase) ||
            ScriptSequence is null && clock.PendingSequence is not null)
            throw new InvalidDataException("Saved door motion differs from its retained source animation selection.");
    }

    internal FalloutDoorMotionState Request(bool open)
    {
        if (open == Open) return this;
        if (Moving) throw new NotSupportedException("Reversing a moving door requires source transition behavior.");
        return this with { Open = open, Moving = true, ScriptSequence = null };
    }
}
