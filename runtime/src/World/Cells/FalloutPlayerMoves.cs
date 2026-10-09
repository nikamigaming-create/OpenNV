using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPlayerMove(FalloutFormKey Source, FalloutFormKey Destination, float X, float Y, float Z);

// Player movement owns one retained source request. Later statements still
// observe the current world until its real source/native consumer commits.
// Storing a request does not certify the independent immediate-mode child.
internal sealed partial class FalloutPlayerMoves
{
    internal string? Error => SourcePending.Error;
    internal bool Pending => SourcePending.Pending;
    internal object State => new
    {
        sourceSlot = SourcePending.State,
        originalImmediateMode = "unowned-independent-TES-byte-pair; registration-is-not-immediate-consumer-return"
    };

    internal void Enqueue(FalloutPlayerMove move)
    {
        if (Error is not null) throw new NotSupportedException($"Player movement is faulted: {Error}");
        if (!float.IsFinite(move.X) || !float.IsFinite(move.Y) || !float.IsFinite(move.Z))
            throw new InvalidDataException("Player MoveTo offset is not finite.");
        _ = SourcePending.Store(FalloutPlayerPendingKind.MoveTo, move, null, "actual-source-Player-MoveTo-command");
    }

    internal FalloutPlayerMove? Next => SourcePending.Next is { Kind: FalloutPlayerPendingKind.MoveTo } request ? request.Move : null;

    internal static FalloutReferencePlacement Resolve(FalloutPlayerMove move, FalloutReferencePlacement destination)
    {
        destination.Validate();
        var placement = new FalloutReferencePlacement(destination.Cell,
            [destination.Position[0] + move.X, destination.Position[1] + move.Y, destination.Position[2] + move.Z],
            (float[])destination.RotationRadians.Clone());
        placement.Validate();
        return placement;
    }

    internal void Complete(FalloutPlayerMove move)
    {
        SourcePending.Complete(RequireMove(move), "actual-source-Player-placement-consumers-returned");
    }

    internal void Fail(FalloutPlayerMove move, Exception error)
    {
        SourcePending.Fail(RequireMove(move), error);
    }

    internal void RequireSettled()
    {
        if (Pending || Error is not null)
            throw new NotSupportedException("Saving pending or failed player movement requires its continuation state.");
    }

    internal void Clear() => RetireSourcePending();
}
