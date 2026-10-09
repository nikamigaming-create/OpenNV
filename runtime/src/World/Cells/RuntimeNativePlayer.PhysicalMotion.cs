using Godot;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private void MovePhysicalPlayer(Vector3 motion)
    {
        if (!motion.IsFinite()) throw new InvalidDataException("Player physical source motion is not finite.");
        if (MoveAndCollide(motion) is not { } collision) return;
        if (collision.GetNormal().Dot(UpDirection) >= MathF.Cos(FloorMaxAngle))
        {
            var remainder = collision.GetRemainder().Slide(collision.GetNormal());
            collision.Dispose();
            collision = MoveAndCollide(remainder);
            if (collision is null) return;
        }
        try
        {
            throw new NotSupportedException($"Player physical source motion was obstructed at {collision.GetPosition()} " +
                $"normal={collision.GetNormal()}; interruption/avoidance needs its actual motion owner.");
        }
        finally { collision.Dispose(); }
    }
}
