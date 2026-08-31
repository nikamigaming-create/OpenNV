using Godot;

using OpenNV.Runtime.SceneGraph;


namespace OpenNV.Runtime.World.Cells;

internal static class CellReferenceLedger
{
    private const float FrustumEpsilon = 0.0001f;
    private const float FiniteEpsilon = 0.000001f;

    internal static Result Measure(
        CellContentLoader.LoadedContent content,
        Camera3D camera)
    {
        var placedByFormId = content.PlacedReferences.ToDictionary(
            reference => reference.FormId,
            StringComparer.OrdinalIgnoreCase);
        var rows = new List<Row>();
        foreach (var source in content.SourceReferences)
        {
            if (source.InitiallyDisabled)
            {
                rows.Add(Row.Disabled(source));
                continue;
            }

            var sourceOrigin = content.Root.ToGlobal(source.PositionGodotUnits);
            var sourceOriginInFrustum = camera.IsPositionInFrustum(sourceOrigin);
            if (!placedByFormId.TryGetValue(source.FormId, out var placed))
            {
                rows.Add(Row.Missing(source, sourceOriginInFrustum));
                continue;
            }

            var geometry = MeasureGeometry(placed.Visual, camera, sourceOrigin);
            var failures = new List<string>();
            if (sourceOriginInFrustum && !geometry.AabbValid)
                failures.Add("invalid-aabb");
            if (sourceOriginInFrustum && geometry.Triangles <= 0)
                failures.Add("zero-triangles");
            if (sourceOriginInFrustum &&
                !geometry.FrustumIntersection &&
                geometry.ProjectedScreenBounds is { } projectedBounds &&
                ProjectedBoundsIntersectsViewport(projectedBounds, camera))
                failures.Add("unexplained-cull");
            if (sourceOriginInFrustum && !geometry.RenderLayerVisible)
                failures.Add("unexplained-cull");
            rows.Add(new Row(
                source.FormId,
                source.BaseFormId,
                source.BaseEditorId,
                source.AssetId,
                source.SourceCellFormId,
                sourceOriginInFrustum,
                true,
                geometry.RenderLayerVisible,
                geometry.AabbValid,
                geometry.FrustumIntersection,
                geometry.Surfaces,
                geometry.Vertices,
                geometry.Triangles,
                geometry.GlobalAabb,
                geometry.CameraSpaceDepth,
                geometry.ProjectedScreenBounds,
                failures.Distinct(StringComparer.Ordinal).ToArray()));
        }

        var sourceIds = content.SourceReferences
            .Select(reference => reference.FormId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var placed in content.PlacedReferences)
        {
            if (sourceIds.Contains(placed.FormId))
                continue;
            rows.Add(Row.Unexpected(placed));
        }

        var expectedRows = rows.Where(row => row.ExpectedInView).ToArray();
        var failuresByCode = rows
            .SelectMany(row => row.FailureCodes)
            .GroupBy(code => code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return new Result(
            expectedRows.Length,
            rows.Count(row => row.Instantiated),
            rows.Count,
            failuresByCode.Values.Sum(),
            failuresByCode,
            failuresByCode.Count == 0,
            rows);
    }

    internal static object Document(Result result) => new
    {
        status = result.Passed ? "pass" : "fail",
        passed = result.Passed,
        denominator = result.Denominator,
        instantiated = result.Instantiated,
        rows = result.RowCount,
        failures = result.FailureCount,
        failuresByCode = result.FailuresByCode,
        references = result.Rows.Select(row => new
        {
            formId = row.FormId,
            baseFormId = row.BaseFormId,
            baseEditorId = row.BaseEditorId,
            assetId = row.AssetId,
            sourceCellFormId = row.SourceCellFormId,
            expectedInView = row.ExpectedInView,
            instantiated = row.Instantiated,
            renderLayerVisible = row.RenderLayerVisible,
            aabbValid = row.AabbValid,
            frustumIntersection = row.FrustumIntersection,
            surfaces = row.Surfaces,
            vertices = row.Vertices,
            triangles = row.Triangles,
            globalAabb = row.GlobalAabb is null
                ? null
                : new
                {
                    position = new[]
                    {
                        row.GlobalAabb.Value.Position.X,
                        row.GlobalAabb.Value.Position.Y,
                        row.GlobalAabb.Value.Position.Z,
                    },
                    size = new[]
                    {
                        row.GlobalAabb.Value.Size.X,
                        row.GlobalAabb.Value.Size.Y,
                        row.GlobalAabb.Value.Size.Z,
                    },
                },
            cameraSpaceDepth = row.CameraSpaceDepth is null
                ? null
                : new[]
                {
                    row.CameraSpaceDepth.Value.X,
                    row.CameraSpaceDepth.Value.Y,
                },
            projectedScreenBounds = row.ProjectedScreenBounds is null
                ? null
                : new[]
                {
                    row.ProjectedScreenBounds.Value.X,
                    row.ProjectedScreenBounds.Value.Y,
                    row.ProjectedScreenBounds.Value.Z,
                    row.ProjectedScreenBounds.Value.W,
                },
            failureCodes = row.FailureCodes,
        }),
    };

    internal static Geometry MeasureGeometry(
        Node3D visual,
        Camera3D camera,
        Vector3 referenceOrigin)
    {
        var minimum = new Vector3(
            float.PositiveInfinity,
            float.PositiveInfinity,
            float.PositiveInfinity);
        var maximum = new Vector3(
            float.NegativeInfinity,
            float.NegativeInfinity,
            float.NegativeInfinity);
        var surfaces = 0;
        var vertices = 0;
        var triangles = 0;
        var renderLayerVisible = false;
        var points = new List<Vector3>();
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(visual))
        {
            if (mesh.Mesh is null)
                continue;
            surfaces += mesh.Mesh.GetSurfaceCount();
            if (mesh.Mesh is ArrayMesh arrayMesh)
            {
                vertices += Enumerable.Range(0, arrayMesh.GetSurfaceCount())
                    .Sum(arrayMesh.SurfaceGetArrayLen);
                triangles += Enumerable.Range(0, arrayMesh.GetSurfaceCount())
                    .Sum(surface =>
                    {
                        var indexCount = arrayMesh.SurfaceGetArrayIndexLen(surface);
                        return (indexCount > 0 ? indexCount : arrayMesh.SurfaceGetArrayLen(surface)) / 3;
                    });
            }
            renderLayerVisible |= mesh.Visible && (mesh.Layers & camera.CullMask) != 0;
            var bounds = mesh.GetAabb();
            foreach (var x in new[] { bounds.Position.X, bounds.End.X })
                foreach (var y in new[] { bounds.Position.Y, bounds.End.Y })
                    foreach (var z in new[] { bounds.Position.Z, bounds.End.Z })
                    {
                        var point = mesh.ToGlobal(new Vector3(x, y, z));
                        points.Add(point);
                        minimum = minimum.Min(point);
                        maximum = maximum.Max(point);
                    }
        }

        if (points.Count == 0 ||
            !float.IsFinite(minimum.X) || !float.IsFinite(minimum.Y) ||
            !float.IsFinite(minimum.Z) || !float.IsFinite(maximum.X) ||
            !float.IsFinite(maximum.Y) || !float.IsFinite(maximum.Z))
        {
            return new Geometry(
                surfaces,
                vertices,
                triangles,
                renderLayerVisible,
                false,
                false,
                null,
                null,
                null);
        }

        var aabb = new Aabb(minimum, maximum - minimum);
        var frustumPlanes = camera.GetFrustum();
        var frustumIntersection = frustumPlanes.Select(plane =>
        {
            var referenceDistance = plane.DistanceTo(referenceOrigin);
            var positiveInside = referenceDistance >= 0.0f;
            var support = new Vector3(
                positiveInside
                    ? (plane.Normal.X >= 0.0f ? aabb.End.X : aabb.Position.X)
                    : (plane.Normal.X >= 0.0f ? aabb.Position.X : aabb.End.X),
                positiveInside
                    ? (plane.Normal.Y >= 0.0f ? aabb.End.Y : aabb.Position.Y)
                    : (plane.Normal.Y >= 0.0f ? aabb.Position.Y : aabb.End.Y),
                positiveInside
                    ? (plane.Normal.Z >= 0.0f ? aabb.End.Z : aabb.Position.Z)
                    : (plane.Normal.Z >= 0.0f ? aabb.Position.Z : aabb.End.Z));
            var supportDistance = plane.DistanceTo(support);
            return positiveInside
                ? supportDistance >= -FrustumEpsilon
                : supportDistance <= FrustumEpsilon;
        }).All(intersects => intersects);
        var cameraTransform = camera.GlobalTransform.AffineInverse();
        var depths = points.Select(point => -(cameraTransform * point).Z).ToArray();
        var projected = points.Select(camera.UnprojectPosition).ToArray();
        var projectedMinimum = new Vector2(
            projected.Min(point => point.X),
            projected.Min(point => point.Y));
        var projectedMaximum = new Vector2(
            projected.Max(point => point.X),
            projected.Max(point => point.Y));
        var projectedBounds = new Vector4(
            projectedMinimum.X,
            projectedMinimum.Y,
            projectedMaximum.X - projectedMinimum.X,
            projectedMaximum.Y - projectedMinimum.Y);
        var depth = new Vector2(depths.Min(), depths.Max());
        var valid = aabb.Size.X >= -FiniteEpsilon &&
            aabb.Size.Y >= -FiniteEpsilon &&
            aabb.Size.Z >= -FiniteEpsilon &&
            depths.All(float.IsFinite) &&
            projected.All(point => float.IsFinite(point.X) && float.IsFinite(point.Y));
        return new Geometry(
            surfaces,
            vertices,
            triangles,
            renderLayerVisible,
            valid,
            frustumIntersection,
            aabb,
            depth,
            projectedBounds);
    }

