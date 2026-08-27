using Godot;

namespace OpenNV.Runtime;

internal static class CellLodLedger
{
    internal static Result Measure(
        CellContentLoader.LoadedContent content,
        Camera3D camera)
    {
        if (content.LodCoverage is null)
            return new Result(
                !content.Interior,
                0,
                0,
                0,
                content.Interior ? 0 : 1,
                content.Interior
                    ? new Dictionary<string, int>(StringComparer.Ordinal)
                    : new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["exterior-has-no-lod-coverage-contract"] = 1,
                    },
                Array.Empty<ArchiveLatticeGap>(),
                content.Interior,
                null,
                Array.Empty<Row>());

        var contract = content.LodCoverage.Value;
        var rows = content.LodBlocks.Select(block =>
        {
            var geometry = CellReferenceLedger.MeasureGeometry(
                block.Visual,
                camera,
                block.Placement.GlobalPosition);
            var projectedInViewport = geometry.ProjectedScreenBounds is { } bounds &&
                CellReferenceLedger.ProjectedBoundsIntersectsViewport(bounds, camera);
            var expectedInView = geometry.FrustumIntersection;
            var failures = new List<string>();
            if (!geometry.AabbValid)
                failures.Add("invalid-lod-aabb");
            if (geometry.Triangles <= 0)
                failures.Add("zero-lod-triangles");
            if (expectedInView && !geometry.RenderLayerVisible)
                failures.Add("hidden-lod-render-layer");
            return new Row(
                block.Id,
                block.AssetId,
                block.LogicalPath,
                block.SourceSha256,
                block.Family,
                block.Level,
                block.Variant,
                block.SelectionReason,
                block.BlockOriginGameUnits,
                expectedInView,
                projectedInViewport,
                geometry.RenderLayerVisible,
                geometry.AabbValid,
                geometry.FrustumIntersection,
                geometry.Surfaces,
                geometry.Vertices,
                geometry.Triangles,
                geometry.GlobalAabb,
                geometry.CameraSpaceDepth,
                geometry.ProjectedScreenBounds,
                failures.Distinct(StringComparer.Ordinal).ToArray());
        }).ToArray();

        var failuresByCode = rows
            .SelectMany(row => row.FailureCodes)
            .GroupBy(code => code, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        AddCountFailure(
            failuresByCode,
            "loaded-lod-block-count-mismatch",
            rows.Length != contract.SelectedBlocks);
        AddCountFailure(
            failuresByCode,
            "loaded-object-lod-count-mismatch",
            rows.Count(row => row.Family == "object") != contract.SelectedObjectBlocks);
        AddCountFailure(
            failuresByCode,
            "loaded-terrain-lod-count-mismatch",
            rows.Count(row => row.Family == "terrain") != contract.SelectedTerrainBlocks);
        var latticeGaps = TerrainLatticeGaps(rows, contract);
        AddCountFailure(
            failuresByCode,
            "terrain-lod-lattice-gap",
            latticeGaps.Count > 0,
            latticeGaps.Count);
        return new Result(
            true,
            rows.Length,
            rows.Count(row => row.ExpectedInView),
            rows.Count(row => row.RenderLayerVisible),
            failuresByCode.Values.Sum(),
            failuresByCode,
            latticeGaps,
            failuresByCode.Count == 0,
            contract,
            rows);
    }

