using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

// Share the source bound's depth across its alpha descendants. Metadata is
// copied with the source prototype; each instance resolves its own geometry.
internal sealed partial class NativeNifAlphaSortBound : Node
{
    private Node3D _owner = null!;
    private GeometryInstance3D[] _geometry = [];
    public override void _Ready()
    {
        _owner = (Node3D)GetParent();
        _geometry = _owner.FindChildren("*", "", true, false).OfType<GeometryInstance3D>().ToArray();
        foreach (var geometry in _geometry) geometry.SortingUseAabbCenter = false;
    }

    public override void _Process(double delta)
    {
        if (GetViewport().GetCamera3D() is not { } camera) return;
        var center = _owner.ToGlobal(GetMeta("center").AsVector3());
        if (!GetMeta("static").AsBool() && _geometry.Length != 0)
        {
            var bounds = _geometry[0].GlobalTransform * _geometry[0].GetAabb();
            foreach (var geometry in _geometry.Skip(1)) bounds = bounds.Merge(geometry.GlobalTransform * geometry.GetAabb());
            center = bounds.GetCenter();
        }
        var forward = -camera.GlobalBasis.Z.Normalized();
        foreach (var geometry in _geometry)
            geometry.SortingOffset = (geometry.GlobalPosition - center).Dot(forward);
    }
}
