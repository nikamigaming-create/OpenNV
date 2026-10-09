using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedNifMenuSurface
{
    private NativeMenuGeometryProjection? _targetProjection;
    private NativeMenuGeometryProjection TargetProjection =>
        _targetProjection ??= new(this, _pixels, View, Camera);

    internal IReadOnlyList<NativeMenuGeometryCandidate> ProjectionCandidates(Func<string, MeshInstance3D, bool> active) =>
        TargetProjection.Survey(_geometry.Where(item => active(item.Key, item.Value)));

    private IReadOnlyList<NativeOwnedNifMenuTarget> ProjectedTargets(Func<string, MeshInstance3D, bool> active) => ProjectionCandidates(active)
        .Where(target => target.InputPoint is not null && target.Bounds is not null)
        .Select(target => new NativeOwnedNifMenuTarget(target.Geometry, target.InputPoint!.Value, target.Bounds!.Value, true)).ToArray();

    internal string? PickFrom(Control inputOwner, Vector2 position, Func<string, MeshInstance3D, bool> active) =>
        PickProjected(NativeMenuGeometryProjection.MapInput(inputOwner, this, position), active);

    private string? PickProjected(Vector2 position, Func<string, MeshInstance3D, bool> active)
    {
        var picked = TargetProjection.Pick(position, _geometry.Where(item => active(item.Key, item.Value)));
        LastPicked = picked; return picked;
    }

    public override void _ExitTree()
    {
        _targetProjection?.Dispose(); _targetProjection = null;
    }
}
