using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArvillagArrivalFraming(
    string Mode,
    IReadOnlyList<int> SourceObjectSerials,
    IReadOnlyList<int> SourceObjectTiles,
    Aabb RouteAndObjectBoundsMeters,
    Vector3 FocusWorldMeters,
    float PaddingFraction);

internal sealed record Fo2ArvillagSceneCoverage(
    Node3D Root,
    string ManifestPath,
    string ManifestSha256,
    string SourceManifestPath,
    string SourceManifestSha256,
    string SourceProfileId,
    string MapSha256,
    int MapIndex,
    int Elevation,
    int ArrivalTile,
    int ArrivalRotation,
    Vector3 ArrivalWorldMeters,
    string WalkMaskSha256,
    int WalkableHexes,
    IReadOnlySet<int> AdmittedArrivalTiles,
    int VerifiedResources,
    int VerifiedArtifacts,
    int ConstructedFloorPatches,
    int SourceRoofPatches,
    bool RoofCutaway,
    int PlacedTopLevelObjects,
    int ReliefPlacements,
    int TransparentPlacements,
    int HiddenSpriteCards,
    int ReliefMeshVariants,
    int ReliefTriangles,
    int SourceMapLightRecords,
    int SourceMapLights,
    int FloorMaterialDepthMeshes,
    int MoldedFloorTriangles,
    float MoldedFloorBoundaryHeightMeters,
    float MoldedFloorHeightScale,
    float MoldedFloorNormalScale,
    float MoldedFloorSourceDetailMix,
    float MoldedFloorAlbedoScale,
    float ObjectReliefDepthScale,
    float ObjectReliefNormalScale,
    string ObjectTwoSidedLightingMode,
    float ObjectBacklightStrength,
    Fo2ArvillagArrivalFraming ArrivalFraming,
    IReadOnlyDictionary<int, float> MoldedFloorHeightByTile,
    Fo2ArroyoCaves3DProfile PresentationProfile,
    string PresentationProfilePath,
    string PresentationProfileSha256,
    int FloorCollisionTriangles,
    float SourcePixelsPerMeter);

internal static class Fo2ArvillagScene
{
    private const int SourceLightIntensityFixedPointOne = 65536;
    private const string SourceLightProjectionMode =
        "source-tile-center-one-hex-circumradius-height-v1";

    private readonly record struct ReliefMeshKey(
        string ArtifactId,
        Vector2I PixelOffset,
        float DepthMeters);

    private sealed record FloorMoldedCoverage(
        int Meshes,
        int Triangles,
        float BoundaryHeightMeters,
        IReadOnlyDictionary<int, float> HeightByTile);

    private sealed record SourceObjectBounds(
        Fo2ArvillagReliefPlacement Placement,
        Aabb BoundsMeters);

