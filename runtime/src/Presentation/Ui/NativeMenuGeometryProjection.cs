using System.Text.RegularExpressions;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using Numerics = System.Numerics;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeMenuGeometryCandidate(string Geometry, Vector2? SourceCenter, bool InFront,
    bool InFrame, Vector2? InputPoint, Rect2? Bounds, int SourceTriangles, int ProjectedTriangles, string Disposition)
{
    internal object Observation => new
    {
        Geometry,
        sourceCenter = SourceCenter is { } center ? new[] { center.X, center.Y } : null,
        inputPoint = InputPoint is { } point ? new[] { point.X, point.Y } : null,
        bounds = Bounds is { } bounds ? new[] { bounds.Position.X, bounds.Position.Y, bounds.Size.X, bounds.Size.Y } : null,
        InFront,
        InFrame,
        SourceTriangles,
        ProjectedTriangles,
        Disposition,
        inputCoordinates = "menu-control-local",
        evidence = "clipped-source-triangles-and-selected-target-ray;pixel-coverage-unowned"
    };
}

// Borrows existing source meshes and the actual rendering camera/texture. It
// allocates no native resource and never changes a pose, viewport or menu state.
internal sealed class NativeMenuGeometryProjection : IDisposable
{
    private sealed record SourceSurface(Vector3[] Vertices, int[] Indices, int Surface);
    private sealed record Projected(string Name, MeshInstance3D Mesh, List<FalloutMenuProjectedTriangle> Triangles,
        List<(Numerics.Vector2 A, Numerics.Vector2 B, Numerics.Vector2 C)> Displayed, int SourceTriangles, string? Exclusion = null);
    private sealed record Frame(FalloutMenuGeometryProjection Frustum, FalloutMenuViewportTransform Display, FalloutMenuDisplayClip Clip, bool CameraMirrored);
    private readonly Control _owner;
    private readonly TextureRect _pixels;
    private readonly SubViewport _view;
    private readonly Camera3D _camera;
    private readonly Dictionary<ArrayMesh, SourceSurface[]> _source = [];
    private bool _disposed;

    internal NativeMenuGeometryProjection(Control owner, TextureRect pixels, SubViewport view, Camera3D camera)
    { _owner = owner; _pixels = pixels; _view = view; _camera = camera; }

    internal IReadOnlyList<NativeMenuGeometryCandidate> Survey(IEnumerable<KeyValuePair<string, MeshInstance3D>> selected)
    {
        var frame = Capture();
        var projected = Read(selected, frame);
        var candidates = new List<NativeMenuGeometryCandidate>(projected.Count);
        foreach (var target in projected)
        {
            var worldCenter = target.Mesh.GlobalTransform * target.Mesh.Mesh.GetAabb().GetCenter();
            var inFront = !_camera.IsPositionBehind(worldCenter);
            Vector2? sourceCenter = null;
            if (inFront)
            {
                var point = _camera.UnprojectPosition(worldCenter);
                if (float.IsFinite(point.X) && float.IsFinite(point.Y)) sourceCenter = Owner(frame, point);
            }
            Vector2? input = null; Rect2? bounds = null;
            if (target.Displayed.Count != 0)
            {
                var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                foreach (var triangle in target.Displayed)
                    foreach (var point in new[] { triangle.A, triangle.B, triangle.C })
                    {
                        var ownerPoint = G(point);
                        minimum = new(Math.Min(minimum.X, ownerPoint.X), Math.Min(minimum.Y, ownerPoint.Y));
                        maximum = new(Math.Max(maximum.X, ownerPoint.X), Math.Max(maximum.Y, ownerPoint.Y));
                    }
                bounds = new(minimum, maximum - minimum);
                // A real interior witness also has to resolve to this identity
                // through the same depth/culling picker. No center is clamped.
                foreach (var triangle in target.Displayed.OrderByDescending(value => Math.Abs(FalloutMenuDisplayClip.Area(value.A, value.B, value.C))))
                {
                    var point = new Vector2((float)(((double)triangle.A.X + triangle.B.X + triangle.C.X) / 3),
                        (float)(((double)triangle.A.Y + triangle.B.Y + triangle.C.Y) / 3));
                    if (Pick(point, frame, projected) != target.Name) continue;
                    input = point; break;
                }
            }
            candidates.Add(new(target.Name, sourceCenter, inFront, target.Displayed.Count != 0, input, bounds,
                target.SourceTriangles, target.Displayed.Count,
                target.Exclusion ?? (target.Triangles.Count == 0 ? "outside-frustum-or-culled" : target.Displayed.Count == 0 ? "outside-displayed-canvas-clips" :
                    input is null ? "no-selected-target-ray-witness" : "clipped-triangle-input-witness")));
        }
        return candidates;
    }

