using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedLoveTesterMenu
{
    private NativeMenuGeometryProjection? _targetProjection;
    private NativeMenuGeometryProjection TargetProjection =>
        _targetProjection ??= new(this, _pixels, _view, _camera);
    private IEnumerable<KeyValuePair<string, MeshInstance3D>> ActiveGeometry =>
        _geometry.Where(item => IsActiveTarget(item.Key, item.Value));

    internal IReadOnlyList<NativeMenuGeometryCandidate> ProjectionCandidates => TargetProjection.Survey(ActiveGeometry);
    private IReadOnlyList<NativeLoveTesterTarget> ProjectedTargets() => _accepted || _turning ? [] : ProjectionCandidates
        .Where(target => target.InputPoint is not null && target.Bounds is not null)
        .Select(target => new NativeLoveTesterTarget(target.Geometry, target.InputPoint!.Value, target.Bounds!.Value, true)).ToArray();

    private string? PickProjected(Vector2 point)
    {
        var result = TargetProjection.Pick(point, ActiveGeometry);
        SetMeta("opennv_love_tester_pointer_geometry", result ?? string.Empty);
        return result;
    }

    public override void _ExitTree()
    {
        _targetProjection?.Dispose(); _targetProjection = null;
    }
}