    internal static Fo2ArvillagSceneCoverage Build(
        Fo2ArvillagPresentationCatalog catalog,
        Fo2ArroyoCaves3DProfile presentationProfile,
        Node3D parent)
    {
        var source = Fo2MapSceneBuilder.Build(
            parent,
            Fo2ArvillagPresentationCatalog.MapIndex,
            "ARVILLAG.MAP",
            catalog.MapSha256,
            Fo2ArvillagPresentationCatalog.Elevation,
            catalog.ArrivalTile,
            catalog.ArrivalRotation,
            Fo2ArvillagPresentationCatalog.DefaultFloorTileId,
            catalog.TileEntries,
            catalog.Artifacts,
            catalog.TileBindings,
            catalog.ObjectPlacements,
            allowOwnedRoofCutaway: true);
        if (source.ConstructedRoofPatches != catalog.SourceRoofPatches)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG source roof-cutaway coverage drifted.");
        source.Root.SetMeta("cache_manifest_sha256", catalog.ManifestSha256);
        source.Root.SetMeta("source_manifest_sha256", catalog.SourceManifestSha256);
        source.Root.SetMeta("source_walk_mask_sha256", catalog.WalkMaskSha256);
        source.Root.SetMeta("walkable_hexes", catalog.WalkableHexes);
        source.Root.SetMeta("destination_presentation_loaded", true);
        source.Root.SetMeta("presentation_profile", presentationProfile.ResourcePath);
        source.Root.SetMeta("presentation_profile_sha256", presentationProfile.Sha256);
        source.Root.SetMeta(
            "presentation_lighting_boundary",
            "existing-versioned-classic-3d-atmosphere-adaptation-not-retail-parity");
        source.Root.SetMeta(
            "roof_cutaway_boundary",
            "owned-map-frm-source-without-accepted-3d-height-contract");

        var sourceSprites = Descendants<Sprite3D>(source.Root).ToArray();
        if (sourceSprites.Length != catalog.ObjectPlacements.Count)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG source sprite placement coverage drifted.");
        foreach (var sprite in sourceSprites)
            sprite.Visible = false;
        var floorMaterialDepth = ApplyFloorMaterialDepth(
            source.Root,
            catalog,
            source.SourcePixelsPerMeter,
            presentationProfile.VillageMoldedSurface);

        var reliefRoot = new Node3D
        {
            Name = "MAP4_EXACT_SOURCE_OBJECT_CLOSED_RELIEF_VOLUMES",
        };
        source.Root.AddChild(reliefRoot);
        var meshCache = new Dictionary<ReliefMeshKey, Fo2FrmReliefMeshSet>();
        var sourceObjectBounds = new List<SourceObjectBounds>();
        var triangleCount = 0;
        foreach (var placement in catalog.ReliefPlacements.OrderBy(row => row.Serial))
        {
            var offset = placement.PixelOffset + placement.Artifact.FrameOffset;
            var key = new ReliefMeshKey(
                placement.Artifact.Id,
                offset,
                placement.DepthMeters *
                    presentationProfile.VillageMoldedSurface.ObjectDepthScale);
            if (!meshCache.TryGetValue(key, out var meshSet))
            {
                meshSet = Fo2FrmReliefMesh.Build(
                    placement.Artifact.Path,
                    placement.Artifact.Width,
                    placement.Artifact.Height,
                    offset,
                    source.SourcePixelsPerMeter,
                    placement.DepthMeters *
                        presentationProfile.VillageMoldedSurface.ObjectDepthScale,
                    catalog.SideRoughness,
                    placement.Relief,
                    sourcePixelsOnly: false);
                meshCache.Add(key, meshSet);
                triangleCount += meshSet.FaceTriangles + meshSet.SideTriangles;
            }
            meshSet.FaceMaterial.NormalScale =
                presentationProfile.VillageMoldedSurface.ObjectNormalScale;
            ApplyOwnedTwoSidedLighting(
                meshSet.FaceMaterial,
                placement.Relief,
                presentationProfile.VillageMoldedSurface);
            var relief = Fo2FrmReliefMesh.Instantiate(
                $"SOURCE_OBJECT_{placement.Serial}_CLOSED_RELIEF",
                meshSet);
            relief.Position = Fo1HexMath.Center(placement.Tile);
            relief.RotationDegrees = new Vector3(
                0.0f,
                -placement.Rotation * 360.0f / Fo1HexMath.DirectionCount,
                0.0f);
            SeatOnSourceFloor(
                relief,
                meshSet,
                floorMaterialDepth.HeightByTile[placement.Tile]);
            relief.SetMeta("fo2_map_serial", placement.Serial);
            relief.SetMeta("fo2_map_tile", placement.Tile);
            relief.SetMeta("fo2_map_rotation", placement.Rotation);
            relief.SetMeta("fo2_source_frame", placement.Frame);
            relief.SetMeta("fo2_source_pixel_offset", placement.PixelOffset);
            relief.SetMeta("fo2_source_fid", placement.Fid);
            relief.SetMeta("fo2_source_pid", placement.Pid);
            relief.SetMeta("fo2_source_object_type", placement.ObjectType);
            relief.SetMeta("fo2_source_logical_path", placement.LogicalPath);
            relief.SetMeta("fo2_source_png_sha256", placement.Artifact.PngSha256);
            relief.SetMeta("fo2_geometry_mode", placement.Relief.Mode);
            relief.SetMeta(
                "fo2_lighting_mode",
                "owned-frm-normal-depth-plus-exact-map-light-distance-intensity-v1");
            relief.SetMeta("fo2_visual_parity", false);
            reliefRoot.AddChild(relief);
            sourceObjectBounds.Add(new SourceObjectBounds(
                placement,
                TransformedReliefBounds(relief, meshSet)));
        }
        if (reliefRoot.GetChildCount() != catalog.ReliefPlacements.Count ||
            sourceSprites.Any(sprite => sprite.Visible))
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG closed-relief scene coverage drifted.");

        var sourceMapLights = BuildSourceMapLights(
            source.Root,
            catalog.ObjectPlacements,
            source.SourcePixelsPerMeter);
        BuildPresentationEnvironment(source.Root, presentationProfile);
        var arrivalFraming = BuildArrivalFraming(
            catalog,
            floorMaterialDepth.HeightByTile,
            sourceObjectBounds,
            presentationProfile.VillageMoldedSurface);

        var floorIds = catalog.TileEntries
            .Select(entry => (int)(entry & 0x0fff))
            .ToArray();
        var floorPatches = Enumerable.Range(0, floorIds.Length)
            .Where(index =>
                floorIds[index] != Fo2ArvillagPresentationCatalog.DefaultFloorTileId)
            .ToArray();
        if (floorPatches.Length != source.ConstructedFloorPatches)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG source floor-support coverage drifted.");
        var floorMesh = Fo2ArroyoCavesPlayerRuntime.BuildFloorCollisionMesh(floorPatches);
        var floorShape = floorMesh.CreateTrimeshShape() ??
            throw new InvalidOperationException(
                "Could not build Fallout 2 ARVILLAG source floor support.");
        if (floorShape is ConcavePolygonShape3D concave)
            concave.BackfaceCollision = true;
        var floorBody = new StaticBody3D
        {
            Name = "MAP4_NON_DEFAULT_SOURCE_FLOOR_PATCH_SUPPORT",
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        floorBody.SetMeta("source_map_sha256", catalog.MapSha256);
        floorBody.SetMeta("source_walk_mask_sha256", catalog.WalkMaskSha256);
        floorBody.AddChild(new CollisionShape3D
        {
            Name = "MAP4_SOURCE_FLOOR_PATCH_TRIMESH_COLLISION",
            Shape = floorShape,
        });
        source.Root.AddChild(floorBody);

        return new Fo2ArvillagSceneCoverage(
            source.Root,
            catalog.ManifestPath,
            catalog.ManifestSha256,
            catalog.SourceManifestPath,
            catalog.SourceManifestSha256,
            catalog.SourceProfileId,
            catalog.MapSha256,
            source.MapIndex,
            source.Elevation,
            source.ArrivalTile,
            source.ArrivalRotation,
            source.ArrivalWorldMeters,
            catalog.WalkMaskSha256,
            catalog.WalkableHexes,
            catalog.AdmittedArrivalTiles,
            catalog.VerifiedResources,
            source.VerifiedArtifacts,
            source.ConstructedFloorPatches,
            source.ConstructedRoofPatches,
            true,
            source.PlacedTopLevelObjects,
            catalog.ReliefPlacements.Count,
            catalog.TransparentPlacements,
            sourceSprites.Length,
            meshCache.Count,
            triangleCount,
            sourceMapLights.Records,
            sourceMapLights.Lights,
            floorMaterialDepth.Meshes,
            floorMaterialDepth.Triangles,
            floorMaterialDepth.BoundaryHeightMeters,
            presentationProfile.VillageMoldedSurface.FloorHeightScale,
            presentationProfile.VillageMoldedSurface.FloorNormalScale,
            presentationProfile.VillageMoldedSurface.FloorSourceDetailMix,
            presentationProfile.VillageMoldedSurface.FloorAlbedoScale,
            presentationProfile.VillageMoldedSurface.ObjectDepthScale,
            presentationProfile.VillageMoldedSurface.ObjectNormalScale,
            presentationProfile.VillageMoldedSurface.ObjectTwoSidedLightingMode,
            presentationProfile.VillageMoldedSurface.ObjectBacklightStrength,
            arrivalFraming,
            floorMaterialDepth.HeightByTile,
            presentationProfile,
            presentationProfile.ResourcePath,
            presentationProfile.Sha256,
            floorPatches.Length * 2,
            source.SourcePixelsPerMeter);
    }

    private static void ApplyOwnedTwoSidedLighting(
        StandardMaterial3D material,
        Fo2FrmReliefArtifact relief,
        Fo2ArroyoVillageMoldedSurfaceProfile presentation)
    {
        var sourceColor = relief.AverageOpaqueColor;
        var maximumChannel = MathF.Max(
            sourceColor.R,
            MathF.Max(sourceColor.G, sourceColor.B));
        if (maximumChannel <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 2 owned FRM backlight color is empty.");
        material.BacklightEnabled = true;
        material.DiffuseMode = BaseMaterial3D.DiffuseModeEnum.LambertWrap;
        material.Backlight = new Color(
            sourceColor.R / maximumChannel * presentation.ObjectBacklightStrength,
            sourceColor.G / maximumChannel * presentation.ObjectBacklightStrength,
            sourceColor.B / maximumChannel * presentation.ObjectBacklightStrength,
            1.0f);
    }

    private static Fo2ArvillagArrivalFraming BuildArrivalFraming(
        Fo2ArvillagPresentationCatalog catalog,
        IReadOnlyDictionary<int, float> floorHeightByTile,
        IReadOnlyList<SourceObjectBounds> objectBounds,
        Fo2ArroyoVillageMoldedSurfaceProfile presentation)
    {
        var selected = objectBounds
            .Select(row => new
            {
                Row = row,
                Distance = Math.Min(
                    Fo1HexMath.Distance(catalog.ArrivalTile, row.Placement.Tile),
                    Fo1HexMath.Distance(catalog.FirstActionTile, row.Placement.Tile)),
            })
            .Where(row => row.Distance <= presentation.ArrivalMaximumObjectHexDistance)
            .OrderBy(row => row.Distance)
            .ThenBy(row => row.Row.Placement.Serial)
            .Take(presentation.ArrivalNearestVisibleObjectCount)
            .Select(row => row.Row)
            .ToArray();
        if (selected.Length != presentation.ArrivalNearestVisibleObjectCount ||
            !floorHeightByTile.ContainsKey(catalog.ArrivalTile) ||
            !floorHeightByTile.ContainsKey(catalog.FirstActionTile))
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG route-derived arrival framing is incomplete.");
        var routePoints = new[] { catalog.ArrivalTile, catalog.FirstActionTile }
            .SelectMany(tile => Fo1HexMath.Corners(tile).Append(Fo1HexMath.Center(tile))
                .Select(point => point + Vector3.Up * floorHeightByTile[tile]))
            .ToArray();
        var bounds = BoundsForPoints(routePoints);
        foreach (var sourceObject in selected)
            bounds = bounds.Merge(sourceObject.BoundsMeters);
        if (bounds.Size.X <= 0.0f || bounds.Size.Y <= 0.0f || bounds.Size.Z <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG arrival framing bounds are degenerate.");
        return new Fo2ArvillagArrivalFraming(
            presentation.ArrivalFramingMode,
            selected.Select(row => row.Placement.Serial).ToArray(),
            selected.Select(row => row.Placement.Tile).ToArray(),
            bounds,
            bounds.GetCenter(),
            presentation.ArrivalBoundsPaddingFraction);
    }

    private static Aabb TransformedReliefBounds(
        Node3D relief,
        Fo2FrmReliefMeshSet meshSet)
    {
        var local = meshSet.Sides is null
            ? meshSet.Faces.GetAabb()
            : meshSet.Faces.GetAabb().Merge(meshSet.Sides.GetAabb());
        local.Position += meshSet.LocalOffsetMeters;
        return BoundsForPoints(BoundsCorners(local).Select(point => relief.Transform * point));
    }

    private static Aabb BoundsForPoints(IEnumerable<Vector3> source)
    {
        var points = source.ToArray();
        if (points.Length == 0)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG framing has no source-derived points.");
        var minimum = points.Aggregate((left, right) => left.Min(right));
        var maximum = points.Aggregate((left, right) => left.Max(right));
        return new Aabb(minimum, maximum - minimum);
    }

    internal static IEnumerable<Vector3> BoundsCorners(Aabb bounds)
    {
        var end = bounds.End;
        yield return new Vector3(bounds.Position.X, bounds.Position.Y, bounds.Position.Z);
        yield return new Vector3(end.X, bounds.Position.Y, bounds.Position.Z);
        yield return new Vector3(bounds.Position.X, end.Y, bounds.Position.Z);
        yield return new Vector3(end.X, end.Y, bounds.Position.Z);
        yield return new Vector3(bounds.Position.X, bounds.Position.Y, end.Z);
        yield return new Vector3(end.X, bounds.Position.Y, end.Z);
        yield return new Vector3(bounds.Position.X, end.Y, end.Z);
        yield return end;
    }

    private static void SeatOnSourceFloor(
        Node3D relief,
        Fo2FrmReliefMeshSet meshSet,
        float sourceFloorHeightMeters)
    {
        var faceBounds = meshSet.Faces.GetAabb();
        var sideBounds = meshSet.Sides?.GetAabb() ?? faceBounds;
        var localBottom = MathF.Min(
            faceBounds.Position.Y,
            sideBounds.Position.Y) + meshSet.LocalOffsetMeters.Y;
        relief.Position += Vector3.Up * (sourceFloorHeightMeters - localBottom);
        if (MathF.Abs(
                relief.Position.Y + localBottom - sourceFloorHeightMeters) > 0.00001f)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG source prop grounding drifted.");
    }

    private static (int Records, int Lights) BuildSourceMapLights(
        Node3D parent,
        IReadOnlyList<Fo2MapObjectPlacement> placements,
        float sourcePixelsPerMeter)
    {
        var sourceLights = placements
            .Where(row => row.LightDistance != 0 || row.LightIntensity != 0)
            .OrderBy(row => row.Serial)
            .ToArray();
        if (sourceLights.Length == 0 || sourceLights.Any(row =>
                row.Elevation != Fo2ArvillagPresentationCatalog.Elevation ||
                row.LightDistance <= 0 || row.LightIntensity <= 0 ||
                row.LightIntensity % SourceLightIntensityFixedPointOne != 0))
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG exact MAP light fields are incomplete.");
        var root = new Node3D { Name = "MAP4_EXACT_SOURCE_LIGHT_FIELDS" };
        parent.AddChild(root);
        foreach (var placement in sourceLights)
        {
            var anchor = new Node3D
            {
                Name = $"SOURCE_MAP_LIGHT_{placement.Serial}",
                Position = Fo1HexMath.Center(placement.Tile) +
                    Vector3.Up * Fo1HexMath.CircumradiusMeters,
            };
            anchor.SetMeta("fo2_map_serial", placement.Serial);
            anchor.SetMeta("fo2_map_tile", placement.Tile);
            anchor.SetMeta("fo2_source_light_distance", placement.LightDistance);
            anchor.SetMeta("fo2_source_light_intensity", placement.LightIntensity);
            anchor.SetMeta("fo2_source_light_intensity_unit", SourceLightIntensityFixedPointOne);
            anchor.SetMeta("fo2_source_light_vertical_projection", SourceLightProjectionMode);
            root.AddChild(anchor);
            anchor.AddChild(new OmniLight3D
            {
                Name = "SOURCE_MAP_LIGHT_FIELD",
                LightEnergy = (float)placement.LightIntensity /
                    SourceLightIntensityFixedPointOne,
                OmniRange = placement.LightDistance * Fo1HexMath.FlatToFlatMeters,
                ShadowEnabled = false,
            });
        }
        if (root.GetChildCount() != sourceLights.Length ||
            root.GetChildren().OfType<Node3D>().Any(anchor =>
                anchor.GetChildren().OfType<OmniLight3D>().Count() != 1))
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG source MAP light instantiation drifted.");
        parent.SetMeta("source_map_light_records", sourceLights.Length);
        parent.SetMeta("source_map_lights", root.GetChildCount());
        parent.SetMeta("source_map_light_pixels_per_meter", sourcePixelsPerMeter);
        return (sourceLights.Length, root.GetChildCount());
    }

    private static FloorMoldedCoverage ApplyFloorMaterialDepth(
        Node3D root,
        Fo2ArvillagPresentationCatalog catalog,
        float sourcePixelsPerMeter,
        Fo2ArroyoVillageMoldedSurfaceProfile presentation)
    {
        var floorIds = catalog.TileEntries
            .Select(entry => (int)(entry & 0x0fff))
            .ToArray();
        var floorMeshes = Descendants<MultiMeshInstance3D>(root)
            .Where(row => row.HasMeta("source_floor_tile_id"))
            .OrderBy(row => row.GetMeta("source_floor_tile_id").AsInt32())
            .ToArray();
        if (floorMeshes.Length != catalog.FloorMaterialDepth.Count)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG floor material mesh coverage drifted.");
        var sharedVertexHeights = BuildSharedFloorVertexHeights(
            catalog,
            floorIds,
            sourcePixelsPerMeter,
            presentation.FloorHeightScale,
            presentation.FloorHeightNeighborhoodRadius);
        var smoothedFloorColors = BuildSmoothedFloorColors(
            catalog,
            floorIds,
            presentation.FloorColorNeighborhoodRadius);
        var meanSharedVertexHeightMeters = sharedVertexHeights.Average();
        var heightByFloorIndex = new Dictionary<int, float>();
        var triangleCount = 0;
        foreach (var instance in floorMeshes)
        {
            var tileId = instance.GetMeta("source_floor_tile_id").AsInt32();
            if (!catalog.FloorMaterialDepth.TryGetValue(tileId, out var source) ||
                instance.GetMeta("source_floor_artifact_id").AsString() !=
                    source.ArtifactId ||
                !catalog.Artifacts.TryGetValue(source.ArtifactId, out var artifact) ||
                instance.Multimesh?.Mesh is not PlaneMesh plane ||
                plane.Material is not StandardMaterial3D material)
                throw new InvalidOperationException(
                    $"Fallout 2 ARVILLAG floor material join drifted: {tileId}.");
            var normalImage = Image.LoadFromFile(source.Relief.NormalPngPath);
            if (normalImage.IsEmpty() ||
                normalImage.GetWidth() != material.AlbedoTexture?.GetWidth() ||
                normalImage.GetHeight() != material.AlbedoTexture?.GetHeight())
                throw new InvalidOperationException(
                    $"Fallout 2 ARVILLAG floor normal dimensions drifted: {tileId}.");
            var floorMaterial = BuildContinuousFloorMaterial(
                material.AlbedoTexture,
                ImageTexture.CreateFromImage(normalImage),
                catalog.FloorNormalScale * presentation.FloorNormalScale,
                catalog.SideRoughness,
                presentation.FloorSourceDetailMix,
                presentation.FloorAlbedoScale);
            var molded = BuildMoldedFloorPatch(
                source,
                artifact,
                sourcePixelsPerMeter,
                floorMaterial,
                presentation.FloorHeightScale);
            var indices = Enumerable.Range(0, floorIds.Length)
                .Where(index => floorIds[index] == tileId)
                .ToArray();
            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                UseColors = true,
                Mesh = molded.Mesh,
                InstanceCount = indices.Length,
            };
            for (var index = 0; index < indices.Length; index++)
            {
                var floorIndex = indices[index];
                var floorX = Fo1HexMath.FloorWidth - 1 -
                    floorIndex % Fo1HexMath.FloorWidth;
                var floorY = floorIndex / Fo1HexMath.FloorWidth;
                var corners = FloorCornerHeights(
                    sharedVertexHeights,
                    floorX,
                    floorY);
                multiMesh.SetInstanceTransform(
                    index,
                    new Transform3D(
                        Basis.Identity,
                        Fo1HexMath.FloorPatchCenter(floorIndex)));
                multiMesh.SetInstanceCustomData(
                    index,
                    new Color(
                        corners.TopLeft,
                        corners.TopRight,
                        corners.BottomLeft,
                        corners.BottomRight));
                multiMesh.SetInstanceColor(index, smoothedFloorColors[floorIndex]);
                heightByFloorIndex.Add(
                    floorIndex,
                    corners.Center + molded.CenterHeightMeters);
            }
            instance.Multimesh = multiMesh;
            instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            instance.SetMeta("source_floor_material_depth_mode", source.Relief.Mode);
            instance.SetMeta(
                "source_floor_material_normal_sha256",
                source.Relief.NormalPngSha256);
            instance.SetMeta("source_floor_material_visual_parity", false);
            instance.SetMeta("source_floor_molded_triangles", molded.Triangles);
            instance.SetMeta(
                "source_floor_shared_vertex_height_mode",
                "adjacent-owned-map-floor-average-luma-v1");
            triangleCount += molded.Triangles;
        }
        var heightByTile = new Dictionary<int, float>();
        for (var index = 0; index < floorIds.Length; index++)
        {
            var floorX = Fo1HexMath.FloorWidth - 1 - index % Fo1HexMath.FloorWidth;
            var floorY = index / Fo1HexMath.FloorWidth;
            var height = floorIds[index] ==
                    Fo2ArvillagPresentationCatalog.DefaultFloorTileId
                ? 0.0f
                : heightByFloorIndex[index];
            for (var offsetY = 0; offsetY < 2; offsetY++)
                for (var offsetX = 0; offsetX < 2; offsetX++)
                    heightByTile.Add(
                        Fo1HexMath.Tile(new Vector2I(
                            floorX * 2 + offsetX,
                            floorY * 2 + offsetY)),
                        height);
        }
        root.SetMeta("source_floor_material_depth_meshes", floorMeshes.Length);
        root.SetMeta("source_floor_molded_triangles", triangleCount);
        root.SetMeta(
            "source_floor_molded_boundary_height_meters",
            meanSharedVertexHeightMeters);
        root.SetMeta(
            "source_floor_molded_height_authority",
            "continuous-owned-map-topology-shared-luma-vertices-plus-local-frm-normal-depth");
        root.SetMeta("source_floor_collision_unchanged", true);
        return new FloorMoldedCoverage(
            floorMeshes.Length,
            triangleCount,
            meanSharedVertexHeightMeters,
            heightByTile);
    }

    private readonly record struct FloorCornerHeight(
        float TopLeft,
        float TopRight,
        float BottomLeft,
        float BottomRight)
    {
        internal float Center =>
            (TopLeft + TopRight + BottomLeft + BottomRight) / 4.0f;
    }

    private static float[] BuildSharedFloorVertexHeights(
        Fo2ArvillagPresentationCatalog catalog,
        IReadOnlyList<int> floorIds,
        float sourcePixelsPerMeter,
        float heightScale,
        int neighborhoodRadius)
    {
        var centerHeights = new float?[floorIds.Count];
        for (var index = 0; index < floorIds.Count; index++)
        {
            var tileId = floorIds[index];
            if (tileId == Fo2ArvillagPresentationCatalog.DefaultFloorTileId)
                continue;
            if (!catalog.FloorMaterialDepth.TryGetValue(tileId, out var source))
                throw new InvalidOperationException(
                    $"Fallout 2 ARVILLAG floor height source is missing: {tileId}.");
            centerHeights[index] = SourceLuminance(source.Relief.AverageOpaqueColor) *
                source.Relief.MaximumInteriorDistancePixels / sourcePixelsPerMeter *
                heightScale;
        }

        var smoothedCenterHeights = SmoothFloorScalarField(
            centerHeights,
            neighborhoodRadius);
        var stride = Fo1HexMath.FloorWidth + 1;
        var vertices = new float[stride * (Fo1HexMath.FloorHeight + 1)];
        for (var vertexY = 0; vertexY <= Fo1HexMath.FloorHeight; vertexY++)
            for (var vertexX = 0; vertexX <= Fo1HexMath.FloorWidth; vertexX++)
            {
                var adjacent = new List<float>(4);
                for (var offsetY = -1; offsetY <= 0; offsetY++)
                    for (var offsetX = -1; offsetX <= 0; offsetX++)
                    {
                        var floorX = vertexX + offsetX;
                        var floorY = vertexY + offsetY;
                        if (floorX is < 0 or >= Fo1HexMath.FloorWidth ||
                            floorY is < 0 or >= Fo1HexMath.FloorHeight)
                            continue;
                        var floorIndex = floorY * Fo1HexMath.FloorWidth +
                            (Fo1HexMath.FloorWidth - 1 - floorX);
                        if (smoothedCenterHeights[floorIndex] is { } height)
                            adjacent.Add(height);
                    }
                vertices[vertexY * stride + vertexX] = adjacent.Count == 0
                    ? 0.0f
                    : adjacent.Average();
            }
        return vertices;
    }

    private static Color[] BuildSmoothedFloorColors(
        Fo2ArvillagPresentationCatalog catalog,
        IReadOnlyList<int> floorIds,
        int neighborhoodRadius)
    {
        var colors = new Color?[floorIds.Count];
        for (var index = 0; index < floorIds.Count; index++)
        {
            var tileId = floorIds[index];
            if (tileId == Fo2ArvillagPresentationCatalog.DefaultFloorTileId)
                continue;
            if (!catalog.FloorMaterialDepth.TryGetValue(tileId, out var source))
                throw new InvalidOperationException(
                    $"Fallout 2 ARVILLAG floor color source is missing: {tileId}.");
            colors[index] = source.Relief.AverageOpaqueColor;
        }
        var result = new Color[floorIds.Count];
        for (var index = 0; index < floorIds.Count; index++)
        {
            if (colors[index] is null)
                continue;
            var sourceX = index % Fo1HexMath.FloorWidth;
            var sourceY = index / Fo1HexMath.FloorWidth;
            var sum = new Color(0.0f, 0.0f, 0.0f, 0.0f);
            var count = 0;
            for (var offsetY = -neighborhoodRadius;
                 offsetY <= neighborhoodRadius;
                 offsetY++)
                for (var offsetX = -neighborhoodRadius;
                     offsetX <= neighborhoodRadius;
                     offsetX++)
                {
                    var x = sourceX + offsetX;
                    var y = sourceY + offsetY;
                    if (x is < 0 or >= Fo1HexMath.FloorWidth ||
                        y is < 0 or >= Fo1HexMath.FloorHeight ||
                        colors[y * Fo1HexMath.FloorWidth + x] is not { } color)
                        continue;
                    sum += color;
                    count++;
                }
            if (count == 0)
                throw new InvalidOperationException(
                    $"Fallout 2 ARVILLAG floor color neighborhood is empty: {index}.");
            result[index] = sum / count;
            result[index].A = 1.0f;
        }
        return result;
    }

    private static float?[] SmoothFloorScalarField(
        IReadOnlyList<float?> source,
        int neighborhoodRadius)
    {
        var result = new float?[source.Count];
        for (var index = 0; index < source.Count; index++)
        {
            if (source[index] is null)
                continue;
            var sourceX = index % Fo1HexMath.FloorWidth;
            var sourceY = index / Fo1HexMath.FloorWidth;
            var sum = 0.0f;
            var count = 0;
            for (var offsetY = -neighborhoodRadius;
                 offsetY <= neighborhoodRadius;
                 offsetY++)
                for (var offsetX = -neighborhoodRadius;
                     offsetX <= neighborhoodRadius;
                     offsetX++)
                {
                    var x = sourceX + offsetX;
                    var y = sourceY + offsetY;
                    if (x is < 0 or >= Fo1HexMath.FloorWidth ||
                        y is < 0 or >= Fo1HexMath.FloorHeight ||
                        source[y * Fo1HexMath.FloorWidth + x] is not { } value)
                        continue;
                    sum += value;
                    count++;
                }
            if (count == 0)
                throw new InvalidOperationException(
                    $"Fallout 2 ARVILLAG floor height neighborhood is empty: {index}.");
            result[index] = sum / count;
        }
        return result;
    }

    private static FloorCornerHeight FloorCornerHeights(
        IReadOnlyList<float> sharedVertexHeights,
        int floorX,
        int floorY)
    {
        var stride = Fo1HexMath.FloorWidth + 1;
        float At(int x, int y) => sharedVertexHeights[y * stride + x];
        return new FloorCornerHeight(
            At(floorX, floorY),
            At(floorX + 1, floorY),
            At(floorX, floorY + 1),
            At(floorX + 1, floorY + 1));
    }

    private static ShaderMaterial BuildContinuousFloorMaterial(
        Texture2D albedoTexture,
        Texture2D normalTexture,
        float normalScale,
        float roughness,
        float sourceDetailMix,
        float albedoScale)
    {
        var shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;
                uniform sampler2D source_albedo : source_color, filter_nearest_mipmap, repeat_disable;
                uniform sampler2D source_normal : hint_normal, filter_nearest_mipmap, repeat_disable;
                uniform float source_normal_scale;
                uniform float source_roughness;
                uniform float source_detail_mix;
                uniform float albedo_scale;
                uniform float patch_half_x;
                uniform float patch_half_z;

                void vertex() {
                    float top = mix(INSTANCE_CUSTOM.r, INSTANCE_CUSTOM.g, UV.x);
                    float bottom = mix(INSTANCE_CUSTOM.b, INSTANCE_CUSTOM.a, UV.x);
                    VERTEX.y += mix(top, bottom, UV.y);
                    float slope_x = mix(
                        INSTANCE_CUSTOM.g - INSTANCE_CUSTOM.r,
                        INSTANCE_CUSTOM.a - INSTANCE_CUSTOM.b,
                        UV.y) / (2.0 * patch_half_x);
                    float slope_z = mix(
                        INSTANCE_CUSTOM.b - INSTANCE_CUSTOM.r,
                        INSTANCE_CUSTOM.a - INSTANCE_CUSTOM.g,
                        UV.x) / (2.0 * patch_half_z);
                    NORMAL = normalize(vec3(NORMAL.x - slope_x, NORMAL.y, NORMAL.z - slope_z));
                }

                void fragment() {
                    ALBEDO = mix(
                        COLOR.rgb,
                        texture(source_albedo, UV).rgb,
                        source_detail_mix) * albedo_scale;
                    NORMAL_MAP = texture(source_normal, UV).rgb;
                    NORMAL_MAP_DEPTH = source_normal_scale;
                    ROUGHNESS = source_roughness;
                }
                """,
        };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("source_albedo", albedoTexture);
        material.SetShaderParameter("source_normal", normalTexture);
        material.SetShaderParameter("source_normal_scale", normalScale);
        material.SetShaderParameter("source_roughness", roughness);
        material.SetShaderParameter("source_detail_mix", sourceDetailMix);
        material.SetShaderParameter("albedo_scale", albedoScale);
        material.SetShaderParameter("patch_half_x", Fo1HexMath.ColumnSpacingMeters);
        material.SetShaderParameter("patch_half_z", Fo1HexMath.FlatToFlatMeters);
        return material;
    }

    private static (ArrayMesh Mesh, int Triangles, float CenterHeightMeters)
        BuildMoldedFloorPatch(
            Fo2ArvillagFloorMaterialDepth source,
            Fo2MapArtifact artifact,
            float sourcePixelsPerMeter,
            Material material,
            float heightScale)
    {
        var sourceImage = Image.LoadFromFile(artifact.Path);
        if (sourceImage.IsEmpty() || sourceImage.GetWidth() != artifact.Width ||
            sourceImage.GetHeight() != artifact.Height || sourcePixelsPerMeter <= 0.0f)
            throw new InvalidOperationException(
                $"Fallout 2 ARVILLAG floor depth dimensions drifted: {source.TileId}.");
        var xSamples = MoldedSampleAxis(
            artifact.Width,
            source.Relief.MaximumInteriorDistancePixels);
        var ySamples = MoldedSampleAxis(
            artifact.Height,
            source.Relief.MaximumInteriorDistancePixels);
        var maximumReliefMeters =
            source.Relief.MaximumInteriorDistancePixels / sourcePixelsPerMeter;
        var lumaReliefMeters = maximumReliefMeters * source.Relief.LumaWeight;
        var meanLuma = SourceLuminance(source.Relief.AverageOpaqueColor);
        float Height(int x, int y)
        {
            var edgeDistance = Math.Min(
                Math.Min(x, artifact.Width - 1 - x),
                Math.Min(y, artifact.Height - 1 - y));
            var seamWeight = Math.Clamp(
                (float)edgeDistance / source.Relief.MaximumInteriorDistancePixels,
                0.0f,
                1.0f);
            return (SourceLuminance(sourceImage.GetPixel(x, y)) - meanLuma) *
                lumaReliefMeters * seamWeight * heightScale;
        }
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        var halfX = Fo1HexMath.ColumnSpacingMeters;
        var halfZ = Fo1HexMath.FlatToFlatMeters;
        Vector3 Point(int x, int y) => new(
            Mathf.Lerp(-halfX, halfX, (float)x / (artifact.Width - 1)),
            Height(x, y),
            Mathf.Lerp(-halfZ, halfZ, (float)y / (artifact.Height - 1)));
        Vector2 Uv(int x, int y) => new(
            (float)x / (artifact.Width - 1),
            (float)y / (artifact.Height - 1));
        void Vertex(int x, int y)
        {
            surface.SetUV(Uv(x, y));
            surface.AddVertex(Point(x, y));
        }
        var triangles = 0;
        for (var y = 0; y < ySamples.Length - 1; y++)
            for (var x = 0; x < xSamples.Length - 1; x++)
            {
                var x0 = xSamples[x];
                var x1 = xSamples[x + 1];
                var y0 = ySamples[y];
                var y1 = ySamples[y + 1];
                Vertex(x0, y0);
                Vertex(x0, y1);
                Vertex(x1, y1);
                Vertex(x0, y0);
                Vertex(x1, y1);
                Vertex(x1, y0);
                triangles += 2;
            }
        surface.GenerateNormals();
        surface.GenerateTangents();
        var mesh = surface.Commit() ?? throw new InvalidOperationException(
            $"Fallout 2 ARVILLAG floor molded mesh is empty: {source.TileId}.");
        mesh.SurfaceSetMaterial(0, material);
        var centerX = (artifact.Width - 1) / 2;
        var centerY = (artifact.Height - 1) / 2;
        return (mesh, triangles, Height(centerX, centerY));
    }

    private static float SourceLuminance(Color color) =>
        0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B;

    private static int[] MoldedSampleAxis(int length, int sourceStepPixels)
    {
        if (length <= 1 || sourceStepPixels <= 0)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG molded floor sampling is invalid.");
        var values = new HashSet<int> { 0, length - 1, (length - 1) / 2 };
        for (var value = sourceStepPixels; value < length - 1; value += sourceStepPixels)
            values.Add(value);
        return values.Order().ToArray();
    }

    private static void BuildPresentationEnvironment(
        Node3D root,
        Fo2ArroyoCaves3DProfile profile)
    {
        var atmosphere = profile.Atmosphere;
        var environment = new WorldEnvironment
        {
            Name = "MAP4_VERSIONED_CLASSIC_3D_PRESENTATION_ENVIRONMENT",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = atmosphere.BackgroundColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = atmosphere.AmbientColor,
                AmbientLightEnergy = atmosphere.AmbientEnergy *
                    profile.VillageMoldedSurface.AmbientEnergyScale,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                TonemapExposure = atmosphere.TonemapExposure *
                    profile.VillageMoldedSurface.TonemapExposureScale,
                FogEnabled = true,
                FogLightColor = atmosphere.FogColor,
                FogLightEnergy = atmosphere.FogLightEnergy,
                FogDensity = atmosphere.FogDensity,
                FogAerialPerspective = atmosphere.FogAerialPerspective,
                FogSkyAffect = atmosphere.FogSkyAffect,
            },
        };
        environment.SetMeta("fo2_3d_profile", profile.ResourcePath);
        environment.SetMeta("fo2_3d_profile_sha256", profile.Sha256);
        environment.SetMeta("fo2_visual_parity", false);
        root.AddChild(environment);
        var directional = new DirectionalLight3D
        {
            Name = "MAP4_VERSIONED_CLASSIC_3D_PRESENTATION_DIRECTIONAL_LIGHT",
            RotationDegrees = atmosphere.DirectionalLight.RotationDegrees,
            LightColor = atmosphere.DirectionalLight.Color,
            LightEnergy = atmosphere.DirectionalLight.Energy *
                profile.VillageMoldedSurface.DirectionalEnergyScale,
            ShadowEnabled = atmosphere.DirectionalLight.ShadowEnabled,
        };
        directional.SetMeta("fo2_3d_profile", profile.ResourcePath);
        directional.SetMeta("fo2_3d_profile_sha256", profile.Sha256);
        directional.SetMeta("fo2_visual_parity", false);
        root.AddChild(directional);
    }

    private static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T typed)
                yield return typed;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }
}