    internal static Vector2 MapInput(Control from, Control to, Vector2 point)
    {
        if (!from.IsInsideTree() || !to.IsInsideTree() || from.GetViewport() != to.GetViewport())
            throw new InvalidOperationException("Rendered menu input requires its actual shared canvas publication.");
        var canvas = to.GetGlobalTransformWithCanvas(); var determinant = canvas.Determinant();
        if (!float.IsFinite(determinant) || determinant == 0 || !point.IsFinite())
            throw new InvalidDataException("Rendered menu input transform is singular or non-finite.");
        var mapped = canvas.AffineInverse() * (from.GetGlobalTransformWithCanvas() * point);
        if (!mapped.IsFinite()) throw new InvalidDataException("Rendered menu input transform overflowed.");
        return mapped;
    }

    internal string? Pick(Vector2 position, IEnumerable<KeyValuePair<string, MeshInstance3D>> selected)
    {
        var frame = Capture();
        if (!frame.Clip.Contains(N(position)) || !frame.Display.TryPixels(N(position), out _)) return null;
        return Pick(position, frame, Read(selected, frame));
    }

    private string? Pick(Vector2 position, Frame frame, IReadOnlyList<Projected> selected)
    {
        if (!frame.Clip.Contains(N(position)) || !frame.Display.TryPixels(N(position), out var pixel)) return null;
        var origin = N(_camera.ProjectRayOrigin(G(pixel))); var direction = N(_camera.ProjectRayNormal(G(pixel)));
        var nearest = double.PositiveInfinity; string? identity = null;
        foreach (var target in selected)
            foreach (var triangle in target.Triangles)
                if (FalloutMenuGeometryProjection.RayTriangle(origin, direction, triangle.World, out var distance) && distance < nearest)
                { nearest = distance; identity = target.Name; }
        return identity;
    }

