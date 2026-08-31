using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArroyoSourceCoverageProfile(
    int NonDefaultFloorPatches,
    int FloorBoundaryEdges,
    int TopLevelObjects,
    IReadOnlyDictionary<int, int> ObjectTypes,
    int WallObjects,
    int UniqueWallTiles,
    int WallComponents,
    int LargestWallComponentTiles,
    int WallBoundaryEdges,
    int HiddenNonWallBlockCards,
    int HiddenSourceMarkerCards,
    int VisibleGroundedSourceProps,
    int SourceTorchProps,
    int SourceMapLights);

internal sealed record Fo2ArroyoFloorGeometryProfile(
    string Mode,
    float SurfaceMeters,
    float ReliefMeters,
    float ReliefFrequency,
    int SubdivisionsPerAxis,
    float BoundaryClosureHeightMeters,
    float BoundaryClosureOverhangMeters);

internal sealed record Fo2ArroyoWallGeometryProfile(
    string Mode,
    int SourceObjectType,
    float HeightMeters,
    float HeightVariationMeters,
    float HeightFrequency,
    float HeightPhase,
    float GroundSinkMeters,
    float SdfSampleMeters,
    float SdfRadiusMeters,
    float CeilingOverhangMeters,
    float SideShoulderHeightFraction,
    float SideBulgeMeters,
    float SideNoiseMeters,
    bool CeilingClosure,
    string ComponentMeshMode,
    Fo2ArroyoWallRoleProfile Roles);

internal sealed record Fo2ArroyoWallRoleProfile(
    string Mode,
    int CaveShellMinimumConnectedTiles,
    int ExpectedCaveShellComponents,
    int ExpectedStonePostInstances,
    IReadOnlySet<string> StonePostLogicalPaths,
    string StonePostGeometryMode,
    float StonePostDepthMeters);

internal sealed record Fo2ArroyoRockMaterialProfile(
    Color Dark,
    Color Light,
    float Roughness,
    float AmbientLift);

internal sealed record Fo2ArroyoMaterialProfile(
    string ShaderContract,
    float WorldScale,
    float NormalStrength,
    float SourceDetailWorldScale,
    float SourceDetailMix,
    float MacroDetailWorldScale,
    float MacroDetailMix,
    Fo2ArroyoRockMaterialProfile Wall,
    Fo2ArroyoRockMaterialProfile Floor);

internal sealed record Fo2ArroyoSourcePropProfile(
    string GeometryMode,
    string GroundingMode,
    string CoLocatedLayerMode,
    float CoLocatedLayerGapMeters,
    float MaximumGroundErrorMeters,
    bool Shaded,
    bool DoubleSided);

internal sealed record Fo2ArroyoVillageMoldedSurfaceProfile(
    string Mode,
    float FloorHeightScale,
    int FloorHeightNeighborhoodRadius,
    float FloorNormalScale,
    float FloorSourceDetailMix,
    float FloorAlbedoScale,
    int FloorColorNeighborhoodRadius,
    float ObjectDepthScale,
    float ObjectNormalScale,
    string ObjectTwoSidedLightingMode,
    float ObjectBacklightStrength,
    string ArrivalFramingMode,
    int ArrivalNearestVisibleObjectCount,
    int ArrivalMaximumObjectHexDistance,
    float ArrivalBoundsPaddingFraction,
    float AmbientEnergyScale,
    float TonemapExposureScale,
    float DirectionalEnergyScale,
    string Policy);

internal sealed record Fo2ArroyoDirectionalLightProfile(
    Vector3 RotationDegrees,
    Color Color,
    float Energy,
    bool ShadowEnabled);

internal sealed record Fo2ArroyoSourceMapLightProfile(
    string Mode,
    string LogicalPath,
    string Fid,
    int ObjectType,
    int ExpectedRecords,
    int ExpectedDistance,
    int ExpectedIntensity,
    int IntensityFixedPointOne,
    string VerticalProjectionMode);

