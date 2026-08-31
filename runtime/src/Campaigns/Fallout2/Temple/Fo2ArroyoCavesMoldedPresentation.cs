using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArroyoCavesMoldedCoverage(
    Fo2ArroyoCaves3DProfile Profile,
    int SourceFloorPatches,
    int SourceTopLevelObjects,
    IReadOnlyDictionary<int, int> SourceObjectTypes,
    int SourceFloorBoundaryEdges,
    int MoldedFloorPatches,
    int MoldedFloorTriangles,
    int MoldedFloorMeshes,
    int FloorBoundaryClosureTriangles,
    int FloorBoundaryClosureMeshes,
    int SourceWallObjects,
    int UniqueWallTiles,
    int WallComponents,
    int CaveShellComponents,
    int CaveShellWallObjects,
    int StonePostInstances,
    int ClosedReliefWorldObjects,
    int LargestWallComponentTiles,
    int WallBoundaryEdges,
    int WallTriangles,
    int WallMeshInstances,
    int HiddenWallSpriteCards,
    int HiddenNonWallBlockCards,
    int HiddenSourceMarkerCards,
    int VisibleSourceProps,
    int GroundedSourceProps,
    float MaximumGroundErrorMeters,
    int VisibleSourceTorchProps,
    int SourceTorchPostLayeredAssemblies,
    int SourceMapLightRecords,
    int SourceMapLights,
    int SourceTorchMotivatedMapLights,
    int VisibleSprite3DCards,
    int ClassifiedSourceObjects,
    int UnaccountedSourceObjects,
    int BehaviorIncompleteSourceObjects,
    string WorldSpaceMaterialContract,
    string SourceWallTextureSha256,
    string SourceWallNormalTextureSha256,
    string SourceFloorTextureSha256,
    string SourceFloorNormalTextureSha256,
    string SourceWallProvenanceSha256,
    int SourceWallMaterialArtifacts,
    int OpaqueSourceWallMaterialArtifacts,
    int SourceFloorMaterialArtifacts,
    bool SourceWalkMaskUnchanged,
    bool GeneratedAssetsUsed);

internal static class Fo2ArroyoCavesMoldedPresentation
{
    private readonly record struct ReliefMeshKey(
        string ArtifactId,
        Vector2I SourcePixelOffset,
        float SourcePixelsPerMeter,
        float DepthMeters,
        float SideRoughness,
        bool SourcePixelsOnly);

    private const int FloorIdMask = 0x0fff;
    private const float Half = 0.5f;
    private const float GoldenRatio = 1.61803398875f;
    private const float VertexPrecision = 100000.0f;
    private const float OverlayThicknessMeters = 0.006f;
    private const float MinimumWallTriangleAreaSquared = 0.0000000001f;
    private const float FullRotationDegrees = 360.0f;
    private const float SdfContourToleranceFraction = 0.01f;
    private const string SourceFloorNode = "MAP_3_ELEVATION_0_FLOOR_FRM";
    private const string SourceObjectNode = "MAP_3_ELEVATION_0_TOP_LEVEL_OBJECT_FRM";

    private const string RockShader = """
        shader_type spatial;
        render_mode cull_disabled, depth_draw_opaque;

        uniform vec4 dark_color : source_color;
        uniform vec4 light_color : source_color;
        uniform float world_scale = 1.0;
        uniform float roughness_value = 1.0;
        uniform float normal_strength = 0.2;
        uniform float ambient_lift = 0.2;
        uniform sampler2D source_surface_albedo : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D source_surface_normal : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform float source_detail_world_scale = 0.2;
        uniform float source_detail_mix = 0.0;
        uniform float macro_detail_world_scale = 0.08;
        uniform float macro_detail_mix = 0.0;

        varying vec3 world_position;

        float hash21(vec2 value) {
            value = fract(value * vec2(123.34, 456.21));
            value += dot(value, value + 45.32);
            return fract(value.x * value.y);
        }

        float value_noise(vec2 value) {
            vec2 cell = floor(value);
            vec2 fraction = fract(value);
            fraction = fraction * fraction * (3.0 - 2.0 * fraction);
            float first = mix(hash21(cell), hash21(cell + vec2(1.0, 0.0)), fraction.x);
            float second = mix(hash21(cell + vec2(0.0, 1.0)), hash21(cell + vec2(1.0, 1.0)), fraction.x);
            return mix(first, second, fraction.y);
        }

        float rock_noise(vec2 value) {
            mat2 rotation = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8));
            float result = 0.0;
            float amplitude = 0.54;
            float normalization = 0.0;
            for (int octave = 0; octave < 4; octave++) {
                result += value_noise(value) * amplitude;
                normalization += amplitude;
                value = rotation * value * 2.03 + vec2(13.7, 9.2);
                amplitude *= 0.5;
            }
            return result / normalization;
        }

        float triplanar_rock(vec3 point, vec3 normal) {
            vec3 weights = pow(abs(normal), vec3(4.0));
            weights /= max(weights.x + weights.y + weights.z, 0.0001);
            float first = rock_noise(point.yz);
            float second = rock_noise(point.xz);
            float third = rock_noise(point.xy);
            float detail = rock_noise(point.xz * 2.7 + vec2(7.0, 11.0));
            return dot(vec3(first, second, third), weights) * 0.82 + detail * 0.18;
        }

        vec3 triplanar_source_detail(vec3 point, vec3 normal) {
            vec3 weights = pow(abs(normal), vec3(4.0));
            weights /= max(weights.x + weights.y + weights.z, 0.0001);
            vec3 first = texture(source_surface_albedo, point.yz).rgb;
            vec3 second = texture(source_surface_albedo, point.xz).rgb;
            vec3 third = texture(source_surface_albedo, point.xy).rgb;
            return first * weights.x + second * weights.y + third * weights.z;
        }

        vec3 source_normal(vec2 coordinates) {
            vec3 sampled = texture(source_surface_normal, coordinates).rgb * 2.0 - 1.0;
            return normalize(vec3(sampled.xy * normal_strength, sampled.z));
        }

        vec3 triplanar_source_normal(vec3 point, vec3 geometric_normal) {
            vec3 weights = pow(abs(geometric_normal), vec3(4.0));
            weights /= max(weights.x + weights.y + weights.z, 0.0001);
            vec3 orientation = sign(geometric_normal);
            vec3 first_sample = source_normal(point.yz);
            vec3 second_sample = source_normal(point.xz);
            vec3 third_sample = source_normal(point.xy);
            vec3 first = vec3(
                first_sample.z * orientation.x,
                first_sample.x,
                first_sample.y);
            vec3 second = vec3(
                second_sample.x,
                second_sample.z * orientation.y,
                second_sample.y);
            vec3 third = vec3(
                third_sample.x,
                third_sample.y,
                third_sample.z * orientation.z);
            return normalize(first * weights.x + second * weights.y + third * weights.z);
        }

        void vertex() {
            world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            vec3 world_normal = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
            float rock = triplanar_rock(world_position * world_scale, world_normal);
            float ridge = smoothstep(0.28, 0.82, rock);
            vec3 analytic_rock = mix(dark_color.rgb, light_color.rgb, ridge);
            vec3 owned_fine_detail = triplanar_source_detail(
                world_position * source_detail_world_scale,
                world_normal);
            vec3 owned_macro_detail = triplanar_source_detail(
                world_position * macro_detail_world_scale,
                world_normal);
            vec3 owned_detail = mix(
                owned_fine_detail,
                owned_macro_detail,
                macro_detail_mix);
            ALBEDO = mix(analytic_rock, owned_detail, source_detail_mix);
            ROUGHNESS = roughness_value;
            METALLIC = 0.0;
            ALBEDO *= 1.12 + ambient_lift * 0.18;
            EMISSION = vec3(0.0);
            vec3 source_world_normal = triplanar_source_normal(
                world_position * source_detail_world_scale,
                world_normal);
            NORMAL = normalize((VIEW_MATRIX * vec4(source_world_normal, 0.0)).xyz);
        }
        """;