    private List<Projected> Read(IEnumerable<KeyValuePair<string, MeshInstance3D>> selected, Frame frame)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Projected>();
        foreach (var (name, mesh) in selected)
        {
            if (string.IsNullOrWhiteSpace(name) || !identities.Add(name)) throw new InvalidDataException("Menu target identity is empty or ambiguous.");
            if (!GodotObject.IsInstanceValid(mesh) || !mesh.IsInsideTree()) throw new InvalidOperationException("Source menu target lost its native publication.");
            if (mesh.Mesh is null) throw new InvalidDataException("Source menu target has no published mesh geometry.");
            if (!mesh.IsVisibleInTree() || (mesh.Layers & _camera.CullMask) == 0)
            {
                result.Add(new(name, mesh, [], [], 0, !mesh.IsVisibleInTree() ? "source-hidden" : "camera-layer-excluded"));
                continue;
            }
            if (mesh.MaterialOverlay is not null)
                throw new NotSupportedException("Menu target overlay requires its actual geometry/culling publication.");
            if (mesh.Skin is not null || mesh.GetBlendShapeCount() != 0)
                throw new NotSupportedException("Deformed menu target requires its actual current vertex publication.");
            if (mesh.Mesh is not ArrayMesh arrays) throw new NotSupportedException("Menu target has no source triangle-array owner.");
            if (!_source.TryGetValue(arrays, out var surfaces))
            {
                surfaces = Enumerable.Range(0, arrays.GetSurfaceCount()).Select(surface => Read(arrays, surface)).ToArray();
                _source.Add(arrays, surfaces);
            }
            var triangles = new List<FalloutMenuProjectedTriangle>(); var sourceCount = 0;
            var transform = mesh.GlobalTransform;
            var determinant = transform.Basis.Determinant();
            if (!float.IsFinite(determinant) || determinant == 0)
                throw new InvalidDataException("Menu target has a singular or non-finite current transform.");
            foreach (var surface in surfaces)
            {
                var cull = FalloutMenuGeometryProjection.EffectiveCull(Cull(mesh.GetActiveMaterial(surface.Surface)), determinant < 0, frame.CameraMirrored);
                for (var index = 0; index < surface.Indices.Length; index += 3)
                {
                    sourceCount++;
                    var triangle = new FalloutMenuWorldTriangle(N(transform * surface.Vertices[surface.Indices[index]]),
                        N(transform * surface.Vertices[surface.Indices[index + 1]]), N(transform * surface.Vertices[surface.Indices[index + 2]]));
                    triangles.AddRange(frame.Frustum.Project(triangle, cull, world => N(_camera.UnprojectPosition(G(world)))));
                }
            }
            var displayed = new List<(Numerics.Vector2 A, Numerics.Vector2 B, Numerics.Vector2 C)>();
            foreach (var triangle in triangles)
            {
                var polygon = frame.Clip.Clip(frame.Display.ToOwner(triangle.A), frame.Display.ToOwner(triangle.B), frame.Display.ToOwner(triangle.C));
                for (var index = 1; index < polygon.Count - 1; index++)
                    if (FalloutMenuDisplayClip.Area(polygon[0], polygon[index], polygon[index + 1]) != 0)
                        displayed.Add((polygon[0], polygon[index], polygon[index + 1]));
            }
            result.Add(new(name, mesh, triangles, displayed, sourceCount));
        }
        return result;
    }

    private static SourceSurface Read(ArrayMesh mesh, int surface)
    {
        if (mesh.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles)
            throw new NotSupportedException("Menu target primitive has no triangle ownership.");
        using var arrays = mesh.SurfaceGetArrays(surface);
        using var vertexValue = arrays[(int)Mesh.ArrayType.Vertex];
        using var indexValue = arrays[(int)Mesh.ArrayType.Index];
        if (vertexValue.VariantType != Variant.Type.PackedVector3Array || indexValue.VariantType is not (Variant.Type.Nil or Variant.Type.PackedInt32Array))
            throw new InvalidDataException("Menu source triangle array types are unowned.");
        var vertices = vertexValue.AsVector3Array();
        int[] indices = indexValue.VariantType == Variant.Type.Nil ? [] : indexValue.AsInt32Array();
        if (indices.Length == 0) indices = Enumerable.Range(0, vertices.Length).ToArray();
        if (indices.Length % 3 != 0 || indices.Any(index => index < 0 || index >= vertices.Length))
            throw new InvalidDataException("Menu source triangle indices are incomplete or out of range.");
        return new(vertices, indices, surface);
    }

    private Frame Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_owner.IsInsideTree() || !_pixels.IsInsideTree() || !_camera.IsInsideTree() || !_view.IsInsideTree() ||
            _camera.GetViewport() != _view || _view.GetCamera3D() != _camera || _pixels.Texture != _view.GetTexture())
            throw new InvalidOperationException("Menu projection requires its actual camera, viewport and displayed texture publication.");
        if (_pixels.StretchMode != TextureRect.StretchModeEnum.Scale || _pixels.FlipH || _pixels.FlipV)
            throw new NotSupportedException("Menu texture stretch/flip requires its actual sampling transform owner.");
        var size = _view.GetVisibleRect().Size;
        if (size.X <= 0 || size.Y <= 0 || _pixels.Size.X <= 0 || _pixels.Size.Y <= 0)
            throw new InvalidOperationException("Menu viewport or displayed texture extent is empty.");
        var ownerCanvas = _owner.GetGlobalTransformWithCanvas();
        if (ownerCanvas.Determinant() == 0) throw new InvalidDataException("Menu owner has a singular displayed canvas.");
        var ownerInverse = ownerCanvas.AffineInverse();
        var display = ownerInverse * _pixels.GetGlobalTransformWithCanvas();
        var scale = _pixels.Size / size;
        var mapping = new FalloutMenuViewportTransform(N(size), new(
            display.X.X * scale.X, display.X.Y * scale.X, display.Y.X * scale.Y, display.Y.Y * scale.Y,
            display.Origin.X, display.Origin.Y));
        var cameraDeterminant = _camera.GetCameraTransform().Basis.Determinant();
        if (!float.IsFinite(cameraDeterminant) || cameraDeterminant == 0)
            throw new InvalidDataException("Menu camera has a singular or non-finite current transform.");
        var frustum = _camera.GetFrustum();
        // The typed collection is a wrapper; dispose the owned underlying Array
        // after copying its six value planes, without retaining native values.
        using var ownedFrustum = (Godot.Collections.Array)frustum;
        var planes = frustum.Select(plane => new FalloutMenuClipPlane(N(plane.Normal), plane.D)).ToArray();
        var rectangles = new List<IReadOnlyList<Numerics.Vector2>>
        {
            Rectangle(_owner.GetViewport().GetVisibleRect(), ownerInverse)
        };
        for (Node? ancestor = _pixels; ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is CanvasItem canvas && canvas.ClipChildren != CanvasItem.ClipChildrenMode.Disabled)
                throw new NotSupportedException("Menu ancestor uses an unowned non-rectangular canvas clipping mask.");
            if (ancestor is Control { ClipContents: true } control)
                rectangles.Add(Rectangle(new(Vector2.Zero, control.Size), ownerInverse * control.GetGlobalTransformWithCanvas()));
        }
        return new(new(planes), mapping, new(rectangles), cameraDeterminant < 0);
    }

    private static IReadOnlyList<Numerics.Vector2> Rectangle(Rect2 rectangle, Transform2D transform) =>
    [N(transform * rectangle.Position), N(transform * new Vector2(rectangle.End.X, rectangle.Position.Y)),
     N(transform * rectangle.End), N(transform * new Vector2(rectangle.Position.X, rectangle.End.Y))];

    private static FalloutMenuTriangleCull Cull(Material? material)
    {
        if (material is BaseMaterial3D standard)
        {
            if (standard.BillboardMode != BaseMaterial3D.BillboardModeEnum.Disabled || standard.Grow)
                throw new NotSupportedException("Menu target material changes its current rendered vertex geometry.");
            return standard.CullMode switch
            {
                BaseMaterial3D.CullModeEnum.Disabled => FalloutMenuTriangleCull.None,
                BaseMaterial3D.CullModeEnum.Back => FalloutMenuTriangleCull.Back,
                BaseMaterial3D.CullModeEnum.Front => FalloutMenuTriangleCull.Front,
                _ => throw new NotSupportedException("Menu material has no culling owner.")
            };
        }
        if (material is not ShaderMaterial shader || shader.Shader is null || shader.ResourceName is not
            (NativeNifLightingMaterial.ResourceIdentity or NativeNifEffectMaterial.ResourceIdentity))
            throw new NotSupportedException("Menu target material has no source culling declaration owner.");
        if (shader.ResourceName == NativeNifEffectMaterial.ResourceIdentity)
        {
            using var parameter = new StringName("source_billboard_mode");
            using var mode = shader.GetShaderParameter(parameter);
            if (mode.VariantType != Variant.Type.Nil && (mode.VariantType != Variant.Type.Int || mode.AsInt32() >= 0))
                throw new NotSupportedException("Menu target billboard requires its actual camera-dependent rendered transform.");
        }
        var modes = Regex.Matches(shader.Shader.Code, @"\brender_mode\s+([^;]+);");
        if (modes.Count != 1) throw new NotSupportedException("Menu shader render declaration is ambiguous.");
        var culls = modes[0].Groups[1].Value.Split(',').Select(value => value.Trim()).Where(value => value.StartsWith("cull_", StringComparison.Ordinal)).ToArray();
        return culls.Length == 1 ? culls[0] switch
        {
            "cull_disabled" => FalloutMenuTriangleCull.None,
            "cull_back" => FalloutMenuTriangleCull.Back,
            "cull_front" => FalloutMenuTriangleCull.Front,
            _ => throw new NotSupportedException("Menu source shader culling is unowned.")
        } : throw new NotSupportedException("Menu source shader lacks a unique culling declaration.");
    }

    public void Dispose() { if (_disposed) return; _disposed = true; _source.Clear(); }
    private static Vector2 Owner(Frame frame, Vector2 pixels) => G(frame.Display.ToOwner(N(pixels)));
    private static Numerics.Vector2 N(Vector2 value) => new(value.X, value.Y);
    private static Numerics.Vector3 N(Vector3 value) => new(value.X, value.Y, value.Z);
    private static Vector2 G(Numerics.Vector2 value) => new(value.X, value.Y);
    private static Vector3 G(Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
}