    internal static bool ProjectedBoundsIntersectsViewport(
        Vector4 bounds,
        Camera3D camera)
    {
        var viewport = camera.GetViewport().GetVisibleRect();
        var maximum = new Vector2(bounds.X + bounds.Z, bounds.Y + bounds.W);
        return bounds.X <= viewport.End.X &&
            maximum.X >= viewport.Position.X &&
            bounds.Y <= viewport.End.Y &&
            maximum.Y >= viewport.Position.Y;
    }

    internal sealed record Result(
        int Denominator,
        int Instantiated,
        int RowCount,
        int FailureCount,
        IReadOnlyDictionary<string, int> FailuresByCode,
        bool Passed,
        IReadOnlyList<Row> Rows);

    internal sealed record Row(
        string FormId,
        string BaseFormId,
        string BaseEditorId,
        string AssetId,
        string SourceCellFormId,
        bool ExpectedInView,
        bool Instantiated,
        bool RenderLayerVisible,
        bool AabbValid,
        bool FrustumIntersection,
        int? Surfaces,
        int? Vertices,
        int? Triangles,
        Aabb? GlobalAabb,
        Vector2? CameraSpaceDepth,
        Vector4? ProjectedScreenBounds,
        IReadOnlyList<string> FailureCodes)
    {
        internal static Row Disabled(CellContentLoader.SourceReference source) => new(
            source.FormId,
            source.BaseFormId,
            source.BaseEditorId,
            source.AssetId,
            source.SourceCellFormId,
            false,
            false,
            false,
            true,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<string>());

        internal static Row Missing(
            CellContentLoader.SourceReference source,
            bool expectedInView) => new(
            source.FormId,
            source.BaseFormId,
            source.BaseEditorId,
            source.AssetId,
            source.SourceCellFormId,
            expectedInView,
            false,
            false,
            false,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            expectedInView ? new[] { "missing-instantiated-geometry" } : Array.Empty<string>());

        internal static Row Unexpected(CellContentLoader.PlacedReference placed) => new(
            placed.FormId,
            placed.BaseFormId,
            placed.BaseEditorId,
            placed.AssetId,
            placed.SourceCellFormId,
            false,
            true,
            false,
            true,
            false,
            placed.Geometry.Surfaces,
            placed.Geometry.Vertices,
            placed.Geometry.Triangles,
            null,
            null,
            null,
            new[] { "unaccounted-instantiated-reference" });
    }

    internal sealed record Geometry(
        int Surfaces,
        int Vertices,
        int Triangles,
        bool RenderLayerVisible,
        bool AabbValid,
        bool FrustumIntersection,
        Aabb? GlobalAabb,
        Vector2? CameraSpaceDepth,
        Vector4? ProjectedScreenBounds);
}
