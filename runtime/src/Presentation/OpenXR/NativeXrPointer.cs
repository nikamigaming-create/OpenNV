using Godot;

namespace OpenNV.Runtime.Presentation.OpenXR;

internal sealed record NativeXrTarget(Node3D Root, string Name, bool RemotePickup);

/// <summary>Controller ray and source-object highlight; owns no inventory state.</summary>
internal sealed partial class NativeXrPointer : Node3D
{
    private readonly MeshInstance3D _beam;
    private readonly StandardMaterial3D _material;
    private readonly StandardMaterial3D _highlight;
    private readonly List<(GeometryInstance3D Mesh, Material? Overlay)> _previous = [];
    private Node3D? _target;
    internal string? TargetName { get; private set; }
    internal NativeXrPointer()
    {
        Name = "ControllerWorldPointer";
        _material = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Colors.White };
        _highlight = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new(1, .65f, .1f, .2f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };
        _beam = new MeshInstance3D
        {
            Mesh = CreateOpenBeamMesh(),
            MaterialOverride = _material,
            Rotation = new(Mathf.Pi / 2, 0, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(_beam); Visible = false;
    }
    internal void Publish(bool active, Transform3D aim, float distance, NativeXrTarget? target)
    {
        Visible = active;
        if (!active) target = null;
        if (_target != target?.Root)
        {
            Clear(); _target = target?.Root;
            if (_target is not null)
                foreach (var mesh in _target.FindChildren("*", "", true, false).OfType<MeshInstance3D>())
                { _previous.Add((mesh, mesh.MaterialOverlay)); mesh.MaterialOverlay = _highlight; }
        }
        TargetName = target?.Name;
        if (!active) return;
        GlobalTransform = aim; _beam.Position = new(0, 0, -distance / 2); _beam.Scale = new(1, distance, 1);
        _material.AlbedoColor = target is null ? Colors.White : new(1, .65f, .1f);
    }

    internal void PublishSurfaceRay(bool active, Transform3D aim, Vector3? hitPoint, float maximumDistance)
    {
        if (!active)
        {
            Publish(false, aim, 0, null);
            return;
        }
        if (_target is not null) Clear();
        var forward = -aim.Basis.Z.Normalized();
        var hitDistance = hitPoint is { } point ? (point - aim.Origin).Dot(forward) : 0;
        var hit = hitPoint is not null && hitDistance >= .04f && hitDistance <= maximumDistance;
        var distance = hit ? hitDistance : maximumDistance;
        TargetName = hit ? "Pip-Boy" : null;
        Visible = true;
        GlobalTransform = aim;
        _beam.Position = new(0, 0, -distance / 2);
        _beam.Scale = new(1, distance, 1);
        _material.AlbedoColor = new(0.15f, 1f, 0.25f);
    }
    private void Clear()
    {
        foreach (var (mesh, overlay) in _previous) if (IsInstanceValid(mesh)) mesh.MaterialOverlay = overlay;
        _previous.Clear(); _target = null;
    }

    private static ArrayMesh CreateOpenBeamMesh()
    {
        const int radialSegments = 8;
        const float radius = .001f;
        var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        for (var index = 0; index < radialSegments; index++)
        {
            var angle0 = 2 * Mathf.Pi * index / radialSegments;
            var angle1 = 2 * Mathf.Pi * (index + 1) / radialSegments;
            var normal0 = new Vector3(Mathf.Cos(angle0), 0, Mathf.Sin(angle0));
            var normal1 = new Vector3(Mathf.Cos(angle1), 0, Mathf.Sin(angle1));
            var normal = (normal0 + normal1).Normalized();
            var bottom0 = new Vector3(normal0.X * radius, -.5f, normal0.Z * radius);
            var bottom1 = new Vector3(normal1.X * radius, -.5f, normal1.Z * radius);
            var top0 = new Vector3(normal0.X * radius, .5f, normal0.Z * radius);
            var top1 = new Vector3(normal1.X * radius, .5f, normal1.Z * radius);

            Add(normal, bottom0);
            Add(normal, top0);
            Add(normal, top1);
            Add(normal, bottom0);
            Add(normal, top1);
            Add(normal, bottom1);
        }
        return mesh.Commit();

        void Add(Vector3 normal, Vector3 vertex)
        {
            mesh.SetNormal(normal);
            mesh.AddVertex(vertex);
        }
    }

    public override void _ExitTree() => Clear();
}