    internal static Fo2ArroyoCavesMoldedCoverage Build(
        Fo2ArroyoCavesPresentationCatalog catalog,
        Fo2MapSceneBuildCoverage scene)
    {
        var profile = Fo2ArroyoCaves3DProfile.Load(catalog);
        var floorIds = catalog.TileEntries.Select(entry => (int)(entry & FloorIdMask)).ToArray();
        var floorPatches = Enumerable.Range(0, floorIds.Length)
            .Where(index => floorIds[index] !=
                Fo2ArroyoCavesPresentationCatalog.DefaultFloorTileId)
            .ToArray();
        var floorPatchSet = floorPatches.ToHashSet();
        var floorBoundaryEdges = FloorBoundaryEdges(floorPatchSet);
        var placements = catalog.ObjectPlacements
            .Where(row => row.Elevation == Fo2ArroyoCavesPresentationCatalog.Elevation)
            .OrderBy(row => row.Serial)
            .ToArray();
        var objectTypes = placements.GroupBy(row => row.ObjectType)
            .ToDictionary(group => group.Key, group => group.Count());
        var walls = placements
            .Where(row => row.ObjectType == profile.WallGeometry.SourceObjectType)
            .ToArray();
        var wallTiles = walls.Select(row => row.Tile).ToHashSet();
        var components = ConnectedComponents(wallTiles);
        var caveShellComponents = components
            .Where(component => component.Count >=
                profile.WallGeometry.Roles.CaveShellMinimumConnectedTiles)
            .ToArray();
        var stonePostTiles = components
            .Where(component => component.Count <
                profile.WallGeometry.Roles.CaveShellMinimumConnectedTiles)
            .SelectMany(component => component)
            .ToHashSet();
        var stonePostPlacements = walls
            .Where(row => stonePostTiles.Contains(row.Tile))
            .ToArray();
        if (caveShellComponents.Length !=
                profile.WallGeometry.Roles.ExpectedCaveShellComponents ||
            stonePostPlacements.Length !=
                profile.WallGeometry.Roles.ExpectedStonePostInstances ||
            stonePostPlacements.Select(row => row.Tile).Distinct().Count() !=
                stonePostPlacements.Length ||
            stonePostPlacements.Any(row =>
                !profile.WallGeometry.Roles.StonePostLogicalPaths.Contains(
                    row.LogicalPath.ToLowerInvariant())))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves wall-role mapping drifted.");
        var wallBoundaryEdges = components.Sum(BoundaryEdges);
        VerifySourceCoverage(
            profile.SourceCoverage,
            floorPatches,
            floorBoundaryEdges,
            placements,
            objectTypes,
            walls,
            wallTiles,
            components,
            wallBoundaryEdges);

        var moldedRoot = new Node3D { Name = "MAP_3_SOURCE_MOLDED_3D_PRESENTATION" };
        moldedRoot.SetMeta("fo2_3d_profile", profile.ResourcePath);
        moldedRoot.SetMeta("fo2_3d_profile_sha256", profile.Sha256);
        moldedRoot.SetMeta("fo2_map_sha256", catalog.MapSha256);
        moldedRoot.SetMeta("fo2_source_walk_mask_sha256", catalog.WalkMaskSha256);
        scene.Root.AddChild(moldedRoot);

        var floorMaterial = BuildRockMaterial(
            profile.Materials,
            profile.Materials.Floor,
            catalog.MoldedSurface.FloorTexturePath,
            catalog.MoldedSurface.FloorNormalTexturePath);
        var wallMaterial = BuildRockMaterial(
            profile.Materials,
            profile.Materials.Wall,
            catalog.MoldedSurface.WallTexturePath,
            catalog.MoldedSurface.WallNormalTexturePath);
        var floorMesh = BuildMoldedFloor(floorPatches, profile.FloorGeometry);
        floorMesh.SurfaceSetMaterial(0, floorMaterial);
        var floorInstance = new MeshInstance3D
        {
            Name = "FUSED_MAP3_SOURCE_FLOOR_RELIEF",
            Mesh = floorMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        floorInstance.SetMeta("fo2_floor_authority", profile.FloorGeometry.Mode);
        moldedRoot.AddChild(floorInstance);
        var floorBoundaryMesh = BuildFloorBoundaryClosure(
            floorPatchSet,
            profile.FloorGeometry,
            floorBoundaryEdges);
        floorBoundaryMesh.SurfaceSetMaterial(0, wallMaterial);
        var floorBoundaryInstance = new MeshInstance3D
        {
            Name = "SOURCE_FLOOR_BOUNDARY_VOID_CLOSURE",
            Mesh = floorBoundaryMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        floorBoundaryInstance.SetMeta("fo2_source_floor_boundary_edges", floorBoundaryEdges);
        floorBoundaryInstance.SetMeta("fo2_walk_space_added", false);
        moldedRoot.AddChild(floorBoundaryInstance);
        scene.Root.GetNode<Node3D>(SourceFloorNode).Visible = false;

        var wallRoot = new Node3D { Name = "FUSED_MAP3_SOURCE_WALL_AND_CEILING_SHELLS" };
        moldedRoot.AddChild(wallRoot);
        var wallTriangles = 0;
        foreach (var (component, index) in caveShellComponents
                     .Select((value, index) => (value, index)))
        {
            var boundaryEdges = BoundaryEdges(component);
            var mesh = BuildWallShell(component, profile.WallGeometry, boundaryEdges);
            mesh.SurfaceSetMaterial(0, wallMaterial);
            var instance = new MeshInstance3D
            {
                Name = $"SOURCE_WALL_COMPONENT_{index:D2}_FUSED_CLOSED_SHELL",
                Mesh = mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            instance.SetMeta("fo2_source_wall_hexes", component.Count);
            instance.SetMeta("fo2_source_boundary_edges", boundaryEdges);
            instance.SetMeta("fo2_ceiling_closed", profile.WallGeometry.CeilingClosure);
            wallRoot.AddChild(instance);
            wallTriangles += mesh.GetFaces().Length / 3;
        }
        var reliefMeshCache = new Dictionary<ReliefMeshKey, Fo2FrmReliefMeshSet>();
        var stonePostCoverage = BuildStonePosts(
            moldedRoot,
            stonePostPlacements,
            catalog,
            scene.SourcePixelsPerMeter,
            profile,
            reliefMeshCache);
        wallTriangles += stonePostCoverage.Triangles;

        var propCoverage = GroundSourceProps(
            scene.Root.GetNode<Node3D>(SourceObjectNode),
            placements,
            catalog,
            scene.SourcePixelsPerMeter,
            moldedRoot,
            profile,
            reliefMeshCache);
        var visibleReliefArtifactIds = catalog.ObjectRelief.Placements
            .Where(row => row.Role != "caveWall")
            .Select(row => row.ArtifactId)
            .ToHashSet(StringComparer.Ordinal);
        var builtReliefArtifactIds = reliefMeshCache.Keys
            .Select(row => row.ArtifactId)
            .ToHashSet(StringComparer.Ordinal);
        if (!builtReliefArtifactIds.SetEquals(visibleReliefArtifactIds))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo visible FRM relief-resource coverage drifted.");
        var visibleTorchProps = propCoverage.SourceTorchAssemblies;
        var sourceMapLights = BuildSourceMapLights(moldedRoot, placements, profile);
        BuildEnvironment(scene.Root, profile);

        return new Fo2ArroyoCavesMoldedCoverage(
            profile,
            floorPatches.Length,
            placements.Length,
            objectTypes,
            floorBoundaryEdges,
            floorPatches.Length,
            floorPatches.Length * 2,
            1,
            floorBoundaryEdges * 2,
            1,
            walls.Length,
            wallTiles.Count,
            components.Count,
            caveShellComponents.Length,
            walls.Length - stonePostPlacements.Length,
            stonePostCoverage.Instances,
            stonePostCoverage.Instances + propCoverage.ReliefVolumes,
            components.Max(component => component.Count),
            wallBoundaryEdges,
            wallTriangles,
            wallRoot.GetChildCount(),
            propCoverage.HiddenWallCards,
            propCoverage.HiddenNonWallBlockCards,
            propCoverage.HiddenSourceMarkerCards,
            propCoverage.VisibleSourceProps,
            propCoverage.GroundedSourceProps,
            propCoverage.MaximumGroundErrorMeters,
            visibleTorchProps,
            propCoverage.SourceTorchPostLayeredAssemblies,
            sourceMapLights.Records,
            sourceMapLights.Lights,
            sourceMapLights.TorchMotivatedLights,
            0,
            placements.Length,
            0,
            profile.SourceCoverage.HiddenSourceMarkerCards,
            profile.Materials.ShaderContract,
            catalog.MoldedSurface.WallTextureSha256,
            catalog.MoldedSurface.WallNormalTextureSha256,
            catalog.MoldedSurface.FloorTextureSha256,
            catalog.MoldedSurface.FloorNormalTextureSha256,
            catalog.MoldedSurface.ProvenanceSha256,
            catalog.MoldedSurface.SourceWallArtifacts,
            catalog.MoldedSurface.OpaqueSourceWallArtifacts,
            catalog.MoldedSurface.SourceFloorArtifacts,
            true,
            profile.GeneratedAssetLane.Used);
    }

    private static void VerifySourceCoverage(
        Fo2ArroyoSourceCoverageProfile expected,
        IReadOnlyList<int> floorPatches,
        int floorBoundaryEdges,
        IReadOnlyList<Fo2MapObjectPlacement> placements,
        IReadOnlyDictionary<int, int> objectTypes,
        IReadOnlyList<Fo2MapObjectPlacement> walls,
        IReadOnlySet<int> wallTiles,
        IReadOnlyList<HashSet<int>> components,
        int wallBoundaryEdges)
    {
        if (floorPatches.Count != expected.NonDefaultFloorPatches ||
            floorBoundaryEdges != expected.FloorBoundaryEdges ||
            placements.Count != expected.TopLevelObjects ||
            !objectTypes.OrderBy(row => row.Key).SequenceEqual(
                expected.ObjectTypes.OrderBy(row => row.Key)) ||
            walls.Count != expected.WallObjects ||
            wallTiles.Count != expected.UniqueWallTiles ||
            components.Count != expected.WallComponents ||
            components.Max(component => component.Count) !=
                expected.LargestWallComponentTiles ||
            wallBoundaryEdges != expected.WallBoundaryEdges)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves exact Map 3 source coverage drifted.");
    }

    private static ArrayMesh BuildMoldedFloor(
        IReadOnlyList<int> floorPatches,
        Fo2ArroyoFloorGeometryProfile profile)
    {
        var builder = new IndexedMeshBuilder();
        var halfX = Fo1HexMath.ColumnSpacingMeters;
        var halfZ = Fo1HexMath.FlatToFlatMeters;
        var stepX = halfX * 2.0f / profile.SubdivisionsPerAxis;
        var stepZ = halfZ * 2.0f / profile.SubdivisionsPerAxis;
        foreach (var index in floorPatches)
        {
            var center = Fo1HexMath.FloorPatchCenter(index);
            for (var row = 0; row < profile.SubdivisionsPerAxis; row++)
                for (var column = 0; column < profile.SubdivisionsPerAxis; column++)
                {
                    var x0 = -halfX + column * stepX;
                    var x1 = x0 + stepX;
                    var z0 = -halfZ + row * stepZ;
                    var z1 = z0 + stepZ;
                    var first = FloorPoint(center + new Vector3(x0, 0.0f, z0), profile);
                    var second = FloorPoint(center + new Vector3(x0, 0.0f, z1), profile);
                    var third = FloorPoint(center + new Vector3(x1, 0.0f, z1), profile);
                    var fourth = FloorPoint(center + new Vector3(x1, 0.0f, z0), profile);
                    builder.AddTriangle(first, second, third);
                    builder.AddTriangle(first, third, fourth);
                }
        }
        return builder.Commit("Fallout 2 Arroyo molded source floor");
    }

    private static ArrayMesh BuildFloorBoundaryClosure(
        IReadOnlySet<int> occupied,
        Fo2ArroyoFloorGeometryProfile profile,
        int expectedBoundaryEdges)
    {
        var builder = new IndexedMeshBuilder();
        var halfX = Fo1HexMath.ColumnSpacingMeters;
        var halfZ = Fo1HexMath.FlatToFlatMeters;
        var boundaryEdges = 0;
        foreach (var index in occupied.Order())
        {
            var coordinate = new Vector2I(index % Fo1HexMath.FloorWidth, index / Fo1HexMath.FloorWidth);
            var center = Fo1HexMath.FloorPatchCenter(index);
            var corners = new[]
            {
                center + new Vector3(-halfX, 0.0f, -halfZ),
                center + new Vector3(-halfX, 0.0f, halfZ),
                center + new Vector3(halfX, 0.0f, halfZ),
                center + new Vector3(halfX, 0.0f, -halfZ),
            };
            var neighbors = new[]
            {
                FloorIndex(coordinate + Vector2I.Right),
                FloorIndex(coordinate + Vector2I.Down),
                FloorIndex(coordinate + Vector2I.Left),
                FloorIndex(coordinate + Vector2I.Up),
            };
            for (var edge = 0; edge < neighbors.Length; edge++)
            {
                if (neighbors[edge] >= 0 && occupied.Contains(neighbors[edge]))
                    continue;
                boundaryEdges++;
                var next = (edge + 1) % corners.Length;
                var firstBottom = FloorPoint(corners[edge], profile);
                var secondBottom = FloorPoint(corners[next], profile);
                var midpoint = (firstBottom + secondBottom) * Half;
                var outward = midpoint - center;
                outward.Y = 0.0f;
                outward = outward.Normalized() * profile.BoundaryClosureOverhangMeters;
                var firstTop = firstBottom + outward +
                    Vector3.Up * profile.BoundaryClosureHeightMeters;
                var secondTop = secondBottom + outward +
                    Vector3.Up * profile.BoundaryClosureHeightMeters;
                builder.AddTriangle(firstBottom, firstTop, secondTop);
                builder.AddTriangle(firstBottom, secondTop, secondBottom);
            }
        }
        if (boundaryEdges != expectedBoundaryEdges)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves floor-boundary closure drifted.");
        return builder.Commit("Fallout 2 Arroyo source floor-boundary closure");
    }

    private static int FloorBoundaryEdges(IReadOnlySet<int> occupied)
    {
        var result = 0;
        foreach (var index in occupied)
        {
            var coordinate = new Vector2I(index % Fo1HexMath.FloorWidth, index / Fo1HexMath.FloorWidth);
            foreach (var offset in new[]
                     {
                         Vector2I.Left,
                         Vector2I.Right,
                         Vector2I.Up,
                         Vector2I.Down,
                     })
            {
                var neighbor = FloorIndex(coordinate + offset);
                if (neighbor < 0 || !occupied.Contains(neighbor))
                    result++;
            }
        }
        return result;
    }

    private static int FloorIndex(Vector2I coordinate) =>
        coordinate.X is < 0 or >= Fo1HexMath.FloorWidth ||
        coordinate.Y is < 0 or >= Fo1HexMath.FloorHeight
            ? -1
            : coordinate.Y * Fo1HexMath.FloorWidth + coordinate.X;

    private static Vector3 FloorPoint(Vector3 point, Fo2ArroyoFloorGeometryProfile profile)
    {
        point.Y = FloorHeight(point, profile);
        return point;
    }

    private static float FloorHeight(Vector3 point, Fo2ArroyoFloorGeometryProfile profile) =>
        profile.SurfaceMeters + profile.ReliefMeters * Half *
        (MathF.Sin(point.X * profile.ReliefFrequency) +
            MathF.Sin(point.Z * profile.ReliefFrequency * GoldenRatio));

    private static ArrayMesh BuildWallShell(
        IReadOnlySet<int> occupied,
        Fo2ArroyoWallGeometryProfile profile,
        int expectedBoundaryEdges)
    {
        if (BoundaryEdges(occupied) != expectedBoundaryEdges)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves wall boundary coverage drifted.");

        var centers = occupied.Order().Select(tile =>
        {
            var point = Fo1HexMath.Center(tile);
            return new Vector2(point.X, point.Z);
        }).ToArray();
        var segments = SourceWallSegments(occupied);
        var margin = profile.SdfRadiusMeters + profile.SideNoiseMeters +
            profile.CeilingOverhangMeters + profile.SdfSampleMeters;
        var minimum = new Vector2(
            centers.Min(point => point.X) - margin,
            centers.Min(point => point.Y) - margin);
        var maximum = new Vector2(
            centers.Max(point => point.X) + margin,
            centers.Max(point => point.Y) + margin);
        var columns = Mathf.CeilToInt((maximum.X - minimum.X) / profile.SdfSampleMeters) + 1;
        var rows = Mathf.CeilToInt((maximum.Y - minimum.Y) / profile.SdfSampleMeters) + 1;
        var field = new float[columns, rows];
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
                field[column, row] = SourceWallField(
                    minimum + new Vector2(
                        column * profile.SdfSampleMeters,
                        row * profile.SdfSampleMeters),
                    centers,
                    segments,
                    profile);

        var polygons = new List<Vector2[]>();
        var contourEdges = new Dictionary<SdfEdgeKey, ContourEdge>();
        for (var row = 0; row < rows - 1; row++)
        {
            for (var column = 0; column < columns - 1; column++)
            {
                var first = minimum + new Vector2(
                    column * profile.SdfSampleMeters,
                    row * profile.SdfSampleMeters);
                var second = first + Vector2.Right * profile.SdfSampleMeters;
                var third = second + Vector2.Down * profile.SdfSampleMeters;
                var fourth = first + Vector2.Down * profile.SdfSampleMeters;
                AddClippedWallPolygon(
                    polygons,
                    contourEdges,
                    [first, second, third],
                    [field[column, row], field[column + 1, row], field[column + 1, row + 1]]);
                AddClippedWallPolygon(
                    polygons,
                    contourEdges,
                    [first, third, fourth],
                    [field[column, row], field[column + 1, row + 1], field[column, row + 1]]);
            }
        }
        var builder = new IndexedMeshBuilder();
        foreach (var polygon in polygons)
        {
            var firstTop = SdfWallTop(polygon[0], profile);
            var firstBottom = WallBottom(polygon[0], profile);
            for (var index = 1; index < polygon.Length - 1; index++)
            {
                var secondTop = SdfWallTop(polygon[index], profile);
                var thirdTop = SdfWallTop(polygon[index + 1], profile);
                var secondBottom = WallBottom(polygon[index], profile);
                var thirdBottom = WallBottom(polygon[index + 1], profile);
                AddWallTriangle(builder, firstTop, thirdTop, secondTop);
                AddWallTriangle(builder, firstBottom, secondBottom, thirdBottom);
            }
        }
        var shellContourEdges = contourEdges.Values.Where(value =>
                value.Count == 1 &&
                MathF.Abs(SourceWallField(value.First, centers, segments, profile)) <=
                    profile.SdfSampleMeters * SdfContourToleranceFraction &&
                MathF.Abs(SourceWallField(value.Second, centers, segments, profile)) <=
                    profile.SdfSampleMeters * SdfContourToleranceFraction)
            .ToArray();
        if (shellContourEdges.Length == 0)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo source-wall SDF produced no closed contour.");
        foreach (var edge in shellContourEdges)
        {
            var firstBottom = WallBottom(edge.First, profile);
            var secondBottom = WallBottom(edge.Second, profile);
            var firstTop = SdfWallTop(edge.First, profile);
            var secondTop = SdfWallTop(edge.Second, profile);
            var midpoint = (edge.First + edge.Second) * Half;
            var outward = SourceWallGradient(midpoint, centers, segments, profile);
            var ownerCenter = new Vector3(
                midpoint.X - outward.X,
                0.0f,
                midpoint.Y - outward.Y);
            AddWallSideSegment(
                builder,
                firstBottom,
                secondBottom,
                firstTop,
                secondTop,
                ownerCenter,
                profile);
        }
        return builder.Commit("Fallout 2 Arroyo fused closed wall shell");
    }

    private static void AddWallTriangle(
        IndexedMeshBuilder builder,
        Vector3 first,
        Vector3 second,
        Vector3 third)
    {
        if ((second - first).Cross(third - first).LengthSquared() >
            MinimumWallTriangleAreaSquared)
            builder.AddTriangle(first, second, third);
    }

    private static void AddClippedWallPolygon(
        ICollection<Vector2[]> polygons,
        IDictionary<SdfEdgeKey, ContourEdge> contourEdges,
        IReadOnlyList<Vector2> points,
        IReadOnlyList<float> values)
    {
        var clipped = new List<Vector2>();
        for (var index = 0; index < points.Count; index++)
        {
            var next = (index + 1) % points.Count;
            var inside = values[index] <= 0.0f;
            var nextInside = values[next] <= 0.0f;
            if (inside)
                clipped.Add(points[index]);
            if (inside == nextInside)
                continue;
            var weight = values[index] / (values[index] - values[next]);
            clipped.Add(points[index].Lerp(points[next], weight));
        }
        clipped = clipped
            .Where((point, index) => index == 0 || SdfKey(point) != SdfKey(clipped[index - 1]))
            .ToList();
        if (clipped.Count > 2 && SdfKey(clipped[0]) == SdfKey(clipped[^1]))
            clipped.RemoveAt(clipped.Count - 1);
        if (clipped.Count < 3)
            return;
        var polygon = clipped.ToArray();
        polygons.Add(polygon);
        for (var index = 0; index < polygon.Length; index++)
        {
            var next = (index + 1) % polygon.Length;
            var firstKey = SdfKey(polygon[index]);
            var secondKey = SdfKey(polygon[next]);
            var key = SdfEdgeKey.Create(firstKey, secondKey);
            if (contourEdges.TryGetValue(key, out var edge))
                contourEdges[key] = edge with { Count = edge.Count + 1 };
            else
                contourEdges.Add(key, new ContourEdge(polygon[index], polygon[next], 1));
        }
    }

    private static SourceWallSegment[] SourceWallSegments(IReadOnlySet<int> occupied)
    {
        var result = new List<SourceWallSegment>();
        foreach (var tile in occupied.Order())
        {
            var firstCenter = Fo1HexMath.Center(tile);
            foreach (var neighbor in Fo1HexMath.Neighbors(tile)
                         .Where(neighbor => neighbor > tile && occupied.Contains(neighbor)))
            {
                var secondCenter = Fo1HexMath.Center(neighbor);
                result.Add(new SourceWallSegment(
                    new Vector2(firstCenter.X, firstCenter.Z),
                    new Vector2(secondCenter.X, secondCenter.Z)));
            }
        }
        return result.ToArray();
    }

    private static float SourceWallField(
        Vector2 point,
        IReadOnlyList<Vector2> centers,
        IReadOnlyList<SourceWallSegment> segments,
        Fo2ArroyoWallGeometryProfile profile)
    {
        var distance = centers.Min(center => point.DistanceTo(center));
        foreach (var segment in segments)
            distance = MathF.Min(distance, DistanceToSegment(point, segment));
        var variation = profile.SideNoiseMeters * Half *
            (MathF.Sin(point.X * profile.HeightFrequency + profile.HeightPhase) +
                MathF.Sin(point.Y * profile.HeightFrequency * GoldenRatio - profile.HeightPhase));
        return distance - profile.SdfRadiusMeters - variation;
    }

    private static Vector2 SourceWallGradient(
        Vector2 point,
        IReadOnlyList<Vector2> centers,
        IReadOnlyList<SourceWallSegment> segments,
        Fo2ArroyoWallGeometryProfile profile)
    {
        var epsilon = profile.SdfSampleMeters * Half;
        var gradient = new Vector2(
            SourceWallField(point + Vector2.Right * epsilon, centers, segments, profile) -
                SourceWallField(point - Vector2.Right * epsilon, centers, segments, profile),
            SourceWallField(point + Vector2.Down * epsilon, centers, segments, profile) -
                SourceWallField(point - Vector2.Down * epsilon, centers, segments, profile));
        return gradient.IsZeroApprox() ? Vector2.Right : gradient.Normalized();
    }

    private static float DistanceToSegment(Vector2 point, SourceWallSegment segment)
    {
        var delta = segment.Second - segment.First;
        var lengthSquared = delta.LengthSquared();
        if (lengthSquared <= 0.0f)
            return point.DistanceTo(segment.First);
        var weight = Mathf.Clamp((point - segment.First).Dot(delta) / lengthSquared, 0.0f, 1.0f);
        return point.DistanceTo(segment.First + delta * weight);
    }

    private static Vector3 SdfWallTop(
        Vector2 point,
        Fo2ArroyoWallGeometryProfile profile) => WallTop(point, profile);

    private static Vector3 WallBottom(
        Vector2 point,
        Fo2ArroyoWallGeometryProfile profile) =>
        new(point.X, -profile.GroundSinkMeters, point.Y);

    private static Vector3 WallTop(
        Vector2 point,
        Fo2ArroyoWallGeometryProfile profile) =>
        new(point.X, WallHeight(new Vector3(point.X, 0.0f, point.Y), profile), point.Y);

    private static Vector3 WallTop(
        Vector3 point,
        Fo2ArroyoWallGeometryProfile profile) =>
        new(point.X, WallHeight(point, profile), point.Z);

    private static void AddWallSideSegment(
        IndexedMeshBuilder builder,
        Vector3 firstBottom,
        Vector3 secondBottom,
        Vector3 firstTop,
        Vector3 secondTop,
        Vector3 ownerCenter,
        Fo2ArroyoWallGeometryProfile profile)
    {
        var firstShoulder = WallShoulder(firstBottom, firstTop, ownerCenter, profile);
        var secondShoulder = WallShoulder(secondBottom, secondTop, ownerCenter, profile);
        builder.AddTriangle(firstBottom, firstShoulder, secondShoulder);
        builder.AddTriangle(firstBottom, secondShoulder, secondBottom);
        builder.AddTriangle(firstShoulder, firstTop, secondTop);
        builder.AddTriangle(firstShoulder, secondTop, secondShoulder);
    }

    private static Vector3 WallShoulder(
        Vector3 bottom,
        Vector3 top,
        Vector3 ownerCenter,
        Fo2ArroyoWallGeometryProfile profile)
    {
        var shoulder = bottom.Lerp(top, profile.SideShoulderHeightFraction);
        var outward = new Vector3(
            shoulder.X - ownerCenter.X,
            0.0f,
            shoulder.Z - ownerCenter.Z).Normalized();
        var sourceBoundNoise = MathF.Sin(
            shoulder.X * profile.HeightFrequency * GoldenRatio +
            shoulder.Z * profile.HeightFrequency - profile.HeightPhase);
        var continuousOverhang = profile.CeilingOverhangMeters *
            (Half + Half * MathF.Sin(
                shoulder.X * profile.HeightFrequency -
                shoulder.Z * profile.HeightFrequency * GoldenRatio +
                profile.HeightPhase));
        return shoulder + outward *
            (profile.SideBulgeMeters + continuousOverhang +
                profile.SideNoiseMeters * sourceBoundNoise);
    }

    private static float WallHeight(Vector3 point, Fo2ArroyoWallGeometryProfile profile)
    {
        var first = MathF.Sin(point.X * profile.HeightFrequency + profile.HeightPhase);
        var second = MathF.Sin(
            point.Z * profile.HeightFrequency * GoldenRatio - profile.HeightPhase);
        return profile.HeightMeters + profile.HeightVariationMeters * Half * (first + second);
    }

    private static StonePostCoverage BuildStonePosts(
        Node3D parent,
        IReadOnlyList<Fo2MapObjectPlacement> placements,
        Fo2ArroyoCavesPresentationCatalog catalog,
        float sourcePixelsPerMeter,
        Fo2ArroyoCaves3DProfile profile,
        IDictionary<ReliefMeshKey, Fo2FrmReliefMeshSet> reliefMeshCache)
    {
        var root = new Node3D { Name = "MAP3_EXACT_FRM_DERIVED_STONE_POSTS" };
        parent.AddChild(root);
        var triangles = 0;
        var reliefBySerial = catalog.ObjectRelief.Placements.ToDictionary(row => row.Serial);
        foreach (var placement in placements.OrderBy(row => row.Serial))
        {
            var artifact = catalog.Artifacts[placement.ArtifactId];
            if (!reliefBySerial.TryGetValue(placement.Serial, out var reliefPlacement) ||
                reliefPlacement.Role != "stonePost" ||
                reliefPlacement.ArtifactId != artifact.Id ||
                !catalog.ObjectRelief.Artifacts.TryGetValue(
                    artifact.Id,
                    out var reliefArtifact))
                throw new InvalidOperationException(
                    $"Fallout 2 Arroyo stone-post relief drifted: {placement.Serial}");
            var sourceOffset = placement.PixelOffset + artifact.FrameOffset;
            var meshSet = GetOrBuildReliefMesh(
                reliefMeshCache,
                artifact.Id,
                artifact.Path,
                artifact.Width,
                artifact.Height,
                sourceOffset,
                sourcePixelsPerMeter,
                reliefPlacement.DepthMeters,
                profile.Materials.Wall.Roughness,
                reliefArtifact);
            var center = Fo1HexMath.Center(placement.Tile);
            var instance = Fo2FrmReliefMesh.Instantiate(
                $"SOURCE_STONE_POST_{placement.Serial}_CLOSED_RELIEF",
                meshSet);
            instance.Position = center;
            instance.RotationDegrees = new Vector3(
                0.0f,
                -placement.Rotation * FullRotationDegrees / Fo1HexMath.DirectionCount,
                0.0f);
            SeatReliefOnFloor(
                instance,
                meshSet,
                FloorHeight(center, profile.FloorGeometry));
            instance.SetMeta("fo2_wall_role", profile.WallGeometry.Roles.Mode);
            instance.SetMeta("fo2_map_serial", placement.Serial);
            instance.SetMeta("fo2_map_tile", placement.Tile);
            instance.SetMeta("fo2_source_logical_path", placement.LogicalPath);
            instance.SetMeta("fo2_source_png_sha256", artifact.PngSha256);
            instance.SetMeta("fo2_geometry_mode", catalog.ObjectRelief.Mode);
            instance.SetMeta("fo2_visible_sprite3d_cards", 0);
            root.AddChild(instance);
            triangles += meshSet.FaceTriangles + meshSet.SideTriangles;
        }
        if (root.GetChildCount() != profile.WallGeometry.Roles.ExpectedStonePostInstances)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo stone-post instance coverage drifted.");
        return new StonePostCoverage(root.GetChildCount(), triangles);
    }

    private static PropCoverage GroundSourceProps(
        Node3D objectRoot,
        IReadOnlyList<Fo2MapObjectPlacement> placements,
        Fo2ArroyoCavesPresentationCatalog catalog,
        float sourcePixelsPerMeter,
        Node3D moldedRoot,
        Fo2ArroyoCaves3DProfile profile,
        IDictionary<ReliefMeshKey, Fo2FrmReliefMeshSet> reliefMeshCache)
    {
        var sprites = objectRoot.GetChildren().OfType<Sprite3D>().ToDictionary(
            sprite => sprite.GetMeta("map_serial").AsInt32());
        if (sprites.Count != placements.Count)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves source object node coverage drifted.");
        var hiddenWalls = 0;
        var hiddenBlocks = 0;
        var hiddenMarkers = 0;
        var visibleProps = 0;
        var groundedProps = 0;
        var maximumError = 0.0f;
        var torchAssemblies = 0;
        var torchPostLayers = 0;
        var reliefRoot = new Node3D
        {
            Name = "MAP3_EXACT_SOURCE_OBJECT_CLOSED_RELIEF_VOLUMES",
        };
        moldedRoot.AddChild(reliefRoot);
        var reliefBySerial = catalog.ObjectRelief.Placements.ToDictionary(row => row.Serial);
        var stonePostByTile = catalog.ObjectRelief.Placements
            .Where(row => row.Role == "stonePost")
            .ToDictionary(row => row.Tile);
        var expectedTorchPostLayers = catalog.ObjectRelief.Placements.Count(row =>
            row.Role == "torch" &&
            stonePostByTile.TryGetValue(row.Tile, out var post) &&
            post.Serial < row.Serial);
        foreach (var placement in placements)
        {
            var sprite = sprites[placement.Serial];
            sprite.Visible = false;
            var isWall = placement.ObjectType == profile.WallGeometry.SourceObjectType;
            if (isWall)
                hiddenWalls++;
            if (profile.HiddenCardLogicalPaths.Contains(placement.LogicalPath.ToLowerInvariant()))
            {
                if (!isWall)
                    hiddenBlocks++;
                continue;
            }
            if (profile.HiddenSourceMarkerLogicalPaths.Contains(
                    placement.LogicalPath.ToLowerInvariant()))
            {
                hiddenMarkers++;
                continue;
            }
            if (!reliefBySerial.TryGetValue(placement.Serial, out var reliefPlacement) ||
                reliefPlacement.ArtifactId != placement.ArtifactId ||
                reliefPlacement.Tile != placement.Tile ||
                reliefPlacement.Rotation != placement.Rotation ||
                reliefPlacement.Frame != placement.Frame ||
                reliefPlacement.PixelOffset != placement.PixelOffset ||
                reliefPlacement.Fid != placement.Fid ||
                reliefPlacement.Pid != placement.Pid ||
                reliefPlacement.ObjectType != placement.ObjectType ||
                reliefPlacement.LogicalPath != placement.LogicalPath ||
                !catalog.ObjectRelief.Artifacts.TryGetValue(
                    placement.ArtifactId,
                    out var reliefArtifact))
                throw new InvalidOperationException(
                    $"Fallout 2 Arroyo Caves source relief is absent: {placement.Serial}");
            if (isWall)
            {
                if (reliefPlacement.Role == "stonePost")
                    continue;
                if (reliefPlacement.Role != "caveWall")
                    throw new InvalidOperationException(
                        $"Fallout 2 Arroyo wall relief has an unknown role: " +
                        $"{placement.Serial}/{reliefPlacement.Role}");
                // Exact wall serials, tiles, rotations, and FRM identities already
                // drive the fused cave shell and its source-derived wall material.
                // Re-instantiating each one as a closed relief duplicates that
                // authority as freestanding slabs.
                continue;
            }
            var artifact = catalog.Artifacts[placement.ArtifactId];
            var isTorch = profile.TorchLogicalPaths.Contains(
                placement.LogicalPath.ToLowerInvariant());
            var meshSet = GetOrBuildReliefMesh(
                reliefMeshCache,
                artifact.Id,
                artifact.Path,
                artifact.Width,
                artifact.Height,
                placement.PixelOffset + artifact.FrameOffset,
                sourcePixelsPerMeter,
                reliefPlacement.DepthMeters,
                profile.Materials.Wall.Roughness,
                reliefArtifact,
                sourcePixelsOnly: isTorch);
            var relief = Fo2FrmReliefMesh.Instantiate(
                $"SOURCE_OBJECT_{placement.Serial}_CLOSED_RELIEF",
                meshSet);
            var center = Fo1HexMath.Center(placement.Tile);
            relief.Position = center;
            relief.RotationDegrees = new Vector3(
                0.0f,
                -placement.Rotation * FullRotationDegrees / Fo1HexMath.DirectionCount,
                0.0f);
            var floor = FloorHeight(
                center,
                profile.FloorGeometry);
            var error = SeatReliefOnFloor(relief, meshSet, floor);
            relief.SetMeta("fo2_grounding_contract", profile.SourceProps.GroundingMode);
            relief.SetMeta("fo2_source_floor_y_meters", floor);
            relief.SetMeta("fo2_ground_error_meters", error);
            relief.SetMeta("fo2_map_serial", placement.Serial);
            relief.SetMeta("fo2_map_tile", placement.Tile);
            relief.SetMeta("fo2_map_rotation", placement.Rotation);
            relief.SetMeta("fo2_source_frame", placement.Frame);
            relief.SetMeta("fo2_source_pixel_offset", placement.PixelOffset);
            relief.SetMeta("fo2_source_fid", placement.Fid);
            relief.SetMeta("fo2_source_pid", placement.Pid);
            relief.SetMeta("fo2_source_logical_path", placement.LogicalPath);
            relief.SetMeta("fo2_source_png_sha256", artifact.PngSha256);
            relief.SetMeta("fo2_geometry_mode", catalog.ObjectRelief.Mode);
            relief.SetMeta("fo2_visible_sprite3d_cards", 0);
            if (isTorch)
            {
                if (stonePostByTile.TryGetValue(placement.Tile, out var post))
                {
                    if (post.Serial >= placement.Serial)
                        throw new InvalidOperationException(
                            $"Fallout 2 torch/post source draw order drifted: " +
                            $"{placement.Serial}/{post.Serial}.");
                    relief.Position += relief.Basis.Z.Normalized() *
                        (profile.WallGeometry.Roles.StonePostDepthMeters +
                            profile.SourceProps.CoLocatedLayerGapMeters);
                    relief.SetMeta(
                        "fo2_colocated_source_layer_mode",
                        profile.SourceProps.CoLocatedLayerMode);
                    relief.SetMeta("fo2_colocated_source_post_serial", post.Serial);
                    torchPostLayers++;
                }
                if (!meshSet.SourcePixelsOnly)
                    throw new InvalidOperationException(
                        $"Fallout 2 torch requires exact source alpha pixels: {placement.Serial}.");
                relief.SetMeta("fo2_torch_visual", "exact-source-frm-alpha-pixels-no-halo");
                relief.SetMeta("fo2_camera_facing", "source-world-relief-never-billboard");
                torchAssemblies++;
            }
            reliefRoot.AddChild(relief);
            maximumError = MathF.Max(maximumError, error);
            visibleProps++;
            groundedProps++;
        }
        if (hiddenWalls != profile.SourceCoverage.WallObjects ||
            hiddenBlocks != profile.SourceCoverage.HiddenNonWallBlockCards ||
            hiddenMarkers != profile.SourceCoverage.HiddenSourceMarkerCards ||
            visibleProps != profile.SourceCoverage.VisibleGroundedSourceProps ||
            groundedProps != visibleProps ||
            reliefRoot.GetChildCount() != visibleProps ||
            torchAssemblies != profile.SourceCoverage.SourceTorchProps ||
            torchPostLayers != expectedTorchPostLayers ||
            sprites.Values.Any(sprite => sprite.Visible) ||
            maximumError > profile.SourceProps.MaximumGroundErrorMeters)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves grounded source-prop coverage drifted.");
        return new PropCoverage(
            hiddenWalls,
            hiddenBlocks,
            hiddenMarkers,
            visibleProps,
            groundedProps,
            maximumError,
            reliefRoot.GetChildCount(),
            torchAssemblies,
            torchPostLayers,
            sprites.Count);
    }

    private static Fo2FrmReliefMeshSet GetOrBuildReliefMesh(
        IDictionary<ReliefMeshKey, Fo2FrmReliefMeshSet> cache,
        string artifactId,
        string sourcePngPath,
        int width,
        int height,
        Vector2I sourcePixelOffset,
        float sourcePixelsPerMeter,
        float depthMeters,
        float sideRoughness,
        Fo2FrmReliefArtifact relief,
        bool sourcePixelsOnly = false)
    {
        var key = new ReliefMeshKey(
            artifactId,
            sourcePixelOffset,
            sourcePixelsPerMeter,
            depthMeters,
            sideRoughness,
            sourcePixelsOnly);
        if (cache.TryGetValue(key, out var existing))
            return existing;
        var built = Fo2FrmReliefMesh.Build(
            sourcePngPath,
            width,
            height,
            sourcePixelOffset,
            sourcePixelsPerMeter,
            depthMeters,
            sideRoughness,
            relief,
            sourcePixelsOnly);
        cache.Add(key, built);
        return built;
    }

    private static float SeatReliefOnFloor(
        Node3D relief,
        Fo2FrmReliefMeshSet meshSet,
        float floor)
    {
        var faceBounds = meshSet.Faces.GetAabb();
        var sideBounds = meshSet.Sides?.GetAabb() ?? faceBounds;
        var localBottom = MathF.Min(
            faceBounds.Position.Y,
            sideBounds.Position.Y) + meshSet.LocalOffsetMeters.Y;
        relief.Position += Vector3.Up * (floor - (relief.Position.Y + localBottom));
        return MathF.Abs(relief.Position.Y + localBottom - floor);
    }

    private static SourceMapLightCoverage BuildSourceMapLights(
        Node3D parent,
        IReadOnlyList<Fo2MapObjectPlacement> placements,
        Fo2ArroyoCaves3DProfile profile)
    {
        var source = profile.Atmosphere.SourceMapLights;
        var lights = placements.Where(row =>
                row.LightDistance != 0 || row.LightIntensity != 0)
            .OrderBy(row => row.Serial)
            .ToArray();
        var torches = placements.Where(row =>
                profile.TorchLogicalPaths.Contains(row.LogicalPath.ToLowerInvariant()))
            .OrderBy(row => row.Serial)
            .ToArray();
        if (lights.Length != source.ExpectedRecords ||
            lights.Any(row =>
                row.LogicalPath.ToLowerInvariant() != source.LogicalPath ||
                row.Fid != source.Fid ||
                row.ObjectType != source.ObjectType ||
                row.LightDistance != source.ExpectedDistance ||
                row.LightIntensity != source.ExpectedIntensity))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo exact MAP light record coverage drifted.");

        var torchByLight = new Dictionary<int, Fo2MapObjectPlacement>();
        foreach (var torch in torches)
        {
            var neighbors = lights.Where(light =>
                    Fo1HexMath.Distance(light.Tile, torch.Tile) == 1)
                .ToArray();
            if (neighbors.Length != 1 || torchByLight.ContainsKey(neighbors[0].Serial))
                throw new InvalidOperationException(
                    $"Fallout 2 Arroyo torch/source-light join drifted: {torch.Serial}.");
            torchByLight.Add(neighbors[0].Serial, torch);
        }
        if (torchByLight.Count != profile.SourceCoverage.SourceTorchProps)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo torch-motivated source-light coverage drifted.");

        var root = new Node3D { Name = "MAP3_EXACT_SOURCE_LIGHT_FIELDS" };
        parent.AddChild(root);
        foreach (var placement in lights)
        {
            var center = Fo1HexMath.Center(placement.Tile);
            var anchor = new Node3D
            {
                Name = $"SOURCE_MAP_LIGHT_{placement.Serial}",
                Position = center + Vector3.Up *
                    (FloorHeight(center, profile.FloorGeometry) +
                        Fo1HexMath.CircumradiusMeters),
            };
            anchor.SetMeta("fo2_map_serial", placement.Serial);
            anchor.SetMeta("fo2_map_tile", placement.Tile);
            anchor.SetMeta("fo2_source_light_distance", placement.LightDistance);
            anchor.SetMeta("fo2_source_light_intensity", placement.LightIntensity);
            anchor.SetMeta("fo2_source_light_vertical_projection", source.VerticalProjectionMode);
            if (torchByLight.TryGetValue(placement.Serial, out var torch))
                anchor.SetMeta("fo2_source_torch_serial", torch.Serial);
            root.AddChild(anchor);
            anchor.AddChild(new OmniLight3D
            {
                Name = "SOURCE_MAP_LIGHT_FIELD",
                LightEnergy = (float)placement.LightIntensity /
                    source.IntensityFixedPointOne,
                OmniRange = placement.LightDistance * Fo1HexMath.FlatToFlatMeters,
                ShadowEnabled = false,
            });
        }
        if (root.GetChildCount() != lights.Length ||
            root.GetChildren().OfType<Node3D>().Any(anchor =>
                anchor.GetChildren().OfType<OmniLight3D>().Count() != 1))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo source MAP light instantiation drifted.");
        return new SourceMapLightCoverage(
            lights.Length,
            root.GetChildCount(),
            torchByLight.Count);
    }

    private static void BuildEnvironment(Node3D root, Fo2ArroyoCaves3DProfile profile)
    {
        var atmosphere = profile.Atmosphere;
        root.AddChild(new WorldEnvironment
        {
            Name = "MAP3_RECIPE_WORLD_ENVIRONMENT",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = atmosphere.BackgroundColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = atmosphere.AmbientColor,
                AmbientLightEnergy = atmosphere.AmbientEnergy,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                TonemapExposure = atmosphere.TonemapExposure,
                FogEnabled = true,
                FogLightColor = atmosphere.FogColor,
                FogLightEnergy = atmosphere.FogLightEnergy,
                FogDensity = atmosphere.FogDensity,
                FogAerialPerspective = atmosphere.FogAerialPerspective,
                FogSkyAffect = atmosphere.FogSkyAffect,
                VolumetricFogEnabled = true,
                VolumetricFogDensity = atmosphere.VolumetricFogDensity,
                VolumetricFogAlbedo = atmosphere.VolumetricFogAlbedo,
                VolumetricFogEmission = atmosphere.VolumetricFogEmission,
                VolumetricFogEmissionEnergy = atmosphere.VolumetricFogEmissionEnergy,
                VolumetricFogLength = atmosphere.VolumetricFogLengthMeters,
                VolumetricFogDetailSpread = atmosphere.VolumetricFogDetailSpread,
                VolumetricFogAmbientInject = atmosphere.VolumetricFogAmbientInject,
                VolumetricFogSkyAffect = atmosphere.VolumetricFogSkyAffect,
            },
        });
        root.AddChild(new DirectionalLight3D
        {
            Name = "MAP3_RECIPE_DIRECTIONAL_LIGHT",
            RotationDegrees = atmosphere.DirectionalLight.RotationDegrees,
            LightColor = atmosphere.DirectionalLight.Color,
            LightEnergy = atmosphere.DirectionalLight.Energy,
            ShadowEnabled = atmosphere.DirectionalLight.ShadowEnabled,
        });
    }

    private static ShaderMaterial BuildRockMaterial(
        Fo2ArroyoMaterialProfile materials,
        Fo2ArroyoRockMaterialProfile rock,
        string sourceTexturePath,
        string sourceNormalTexturePath)
    {
        var shader = new Shader { Code = RockShader };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("dark_color", rock.Dark);
        material.SetShaderParameter("light_color", rock.Light);
        material.SetShaderParameter("world_scale", materials.WorldScale);
        material.SetShaderParameter("roughness_value", rock.Roughness);
        material.SetShaderParameter("normal_strength", materials.NormalStrength);
        material.SetShaderParameter("ambient_lift", rock.AmbientLift);
        material.SetShaderParameter(
            "source_detail_world_scale",
            materials.SourceDetailWorldScale);
        material.SetShaderParameter(
            "source_detail_mix",
            materials.SourceDetailMix);
        material.SetShaderParameter(
            "macro_detail_world_scale",
            materials.MacroDetailWorldScale);
        material.SetShaderParameter(
            "macro_detail_mix",
            materials.MacroDetailMix);
        var image = Image.LoadFromFile(sourceTexturePath);
        var normalImage = Image.LoadFromFile(sourceNormalTexturePath);
        if (image.IsEmpty() || normalImage.IsEmpty() || image.GetSize() != normalImage.GetSize())
            throw new InvalidOperationException(
                "Fallout 2 Arroyo owned albedo/normal surface did not decode.");
        material.SetShaderParameter(
            "source_surface_albedo",
            ImageTexture.CreateFromImage(image));
        material.SetShaderParameter(
            "source_surface_normal",
            ImageTexture.CreateFromImage(normalImage));
        return material;
    }

    private static List<HashSet<int>> ConnectedComponents(IReadOnlySet<int> occupied)
    {
        var result = new List<HashSet<int>>();
        var visited = new HashSet<int>();
        foreach (var start in occupied.Order())
        {
            if (!visited.Add(start))
                continue;
            var component = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
                foreach (var neighbor in Fo1HexMath.Neighbors(queue.Dequeue()))
                    if (occupied.Contains(neighbor) && visited.Add(neighbor))
                    {
                        component.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
            result.Add(component);
        }
        return result;
    }

    private static int BoundaryEdges(IReadOnlySet<int> occupied) =>
        occupied.Sum(tile => Enumerable.Range(0, Fo1HexMath.DirectionCount)
            .Count(edge => !occupied.Contains(Fo1HexMath.NeighborAcrossEdge(tile, edge))));

    private static Vector2I SdfKey(Vector2 position) => new(
        Mathf.RoundToInt(position.X * VertexPrecision),
        Mathf.RoundToInt(position.Y * VertexPrecision));

    private readonly record struct SdfEdgeKey(Vector2I First, Vector2I Second)
    {
        internal static SdfEdgeKey Create(Vector2I first, Vector2I second) =>
            first.X < second.X || first.X == second.X && first.Y <= second.Y
                ? new SdfEdgeKey(first, second)
                : new SdfEdgeKey(second, first);
    }

    private sealed record ContourEdge(Vector2 First, Vector2 Second, int Count);

    private sealed record SourceWallSegment(Vector2 First, Vector2 Second);

    private sealed record StonePostCoverage(int Instances, int Triangles);

    private sealed record PropCoverage(
        int HiddenWallCards,
        int HiddenNonWallBlockCards,
        int HiddenSourceMarkerCards,
        int VisibleSourceProps,
        int GroundedSourceProps,
        float MaximumGroundErrorMeters,
        int ReliefVolumes,
        int SourceTorchAssemblies,
        int SourceTorchPostLayeredAssemblies,
        int HiddenSourceSpriteCards);

    private sealed record SourceMapLightCoverage(
        int Records,
        int Lights,
        int TorchMotivatedLights);

    private sealed class IndexedMeshBuilder
    {
        private readonly Dictionary<Vector3I, int> _lookup = [];
        private readonly List<Vector3> _vertices = [];
        private readonly List<Vector3> _normalSums = [];
        private readonly List<int> _indices = [];

        internal void AddTriangle(Vector3 first, Vector3 second, Vector3 third)
        {
            var normal = (second - first).Cross(third - first);
            if (normal.IsZeroApprox())
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo Caves molded presentation produced a degenerate triangle.");
            foreach (var position in new[] { first, second, third })
            {
                var index = AddVertex(position);
                _indices.Add(index);
                _normalSums[index] += normal;
            }
        }

        internal ArrayMesh Commit(string label)
        {
            if (_indices.Count == 0 || _indices.Count % 3 != 0)
                throw new InvalidOperationException($"{label} has no complete triangles.");
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = _normalSums
                .Select(value => value.Normalized()).ToArray();
            arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            if (mesh.GetFaces().Length != _indices.Count)
                throw new InvalidOperationException($"{label} indexed coverage drifted.");
            return mesh;
        }

        private int AddVertex(Vector3 position)
        {
            var key = new Vector3I(
                Mathf.RoundToInt(position.X * VertexPrecision),
                Mathf.RoundToInt(position.Y * VertexPrecision),
                Mathf.RoundToInt(position.Z * VertexPrecision));
            if (_lookup.TryGetValue(key, out var index))
                return index;
            index = _vertices.Count;
            _lookup.Add(key, index);
            _vertices.Add(position);
            _normalSums.Add(Vector3.Zero);
            return index;
        }
    }
}