internal sealed record Fo2ArroyoAtmosphereProfile(
    Color BackgroundColor,
    Color AmbientColor,
    float AmbientEnergy,
    float TonemapExposure,
    Color FogColor,
    float FogLightEnergy,
    float FogDensity,
    float FogAerialPerspective,
    float FogSkyAffect,
    float VolumetricFogDensity,
    Color VolumetricFogAlbedo,
    Color VolumetricFogEmission,
    float VolumetricFogEmissionEnergy,
    float VolumetricFogLengthMeters,
    float VolumetricFogDetailSpread,
    float VolumetricFogAmbientInject,
    float VolumetricFogSkyAffect,
    Fo2ArroyoDirectionalLightProfile DirectionalLight,
    Fo2ArroyoSourceMapLightProfile SourceMapLights);

internal sealed record Fo2ArroyoStaticCaptureProfile(
    string Projection,
    Vector3 PositionOffsetMeters,
    Vector3 FocusOffsetMeters,
    float FovDegrees,
    float NearClipMeters,
    float FarClipMeters,
    double MinimumLuminanceDeviation,
    int MinimumNonBackgroundPixels,
    double MaximumBackgroundPixelFraction);

internal sealed record Fo2ArroyoGeneratedAssetLaneProfile(
    bool Used,
    int TrellisCandidatesAdmitted,
    bool OwnedOrGeneratedMeshesPackaged,
    string Reason);

internal sealed record Fo2ArroyoPromotionProfile(
    bool BoundedStaticVisualGateOnly,
    bool PairReady,
    bool RetailParity,
    bool Fo1QualityParity,
    bool CinematicHandoffReviewed,
    IReadOnlyList<string> PresentationBlockers);

