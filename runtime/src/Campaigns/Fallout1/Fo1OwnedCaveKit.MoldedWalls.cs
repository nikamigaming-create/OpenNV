using System.Security.Cryptography;
using System.Text.Json;
using Godot;

using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static partial class Fo1OwnedCaveKit
{
    private static ReliefCoverage BuildConnectedWallVolumes(
        Node3D container,
        JsonElement composition,
        JsonElement caveKit,
        RuntimeMaterialLoader.LoadedTextures textures,
        IReadOnlyDictionary<string, Prototype> prototypes)
    {
        var contract = composition.GetProperty("connectedWallVolume");
        if (contract.GetProperty("schema").GetString() !=
            "opennv-fo1-connected-wall-volume/v2" ||
            contract.GetProperty("sourcePlacementContract").GetString() !=
            "frmRelief.placements")
            throw new InvalidOperationException(
                "Unexpected Fallout connected wall-volume contract.");

        var minimumContourSegments = contract.GetProperty("minimumContourSegments").GetInt32();
        var groundSink = contract.GetProperty("groundSinkMeters").GetSingle();
        var minimumRadius = contract.GetProperty("minimumRadiusMeters").GetSingle();
        var maximumRadius = contract.GetProperty("maximumRadiusMeters").GetSingle();
        var radiusFromWidth = contract.GetProperty("radiusFromFrmWidthScale").GetSingle();
        var minimumHeight = contract.GetProperty("minimumHeightMeters").GetSingle();
        var maximumHeight = contract.GetProperty("maximumHeightMeters").GetSingle();
        var heightFromPixels = contract.GetProperty("heightFromFrmPixelsScale").GetSingle();
        var radialNoise = contract.GetProperty("radialNoiseFraction").GetSingle();
        var verticalNoise = contract.GetProperty("verticalNoiseMeters").GetSingle();
        var sampleSpacing = contract.GetProperty("surfaceSampleSpacingMeters").GetSingle();
        var contourResampleSpacing = contract.GetProperty("contourResampleSpacingMeters").GetSingle();
        var contourSmoothIterations = contract.GetProperty("contourSmoothIterations").GetInt32();
        var contourSmoothStrength = contract.GetProperty("contourSmoothStrength").GetSingle();
        var contourInflation = contract.GetProperty("contourInflationMeters").GetSingle();
        var boundaryBulge = contract.GetProperty("boundaryBulgeMeters").GetSingle();
        var macroNoiseWavelength = contract.GetProperty("macroNoiseWavelengthMeters").GetSingle();
        var microNoiseWavelength = contract.GetProperty("microNoiseWavelengthMeters").GetSingle();
        var noiseSeed = contract.GetProperty("noiseSeed").GetInt32();
        var noiseSource = contract.GetProperty("noiseBlend");
        var noiseBlend = new WallNoiseBlend(
            noiseSource.GetProperty("ringWavelengthBase").GetSingle(),
            noiseSource.GetProperty("ringWavelengthHeightScale").GetSingle(),
            noiseSource.GetProperty("macroWeight").GetSingle(),
            noiseSource.GetProperty("ringMacroWeight").GetSingle(),
            noiseSource.GetProperty("microRadialWeight").GetSingle(),
            noiseSource.GetProperty("microJitterWeight").GetSingle(),
            noiseSource.GetProperty("verticalMacroWeight").GetSingle(),
            noiseSource.GetProperty("verticalMicroWeight").GetSingle(),
            noiseSource.GetProperty("periodicPrimaryWeight").GetSingle(),
            noiseSource.GetProperty("periodicSecondaryWeight").GetSingle(),
            noiseSource.GetProperty("periodicSecondaryFrequencyMultiplier").GetInt32(),
            noiseSource.GetProperty("periodicSecondaryFrequencyOffset").GetInt32(),
            noiseSource.GetProperty("periodicSecondaryPhaseScale").GetSingle());
        var pixelsPerMeter = contract.GetProperty("pixelsPerMeter").GetSingle();
        if (minimumContourSegments is < Fo1OwnedCaveKitNumericContracts.GeometryInt6 or > Fo1OwnedCaveKitNumericContracts.GeometryInt64 ||
            groundSink is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point05f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point75f ||
            minimumRadius is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point20f or > 3.0f || maximumRadius < minimumRadius ||
            radiusFromWidth is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point1f or > 2.0f ||
            minimumHeight is < 2.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat12Point0f || maximumHeight < minimumHeight ||
            heightFromPixels is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point25f or > 4.0f ||
            radialNoise is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point45f || verticalNoise is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f ||
            sampleSpacing is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point05f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f ||
            contourResampleSpacing is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point08f or > 1.0f ||
            contourSmoothIterations is < 1 or > Fo1OwnedCaveKitNumericContracts.GeometryInt12 ||
            contourSmoothStrength is <= 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point45f ||
            contourInflation is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point75f ||
            boundaryBulge is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point75f ||
            macroNoiseWavelength is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat12Point0f ||
            microNoiseWavelength is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point2f or > 4.0f ||
            noiseBlend.RingWavelengthBase is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point25f or > 2.0f ||
            noiseBlend.RingWavelengthHeightScale is < 0.0f or > 1.0f ||
            noiseBlend.MacroWeight is < 0.0f or > 1.0f ||
            noiseBlend.RingMacroWeight is < 0.0f or > 1.0f ||
            noiseBlend.MicroRadialWeight is < 0.0f or > 2.0f ||
            noiseBlend.MicroJitterWeight is < 0.0f or > 2.0f ||
            noiseBlend.VerticalMacroWeight is < 0.0f or > 1.0f ||
            noiseBlend.VerticalMicroWeight is < 0.0f or > 1.0f ||
            noiseBlend.PeriodicPrimaryWeight is < 0.0f or > 1.0f ||
            noiseBlend.PeriodicSecondaryWeight is < 0.0f or > 1.0f ||
            noiseBlend.PeriodicSecondaryFrequencyMultiplier is < 1 or > Fo1OwnedCaveKitNumericContracts.GeometryInt6 ||
            noiseBlend.PeriodicSecondaryFrequencyOffset is < 0 or > 4 ||
            noiseBlend.PeriodicSecondaryPhaseScale is < 0.0f or > 2.0f ||
            !Mathf.IsEqualApprox(
                noiseBlend.PeriodicPrimaryWeight + noiseBlend.PeriodicSecondaryWeight,
                1.0f) ||
            !Mathf.IsEqualApprox(
                noiseBlend.VerticalMacroWeight + noiseBlend.VerticalMicroWeight,
                1.0f) ||
            pixelsPerMeter <= 0.0f)
            throw new InvalidOperationException(
                "Fallout connected wall-volume dimensions are invalid.");

        var rings = contract.GetProperty("rings").EnumerateArray()
            .Select(row => new WallVolumeRing(
                row.GetProperty("heightFraction").GetSingle(),
                row.GetProperty("radiusMultiplier").GetSingle(),
                row.GetProperty("centerJitterFraction").GetSingle()))
            .ToArray();
        if (rings.Length is < 4 or > Fo1OwnedCaveKitNumericContracts.GeometryInt12 ||
            !Mathf.IsEqualApprox(rings[0].HeightFraction, 0.0f) ||
            !Mathf.IsEqualApprox(rings[^1].HeightFraction, 1.0f) ||
            rings.Select(row => row.HeightFraction).Distinct().Count() != rings.Length ||
            !rings.Select(row => row.HeightFraction).SequenceEqual(
                rings.Select(row => row.HeightFraction).Order()))
            throw new InvalidOperationException("Fallout connected wall-volume rings are invalid.");
        foreach (var ring in rings)
            if (ring.HeightFraction is < 0.0f or > 1.0f ||
                ring.RadiusMultiplier is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point1f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat1Point6f ||
                ring.CenterJitterFraction is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point3f)
                throw new InvalidOperationException(
                    "Fallout connected wall-volume ring dimension is invalid.");

        var profiles = new Dictionary<string, WallVolumeProfile>(StringComparer.Ordinal);
        foreach (var property in contract.GetProperty("profiles").EnumerateObject())
        {
            var row = property.Value;
            var textureRepeat = row.GetProperty("textureRepeatMeters").GetSingle();
            var triplanarSharpness = row.GetProperty("triplanarSharpness").GetSingle();
            var profile = new WallVolumeProfile(
                textureRepeat,
                row.GetProperty("roughness").GetSingle(),
                row.GetProperty("normalScale").GetSingle(),
                row.GetProperty("radiusScale").GetSingle(),
                row.GetProperty("heightScale").GetSingle(),
                triplanarSharpness,
                new StandardMaterial3D
                {
                    ResourceName = $"FO1 connected {property.Name} wall volume",
                    AlbedoTexture = textures.TwoDimensional[TextureId(
                        caveKit,
                        row.GetProperty("diffusePath").GetString()!)],
                    NormalTexture = textures.TwoDimensional[TextureId(
                        caveKit,
                        row.GetProperty("normalPath").GetString()!)],
                    AlbedoColor = ReadColor(row.GetProperty("albedoColor")),
                    Roughness = row.GetProperty("roughness").GetSingle(),
                    Metallic = 0.0f,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                    NormalEnabled = true,
                    NormalScale = row.GetProperty("normalScale").GetSingle(),
                    Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
                    TextureRepeat = true,
                    Uv1Scale = Vector3.One / textureRepeat,
                    Uv1Triplanar = true,
                    Uv1WorldTriplanar = true,
                    Uv1TriplanarSharpness = triplanarSharpness,
                });
            if (profile.TextureRepeatMeters <= Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f ||
                profile.Roughness is < 0.0f or > 1.0f ||
                profile.NormalScale is < 0.0f or > 2.0f ||
                profile.RadiusScale is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point25f or > 2.0f ||
                profile.HeightScale is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point25f or > 2.0f ||
                profile.TriplanarSharpness is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat16Point0f ||
                !profiles.TryAdd(property.Name, profile))
                throw new InvalidOperationException(
                    $"Fallout connected wall-volume profile is invalid: {property.Name}");
        }
        if (!new HashSet<string>(profiles.Keys, StringComparer.Ordinal)
                .SetEquals(new[] { "cave", "vault" }))
            throw new InvalidOperationException(
                "Fallout connected wall-volume profiles drifted.");

        var dressingSource = contract.GetProperty("surfaceDressing");
        if (dressingSource.GetProperty("schema").GetString() !=
            "opennv-fo1-owned-cave-wall-dressing/v1")
            throw new InvalidOperationException(
                "Unexpected Fallout owned cave-wall dressing contract.");
        var dressingEnabled = dressingSource.GetProperty("enabled").GetBoolean();
        var dressingProfiles = dressingSource.GetProperty("profiles").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var dressingRole = dressingSource.GetProperty("assetRole").GetString()!;
        var dressingPrototypes = prototypes.Values
            .Where(candidate => candidate.Role == dressingRole)
            .ToArray();
        var hiddenSurfaceIdentities = dressingSource
            .GetProperty("hiddenSurfaceIdentities")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var dressing = new WallSurfaceDressing(
            dressingEnabled,
            dressingProfiles,
            dressingRole,
            dressingSource.GetProperty("spacingMeters").GetSingle(),
            dressingSource.GetProperty("minimumInstancesPerContour").GetInt32(),
            dressingSource.GetProperty("minimumContourPerimeterMeters").GetSingle(),
            dressingSource.GetProperty("maximumInstances").GetInt32(),
            ReadVector(dressingSource.GetProperty("scale")),
            dressingSource.GetProperty("embedBehindContourMeters").GetSingle(),
            dressingSource.GetProperty("groundSinkMeters").GetSingle(),
            dressingSource.GetProperty("yawOffsetDegrees").GetSingle(),
            dressingSource.GetProperty("yawJitterDegrees").GetSingle(),
            dressingSource.GetProperty("uniformScaleJitterFraction").GetSingle(),
            dressingSource.GetProperty("verticalScaleJitterFraction").GetSingle(),
            hiddenSurfaceIdentities,
            dressingPrototypes.Length == 1 ? dressingPrototypes[0] : null);
        if (dressingProfiles.Count == 0 ||
            !dressingProfiles.IsSubsetOf(profiles.Keys) ||
            dressingPrototypes.Length != 1 ||
            dressing.SpacingMeters is < 1.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat12Point0f ||
            dressing.MinimumInstancesPerContour is < 1 or > Fo1OwnedCaveKitNumericContracts.GeometryInt8 ||
            dressing.MinimumContourPerimeterMeters is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat12Point0f ||
            dressing.MaximumInstances is < 1 or > Fo1OwnedCaveKitNumericContracts.GeometryInt1024 ||
            !dressing.Scale.IsFinite() ||
            dressing.Scale.X is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point1f or > 2.0f ||
            dressing.Scale.Y is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point1f or > 2.0f ||
            dressing.Scale.Z is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point1f or > 2.0f ||
            dressing.EmbedBehindContourMeters is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat5Point0f ||
            dressing.GroundSinkMeters is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point75f ||
            !float.IsFinite(dressing.YawOffsetDegrees) ||
            dressing.YawJitterDegrees is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat25Point0f ||
            dressing.UniformScaleJitterFraction is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point25f ||
            dressing.VerticalScaleJitterFraction is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point25f ||
            dressing.HiddenSurfaceIdentities.Count == 0)
            throw new InvalidOperationException(
                "Fallout owned cave-wall dressing values are invalid.");
        var prototypeSurfaceIdentities = NodeTraversal.Descendants<MeshInstance3D>(dressing.Prototype!.Root)
            .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh?.GetSurfaceCount() ?? 0)
                .Select(surface => RuntimeMaterialLoader.SourceSurfaceIdentity(mesh, surface) ??
                    mesh.GetActiveMaterial(surface)?.ResourceName ??
                    mesh.Mesh!.SurfaceGetMaterial(surface)?.ResourceName ?? string.Empty))
            .ToHashSet(StringComparer.Ordinal);
        if (!dressing.HiddenSurfaceIdentities.IsSubsetOf(prototypeSurfaceIdentities))
            throw new InvalidOperationException(
                "Fallout cave-wall hidden surface identity drifted from the owned prototype: " +
                $"expected=[{string.Join(",", dressing.HiddenSurfaceIdentities)}] " +
                $"actual=[{string.Join(",", prototypeSurfaceIdentities.Order())}]");

        var relief = composition.GetProperty("frmRelief");
        var artifacts = relief.GetProperty("artifacts").EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("id").GetString()!,
                row => new Vector2I(
                    row.GetProperty("width").GetInt32(),
                    row.GetProperty("height").GetInt32()),
                StringComparer.Ordinal);
        var sourcesBySerial = new Dictionary<int, WallVolumeSource>();
        var sourceProfiles = new Dictionary<int, string>();
        var serials = new HashSet<int>();
        var tiles = new HashSet<int>();
        foreach (var row in relief.GetProperty("placements").EnumerateArray())
        {
            var serial = row.GetProperty("serial").GetInt32();
            var tile = row.GetProperty("tile").GetInt32();
            var profileName = row.GetProperty("profile").GetString()!;
            var artifactId = row.GetProperty("artifactId").GetString()!;
            if (!serials.Add(serial) || !tiles.Add(tile) ||
                !profiles.TryGetValue(profileName, out var profile) ||
                !artifacts.TryGetValue(artifactId, out var dimensions))
                throw new InvalidOperationException(
                    $"Fallout connected wall-volume source drifted: {serial}");
            var expected = Fo1HexMath.Center(tile);
            var declared = ReadVector(row.GetProperty("worldMeters"));
            if (!expected.IsEqualApprox(declared))
                throw new InvalidOperationException(
                    $"Fallout connected wall-volume tile drifted: {serial}");
            var radius = Math.Clamp(
                dimensions.X / pixelsPerMeter * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f * radiusFromWidth * profile.RadiusScale,
                minimumRadius * profile.RadiusScale,
                maximumRadius * profile.RadiusScale);
            var height = Math.Clamp(
                dimensions.Y / pixelsPerMeter * heightFromPixels * profile.HeightScale,
                minimumHeight * profile.HeightScale,
                maximumHeight * profile.HeightScale);
            if (!sourcesBySerial.TryAdd(
                    serial,
                    new WallVolumeSource(serial, tile, declared, radius, height)) ||
                !sourceProfiles.TryAdd(serial, profileName))
                throw new InvalidOperationException(
                    $"Duplicate Fallout connected wall-volume source: {serial}");
        }

        var components = new List<WallVolumeComponent>();
        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        var componentSerials = new HashSet<int>();
        var componentTiles = new HashSet<int>();
        foreach (var row in contract.GetProperty("components").EnumerateArray())
        {
            var id = row.GetProperty("id").GetString()!;
            var profileName = row.GetProperty("profile").GetString()!;
            var sourceSerials = row.GetProperty("serials").EnumerateArray()
                .Select(value => value.GetInt32())
                .ToArray();
            var sourceTiles = row.GetProperty("tiles").EnumerateArray()
                .Select(value => value.GetInt32())
                .ToArray();
            if (!componentIds.Add(id) || !profiles.ContainsKey(profileName) ||
                sourceSerials.Length == 0 || sourceSerials.Length != sourceTiles.Length ||
                sourceSerials.Length != sourceSerials.Distinct().Count() ||
                sourceTiles.Length != sourceTiles.Distinct().Count())
                throw new InvalidOperationException(
                    $"Fallout connected wall-volume component is invalid: {id}");
            var componentSources = sourceSerials
                .Select(serial =>
                {
                    if (!sourcesBySerial.TryGetValue(serial, out var source) ||
                        sourceProfiles[serial] != profileName ||
                        !componentSerials.Add(serial) || !componentTiles.Add(source.Tile))
                        throw new InvalidOperationException(
                            $"Fallout connected wall-volume component source drifted: {id}/{serial}");
                    return source;
                })
                .ToArray();
            if (!componentSources.Select(source => source.Tile)
                    .Order()
                    .SequenceEqual(sourceTiles.Order()))
                throw new InvalidOperationException(
                    $"Fallout connected wall-volume component tile drifted: {id}");
            components.Add(new WallVolumeComponent(id, profileName, componentSources));
        }

        var coverage = contract.GetProperty("coverage");
        var expectedProfiles = coverage.GetProperty("profiles").EnumerateArray()
            .Select(row => row.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        if (!expectedProfiles.SetEquals(profiles.Keys) ||
            coverage.GetProperty("profileMeshes").GetInt32() != components.Count ||
            coverage.GetProperty("sourcePlacements").GetInt32() != serials.Count ||
            coverage.GetProperty("sourceSerials").GetInt32() != serials.Count ||
            coverage.GetProperty("sourceTiles").GetInt32() != tiles.Count ||
            !componentSerials.SetEquals(serials) || !componentTiles.SetEquals(tiles))
            throw new InvalidOperationException(
                "Fallout connected wall-volume coverage drifted.");

        var componentCount = 0;
        var meshCount = 0;
        var surfaceCount = 0;
        var dressingInstanceCount = 0;
        var unifiedMaterialSurfaces = 0;
        foreach (var component in components)
        {
            var profile = profiles[component.Profile];
            var tool = new SurfaceTool();
            tool.Begin(Mesh.PrimitiveType.Triangles);
            var shell = AddConnectedWallShell(
                tool,
                component,
                profile.TextureRepeatMeters,
                rings,
                minimumContourSegments,
                groundSink,
                radialNoise,
                verticalNoise,
                sampleSpacing,
                contourResampleSpacing,
                contourSmoothIterations,
                contourSmoothStrength,
                contourInflation,
                boundaryBulge,
                macroNoiseWavelength,
                microNoiseWavelength,
                noiseSeed,
                noiseBlend,
                dressing);
            if (shell.BoundarySegments < minimumContourSegments ||
                shell.ClosedContours < 1 || shell.Vertices < minimumContourSegments * rings.Length)
                throw new InvalidOperationException(
                    $"Fallout connected wall-volume boundary is incomplete: {component.Id}");
            tool.GenerateNormals();
            tool.GenerateTangents();
            var mesh = tool.Commit() ?? throw new InvalidOperationException(
                $"Could not build Fallout connected wall volume: {component.Id}");
            var root = new Node3D
            {
                Name = $"CAVE_connected-wall-volume_{component.Id}",
            };
            root.SetMeta("fo1_asset_role", "wall-ribbon");
            root.SetMeta("fo1_geometry_contract", contract.GetProperty("schema").GetString()!);
            root.SetMeta("fo1_source_cards_are_geometry", false);
            root.SetMeta("fo1_source_wall_placements", component.Sources.Count);
            root.SetMeta("fo1_boundary_segments", shell.BoundarySegments);
            root.SetMeta("fo1_closed_contours", shell.ClosedContours);
            root.SetMeta("fo1_molded_vertices", shell.Vertices);
            root.SetMeta("fo1_world_triplanar", true);
            root.SetMeta("fo1_cutaway_exempt", false);
            root.SetMeta("fo1_source_tactical_visibility", "hide-wall-volume");
            container.AddChild(root);
            var shellInstance = new MeshInstance3D
            {
                Name = $"CAVE_connected-wall-closure_{component.Id}",
                Mesh = mesh,
                MaterialOverride = profile.Material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            root.AddChild(shellInstance);
            var componentDressingInstances = 0;
            if (dressing.Enabled && dressing.Profiles.Contains(component.Profile))
            {
                foreach (var anchor in shell.DressingAnchors)
                {
                    if (dressingInstanceCount >= dressing.MaximumInstances)
                        throw new InvalidOperationException(
                            "Fallout cave-wall dressing exceeded its configured instance limit.");
                    var instance = dressing.Prototype!.Root.Duplicate() as Node3D
                        ?? throw new InvalidOperationException(
                            $"Could not duplicate the owned cave-wall dressing: {component.Id}");
                    var uniform = 1.0f + dressing.UniformScaleJitterFraction * Noise(
                        noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt401,
                        anchor.IdentityOne,
                        anchor.IdentityTwo);
                    var vertical = 1.0f + dressing.VerticalScaleJitterFraction * Noise(
                        noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt419,
                        anchor.IdentityTwo,
                        anchor.IdentityOne);
                    instance.Name = $"CAVE_owned-wall-relief_{component.Id}_{anchor.Index:000}";
                    instance.Scale = new Vector3(
                        instance.Scale.X * dressing.Scale.X * uniform,
                        instance.Scale.Y * dressing.Scale.Y * uniform * vertical,
                        instance.Scale.Z * dressing.Scale.Z * uniform);
                    var yaw = Mathf.RadToDeg(MathF.Atan2(
                        anchor.Outward.X,
                        anchor.Outward.Y)) + dressing.YawOffsetDegrees +
                        dressing.YawJitterDegrees * Noise(
                            noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt433,
                            anchor.IdentityOne,
                            anchor.IdentityTwo);
                    instance.RotationDegrees = new Vector3(0.0f, yaw, 0.0f);
                    instance.Position = new Vector3(
                        anchor.Position.X - anchor.Outward.X * dressing.EmbedBehindContourMeters,
                        0.0f,
                        anchor.Position.Y - anchor.Outward.Y * dressing.EmbedBehindContourMeters);
                    instance.SetMeta("fo1_asset_role", "wall-relief-owned");
                    instance.SetMeta("fo1_presentation_only", true);
                    instance.SetMeta("fo1_source_component", component.Id);
                    instance.SetMeta("fo1_contour_index", anchor.ContourIndex);
                    instance.SetMeta("fo1_contour_distance_meters", anchor.DistanceMeters);
                    HideOwnedWallDressingSurfaces(
                        instance,
                        dressing.HiddenSurfaceIdentities,
                        $"{component.Id}/{anchor.Index}");
                    unifiedMaterialSurfaces += ApplyConnectedWallSurfaceMaterial(
                        instance,
                        profile.Material,
                        $"{component.Id}/{anchor.Index}");
                    root.AddChild(instance);
                    var bounds = WorldBounds(instance);
                    instance.Position += Vector3.Up *
                        (-dressing.GroundSinkMeters - bounds.Position.Y);
                    instance.SetMeta("fo1_grounded_to_floor", true);
                    instance.SetMeta("fo1_ground_sink_meters", dressing.GroundSinkMeters);
                    componentDressingInstances++;
                    dressingInstanceCount++;
                    meshCount += dressing.Prototype.Meshes;
                    surfaceCount += dressing.Prototype.Surfaces;
                }
            }
            root.SetMeta("fo1_owned_wall_relief_instances", componentDressingInstances);
            root.SetMeta("fo1_owned_wall_relief_role", dressing.AssetRole);
            componentCount++;
            meshCount++;
            surfaceCount++;
        }
        if (dressing.Enabled && dressingInstanceCount == 0)
            throw new InvalidOperationException(
                "Fallout cave-wall dressing produced no owned relief instances.");
        return new ReliefCoverage(
            componentCount,
            meshCount,
            surfaceCount,
            componentCount * 2,
            unifiedMaterialSurfaces,
            profiles["cave"].Material);
    }

    private static MoldedWallCoverage AddConnectedWallShell(
        SurfaceTool tool,
        WallVolumeComponent component,
        float textureRepeatMeters,
        IReadOnlyList<WallVolumeRing> rings,
        int minimumBoundarySegments,
        float groundSinkMeters,
        float radialNoiseFraction,
        float verticalNoiseMeters,
        float sampleSpacingMeters,
        float contourResampleSpacingMeters,
        int contourSmoothIterations,
        float contourSmoothStrength,
        float contourInflationMeters,
        float boundaryBulgeMeters,
        float macroNoiseWavelengthMeters,
        float microNoiseWavelengthMeters,
        int noiseSeed,
        WallNoiseBlend noiseBlend,
        WallSurfaceDressing dressing)
    {
        var sources = component.Sources;
        var minimumX = sources.Min(source => source.Center.X - source.Radius) -
            sampleSpacingMeters;
        var maximumX = sources.Max(source => source.Center.X + source.Radius) +
            sampleSpacingMeters;
        var minimumZ = sources.Min(source => source.Center.Z - source.Radius) -
            sampleSpacingMeters;
        var maximumZ = sources.Max(source => source.Center.Z + source.Radius) +
            sampleSpacingMeters;
        minimumX = MathF.Floor(minimumX / sampleSpacingMeters) * sampleSpacingMeters;
        maximumX = MathF.Ceiling(maximumX / sampleSpacingMeters) * sampleSpacingMeters;
        minimumZ = MathF.Floor(minimumZ / sampleSpacingMeters) * sampleSpacingMeters;
        maximumZ = MathF.Ceiling(maximumZ / sampleSpacingMeters) * sampleSpacingMeters;
        var columns = (int)MathF.Round((maximumX - minimumX) / sampleSpacingMeters) + 1;
        var rows = (int)MathF.Round((maximumZ - minimumZ) / sampleSpacingMeters) + 1;
        if (columns < 3 || rows < 3)
            throw new InvalidOperationException(
                $"Fallout connected wall-volume sampling grid is incomplete: {component.Id}");

        var field = new float[columns, rows];
        for (var x = 0; x < columns; x++)
            for (var z = 0; z < rows; z++)
                field[x, z] = WallVolumeField(
                    new Vector2(
                        minimumX + x * sampleSpacingMeters,
                        minimumZ + z * sampleSpacingMeters),
                    sources);

        var segments = new List<(Vector2 First, Vector2 Second)>();
        for (var x = 0; x < columns - 1; x++)
            for (var z = 0; z < rows - 1; z++)
            {
                var corners = new[]
                {
                    new Vector2(
                        minimumX + x * sampleSpacingMeters,
                        minimumZ + z * sampleSpacingMeters),
                    new Vector2(
                        minimumX + (x + 1) * sampleSpacingMeters,
                        minimumZ + z * sampleSpacingMeters),
                    new Vector2(
                        minimumX + (x + 1) * sampleSpacingMeters,
                        minimumZ + (z + 1) * sampleSpacingMeters),
                    new Vector2(
                        minimumX + x * sampleSpacingMeters,
                        minimumZ + (z + 1) * sampleSpacingMeters),
                };
                var values = new[]
                {
                    field[x, z],
                    field[x + 1, z],
                    field[x + 1, z + 1],
                    field[x, z + 1],
                };
                var configuration = 0;
                for (var index = 0; index < values.Length; index++)
                    if (values[index] >= 0.0f)
                        configuration |= 1 << index;
                if (configuration is 0 or Fo1OwnedCaveKitNumericContracts.GeometryInt15)
                    continue;

                Vector2 Edge(int edge)
                {
                    var (first, second) = edge switch
                    {
                        0 => (0, 1),
                        1 => (1, 2),
                        2 => (2, 3),
                        3 => (3, 0),
                        _ => throw new InvalidOperationException(
                            "Fallout connected wall-volume marching edge is invalid."),
                    };
                    var denominator = values[first] - values[second];
                    var amount = Mathf.IsZeroApprox(denominator)
                        ? Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f
                        : Math.Clamp(values[first] / denominator, 0.0f, 1.0f);
                    return corners[first].Lerp(corners[second], amount);
                }

                void Add(int firstEdge, int secondEdge) =>
                    segments.Add((Edge(firstEdge), Edge(secondEdge)));
                var centerInside = WallVolumeField(
                    (corners[0] + corners[2]) * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f,
                    sources) >= 0.0f;
                switch (configuration)
                {
                    case 1: Add(3, 0); break;
                    case 2: Add(0, 1); break;
                    case 3: Add(3, 1); break;
                    case 4: Add(1, 2); break;
                    case Fo1OwnedCaveKitNumericContracts.GeometryInt5:
                        if (centerInside)
                        {
                            Add(0, 1);
                            Add(2, 3);
                        }
                        else
                        {
                            Add(3, 0);
                            Add(1, 2);
                        }
                        break;
                    case Fo1OwnedCaveKitNumericContracts.GeometryInt6: Add(0, 2); break;
                    case Fo1OwnedCaveKitNumericContracts.GeometryInt7: Add(3, 2); break;
                    case Fo1OwnedCaveKitNumericContracts.GeometryInt8: Add(2, 3); break;
                    case Fo1OwnedCaveKitNumericContracts.GeometryInt9: Add(2, 0); break;
                    case Fo1OwnedCaveKitNumericContracts.GeometryInt10:
                        if (centerInside)
                        {
                            Add(3, 0);
                            Add(1, 2);
                        }
                        else
                        {
                            Add(0, 1);
                            Add(2, 3);
                        }
                        break;
                    case Fo1OwnedCaveKitNumericContracts.GeometryInt11: Add(2, 1); break;
                    case Fo1OwnedCaveKitNumericContracts.GeometryInt12: Add(1, 3); break;
                    case Fo1OwnedCaveKitNumericContracts.GeometryInt13: Add(1, 0); break;
                    case Fo1OwnedCaveKitNumericContracts.GeometryInt14: Add(0, 3); break;
                    default:
                        throw new InvalidOperationException(
                            "Fallout connected wall-volume marching configuration is invalid.");
                }
            }
        // Interpolated marching segments can meet four-at-a-corner when the
        // exact source field crosses a sampled vertex.  Extract the same field
        // as closed cell boundaries before sculpting so a saddle can never
        // turn one component into an open visual strip.
        segments = BuildClosedWallCellSegments(
            sources,
            minimumX,
            minimumZ,
            columns,
            rows,
            sampleSpacingMeters);
        if (segments.Count < minimumBoundarySegments)
            return new MoldedWallCoverage(segments.Count, 0, 0, []);

        var contours = BuildClosedWallContours(segments, sampleSpacingMeters);
        var vertexOffset = 0;
        var closedContours = 0;
        var dressingAnchors = new List<WallDressingAnchor>();
        foreach (var sourceContour in contours)
        {
            var contour = ResampleClosedWallContour(
                sourceContour,
                contourResampleSpacingMeters,
                minimumBoundarySegments);
            SmoothClosedWallContour(
                contour,
                contourSmoothIterations,
                contourSmoothStrength);
            OrientWallContourOutward(contour, sources, sampleSpacingMeters);
            AddMoldedWallContour(
                tool,
                contour,
                sources,
                rings,
                textureRepeatMeters,
                groundSinkMeters,
                radialNoiseFraction,
                verticalNoiseMeters,
                contourInflationMeters,
                boundaryBulgeMeters,
                macroNoiseWavelengthMeters,
                microNoiseWavelengthMeters,
                contourSmoothIterations,
                contourSmoothStrength,
                noiseSeed,
                noiseBlend,
                closedContours,
                ref vertexOffset);
            if (dressing.Enabled && dressing.Profiles.Contains(component.Profile))
                dressingAnchors.AddRange(BuildWallDressingAnchors(
                    contour,
                    dressing,
                    component.Sources.Min(source => source.Serial),
                    component.Sources.Min(source => source.Tile),
                    closedContours,
                    dressingAnchors.Count));
            closedContours++;
        }
        return new MoldedWallCoverage(
            segments.Count,
            closedContours,
            vertexOffset,
            dressingAnchors);
    }

    private static float WallVolumeField(
        Vector2 point,
        IReadOnlyList<WallVolumeSource> sources) =>
        sources.Max(source => source.Radius - point.DistanceTo(
            new Vector2(source.Center.X, source.Center.Z)));

    private static float WallVolumeHeight(
        Vector2 point,
        IReadOnlyList<WallVolumeSource> sources) =>
        sources.MinBy(source => point.DistanceSquaredTo(
            new Vector2(source.Center.X, source.Center.Z))).Height;

    private static List<(Vector2 First, Vector2 Second)> BuildClosedWallCellSegments(
        IReadOnlyList<WallVolumeSource> sources,
        float minimumX,
        float minimumZ,
        int columns,
        int rows,
        float sampleSpacingMeters)
    {
        var cellColumns = columns - 1;
        var cellRows = rows - 1;
        var inside = new bool[cellColumns, cellRows];
        for (var x = 0; x < cellColumns; x++)
            for (var z = 0; z < cellRows; z++)
                inside[x, z] = WallVolumeField(
                    new Vector2(
                        minimumX + (x + Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f) * sampleSpacingMeters,
                        minimumZ + (z + Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f) * sampleSpacingMeters),
                    sources) >= 0.0f;

        var result = new List<(Vector2 First, Vector2 Second)>();
        Vector2 Point(int x, int z) => new(
            minimumX + x * sampleSpacingMeters,
            minimumZ + z * sampleSpacingMeters);
        for (var x = 0; x < cellColumns; x++)
            for (var z = 0; z < cellRows; z++)
            {
                if (!inside[x, z])
                    continue;
                if (x == 0 || !inside[x - 1, z])
                    result.Add((Point(x, z + 1), Point(x, z)));
                if (z == 0 || !inside[x, z - 1])
                    result.Add((Point(x, z), Point(x + 1, z)));
                if (x == cellColumns - 1 || !inside[x + 1, z])
                    result.Add((Point(x + 1, z), Point(x + 1, z + 1)));
                if (z == cellRows - 1 || !inside[x, z + 1])
                    result.Add((Point(x + 1, z + 1), Point(x, z + 1)));
            }
        return result;
    }

    private static IReadOnlyList<Vector2[]> BuildClosedWallContours(
        IReadOnlyList<(Vector2 First, Vector2 Second)> segments,
        float sampleSpacingMeters)
    {
        var quantization = MathF.Max(sampleSpacingMeters * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point001f, Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point00001f);
        WallContourKey Key(Vector2 point) => new(
            (int)MathF.Round(point.X / quantization),
            (int)MathF.Round(point.Y / quantization));
        var adjacency = new Dictionary<WallContourKey, List<int>>();
        for (var index = 0; index < segments.Count; index++)
        {
            foreach (var key in new[] { Key(segments[index].First), Key(segments[index].Second) })
            {
                if (!adjacency.TryGetValue(key, out var indices))
                {
                    indices = [];
                    adjacency.Add(key, indices);
                }
                indices.Add(index);
            }
        }

        var consumed = new bool[segments.Count];
        var contours = new List<Vector2[]>();
        for (var startSegment = 0; startSegment < segments.Count; startSegment++)
        {
            if (consumed[startSegment])
                continue;
            var start = segments[startSegment].First;
            var previous = start;
            var current = segments[startSegment].Second;
            var startKey = Key(start);
            var contour = new List<Vector2> { start };
            consumed[startSegment] = true;
            var closed = false;
            for (var step = 0; step <= segments.Count; step++)
            {
                if (Key(current) == startKey)
                {
                    closed = true;
                    break;
                }
                contour.Add(current);
                if (!adjacency.TryGetValue(Key(current), out var candidates))
                    break;
                var incoming = (current - previous).Normalized();
                var selected = -1;
                var selectedPoint = Vector2.Zero;
                var selectedAlignment = float.NegativeInfinity;
                foreach (var candidate in candidates)
                {
                    if (consumed[candidate])
                        continue;
                    var segment = segments[candidate];
                    var next = Key(segment.First) == Key(current)
                        ? segment.Second
                        : segment.First;
                    var direction = (next - current).Normalized();
                    var alignment = incoming.Dot(direction);
                    if (alignment <= selectedAlignment)
                        continue;
                    selected = candidate;
                    selectedPoint = next;
                    selectedAlignment = alignment;
                }
                if (selected < 0)
                    break;
                consumed[selected] = true;
                previous = current;
                current = selectedPoint;
            }
            if (!closed || contour.Count < 3)
                throw new InvalidOperationException(
                    "Fallout molded wall contour did not close over its source field.");
            contours.Add(contour.ToArray());
        }
        if (consumed.Any(value => !value) || contours.Count == 0)
            throw new InvalidOperationException(
                "Fallout molded wall contour coverage is incomplete.");
        return contours;
    }

    private static Vector2[] ResampleClosedWallContour(
        IReadOnlyList<Vector2> source,
        float spacingMeters,
        int minimumSegments)
    {
        var lengths = new float[source.Count];
        var perimeter = 0.0f;
        for (var index = 0; index < source.Count; index++)
        {
            lengths[index] = source[index].DistanceTo(source[(index + 1) % source.Count]);
            perimeter += lengths[index];
        }
        if (perimeter <= spacingMeters * 2.0f)
            throw new InvalidOperationException("Fallout molded wall contour is degenerate.");
        var count = Math.Max(minimumSegments, (int)MathF.Ceiling(perimeter / spacingMeters));
        var result = new Vector2[count];
        var segment = 0;
        var segmentStartDistance = 0.0f;
        for (var index = 0; index < count; index++)
        {
            var distance = perimeter * index / count;
            while (segment < lengths.Length - 1 &&
                distance > segmentStartDistance + lengths[segment])
            {
                segmentStartDistance += lengths[segment];
                segment++;
            }
            var amount = lengths[segment] <= Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point000001f
                ? 0.0f
                : (distance - segmentStartDistance) / lengths[segment];
            result[index] = source[segment].Lerp(
                source[(segment + 1) % source.Count],
                Math.Clamp(amount, 0.0f, 1.0f));
        }
        return result;
    }

    private static void SmoothClosedWallContour(
        Vector2[] contour,
        int iterations,
        float strength)
    {
        var next = new Vector2[contour.Length];
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            for (var index = 0; index < contour.Length; index++)
                next[index] = contour[index] * (1.0f - strength * 2.0f) +
                    (contour[(index + contour.Length - 1) % contour.Length] +
                        contour[(index + 1) % contour.Length]) * strength;
            Array.Copy(next, contour, contour.Length);
        }
    }

    private static void SmoothClosedWallScalars(
        float[] values,
        int iterations,
        float strength)
    {
        var next = new float[values.Length];
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            for (var index = 0; index < values.Length; index++)
                next[index] = values[index] * (1.0f - strength * 2.0f) +
                    (values[(index + values.Length - 1) % values.Length] +
                        values[(index + 1) % values.Length]) * strength;
            Array.Copy(next, values, values.Length);
        }
    }

    private static void OrientWallContourOutward(
        Vector2[] contour,
        IReadOnlyList<WallVolumeSource> sources,
        float sampleSpacingMeters)
    {
        for (var index = 0; index < contour.Length; index++)
        {
            var previous = contour[(index + contour.Length - 1) % contour.Length];
            var next = contour[(index + 1) % contour.Length];
            var tangent = (next - previous).Normalized();
            if (tangent.IsZeroApprox())
                continue;
            var left = new Vector2(-tangent.Y, tangent.X);
            if (WallVolumeField(contour[index] + left * sampleSpacingMeters, sources) >
                WallVolumeField(contour[index] - left * sampleSpacingMeters, sources))
                Array.Reverse(contour);
            return;
        }
        throw new InvalidOperationException("Fallout molded wall contour has no tangent.");
    }

    private static IReadOnlyList<WallDressingAnchor> BuildWallDressingAnchors(
        IReadOnlyList<Vector2> contour,
        WallSurfaceDressing dressing,
        int identitySerial,
        int identityTile,
        int contourIndex,
        int firstIndex)
    {
        var segmentLengths = new float[contour.Count];
        var perimeter = 0.0f;
        for (var index = 0; index < contour.Count; index++)
        {
            segmentLengths[index] = contour[index].DistanceTo(
                contour[(index + 1) % contour.Count]);
            perimeter += segmentLengths[index];
        }
        if (perimeter < dressing.MinimumContourPerimeterMeters)
            return [];
        var count = Math.Max(
            dressing.MinimumInstancesPerContour,
            (int)MathF.Round(perimeter / dressing.SpacingMeters));
        var interval = perimeter / count;
        var anchors = new List<WallDressingAnchor>(count);
        for (var anchorIndex = 0; anchorIndex < count; anchorIndex++)
        {
            var target = (anchorIndex + Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f) * interval;
            var accumulated = 0.0f;
            for (var segment = 0; segment < contour.Count; segment++)
            {
                var length = segmentLengths[segment];
                if (target > accumulated + length && segment < contour.Count - 1)
                {
                    accumulated += length;
                    continue;
                }
                if (length <= Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point00001f)
                    break;
                var first = contour[segment];
                var second = contour[(segment + 1) % contour.Count];
                var tangent = (second - first).Normalized();
                var outward = new Vector2(-tangent.Y, tangent.X);
                var amount = Math.Clamp((target - accumulated) / length, 0.0f, 1.0f);
                anchors.Add(new WallDressingAnchor(
                    firstIndex + anchorIndex,
                    contourIndex,
                    target,
                    first.Lerp(second, amount),
                    outward,
                    identitySerial + contourIndex,
                    identityTile + anchorIndex));
                break;
            }
        }
        if (anchors.Count != count)
            throw new InvalidOperationException(
                "Fallout cave-wall contour dressing sampling was incomplete.");
        return anchors;
    }

    private static float PeriodicWallNoise(
        float distance,
        float perimeter,
        float wavelength,
        int seed,
        int identityOne,
        int identityTwo,
        WallNoiseBlend blend)
    {
        var cycles = Math.Max(1, (int)MathF.Round(perimeter / wavelength));
        var phase = Noise(seed, identityOne, identityTwo) * MathF.PI;
        var normalized = distance / perimeter;
        return MathF.Sin(Mathf.Tau * cycles * normalized + phase) *
                blend.PeriodicPrimaryWeight +
            MathF.Sin(
                Mathf.Tau *
                    (cycles * blend.PeriodicSecondaryFrequencyMultiplier +
                        blend.PeriodicSecondaryFrequencyOffset) * normalized -
                phase * blend.PeriodicSecondaryPhaseScale) *
                blend.PeriodicSecondaryWeight;
    }

    private static void AddMoldedWallContour(
        SurfaceTool tool,
        IReadOnlyList<Vector2> contour,
        IReadOnlyList<WallVolumeSource> sources,
        IReadOnlyList<WallVolumeRing> rings,
        float textureRepeatMeters,
        float groundSinkMeters,
        float radialNoiseFraction,
        float verticalNoiseMeters,
        float contourInflationMeters,
        float boundaryBulgeMeters,
        float macroNoiseWavelengthMeters,
        float microNoiseWavelengthMeters,
        int contourSmoothIterations,
        float contourSmoothStrength,
        int noiseSeed,
        WallNoiseBlend noiseBlend,
        int contourIndex,
        ref int vertexOffset)
    {
        var pointCount = contour.Count;
        var distances = new float[pointCount];
        var heights = new float[pointCount];
        var radii = new float[pointCount];
        var perimeter = 0.0f;
        for (var index = 0; index < pointCount; index++)
        {
            if (index > 0)
            {
                perimeter += contour[index - 1].DistanceTo(contour[index]);
                distances[index] = perimeter;
            }
            var nearest = sources.MinBy(source => contour[index].DistanceSquaredTo(
                new Vector2(source.Center.X, source.Center.Z)));
            heights[index] = nearest.Height;
            radii[index] = nearest.Radius;
        }
        perimeter += contour[^1].DistanceTo(contour[0]);
        SmoothClosedWallScalars(heights, contourSmoothIterations, contourSmoothStrength);
        SmoothClosedWallScalars(radii, contourSmoothIterations, contourSmoothStrength);

        var identitySerial = sources.Min(source => source.Serial);
        var identityTile = sources.Min(source => source.Tile);
        var baseVertex = vertexOffset;
        for (var ringIndex = 0; ringIndex < rings.Count; ringIndex++)
        {
            var ring = rings[ringIndex];
            for (var index = 0; index < pointCount; index++)
            {
                var previous = contour[(index + pointCount - 1) % pointCount];
                var next = contour[(index + 1) % pointCount];
                var tangent = (next - previous).Normalized();
                var outward = new Vector2(-tangent.Y, tangent.X);
                var macro = PeriodicWallNoise(
                    distances[index],
                    perimeter,
                    macroNoiseWavelengthMeters,
                    noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt107 + contourIndex * Fo1OwnedCaveKitNumericContracts.GeometryInt31,
                    identitySerial,
                    identityTile,
                    noiseBlend);
                var ringMacro = PeriodicWallNoise(
                    distances[index],
                    perimeter,
                    macroNoiseWavelengthMeters *
                        (noiseBlend.RingWavelengthBase +
                            ring.HeightFraction * noiseBlend.RingWavelengthHeightScale),
                    noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt211 + ringIndex * Fo1OwnedCaveKitNumericContracts.GeometryInt37 + contourIndex * Fo1OwnedCaveKitNumericContracts.GeometryInt19,
                    identityTile,
                    identitySerial,
                    noiseBlend);
                var micro = PeriodicWallNoise(
                    distances[index],
                    perimeter,
                    microNoiseWavelengthMeters,
                    noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt149 + ringIndex * Fo1OwnedCaveKitNumericContracts.GeometryInt17,
                    identityTile,
                    identitySerial + contourIndex,
                    noiseBlend);
                var profileOffset = contourInflationMeters +
                    radii[index] * (ring.RadiusMultiplier - 1.0f) +
                    boundaryBulgeMeters *
                        (macro * noiseBlend.MacroWeight +
                            ringMacro * noiseBlend.RingMacroWeight +
                            micro * radialNoiseFraction * noiseBlend.MicroRadialWeight +
                            micro * ring.CenterJitterFraction *
                                noiseBlend.MicroJitterWeight);
                var planar = contour[index] + outward * profileOffset;
                var vertical = -groundSinkMeters +
                    ring.HeightFraction * (heights[index] + groundSinkMeters) +
                    verticalNoiseMeters * MathF.Sin(MathF.PI * ring.HeightFraction) *
                        (ringMacro * noiseBlend.VerticalMacroWeight +
                            micro * noiseBlend.VerticalMicroWeight);
                tool.SetUV(new Vector2(planar.X, vertical) / textureRepeatMeters);
                tool.AddVertex(new Vector3(planar.X, vertical, planar.Y));
                vertexOffset++;
            }
        }

        for (var ringIndex = 0; ringIndex < rings.Count - 1; ringIndex++)
            for (var index = 0; index < pointCount; index++)
            {
                var next = (index + 1) % pointCount;
                var lower = baseVertex + ringIndex * pointCount + index;
                var lowerNext = baseVertex + ringIndex * pointCount + next;
                var upper = baseVertex + (ringIndex + 1) * pointCount + index;
                var upperNext = baseVertex + (ringIndex + 1) * pointCount + next;
                tool.AddIndex(lower);
                tool.AddIndex(upperNext);
                tool.AddIndex(upper);
                tool.AddIndex(lower);
                tool.AddIndex(lowerNext);
                tool.AddIndex(upperNext);
            }
    }

    private static void AddWallBoundaryStrip(
        SurfaceTool tool,
        Vector2 first,
        Vector2 second,
        IReadOnlyList<WallVolumeSource> sources,
        IReadOnlyList<WallVolumeRing> rings,
        float textureRepeatMeters,
        float groundSinkMeters,
        float radialNoiseFraction,
        float verticalNoiseMeters,
        int boundarySubdivisions,
        float boundaryBulgeMeters,
        float sampleSpacingMeters,
        Vector2 uvOffset,
        int noiseSeed,
        int segmentIndex)
    {
        var direction = (second - first).Normalized();
        var outward = new Vector2(-direction.Y, direction.X);
        var midpoint = (first + second) * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f;
        var probeDistance = sampleSpacingMeters * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f;
        if (WallVolumeField(midpoint + outward * probeDistance, sources) >
            WallVolumeField(midpoint - outward * probeDistance, sources))
            outward = -outward;
        var points = new Vector3[rings.Count, boundarySubdivisions + 1];
        var normals = new Vector3[rings.Count, boundarySubdivisions + 1];
        var uvs = new Vector2[rings.Count, boundarySubdivisions + 1];
        for (var subdivision = 0; subdivision <= boundarySubdivisions; subdivision++)
        {
            var amount = subdivision / (float)boundarySubdivisions;
            var basePoint = first.Lerp(second, amount);
            var edgeInfluence = MathF.Sin(MathF.PI * amount);
            var quantizedX = (int)MathF.Round(basePoint.X / sampleSpacingMeters);
            var quantizedZ = (int)MathF.Round(basePoint.Y / sampleSpacingMeters);
            var height = WallVolumeHeight(basePoint, sources);
            for (var ringIndex = 0; ringIndex < rings.Count; ringIndex++)
            {
                var ring = rings[ringIndex];
                var contourNoise = Noise(
                    noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt107,
                    quantizedX + segmentIndex,
                    quantizedZ + ringIndex);
                var profileOffset = boundaryBulgeMeters * edgeInfluence *
                    ((ring.RadiusMultiplier - 1.0f) +
                        contourNoise * radialNoiseFraction +
                        ring.CenterJitterFraction * contourNoise);
                var planar = basePoint + outward * profileOffset;
                var vertical = -groundSinkMeters +
                    ring.HeightFraction * (height + groundSinkMeters) +
                    verticalNoiseMeters * MathF.Sin(MathF.PI * ring.HeightFraction) *
                        Noise(noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt127, quantizedX, quantizedZ + ringIndex);
                points[ringIndex, subdivision] = new Vector3(planar.X, vertical, planar.Y);
                normals[ringIndex, subdivision] = new Vector3(
                    outward.X,
                    ring.HeightFraction >= 1.0f ? Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point18f : Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point04f,
                    outward.Y).Normalized();
                var horizontal = planar.Dot(Vector2.One.Normalized());
                uvs[ringIndex, subdivision] = new Vector2(
                    horizontal / textureRepeatMeters,
                    vertical / textureRepeatMeters) + uvOffset;
            }
        }

        for (var ringIndex = 0; ringIndex < rings.Count - 1; ringIndex++)
            for (var subdivision = 0; subdivision < boundarySubdivisions; subdivision++)
            {
                AddWallShellVertex(
                    tool,
                    points[ringIndex, subdivision],
                    uvs[ringIndex, subdivision]);
                AddWallShellVertex(
                    tool,
                    points[ringIndex + 1, subdivision + 1],
                    uvs[ringIndex + 1, subdivision + 1]);
                AddWallShellVertex(
                    tool,
                    points[ringIndex + 1, subdivision],
                    uvs[ringIndex + 1, subdivision]);
                AddWallShellVertex(
                    tool,
                    points[ringIndex, subdivision],
                    uvs[ringIndex, subdivision]);
                AddWallShellVertex(
                    tool,
                    points[ringIndex, subdivision + 1],
                    uvs[ringIndex, subdivision + 1]);
                AddWallShellVertex(
                    tool,
                    points[ringIndex + 1, subdivision + 1],
                    uvs[ringIndex + 1, subdivision + 1]);
            }
    }

    private static void AddWallShellVertex(
        SurfaceTool tool,
        Vector3 point,
        Vector2 uv)
    {
        tool.SetUV(uv);
        tool.AddVertex(point);
    }

    private static void AddWallRockMass(
        SurfaceTool tool,
        WallVolumeSource source,
        float textureRepeatMeters,
        IReadOnlyList<WallVolumeRing> rings,
        int radialSegments,
        float groundSinkMeters,
        float radialNoiseFraction,
        float verticalNoiseMeters,
        float ringTwistRadians,
        float uvRandomOffsetRepeats,
        int noiseSeed)
    {
        var points = new Vector3[rings.Count, radialSegments];
        var normals = new Vector3[rings.Count, radialSegments];
        var phase = Noise(noiseSeed, source.Serial, source.Tile) * MathF.PI;
        var uvOffset = new Vector2(
            (Noise(noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt73, source.Serial, source.Tile) + 1.0f) *
                Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f * uvRandomOffsetRepeats,
            (Noise(noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt89, source.Tile, source.Serial) + 1.0f) *
                Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f * uvRandomOffsetRepeats);
        for (var ringIndex = 0; ringIndex < rings.Count; ringIndex++)
        {
            var ring = rings[ringIndex];
            var jitter = source.Radius * ring.CenterJitterFraction;
            var center = source.Center + new Vector3(
                Noise(noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt17, source.Serial, ringIndex) * jitter,
                0.0f,
                Noise(noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt31, source.Tile, ringIndex) * jitter);
            var baseHeight = -groundSinkMeters +
                ring.HeightFraction * (source.Height + groundSinkMeters);
            for (var segment = 0; segment < radialSegments; segment++)
            {
                var angle = phase + Mathf.Tau * segment / radialSegments +
                    Noise(noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt101, source.Serial, ringIndex) * ringTwistRadians;
                var direction = new Vector3(MathF.Cos(angle), 0.0f, MathF.Sin(angle));
                var radiusNoise = 1.0f + radialNoiseFraction *
                    Noise(noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt47, source.Serial * radialSegments + segment, ringIndex);
                var heightNoise = ringIndex == 0 || ringIndex == rings.Count - 1
                    ? 0.0f
                    : verticalNoiseMeters *
                        Noise(noiseSeed + Fo1OwnedCaveKitNumericContracts.GeometryInt59, source.Tile * radialSegments + segment, ringIndex);
                points[ringIndex, segment] = center +
                    direction * source.Radius * ring.RadiusMultiplier * radiusNoise +
                    Vector3.Up * (baseHeight + heightNoise);
                normals[ringIndex, segment] = new Vector3(
                    direction.X,
                    ringIndex == rings.Count - 1 ? Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point35f : Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point08f,
                    direction.Z).Normalized();
            }
        }

        for (var ringIndex = 0; ringIndex < rings.Count - 1; ringIndex++)
            for (var segment = 0; segment < radialSegments; segment++)
            {
                var next = (segment + 1) % radialSegments;
                var lowerFirst = points[ringIndex, segment];
                var lowerSecond = points[ringIndex, next];
                var upperFirst = points[ringIndex + 1, segment];
                var upperSecond = points[ringIndex + 1, next];
                var firstU = segment / (float)radialSegments *
                    Mathf.Tau * source.Radius / textureRepeatMeters;
                var secondU = (segment + 1) / (float)radialSegments *
                    Mathf.Tau * source.Radius / textureRepeatMeters;
                AddWallVolumeVertex(
                    tool,
                    lowerFirst,
                    normals[ringIndex, segment],
                    new Vector2(firstU, lowerFirst.Y / textureRepeatMeters) + uvOffset);
                AddWallVolumeVertex(
                    tool,
                    upperSecond,
                    normals[ringIndex + 1, next],
                    new Vector2(secondU, upperSecond.Y / textureRepeatMeters) + uvOffset);
                AddWallVolumeVertex(
                    tool,
                    upperFirst,
                    normals[ringIndex + 1, segment],
                    new Vector2(firstU, upperFirst.Y / textureRepeatMeters) + uvOffset);
                AddWallVolumeVertex(
                    tool,
                    lowerFirst,
                    normals[ringIndex, segment],
                    new Vector2(firstU, lowerFirst.Y / textureRepeatMeters) + uvOffset);
                AddWallVolumeVertex(
                    tool,
                    lowerSecond,
                    normals[ringIndex, next],
                    new Vector2(secondU, lowerSecond.Y / textureRepeatMeters) + uvOffset);
                AddWallVolumeVertex(
                    tool,
                    upperSecond,
                    normals[ringIndex + 1, next],
                    new Vector2(secondU, upperSecond.Y / textureRepeatMeters) + uvOffset);
            }

        var topCenter = Enumerable.Range(0, radialSegments)
            .Select(segment => points[rings.Count - 1, segment])
            .Aggregate(Vector3.Zero, (sum, point) => sum + point) / radialSegments;
        var bottomCenter = Enumerable.Range(0, radialSegments)
            .Select(segment => points[0, segment])
            .Aggregate(Vector3.Zero, (sum, point) => sum + point) / radialSegments;
        for (var segment = 0; segment < radialSegments; segment++)
        {
            var next = (segment + 1) % radialSegments;
            AddWallVolumeVertex(
                tool,
                topCenter,
                Vector3.Up,
                new Vector2(topCenter.X, topCenter.Z) / textureRepeatMeters + uvOffset);
            AddWallVolumeVertex(
                tool,
                points[rings.Count - 1, segment],
                Vector3.Up,
                new Vector2(
                    points[rings.Count - 1, segment].X,
                    points[rings.Count - 1, segment].Z) / textureRepeatMeters + uvOffset);
            AddWallVolumeVertex(
                tool,
                points[rings.Count - 1, next],
                Vector3.Up,
                new Vector2(
                    points[rings.Count - 1, next].X,
                    points[rings.Count - 1, next].Z) / textureRepeatMeters + uvOffset);
            AddWallVolumeVertex(
                tool,
                bottomCenter,
                Vector3.Down,
                new Vector2(bottomCenter.X, bottomCenter.Z) / textureRepeatMeters + uvOffset);
            AddWallVolumeVertex(
                tool,
                points[0, next],
                Vector3.Down,
                new Vector2(points[0, next].X, points[0, next].Z) /
                    textureRepeatMeters + uvOffset);
            AddWallVolumeVertex(
                tool,
                points[0, segment],
                Vector3.Down,
                new Vector2(points[0, segment].X, points[0, segment].Z) /
                    textureRepeatMeters + uvOffset);
        }
    }

    private static void AddWallVolumeVertex(
        SurfaceTool tool,
        Vector3 point,
        Vector3 normal,
        Vector2 uv)
    {
        tool.SetNormal(normal);
        tool.SetUV(uv);
        tool.AddVertex(point);
    }

    private static ReliefCoverage BuildFrmReliefWalls(
        Node3D container,
        JsonElement composition,
        JsonElement caveKit,
        RuntimeMaterialLoader.LoadedTextures textures,
        float canonicalYawDegrees,
        float canonicalPitchDegrees)
    {
        var relief = composition.GetProperty("frmRelief");
        if (relief.GetProperty("schema").GetString() !=
            "opennv-fo1-frm-relief-wall-set/v1")
            throw new InvalidOperationException("Unexpected Fallout FRM relief contract.");
        var pixelsPerMeter = relief.GetProperty("pixelsPerMeter").GetSingle();
        var groundAnchor = relief.GetProperty("groundAnchorMeters").GetSingle();
        var pitchCosine = MathF.Abs(MathF.Cos(Mathf.DegToRad(canonicalPitchDegrees)));
        if (pixelsPerMeter <= 0.0f || groundAnchor is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point1f ||
            pitchCosine < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point25f || !float.IsFinite(canonicalYawDegrees))
            throw new InvalidOperationException("Fallout FRM relief projection is invalid.");
        var verticalCompensation = 1.0f / pitchCosine;
        var frontRoughness = relief.GetProperty("frontRoughness").GetSingle();
        var frontEmission = relief.GetProperty("frontEmissionEnergy").GetSingle();
        var profiles = relief.GetProperty("profiles");
        var sideMaterials = new Dictionary<string, StandardMaterial3D>(StringComparer.Ordinal);
        foreach (var property in profiles.EnumerateObject())
        {
            var profile = property.Value;
            var diffuseId = TextureId(
                caveKit,
                profile.GetProperty("sideDiffusePath").GetString()!);
            var normalId = TextureId(
                caveKit,
                profile.GetProperty("sideNormalPath").GetString()!);
            sideMaterials.Add(property.Name, new StandardMaterial3D
            {
                ResourceName = $"FO1 FRM relief {property.Name} generated depth",
                AlbedoTexture = textures.TwoDimensional[diffuseId],
                AlbedoColor = ReadColor(profile.GetProperty("sideAlbedoColor")),
                Roughness = profile.GetProperty("sideRoughness").GetSingle(),
                Metallic = 0.0f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                NormalEnabled = true,
                NormalTexture = textures.TwoDimensional[normalId],
                NormalScale = profile.GetProperty("sideNormalScale").GetSingle(),
            });
        }
        if (!new HashSet<string>(sideMaterials.Keys, StringComparer.Ordinal)
                .SetEquals(new[] { "cave", "vault" }))
            throw new InvalidOperationException("Fallout FRM relief profiles drifted.");

        var artifacts = new Dictionary<string, ReliefArtifact>(StringComparer.Ordinal);
        foreach (var source in relief.GetProperty("artifacts").EnumerateArray())
        {
            var id = source.GetProperty("id").GetString()!;
            if (artifacts.ContainsKey(id))
                throw new InvalidOperationException($"Duplicate Fallout FRM relief artifact: {id}");
            var width = source.GetProperty("width").GetInt32();
            var height = source.GetProperty("height").GetInt32();
            var albedo = LoadVerifiedImageTexture(
                source.GetProperty("sourcePng").GetString()!,
                source.GetProperty("sourcePngSha256").GetString()!,
                width,
                height);
            var normal = LoadVerifiedImageTexture(
                source.GetProperty("normalPng").GetString()!,
                source.GetProperty("normalPngSha256").GetString()!,
                width,
                height);
            var frontMaterial = new StandardMaterial3D
            {
                ResourceName = $"FO1 exact FRM relief face {id}",
                AlbedoTexture = albedo,
                AlbedoColor = Colors.White,
                Roughness = frontRoughness,
                Metallic = 0.0f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                AlphaScissorThreshold = Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point01f,
                NormalEnabled = true,
                NormalTexture = normal,
                NormalScale = 1.0f,
                EmissionEnabled = frontEmission > 0.0f,
                EmissionTexture = albedo,
                Emission = Colors.White,
                EmissionEnergyMultiplier = frontEmission,
            };
            var contours = source.GetProperty("contours").EnumerateArray()
                .Select(contour => contour.EnumerateArray()
                    .Select(ReadVector2)
                    .ToArray())
                .ToArray();
            if (width <= 0 || height <= 0 || contours.Length == 0 ||
                contours.Any(contour => contour.Length < 4))
                throw new InvalidOperationException($"Fallout FRM relief contour is invalid: {id}");
            artifacts.Add(id, new ReliefArtifact(
                width,
                height,
                ReadVector2(source.GetProperty("frameOffset")),
                frontMaterial,
                contours));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var serials = new HashSet<int>();
        var placements = 0;
        var contourCount = 0;
        foreach (var source in relief.GetProperty("placements").EnumerateArray())
        {
            var id = source.GetProperty("id").GetString()!;
            var serial = source.GetProperty("serial").GetInt32();
            var tile = source.GetProperty("tile").GetInt32();
            var artifactId = source.GetProperty("artifactId").GetString()!;
            var profileName = source.GetProperty("profile").GetString()!;
            if (!ids.Add(id) || !serials.Add(serial) ||
                !artifacts.TryGetValue(artifactId, out var artifact) ||
                !sideMaterials.TryGetValue(profileName, out var sideMaterial))
                throw new InvalidOperationException($"Fallout FRM relief placement drifted: {id}");
            var expected = Fo1HexMath.Center(tile);
            var declared = ReadVector(source.GetProperty("worldMeters"));
            if (!expected.IsEqualApprox(declared))
                throw new InvalidOperationException($"Fallout FRM relief tile drifted: {id}");
            var profile = profiles.GetProperty(profileName);
            var depth = profile.GetProperty("depthMeters").GetSingle();
            var repeat = profile.GetProperty("sideTextureRepeatMeters").GetSingle();
            var pixelOffset = ReadVector2(source.GetProperty("pixelOffset"));
            var root = new Node3D
            {
                Name = $"CAVE_wall-ribbon_{id}",
                Position = declared,
                RotationDegrees = new Vector3(0.0f, canonicalYawDegrees, 0.0f),
            };
            root.SetMeta("fo1_asset_role", "wall-ribbon");
            root.SetMeta("fo1_cutaway_exempt", false);
            root.SetMeta("fo1_source_serial", serial);
            root.SetMeta("fo1_source_tile", tile);
            container.AddChild(root);

            var visual = new Node3D
            {
                Name = $"FRM_RELIEF_VISUAL_{artifactId}",
                Position = new Vector3(
                    (pixelOffset.X + artifact.FrameOffset.X) / pixelsPerMeter,
                    groundAnchor +
                        (-(pixelOffset.Y + artifact.FrameOffset.Y) + artifact.Height / 2.0f) /
                        pixelsPerMeter * verticalCompensation,
                    0.0f),
                Scale = new Vector3(1.0f, verticalCompensation, 1.0f),
            };
            root.AddChild(visual);
            var face = new QuadMesh
            {
                Size = new Vector2(
                    artifact.Width / pixelsPerMeter,
                    artifact.Height / pixelsPerMeter),
                Material = artifact.FrontMaterial,
            };
            visual.AddChild(new MeshInstance3D
            {
                Name = "ExactFrmFace",
                Mesh = face,
                Position = Vector3.Zero,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            });
            visual.AddChild(new MeshInstance3D
            {
                Name = "ExactFrmBack",
                Mesh = face,
                Position = new Vector3(0.0f, 0.0f, -depth),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            });
            visual.AddChild(new MeshInstance3D
            {
                Name = "ImageDerivedFrmDepth",
                Mesh = BuildReliefSideMesh(
                    artifact.Contours,
                    artifact.Width,
                    artifact.Height,
                    pixelsPerMeter,
                    depth,
                    repeat),
                MaterialOverride = sideMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            });
            contourCount += artifact.Contours.Count;
            placements++;
        }
        var coverage = relief.GetProperty("coverage");
        if (artifacts.Count != coverage.GetProperty("artifacts").GetInt32() ||
            placements != coverage.GetProperty("placements").GetInt32() ||
            contourCount < placements)
            throw new InvalidOperationException("Fallout FRM relief coverage drifted.");
        return new ReliefCoverage(
            placements,
            placements * 3,
            placements * 3,
            placements * 3,
            0,
            null);
    }

    private static ArrayMesh BuildReliefSideMesh(
        IReadOnlyList<Vector2[]> contours,
        int width,
        int height,
        float pixelsPerMeter,
        float depth,
        float repeat)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Point(Vector2 pixel, float z) => new(
            (pixel.X - width / 2.0f) / pixelsPerMeter,
            (height / 2.0f - pixel.Y) / pixelsPerMeter,
            z);
        foreach (var contour in contours)
            for (var index = 0; index < contour.Length; index++)
            {
                var next = (index + 1) % contour.Length;
                var first = Point(contour[index], 0.0f);
                var second = Point(contour[next], 0.0f);
                var secondBack = Point(contour[next], -depth);
                var firstBack = Point(contour[index], -depth);
                AddReliefQuad(tool, first, second, secondBack, firstBack, repeat);
            }
        tool.Index();
        tool.GenerateTangents();
        return tool.Commit() ?? throw new InvalidOperationException(
            "Could not build Fallout image-derived FRM relief depth.");
    }

    private static void AddReliefQuad(
        SurfaceTool tool,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Vector3 fourth,
        float repeat)
    {
        var normal = (second - first).Cross(third - first).Normalized();
        foreach (var point in new[] { first, second, third, first, third, fourth })
        {
            tool.SetNormal(normal);
            tool.SetUV(new Vector2(point.X / repeat, (point.Y - point.Z) / repeat));
            tool.AddVertex(point);
        }
    }
}