    internal static object Document(Result result) => new
    {
        applicable = result.Applicable,
        status = result.Passed ? "pass" : "fail",
        passed = result.Passed,
        selectedBlocks = result.SelectedBlocks,
        expectedInView = result.ExpectedInView,
        renderLayerVisible = result.RenderLayerVisible,
        runtimeFailures = result.RuntimeFailureCount,
        failuresByCode = result.FailuresByCode,
        coverageContract = result.Contract is null
            ? null
            : new
            {
                level = result.Contract.Value.Level,
                blockStrideCells = result.Contract.Value.BlockStrideCells,
                cellSizeGameUnits = result.Contract.Value.CellSizeGameUnits,
                blockStrideGameUnits =
                    result.Contract.Value.CellSizeGameUnits *
                    result.Contract.Value.BlockStrideCells,
                result.Contract.Value.SelectionRadiusCells,
                result.Contract.Value.SelectedObjectBlocks,
                result.Contract.Value.SelectedTerrainBlocks,
                result.Contract.Value.NearCellHolePolicy,
                loadedGridBounds = new
                {
                    result.Contract.Value.LoadedGridBounds.MinX,
                    result.Contract.Value.LoadedGridBounds.MaxX,
                    result.Contract.Value.LoadedGridBounds.MinY,
                    result.Contract.Value.LoadedGridBounds.MaxY,
                },
                archiveLatticeGaps = result.ArchiveLatticeGaps.Select(gap => new
                {
                    grid = new[] { gap.X, gap.Y },
                    reason = gap.Reason,
                }),
                archiveLatticeGapCount = result.ArchiveLatticeGaps.Count,
            },
        blocks = result.Rows.Select(row => new
        {
            id = row.Id,
            assetId = row.AssetId,
            logicalPath = row.LogicalPath,
            sourceSha256 = row.SourceSha256,
            family = row.Family,
            level = row.Level,
            variant = row.Variant,
            selectionReason = row.SelectionReason,
            blockOriginGameUnits = new[]
            {
                row.BlockOriginGameUnits.X,
                row.BlockOriginGameUnits.Y,
                row.BlockOriginGameUnits.Z,
            },
            expectedInView = row.ExpectedInView,
            projectedScreenIntersectsViewport = row.ProjectedScreenIntersectsViewport,
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

    private static void AddCountFailure(
        IDictionary<string, int> failures,
        string code,
        bool failed,
        int count = 1)
    {
        if (failed)
            failures.Add(code, count);
    }

    private static IReadOnlyList<ArchiveLatticeGap> TerrainLatticeGaps(
        IReadOnlyList<Row> rows,
        CellContentLoader.LodCoverageContract contract)
    {
        var runtimeTerrainByOrigin = rows
            .Where(row => row.Family == "terrain")
            .GroupBy(row => new Grid(row.BlockOriginGameUnits, contract.CellSizeGameUnits))
            .ToDictionary(group => group.Key, group => group.Count());
        var compilerTerrainByOrigin = contract.ExpectedBlocks
            .Where(block => block.Family == "terrain")
            .GroupBy(block => new Grid(
                block.BlockOriginGameUnits,
                contract.CellSizeGameUnits))
            .ToDictionary(group => group.Key, group => group.Count());
        var contractMismatches = compilerTerrainByOrigin
            .Where(pair =>
                pair.Value != 1 ||
                runtimeTerrainByOrigin.GetValueOrDefault(pair.Key) != 1)
            .Select(pair => new ArchiveLatticeGap(
                pair.Key.X,
                pair.Key.Y,
                pair.Value != 1
                    ? "terrain-lod-lattice-position-has-duplicate-compiler-blocks"
                    : runtimeTerrainByOrigin.ContainsKey(pair.Key)
                        ? "terrain-lod-lattice-position-has-duplicate-runtime-blocks"
                        : "terrain-lod-lattice-position-has-no-owned-runtime-block"));
        var unexpectedRuntimeOrigins = runtimeTerrainByOrigin.Keys
            .Where(origin => !compilerTerrainByOrigin.ContainsKey(origin))
            .Select(origin => new ArchiveLatticeGap(
                origin.X,
                origin.Y,
                "terrain-lod-lattice-position-is-not-compiler-selected"));
        return contractMismatches
            .Concat(unexpectedRuntimeOrigins)
            .OrderBy(gap => gap.Y)
            .ThenBy(gap => gap.X)
            .ToArray();
    }

    private readonly record struct Grid(int X, int Y)
    {
        internal Grid(Vector3 origin, float cellSizeGameUnits) : this(
            Mathf.RoundToInt(origin.X / cellSizeGameUnits),
            Mathf.RoundToInt(origin.Y / cellSizeGameUnits))
        {
        }
    }

    internal sealed record Result(
        bool Applicable,
        int SelectedBlocks,
        int ExpectedInView,
        int RenderLayerVisible,
        int RuntimeFailureCount,
        IReadOnlyDictionary<string, int> FailuresByCode,
        IReadOnlyList<ArchiveLatticeGap> ArchiveLatticeGaps,
        bool Passed,
        CellContentLoader.LodCoverageContract? Contract,
        IReadOnlyList<Row> Rows);

    internal sealed record ArchiveLatticeGap(int X, int Y, string Reason);

    internal sealed record Row(
        string Id,
        string AssetId,
        string LogicalPath,
        string SourceSha256,
        string Family,
        int Level,
        string Variant,
        string SelectionReason,
        Vector3 BlockOriginGameUnits,
        bool ExpectedInView,
        bool ProjectedScreenIntersectsViewport,
        bool RenderLayerVisible,
        bool AabbValid,
        bool FrustumIntersection,
        int Surfaces,
        int Vertices,
        int Triangles,
        Aabb? GlobalAabb,
        Vector2? CameraSpaceDepth,
        Vector4? ProjectedScreenBounds,
        IReadOnlyList<string> FailureCodes);
}
