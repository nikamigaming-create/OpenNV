using Godot;

namespace OpenNV.Runtime.Presentation.OpenXR;

internal sealed record NativeXrTarget(Node3D Root, string Name, bool RemotePickup);

/// <summary>Controller ray and source-object highlight; owns no inventory state.</summary>
internal sealed partial class NativeXrPointer : Node3D
{
    private readonly MeshInstance3D _beam, _dot;
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
            Mesh = new CylinderMesh { TopRadius = .001f, BottomRadius = .001f, Height = 1, RadialSegments = 6 },
            MaterialOverride = _material,
            Rotation = new(Mathf.Pi / 2, 0, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        _dot = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = .006f, Height = .012f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(_beam); AddChild(_dot); Visible = false;
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
        _dot.Position = new(0, 0, -distance); _dot.Visible = target is not null;
        _material.AlbedoColor = target is null ? Colors.White : new(1, .65f, .1f);
    }
    private void Clear()
    {
        foreach (var (mesh, overlay) in _previous) if (IsInstanceValid(mesh)) mesh.MaterialOverlay = overlay;
        _previous.Clear(); _target = null;
    }
    public override void _ExitTree() => Clear();
}
