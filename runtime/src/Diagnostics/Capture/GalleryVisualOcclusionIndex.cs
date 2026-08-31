using Godot;

using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.Diagnostics.Capture;

internal sealed class GalleryVisualOcclusionIndex
{
    private readonly IReadOnlyList<Surface> _surfaces;

    private GalleryVisualOcclusionIndex(IReadOnlyList<Surface> surfaces) =>
        _surfaces = surfaces;

    internal static GalleryVisualOcclusionIndex Build(
        Node sceneRoot,
        Node excludedRoot)
    {
        var surfaces = new List<Surface>();
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(sceneRoot, excludedRoot))
        {
            if (!mesh.IsVisibleInTree() || mesh.Mesh is null)
                continue;
            var faces = mesh.Mesh.GetFaces();
            if (faces.Length == 0)
                continue;
            if (faces.Length % 3 != 0)
                throw new InvalidOperationException(
                    $"Rendered mesh has a non-triangular face stream: {mesh.GetPath()}");
            var transform = mesh.GlobalTransform;
            var worldFaces = faces
                .Select(vertex => transform * vertex)
                .ToArray();
            var minimum = worldFaces[0];
            var maximum = worldFaces[0];
            foreach (var vertex in worldFaces.Skip(1))
            {
                minimum = minimum.Min(vertex);
                maximum = maximum.Max(vertex);
            }
            surfaces.Add(new Surface(
                mesh.GetPath().ToString(),
                new Aabb(minimum, maximum - minimum),
                worldFaces));
        }
        return new GalleryVisualOcclusionIndex(surfaces);
    }

    internal Hit CastSegment(Vector3 from, Vector3 to)
    {
        foreach (var surface in _surfaces)
        {
            if (!surface.Bounds.IntersectsSegment(from, to))
                continue;
            for (var index = 0; index < surface.Faces.Length; index += 3)
            {
                var intersection = Geometry3D.SegmentIntersectsTriangle(
                    from,
                    to,
                    surface.Faces[index],
                    surface.Faces[index + 1],
                    surface.Faces[index + 2]);
                if (intersection.VariantType == Variant.Type.Nil)
                {
                    intersection = Geometry3D.SegmentIntersectsTriangle(
                        to,
                        from,
                        surface.Faces[index],
                        surface.Faces[index + 1],
                        surface.Faces[index + 2]);
                }
                if (intersection.VariantType == Variant.Type.Nil)
                    continue;
                return new Hit(
                    true,
                    intersection.AsVector3(),
                    surface.Path);
            }
        }
        return new Hit(false, Vector3.Zero, "");
    }

    private readonly record struct Surface(
        string Path,
        Aabb Bounds,
        Vector3[] Faces);

    internal readonly record struct Hit(
        bool HitSurface,
        Vector3 Position,
        string SurfacePath);
}
