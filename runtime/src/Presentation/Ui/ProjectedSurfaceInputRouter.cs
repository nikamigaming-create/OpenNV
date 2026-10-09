using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed class ProjectedSurfaceInputRouter
{
    private readonly MeshInstance3D _mesh;
    private readonly int _surface;
    private readonly Camera3D _camera;
    private readonly SubViewport _target;
    private bool _pointerInside;
    private Vector2 _lastTargetPosition;

    internal ProjectedSurfaceInputRouter(
        MeshInstance3D mesh,
        int surface,
        Camera3D camera,
        SubViewport target)
    {
        _mesh = mesh;
        _surface = surface;
        _camera = camera;
        _target = target;
    }

    internal void Forward(InputEvent input)
    {
        if (!_mesh.IsVisibleInTree() || !_target.IsInsideTree()) return;
        if (input is InputEventKey or InputEventJoypadButton or InputEventJoypadMotion)
        {
            _target.PushInput(input, true);
            return;
        }
        if (input is not InputEventMouse mouse)
            return;
        if (!TryMap(mouse.Position, out var targetPosition))
        {
            if (_pointerInside && input is InputEventMouseButton { Pressed: false })
                Push(input, _lastTargetPosition);
            if (_pointerInside)
                _target.NotifyMouseExited();
            _pointerInside = false;
            return;
        }
        if (!_pointerInside)
            _target.NotifyMouseEntered();
        Push(input, targetPosition);
        _pointerInside = true;
    }

    private void Push(InputEvent input, Vector2 targetPosition)
    {
        using var forwarded = (InputEventMouse)input.Duplicate();
        forwarded.Position = targetPosition;
        forwarded.GlobalPosition = targetPosition;
        if (forwarded is InputEventMouseMotion motion)
            motion.Relative = _pointerInside ? targetPosition - _lastTargetPosition : Vector2.Zero;
        _target.PushInput(forwarded, true);
        _lastTargetPosition = targetPosition;
    }

    private bool TryMap(Vector2 hostPosition, out Vector2 targetPosition)
    {
        targetPosition = default;
        var mesh = _mesh.Mesh
            ?? throw new InvalidOperationException(
                "Projected input surface has no mesh.");
        var arrays = mesh.SurfaceGetArrays(_surface);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var textureCoordinates = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        if (vertices.Length != textureCoordinates.Length ||
            indices.Length < 3 || indices.Length % 3 != 0 ||
            indices.Any(value => value < 0 || value >= vertices.Length))
            throw new InvalidOperationException(
                "Projected input surface topology is incomplete.");
        var inverse = _mesh.GlobalTransform.AffineInverse();
        var origin = inverse * _camera.ProjectRayOrigin(hostPosition);
        var direction = inverse.Basis * _camera.ProjectRayNormal(hostPosition);
        var nearest = float.PositiveInfinity;
        Vector2? picked = null;
        for (var offset = 0; offset < indices.Length; offset += 3)
        {
            var first = indices[offset];
            var second = indices[offset + 1];
            var third = indices[offset + 2];
            if (!TryRayTriangle(origin, direction, vertices[first], vertices[second], vertices[third],
                out var distance, out var weights) || distance >= nearest)
                continue;
            var uv = textureCoordinates[first] * weights.X +
                textureCoordinates[second] * weights.Y +
                textureCoordinates[third] * weights.Z;
            var point = uv * new Vector2(_target.Size.X, _target.Size.Y);
            if (!point.IsFinite() || point.X < 0 || point.Y < 0 || point.X > _target.Size.X || point.Y > _target.Size.Y)
                continue;
            nearest = distance;
            picked = point;
        }
        if (picked is not { } result) return false;
        targetPosition = result;
        return true;
    }

    private static bool TryRayTriangle(Vector3 origin, Vector3 direction,
        Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector3 weights)
    {
        distance = default;
        weights = default;
        var firstEdge = b - a;
        var secondEdge = c - a;
        var cross = direction.Cross(secondEdge);
        var determinant = firstEdge.Dot(cross);
        if (!float.IsFinite(determinant) || determinant == 0) return false;
        var offset = origin - a;
        var second = offset.Dot(cross) / determinant;
        var otherCross = offset.Cross(firstEdge);
        var third = direction.Dot(otherCross) / determinant;
        distance = secondEdge.Dot(otherCross) / determinant;
        var first = 1.0f - second - third;
        weights = new Vector3(first, second, third);
        return float.IsFinite(distance) && distance >= 0 && weights.IsFinite() &&
            first >= 0 && second >= 0 && third >= 0;
    }
}