internal sealed record Fo2ArroyoCaves3DProfile(
    string ResourcePath,
    string Sha256,
    string Id,
    Fo2ArroyoSourceCoverageProfile SourceCoverage,
    IReadOnlySet<string> HiddenCardLogicalPaths,
    IReadOnlySet<string> HiddenSourceMarkerLogicalPaths,
    IReadOnlySet<string> TorchLogicalPaths,
    Fo2ArroyoFloorGeometryProfile FloorGeometry,
    Fo2ArroyoWallGeometryProfile WallGeometry,
    Fo2ArroyoMaterialProfile Materials,
    Fo2ArroyoSourcePropProfile SourceProps,
    Fo2ArroyoVillageMoldedSurfaceProfile VillageMoldedSurface,
    Fo2ArroyoAtmosphereProfile Atmosphere,
    Fo2ArroyoStaticCaptureProfile StaticCapture,
    Fo2ArroyoGeneratedAssetLaneProfile GeneratedAssetLane,
    Fo2ArroyoPromotionProfile Promotion)
{
    private const string ProfileResourcePath = "res://config/fo2-arroyo-caves-3d-v1.json";
    private const string Schema = "opennv-fo2-arroyo-caves-3d-runtime/v1";
    private const string FloorMode = "source-floor-patch-fused-relief-v1";
    private const string WallMode =
        "edge-connected-source-wall-graph-sdf-closed-shell-v2";
    private const string ComponentMeshMode =
        "one-indexed-sdf-shell-per-connected-component";
    private const string WallRoleMode = "source-component-and-frm-identity-role-map-v1";
    private const string StonePostGeometryMode =
        "exact-frm-alpha-island-molded-relief-v2";
    private const string SourcePropGeometryMode =
        "exact-frm-alpha-island-molded-relief-v2";
    private const string SourceMapLightMode =
        "exact-map-object-light-distance-intensity-v1";
    private const string SourceMapLightVerticalProjectionMode =
        "source-tile-center-one-hex-circumradius-height-v1";
    private const string ShaderContract =
        "opennv-world-space-owned-frm-triplanar-albedo-normal-rock/v3";
    private const string GroundingMode = "runtime-aabb-seat-to-molded-source-floor-v1";
    private const string CoLocatedLayerMode =
        "same-tile-source-serial-front-order-v1";
    private const string VillageMoldedSurfaceMode =
        "owned-map-floor-shared-height-and-frm-relief-v1";
    private const string VillageObjectTwoSidedLightingMode =
        "owned-frm-average-color-normalized-backlight-with-owned-normal-v1";
    private const string VillageArrivalFramingMode =
        "exact-route-plus-nearest-visible-source-object-aabb-v1";
    private const int Sha256HexCharacters = 64;
    private const int MaximumMapObjectType = 5;

    internal static Fo2ArroyoCaves3DProfile Load(Fo2ArroyoCavesPresentationCatalog catalog)
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(ProfileResourcePath);
        if (bytes.Length == 0)
            throw new FileNotFoundException(
                "Fallout 2 Arroyo Caves 3D profile is missing.",
                ProfileResourcePath);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var map = root.GetProperty("map");
        var authority = root.GetProperty("authority");
        var sourceCoverage = root.GetProperty("sourceCoverage");
        var floor = root.GetProperty("floorGeometry");
        var wall = root.GetProperty("wallGeometry");
        var materials = root.GetProperty("materials");
        var props = root.GetProperty("sourceProps");
        var villageMoldedSurface = root.GetProperty("villageMoldedSurface");
        var atmosphere = root.GetProperty("atmosphere");
        var capture = root.GetProperty("staticCapture");
        var generated = root.GetProperty("generatedAssetLane");
        var promotion = root.GetProperty("promotion");
        if (RequiredString(root, "schema") != Schema ||
            RequiredString(root, "campaign") != "Fallout2" ||
            map.GetProperty("index").GetInt32() != Fo2ArroyoCavesPresentationCatalog.MapIndex ||
            RequiredString(map, "name") != "ARCAVES.MAP" ||
            RequiredString(map, "sha256") != catalog.MapSha256 ||
            map.GetProperty("elevation").GetInt32() != Fo2ArroyoCavesPresentationCatalog.Elevation ||
            RequiredString(authority, "wallGrouping") != "edge-connected source wall hexes" ||
            RequiredString(authority, "collisionAndWalkability") !=
                "existing source floor plus blocking-object walk mask; presentation geometry does not alter it" ||
            RequiredString(floor, "mode") != FloorMode ||
            RequiredString(wall, "mode") != WallMode ||
            RequiredString(wall, "componentMeshMode") != ComponentMeshMode ||
            RequiredString(materials, "shaderContract") != ShaderContract ||
            RequiredString(props, "groundingMode") != GroundingMode ||
            RequiredString(capture, "projection") != "perspective" ||
            generated.GetProperty("used").GetBoolean() ||
            generated.GetProperty("trellisCandidatesAdmitted").GetInt32() != 0 ||
            generated.GetProperty("ownedOrGeneratedMeshesPackaged").GetBoolean() ||
            !promotion.GetProperty("boundedStaticVisualGateOnly").GetBoolean() ||
            promotion.GetProperty("pairReady").GetBoolean() ||
            promotion.GetProperty("retailParity").GetBoolean() ||
            promotion.GetProperty("fo1QualityParity").GetBoolean() ||
            promotion.GetProperty("cinematicHandoffReviewed").GetBoolean())
            throw new InvalidOperationException(
                "Unexpected Fallout 2 Arroyo Caves 3D runtime profile.");

        var profile = new Fo2ArroyoCaves3DProfile(
            ProfileResourcePath,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            RequiredString(root, "id"),
            new Fo2ArroyoSourceCoverageProfile(
                PositiveInt(sourceCoverage, "nonDefaultFloorPatches"),
                PositiveInt(sourceCoverage, "floorBoundaryEdges"),
                PositiveInt(sourceCoverage, "topLevelObjects"),
                ReadObjectTypeCounts(sourceCoverage.GetProperty("objectTypes")),
                PositiveInt(sourceCoverage, "wallObjects"),
                PositiveInt(sourceCoverage, "uniqueWallTiles"),
                PositiveInt(sourceCoverage, "wallComponents"),
                PositiveInt(sourceCoverage, "largestWallComponentTiles"),
                PositiveInt(sourceCoverage, "wallBoundaryEdges"),
                PositiveInt(sourceCoverage, "hiddenNonWallBlockCards"),
                PositiveInt(sourceCoverage, "hiddenSourceMarkerCards"),
                PositiveInt(sourceCoverage, "visibleGroundedSourceProps"),
                PositiveInt(sourceCoverage, "sourceTorchProps"),
                PositiveInt(sourceCoverage, "sourceMapLights")),
            ReadStringSet(authority, "hiddenCardLogicalPaths"),
            ReadStringSet(authority, "hiddenSourceMarkerLogicalPaths"),
            ReadStringSet(authority, "torchLogicalPaths"),
            new Fo2ArroyoFloorGeometryProfile(
                RequiredString(floor, "mode"),
                Finite(floor, "surfaceMeters"),
                NonNegative(floor, "reliefMeters"),
                Positive(floor, "reliefFrequency"),
                PositiveInt(floor, "subdivisionsPerAxis"),
                Positive(floor, "boundaryClosureHeightMeters"),
                NonNegative(floor, "boundaryClosureOverhangMeters")),
            new Fo2ArroyoWallGeometryProfile(
                RequiredString(wall, "mode"),
                wall.GetProperty("sourceObjectType").GetInt32(),
                Positive(wall, "heightMeters"),
                NonNegative(wall, "heightVariationMeters"),
                Positive(wall, "heightFrequency"),
                Finite(wall, "heightPhase"),
                NonNegative(wall, "groundSinkMeters"),
                Positive(wall, "sdfSampleMeters"),
                Positive(wall, "sdfRadiusMeters"),
                NonNegative(wall, "ceilingOverhangMeters"),
                Fraction(wall, "sideShoulderHeightFraction"),
                NonNegative(wall, "sideBulgeMeters"),
                NonNegative(wall, "sideNoiseMeters"),
                wall.GetProperty("ceilingClosure").GetBoolean(),
                RequiredString(wall, "componentMeshMode"),
                ReadWallRoles(wall.GetProperty("roles"))),
            new Fo2ArroyoMaterialProfile(
                RequiredString(materials, "shaderContract"),
                Positive(materials, "worldScale"),
                NonNegative(materials, "normalStrength"),
                Positive(materials, "sourceDetailWorldScale"),
                Fraction(materials, "sourceDetailMix"),
                Positive(materials, "macroDetailWorldScale"),
                Fraction(materials, "macroDetailMix"),
                ReadRockMaterial(materials.GetProperty("wall")),
                ReadRockMaterial(materials.GetProperty("floor"))),
            new Fo2ArroyoSourcePropProfile(
                RequiredString(props, "geometryMode"),
                RequiredString(props, "groundingMode"),
                RequiredString(props, "coLocatedLayerMode"),
                Positive(props, "coLocatedLayerGapMeters"),
                Positive(props, "maximumGroundErrorMeters"),
                props.GetProperty("shaded").GetBoolean(),
                props.GetProperty("doubleSided").GetBoolean()),
            new Fo2ArroyoVillageMoldedSurfaceProfile(
                RequiredString(villageMoldedSurface, "mode"),
                Positive(villageMoldedSurface, "floorHeightScale"),
                PositiveInt(villageMoldedSurface, "floorHeightNeighborhoodRadius"),
                Positive(villageMoldedSurface, "floorNormalScale"),
                Fraction(villageMoldedSurface, "floorSourceDetailMix"),
                Fraction(villageMoldedSurface, "floorAlbedoScale"),
                PositiveInt(villageMoldedSurface, "floorColorNeighborhoodRadius"),
                Positive(villageMoldedSurface, "objectDepthScale"),
                Positive(villageMoldedSurface, "objectNormalScale"),
                RequiredString(villageMoldedSurface, "objectTwoSidedLightingMode"),
                Fraction(villageMoldedSurface, "objectBacklightStrength"),
                RequiredString(villageMoldedSurface, "arrivalFramingMode"),
                PositiveInt(villageMoldedSurface, "arrivalNearestVisibleObjectCount"),
                PositiveInt(villageMoldedSurface, "arrivalMaximumObjectHexDistance"),
                Fraction(villageMoldedSurface, "arrivalBoundsPaddingFraction"),
                Fraction(villageMoldedSurface, "ambientEnergyScale"),
                Fraction(villageMoldedSurface, "tonemapExposureScale"),
                Fraction(villageMoldedSurface, "directionalEnergyScale"),
                RequiredString(villageMoldedSurface, "policy")),
            new Fo2ArroyoAtmosphereProfile(
                ReadColor(atmosphere.GetProperty("backgroundColor")),
                ReadColor(atmosphere.GetProperty("ambientColor")),
                Positive(atmosphere, "ambientEnergy"),
                Positive(atmosphere, "tonemapExposure"),
                ReadColor(atmosphere.GetProperty("fogColor")),
                Positive(atmosphere, "fogLightEnergy"),
                Positive(atmosphere, "fogDensity"),
                Fraction(atmosphere, "fogAerialPerspective"),
                Unit(atmosphere, "fogSkyAffect"),
                Positive(atmosphere, "volumetricFogDensity"),
                ReadColor(atmosphere.GetProperty("volumetricFogAlbedo")),
                ReadColor(atmosphere.GetProperty("volumetricFogEmission")),
                NonNegative(atmosphere, "volumetricFogEmissionEnergy"),
                Positive(atmosphere, "volumetricFogLengthMeters"),
                Positive(atmosphere, "volumetricFogDetailSpread"),
                Unit(atmosphere, "volumetricFogAmbientInject"),
                Unit(atmosphere, "volumetricFogSkyAffect"),
                ReadDirectionalLight(atmosphere.GetProperty("directionalLight")),
                ReadSourceMapLights(atmosphere.GetProperty("sourceMapLights"))),
            new Fo2ArroyoStaticCaptureProfile(
                RequiredString(capture, "projection"),
                ReadVector(capture.GetProperty("positionOffsetMeters")),
                ReadVector(capture.GetProperty("focusOffsetMeters")),
                Positive(capture, "fovDegrees"),
                Positive(capture, "nearClipMeters"),
                Positive(capture, "farClipMeters"),
                Positive(capture, "minimumLuminanceDeviation"),
                PositiveInt(capture, "minimumNonBackgroundPixels"),
                Fraction(capture, "maximumBackgroundPixelFraction")),
            new Fo2ArroyoGeneratedAssetLaneProfile(
                generated.GetProperty("used").GetBoolean(),
                generated.GetProperty("trellisCandidatesAdmitted").GetInt32(),
                generated.GetProperty("ownedOrGeneratedMeshesPackaged").GetBoolean(),
                RequiredString(generated, "reason")),
            new Fo2ArroyoPromotionProfile(
                promotion.GetProperty("boundedStaticVisualGateOnly").GetBoolean(),
                promotion.GetProperty("pairReady").GetBoolean(),
                promotion.GetProperty("retailParity").GetBoolean(),
                promotion.GetProperty("fo1QualityParity").GetBoolean(),
                promotion.GetProperty("cinematicHandoffReviewed").GetBoolean(),
                ReadStringList(promotion, "presentationBlockers")));
        Validate(profile);
        return profile;
    }

    private static void Validate(Fo2ArroyoCaves3DProfile profile)
    {
        if (profile.Sha256.Length != Sha256HexCharacters ||
            profile.SourceCoverage.ObjectTypes.Values.Sum() !=
                profile.SourceCoverage.TopLevelObjects ||
            !profile.SourceCoverage.ObjectTypes.TryGetValue(
                profile.WallGeometry.SourceObjectType,
                out var wallObjects) ||
            wallObjects != profile.SourceCoverage.WallObjects ||
            profile.SourceCoverage.UniqueWallTiles > profile.SourceCoverage.WallObjects ||
            profile.SourceCoverage.LargestWallComponentTiles >
                profile.SourceCoverage.UniqueWallTiles ||
            profile.SourceCoverage.VisibleGroundedSourceProps +
                profile.SourceCoverage.HiddenNonWallBlockCards +
                profile.SourceCoverage.HiddenSourceMarkerCards +
                profile.SourceCoverage.WallObjects != profile.SourceCoverage.TopLevelObjects ||
            profile.HiddenCardLogicalPaths.Count == 0 ||
            profile.HiddenSourceMarkerLogicalPaths.Count == 0 ||
            profile.TorchLogicalPaths.Count == 0 ||
            profile.Promotion.PresentationBlockers.Count == 0 ||
            profile.FloorGeometry.SubdivisionsPerAxis is < 2 or > 8 ||
            profile.WallGeometry.GroundSinkMeters >= profile.WallGeometry.HeightMeters ||
            profile.WallGeometry.Roles.CaveShellMinimumConnectedTiles <= 1 ||
            profile.WallGeometry.Roles.ExpectedCaveShellComponents <= 0 ||
            profile.WallGeometry.Roles.ExpectedStonePostInstances <= 0 ||
            profile.WallGeometry.Roles.StonePostLogicalPaths.Count == 0 ||
            profile.SourceProps.GeometryMode != SourcePropGeometryMode ||
            profile.SourceProps.CoLocatedLayerMode != CoLocatedLayerMode ||
            profile.VillageMoldedSurface.Mode != VillageMoldedSurfaceMode ||
            profile.VillageMoldedSurface.ObjectTwoSidedLightingMode !=
                VillageObjectTwoSidedLightingMode ||
            profile.VillageMoldedSurface.ArrivalFramingMode !=
                VillageArrivalFramingMode ||
            profile.Atmosphere.SourceMapLights.Mode != SourceMapLightMode ||
            profile.Atmosphere.SourceMapLights.VerticalProjectionMode !=
                SourceMapLightVerticalProjectionMode ||
            profile.Atmosphere.SourceMapLights.ExpectedRecords !=
                profile.SourceCoverage.SourceMapLights ||
            profile.Atmosphere.SourceMapLights.ObjectType < 0 ||
            profile.Atmosphere.SourceMapLights.ObjectType > MaximumMapObjectType ||
            profile.Atmosphere.SourceMapLights.ExpectedDistance <= 0 ||
            profile.Atmosphere.SourceMapLights.ExpectedIntensity <= 0 ||
            profile.Atmosphere.SourceMapLights.IntensityFixedPointOne <= 0 ||
            !profile.WallGeometry.CeilingClosure ||
            profile.StaticCapture.NearClipMeters >= profile.StaticCapture.FarClipMeters ||
            profile.StaticCapture.MinimumNonBackgroundPixels <= 0)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves 3D profile dimensions are invalid.");
    }

    private static IReadOnlyDictionary<int, int> ReadObjectTypeCounts(JsonElement source)
    {
        var counts = source.EnumerateObject().ToDictionary(
            row => int.Parse(row.Name, System.Globalization.CultureInfo.InvariantCulture),
            row => row.Value.GetInt32());
        if (counts.Count == 0 ||
            counts.Keys.Any(key => key is < 0 or > MaximumMapObjectType) ||
            counts.Values.Any(value => value <= 0))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves object-type coverage is invalid.");
        return counts;
    }

    private static Fo2ArroyoRockMaterialProfile ReadRockMaterial(JsonElement source) => new(
        ReadColor(source.GetProperty("dark")),
        ReadColor(source.GetProperty("light")),
        Unit(source, "roughness"),
        Unit(source, "ambientLift"));

    private static Fo2ArroyoWallRoleProfile ReadWallRoles(JsonElement source)
    {
        if (RequiredString(source, "mode") != WallRoleMode ||
            RequiredString(source, "stonePostGeometryMode") != StonePostGeometryMode)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo wall-role mapping drifted.");
        return new Fo2ArroyoWallRoleProfile(
            RequiredString(source, "mode"),
            PositiveInt(source, "caveShellMinimumConnectedTiles"),
            PositiveInt(source, "expectedCaveShellComponents"),
            PositiveInt(source, "expectedStonePostInstances"),
            ReadStringSet(source, "stonePostLogicalPaths"),
            RequiredString(source, "stonePostGeometryMode"),
            Positive(source, "stonePostDepthMeters"));
    }

    private static Fo2ArroyoDirectionalLightProfile ReadDirectionalLight(JsonElement source) => new(
        ReadVector(source.GetProperty("rotationDegrees")),
        ReadColor(source.GetProperty("color")),
        Positive(source, "energy"),
        source.GetProperty("shadowEnabled").GetBoolean());

    private static Fo2ArroyoSourceMapLightProfile ReadSourceMapLights(
        JsonElement source) => new(
        RequiredString(source, "mode"),
        RequiredString(source, "logicalPath").ToLowerInvariant(),
        RequiredString(source, "fid").ToLowerInvariant(),
        source.GetProperty("objectType").GetInt32(),
        PositiveInt(source, "expectedRecords"),
        PositiveInt(source, "expectedDistance"),
        PositiveInt(source, "expectedIntensity"),
        PositiveInt(source, "intensityFixedPointOne"),
        RequiredString(source, "verticalProjectionMode"));

    private static IReadOnlySet<string> ReadStringSet(JsonElement source, string property)
    {
        var values = source.GetProperty(property).EnumerateArray()
            .Select(row => row.GetString()?.ToLowerInvariant())
            .ToArray();
        if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidOperationException(
                $"Fallout 2 Arroyo Caves path set is invalid: {property}");
        return values.Select(value => value!).ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> ReadStringList(JsonElement source, string property)
    {
        var values = source.GetProperty(property).EnumerateArray()
            .Select(row => row.GetString())
            .ToArray();
        if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidOperationException(
                $"Fallout 2 Arroyo Caves string list is invalid: {property}");
        return values.Select(value => value!).ToArray();
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Fallout 2 Arroyo Caves 3D profile string is empty: {property}");
        return value;
    }

    private static int PositiveInt(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetInt32();
        if (value <= 0)
            throw new InvalidOperationException(
                $"Fallout 2 Arroyo Caves 3D profile integer is invalid: {property}");
        return value;
    }

    private static float Finite(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetSingle();
        if (!float.IsFinite(value))
            throw new InvalidOperationException(
                $"Fallout 2 Arroyo Caves 3D profile number is invalid: {property}");
        return value;
    }

    private static float NonNegative(JsonElement source, string property)
    {
        var value = Finite(source, property);
        if (value < 0.0f)
            throw new InvalidOperationException(
                $"Fallout 2 Arroyo Caves 3D profile number is negative: {property}");
        return value;
    }

    private static float Positive(JsonElement source, string property)
    {
        var value = Finite(source, property);
        if (value <= 0.0f)
            throw new InvalidOperationException(
                $"Fallout 2 Arroyo Caves 3D profile number is not positive: {property}");
        return value;
    }

    private static float Unit(JsonElement source, string property)
    {
        var value = Finite(source, property);
        if (value is < 0.0f or > 1.0f)
            throw new InvalidOperationException(
                $"Fallout 2 Arroyo Caves 3D profile number is outside [0,1]: {property}");
        return value;
    }

    private static float Fraction(JsonElement source, string property)
    {
        var value = Finite(source, property);
        if (value is <= 0.0f or >= 1.0f)
            throw new InvalidOperationException(
                $"Fallout 2 Arroyo Caves 3D profile number is outside (0,1): {property}");
        return value;
    }

    private static Color ReadColor(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != 4 || values.Any(value => !float.IsFinite(value) || value is < 0.0f or > 1.0f))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves 3D profile color is invalid.");
        return new Color(values[0], values[1], values[2], values[3]);
    }

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves 3D profile vector is invalid.");
        return new Vector3(values[0], values[1], values[2]);
    }
}
