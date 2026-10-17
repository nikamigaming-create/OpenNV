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
        _pointerInside = true;
        _lastTargetPosition = targetPosition;
        Push(input, targetPosition);
    }

    private void Push(InputEvent input, Vector2 targetPosition)
    {
        var forwarded = (InputEventMouse)input.Duplicate();
        forwarded.Position = targetPosition;
        forwarded.GlobalPosition = targetPosition;
        _target.PushInput(forwarded, true);
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
        var transform = _mesh.GlobalTransform;
        for (var offset = 0; offset < indices.Length; offset += 3)
        {
            var first = indices[offset];
            var second = indices[offset + 1];
            var third = indices[offset + 2];
            var a = _camera.UnprojectPosition(transform * vertices[first]);
            var b = _camera.UnprojectPosition(transform * vertices[second]);
            var c = _camera.UnprojectPosition(transform * vertices[third]);
            if (!TryBarycentric(hostPosition, a, b, c, out var weights))
                continue;
            var uv = textureCoordinates[first] * weights.X +
                textureCoordinates[second] * weights.Y +
                textureCoordinates[third] * weights.Z;
            targetPosition = uv * new Vector2(_target.Size.X, _target.Size.Y);
            return targetPosition.IsFinite() &&
                targetPosition.X >= 0.0f && targetPosition.Y >= 0.0f &&
                targetPosition.X <= _target.Size.X &&
                targetPosition.Y <= _target.Size.Y;
        }
        return false;
    }

    private static bool TryBarycentric(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        out Vector3 weights)
    {
        weights = default;
        var v0 = b - a;
        var v1 = c - a;
        var v2 = point - a;
        var denominator = v0.X * v1.Y - v1.X * v0.Y;
        if (!float.IsFinite(denominator) || Mathf.IsZeroApprox(denominator))
            return false;
        var second = (v2.X * v1.Y - v1.X * v2.Y) / denominator;
        var third = (v0.X * v2.Y - v2.X * v0.Y) / denominator;
        var first = 1.0f - second - third;
        weights = new Vector3(first, second, third);
        return first >= 0.0f && second >= 0.0f && third >= 0.0f;
    }
}
