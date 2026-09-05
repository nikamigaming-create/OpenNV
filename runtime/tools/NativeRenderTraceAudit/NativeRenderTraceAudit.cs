using Godot;
using OpenNV.Runtime.Diagnostics.Parity;

public partial class NativeRenderTraceAudit : Node
{
    public override void _Ready()
    {
        try
        {
            var viewport = new SubViewport { Size = new Vector2I(1280, 720) };
            AddChild(viewport);
            var camera = new Camera3D { Near = 0.1f, Fov = 60, Current = true };
            viewport.AddChild(camera);
            var surrounding = new Aabb(new Vector3(-2, -2, -2), Vector3.One * 4);
            var projected = RuntimeRenderTrace.ProjectedBounds(camera, surrounding, Transform3D.Identity)
                ?? throw new InvalidOperationException("Camera-surrounding room geometry vanished from click candidates.");
            foreach (var pixel in new[] { new Vector2(1, 1), new Vector2(1279, 1), new Vector2(1, 719), new Vector2(1279, 719) })
                if (!projected.HasPoint(pixel)) throw new InvalidOperationException("Clipped room bounds omit a visible viewport corner.");
            foreach (var behind in new[]
            {
                new Aabb(new Vector3(-1, -1, 1), Vector3.One),
                new Aabb(new Vector3(-1, -1, -0.09f), new Vector3(2, 2, 0.08f))
            })
                if (RuntimeRenderTrace.ProjectedBounds(camera, behind, Transform3D.Identity) is not null)
                    throw new InvalidOperationException("Geometry fully before the near plane was projected.");
            var placement = new Transform3D(new Basis(Vector3.Up, 0.63f), new Vector3(0.2f, -0.3f, -4));
            var box = new Aabb(-Vector3.One, Vector3.One * 2);
            foreach (var projection in new[] { Camera3D.ProjectionType.Perspective, Camera3D.ProjectionType.Orthogonal })
            {
                camera.Projection = projection;
                var visible = RuntimeRenderTrace.ProjectedBounds(camera, box, placement)
                    ?? throw new InvalidOperationException("Visible placed geometry has no projected bounds.");
                var point = camera.UnprojectPosition(placement * Vector3.Zero);
                if (!visible.HasPoint(point) || !visible.Position.IsFinite() || !visible.Size.IsFinite())
                    throw new InvalidOperationException("Placed geometry coverage is invalid.");
            }
            viewport.Free();
            GD.Print("OPENNV_NATIVE_RENDER_TRACE_AUDIT_PASS nearPlane=clipped coverage=bounding-box-candidates exactPixels=unverified");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
}
