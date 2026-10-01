using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPlayerMove(FalloutFormKey Source, FalloutFormKey Destination, float X, float Y, float Z);

// Player MoveTo queues a request; subsequent source statements execute against
// the current world. The presentation adapter drains requests after execution.
internal sealed class FalloutPlayerMoves
{
    private readonly Queue<FalloutPlayerMove> _pending = [];
    internal string? Error { get; private set; }
    internal bool Pending => _pending.Count != 0;
    internal object State => new { pending = _pending.ToArray(), error = Error };

    internal void Enqueue(FalloutPlayerMove move)
    {
        if (Error is not null) throw new NotSupportedException($"Player movement is faulted: {Error}");
        if (!float.IsFinite(move.X) || !float.IsFinite(move.Y) || !float.IsFinite(move.Z))
            throw new InvalidDataException("Player MoveTo offset is not finite.");
        _pending.Enqueue(move);
    }

    internal FalloutPlayerMove? Next => Error is null && _pending.TryPeek(out var move) ? move : null;

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
        if (!ReferenceEquals(Next, move)) throw new InvalidOperationException("Player MoveTo completion has a different request owner.");
        _pending.Dequeue();
    }

    internal void Fail(FalloutPlayerMove move, Exception error)
    {
        if (!ReferenceEquals(Next, move)) throw new InvalidOperationException("Player MoveTo failure has a different request owner.");
        Error = error.Message;
    }

    internal void RequireSettled()
    {
        if (Pending || Error is not null)
            throw new NotSupportedException("Saving pending or failed player movement requires its continuation state.");
    }

    internal void Clear() { _pending.Clear(); Error = null; }
}
