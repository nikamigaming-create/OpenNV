using Godot;
using OpenNV.Runtime.Presentation.OpenXR;

public partial class NativeXrPlayerAudit
{
    private static void CheckCoordinateContracts()
    {
        foreach (var left in new[] { false, true })
        {
            var sign = left ? 1 : -1;
            var grip = NativeXrArm.GripFrame(Vector3.Zero, new(0, 0, -.1f),
                new(sign * .03f, 0, -.08f), new(-sign * .04f, 0, -.08f), left);
            // These hands lie palm-down, with fingertips along -Z. Left +X
            // therefore points down out of the palm; right +X points up into it.
            if (!grip.Basis.Y.IsEqualApprox(Vector3.Back) ||
                !(-grip.Basis.Z).IsEqualApprox(Vector3.Right * sign) ||
                !grip.Basis.X.IsEqualApprox(Vector3.Down * sign) ||
                MathF.Abs(grip.Basis.Determinant() - 1) > 1e-5f)
                throw new InvalidOperationException("OpenXR grip axes do not match the palm and curled-knuckle contract.");
        }
        var frame = NativeXrWristPresentation.ScreenFrame(
            [new(-.1f, 0, -.075f), new(.1f, 0, -.075f), new(-.1f, 0, .075f), new(.1f, 0, .075f)],
            [Vector2.Zero, Vector2.Right, Vector2.Down, Vector2.One]);
        if (!frame.Basis.X.IsEqualApprox(Vector3.Right) || !frame.Basis.Y.IsEqualApprox(Vector3.Forward) ||
            !frame.Basis.Z.IsEqualApprox(Vector3.Up))
            throw new InvalidOperationException("Wrist screen does not preserve source texture right/up/front.");
        // Eye translations affect finite points but must not move sky directions.
        var leftProjection = Projection.CreatePerspective(90, 1, .03f, 1000, false);
        var rightProjection = leftProjection;
        leftProjection.W += new Vector4(.032f, 0, 0, 0);
        rightProjection.W -= new Vector4(.032f, 0, 0, 0);
        var direction = new Vector4(.2f, .1f, -1, 0);
        if (!(leftProjection * direction).IsEqualApprox(rightProjection * direction) ||
            (leftProjection * new Vector4(.2f, .1f, -1, 1)).IsEqualApprox(rightProjection * new Vector4(.2f, .1f, -1, 1)))
            throw new InvalidOperationException("Sky direction retained finite eye-translation parallax.");
        GD.Print("OPENNV_XR_COORDINATE_CONTRACT_PASS grip=knuckle-axis screen=source-uv sky=infinite-direction");
    }
}
