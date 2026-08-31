using System.Security.Cryptography;
using System.Text.Json;
using Godot;

using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1OwnedCaveKitNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float GeometryFloatNEgativE0Point0001f = -0.0001f;
    internal const float GeometryFloatNEgativE0Point05f = -0.05f;
    internal const float GeometryFloatNEgativE0Point10f = -0.10f;
    internal const float GeometryFloatNEgativE0Point20f = -0.20f;
    internal const float GeometryFloatNEgativE0Point3f = -0.3f;
    internal const float GeometryFloat0Point000001f = 0.000001f;
    internal const float GeometryFloat0Point00001f = 0.00001f;
    internal const float GeometryFloat0Point0001f = 0.0001f;
    internal const float GeometryFloat0Point001f = 0.001f;
    internal const float GeometryFloat0Point002f = 0.002f;
    internal const float GeometryFloat0Point005f = 0.005f;
    internal const float GeometryFloat0Point01f = 0.01f;
    internal const float GeometryFloat0Point028f = 0.028f;
    internal const float GeometryFloat0Point04f = 0.04f;
    internal const float GeometryFloat0Point05f = 0.05f;
    internal const float GeometryFloat0Point08f = 0.08f;
    internal const float GeometryFloat0Point095f = 0.095f;
    internal const float GeometryFloat0Point09f = 0.09f;
    internal const float GeometryFloat0Point11f = 0.11f;
    internal const float GeometryFloat0Point135f = 0.135f;
    internal const float GeometryFloat0Point14f = 0.14f;
    internal const float GeometryFloat0Point16f = 0.16f;
    internal const float GeometryFloat0Point18f = 0.18f;
    internal const float GeometryFloat0Point1f = 0.1f;
    internal const float GeometryFloat0Point20f = 0.20f;
    internal const float GeometryFloat0Point22f = 0.22f;
    internal const float GeometryFloat0Point25f = 0.25f;
    internal const float GeometryFloat0Point27f = 0.27f;
    internal const float GeometryFloat0Point2f = 0.2f;
    internal const float GeometryFloat0Point30f = 0.30f;
    internal const float GeometryFloat0Point32f = 0.32f;
    internal const float GeometryFloat0Point35f = 0.35f;
    internal const float GeometryFloat0Point36f = 0.36f;
    internal const float GeometryFloat0Point38f = 0.38f;
    internal const float GeometryFloat0Point3f = 0.3f;
    internal const float GeometryFloat0Point40f = 0.40f;
    internal const float GeometryFloat0Point45f = 0.45f;
    internal const float GeometryFloat0Point50f = 0.50f;
    internal const float GeometryFloat0Point52f = 0.52f;
    internal const float GeometryFloat0Point56f = 0.56f;
    internal const float GeometryFloat0Point58f = 0.58f;
    internal const float GeometryFloat0Point5f = 0.5f;
    internal const float GeometryFloat0Point62f = 0.62f;
    internal const float GeometryFloat0Point64f = 0.64f;
    internal const float GeometryFloat0Point68f = 0.68f;
    internal const float GeometryFloat0Point69f = 0.69f;
    internal const float GeometryFloat0Point6f = 0.6f;
    internal const float GeometryFloat0Point72f = 0.72f;
    internal const float GeometryFloat0Point75f = 0.75f;
    internal const float GeometryFloat0Point76f = 0.76f;
    internal const float GeometryFloat0Point77f = 0.77f;
    internal const float GeometryFloat0Point80f = 0.80f;
    internal const float GeometryFloat0Point82f = 0.82f;
    internal const float GeometryFloat0Point84f = 0.84f;
    internal const float GeometryFloat0Point86f = 0.86f;
    internal const float GeometryFloat0Point8f = 0.8f;
    internal const float GeometryFloat0Point92f = 0.92f;
    internal const float GeometryFloat0Point96f = 0.96f;
    internal const float GeometryFloat1Point0ENEgativE10f = 1.0e-10f;
    internal const float GeometryFloat1Point2f = 1.2f;
    internal const float GeometryFloat1Point65f = 1.65f;
    internal const float GeometryFloat1Point6f = 1.6f;
    internal const float GeometryFloat1Point95f = 1.95f;
    internal const float GeometryFloat1Point9f = 1.9f;
    internal const int GeometryInt10 = 10;
    internal const int GeometryInt100 = 100;
    internal const float GeometryFloat1000Point0f = 1000.0f;
    internal const int GeometryInt101 = 101;
    internal const int GeometryInt1024 = 1024;
    internal const int GeometryInt107 = 107;
    internal const int GeometryInt11 = 11;
    internal const int GeometryInt12 = 12;
    internal const float GeometryFloat12Point0f = 12.0f;
    internal const int GeometryInt127 = 127;
    internal const uint GeometryUint1274126177u = 1274126177u;
    internal const int GeometryInt13 = 13;
    internal const int GeometryInt130013 = 130013;
    internal const int GeometryInt13013 = 13013;
    internal const int GeometryInt14 = 14;
    internal const float GeometryFloat14Point0f = 14.0f;
    internal const int GeometryInt149 = 149;
    internal const int GeometryInt15 = 15;
    internal const int GeometryInt16 = 16;
    internal const float GeometryFloat16Point0f = 16.0f;
    internal const int GeometryInt17 = 17;
    internal const int GeometryInt19 = 19;
    internal const int GeometryInt19349663 = 19349663;
    internal const float GeometryFloat2Point2f = 2.2f;
    internal const float GeometryFloat2Point35f = 2.35f;
    internal const float GeometryFloat2Point5f = 2.5f;
    internal const int GeometryInt2000 = 2000;
    internal const int GeometryInt211 = 211;
    internal const float GeometryFloat25Point0f = 25.0f;
    internal const int GeometryInt271828 = 271828;
    internal const int GeometryInt31 = 31;
    internal const int GeometryInt31337 = 31337;
    internal const float GeometryFloat32767Point5f = 32767.5f;
    internal const int GeometryInt37 = 37;
    internal const int GeometryInt401 = 401;
    internal const int GeometryInt419 = 419;
    internal const int GeometryInt43 = 43;
    internal const int GeometryInt433 = 433;
    internal const int GeometryInt47 = 47;
    internal const int GeometryInt5 = 5;
    internal const float GeometryFloat5Point0f = 5.0f;
    internal const int GeometryInt59 = 59;
    internal const int GeometryInt6 = 6;
    internal const float GeometryFloat6Point5f = 6.5f;
    internal const int GeometryInt64 = 64;
    internal const int GeometryInt7 = 7;
    internal const int GeometryInt73 = 73;
    internal const int GeometryInt73856093 = 73856093;
    internal const int GeometryInt8 = 8;
    internal const float GeometryFloat8Point5f = 8.5f;
    internal const int GeometryInt83492791 = 83492791;
    internal const int GeometryInt89 = 89;
    internal const int GeometryInt9 = 9;
    internal const int GeometryInt97 = 97;
}

internal static partial class Fo1OwnedCaveKit
{
    private const string PresentationSchema = "opennv-fo1-3d-presentation/v1";
    private const string CompositionSchema = "opennv-fo1-owned-cave-composition/v1";
    private const string DustyGravelFloorShader = """
        shader_type spatial;
        render_mode cull_disabled, depth_draw_opaque;

        uniform sampler2D albedo_texture : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
        uniform sampler2D normal_texture : repeat_enable, filter_linear_mipmap_anisotropic;
        uniform vec4 albedo_tint : source_color = vec4(1.0);
        uniform float repeat_meters = 2.2;
        uniform float source_contrast = 0.72;
        uniform float roughness_value = 0.96;
        uniform float normal_strength = 0.65;
        uniform float emission_energy = 0.02;
        uniform float ambient_bounce = 0.12;

        varying vec3 world_position;

        void vertex() {
            world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            vec2 world_uv = world_position.xz / repeat_meters;
            vec3 source_sample = texture(albedo_texture, world_uv).rgb;
            source_sample = clamp(
                (source_sample - vec3(0.24)) * source_contrast + vec3(0.24),
                vec3(0.0),
                vec3(1.0));
            vec3 dusty_gravel = source_sample * albedo_tint.rgb;
            ALBEDO = dusty_gravel;
            ROUGHNESS = roughness_value;
            METALLIC = 0.0;
            NORMAL_MAP = texture(normal_texture, world_uv).rgb;
            NORMAL_MAP_DEPTH = normal_strength * 0.45;
            EMISSION = dusty_gravel * (ambient_bounce + emission_energy);
        }
        """;

    internal static Coverage Load(
        JsonElement presentation,
        Node3D parent,
        bool[] floorBacked,
        float canonicalYawDegrees,
        float canonicalPitchDegrees)
    {
        var owned = presentation.GetProperty("ownedPresentation");
        var manifestPath = VerifiedGltfLoader.ResolvePath(owned.GetProperty("manifest").GetString()!);
        VerifiedGltfLoader.VerifyHash(manifestPath, owned.GetProperty("manifestSha256").GetString()!);
        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = manifestDocument.RootElement;
        if (manifest.GetProperty("schema").GetString() != PresentationSchema ||
            manifest.GetProperty("status").GetString() != "transported-owned-presentation" ||
            manifest.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean())
            throw new InvalidOperationException($"Unexpected owned Fallout presentation manifest: {manifestPath}");

        var caveKit = manifest.GetProperty("caveKit");
        var embeddedCaveKit = owned.GetProperty("caveKit");
        if (caveKit.GetProperty("donorSceneSha256").GetString() !=
                embeddedCaveKit.GetProperty("donorSceneSha256").GetString() ||
            caveKit.GetProperty("assets").GetArrayLength() !=
                embeddedCaveKit.GetProperty("assets").GetArrayLength())
            throw new InvalidOperationException("Embedded Fallout cave-kit identity drifted from its verified manifest.");

        var composition = owned.GetProperty("composition");
        if (composition.GetProperty("schema").GetString() != CompositionSchema ||
            composition.GetProperty("status").GetString() != "source-bound-owned-3d-composition")
            throw new InvalidOperationException("Unexpected Fallout owned cave composition contract.");
        var grounding = ReadGroundingContract(composition);

        var textures = RuntimeMaterialLoader.LoadTextures(caveKit);
        var floor = BuildContinuousFloor(
            parent,
            composition,
            caveKit,
            textures,
            floorBacked);
        var prototypes = new Dictionary<string, Prototype>(StringComparer.Ordinal);
        var materialBindings = 0;
        foreach (var asset in caveKit.GetProperty("assets").EnumerateArray())
        {
            var id = asset.GetProperty("id").GetString()!;
            var role = asset.GetProperty("role").GetString()!;
            if (prototypes.ContainsKey(id))
                throw new InvalidOperationException($"Duplicate Fallout cave-kit asset identity: {id}");
            var loaded = VerifiedGltfLoader.Load(
                asset.GetProperty("model").GetString()!,
                asset.GetProperty("sidecar").GetString()!);
            loaded.CollisionScene?.Free();
            if (!loaded.SourceSha256.Equals(
                    asset.GetProperty("sourceSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Fallout cave-kit source hash drift: {id}");
            loaded.Scene.Name = $"CAVE_ASSET_{role}_{id}";
            loaded.Scene.Scale = Vector3.One * asset.GetProperty("unitsToMeters").GetSingle();
            materialBindings += RuntimeMaterialLoader.Apply(loaded.Scene, asset, textures);
            loaded.Scene.Visible = false;
            parent.AddChild(loaded.Scene);
            var bounds = WorldBounds(loaded.Scene);
            VerifyBounds(asset.GetProperty("bounds"), bounds, id);
            var meshes = NodeTraversal.Descendants<MeshInstance3D>(loaded.Scene).Count(mesh => mesh.Mesh is not null);
            var surfaces = NodeTraversal.Descendants<MeshInstance3D>(loaded.Scene)
                .Sum(mesh => mesh.Mesh?.GetSurfaceCount() ?? 0);
            if (meshes < 1 || surfaces != asset.GetProperty("surfaces").GetInt32())
                throw new InvalidOperationException(
                    $"Fallout cave-kit render coverage drift: {id} meshes={meshes} surfaces={surfaces}");
            parent.RemoveChild(loaded.Scene);
            loaded.Scene.Visible = true;
            prototypes.Add(id, new Prototype(loaded.Scene, role, bounds, meshes, surfaces));
        }

        var container = new Node3D { Name = "FO1_OWNED_CAVE_COMPOSITION" };
        parent.AddChild(container);
        var instanceCount = 0;
        var meshInstances = 0;
        var surfaceInstances = 0;
        var roleCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var placementIds = new HashSet<string>(StringComparer.Ordinal);
        var wallRelief = composition.TryGetProperty("connectedWallVolume", out _)
            ? BuildConnectedWallVolumes(container, composition, caveKit, textures, prototypes)
            : BuildFrmReliefWalls(
                container,
                composition,
                caveKit,
                textures,
                canonicalYawDegrees,
                canonicalPitchDegrees);
        var envelopeCoverage = BuildCaveEnvelope(
            container,
            composition,
            caveKit,
            textures,
            floorBacked,
            wallRelief.CohesiveCaveMaterial);
        instanceCount += envelopeCoverage.Instances;
        meshInstances += envelopeCoverage.MeshInstances;
        surfaceInstances += envelopeCoverage.SurfaceInstances;
        roleCounts["terrain-envelope"] = envelopeCoverage.Instances;
        instanceCount += wallRelief.Placements;
        meshInstances += wallRelief.MeshInstances;
        surfaceInstances += wallRelief.SurfaceInstances;
        materialBindings += wallRelief.MaterialBindings;
        roleCounts["wall-ribbon"] = wallRelief.Placements;
        var unifiedCaveMaterialSurfaces = wallRelief.UnifiedCaveMaterialSurfaces +
            (wallRelief.CohesiveCaveMaterial is null
                ? 0
                : envelopeCoverage.SurfaceInstances);
        var vaultPortalInstances = BuildVaultPortal(
            container,
            composition,
            caveKit,
            textures,
            wallRelief.CohesiveCaveMaterial);
        instanceCount += vaultPortalInstances;
        meshInstances += vaultPortalInstances;
        surfaceInstances += vaultPortalInstances;
        if (wallRelief.CohesiveCaveMaterial is not null)
            unifiedCaveMaterialSurfaces += vaultPortalInstances;
        roleCounts["vault-portal"] = vaultPortalInstances;
        var groundedInstances = 0;
        var minimumGroundSeatDepthMeters = float.PositiveInfinity;
        var maximumGroundSeatDepthMeters = 0.0f;
        var maximumGroundErrorMeters = 0.0f;
        var groundedRoleCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var placement in composition.GetProperty("placements").EnumerateArray())
        {
            var placementId = placement.GetProperty("id").GetString()!;
            if (!placementIds.Add(placementId))
                throw new InvalidOperationException($"Duplicate Fallout cave placement identity: {placementId}");
            var assetId = placement.GetProperty("assetId").GetString()!;
            if (!prototypes.TryGetValue(assetId, out var prototype))
                throw new InvalidOperationException($"Fallout cave placement references missing asset: {assetId}");
            var role = placement.GetProperty("assetRole").GetString()!;
            if (role != prototype.Role)
                throw new InvalidOperationException(
                    $"Fallout cave placement role drift: {placementId} {role} != {prototype.Role}");
            var position = ReadVector(placement.GetProperty("positionMeters"));
            var presentationScale = ReadVector(placement.GetProperty("scale"));
            var yawDegrees = placement.GetProperty("yawDegrees").GetSingle();
            var rotationDegrees = placement.TryGetProperty("rotationDegrees", out var authoredRotation)
                ? ReadVector(authoredRotation)
                : new Vector3(0.0f, yawDegrees, 0.0f);
            if (!position.IsFinite() || !presentationScale.IsFinite() ||
                presentationScale.X <= 0.0f || presentationScale.Y <= 0.0f ||
                presentationScale.Z <= 0.0f || !float.IsFinite(yawDegrees) ||
                !rotationDegrees.IsFinite())
                throw new InvalidOperationException($"Fallout cave placement transform is invalid: {placementId}");
            var instance = prototype.Root.Duplicate() as Node3D
                ?? throw new InvalidOperationException($"Could not duplicate Fallout cave asset: {assetId}");
            instance.Name = $"CAVE_{role}_{placementId}";
            instance.Scale = new Vector3(
                instance.Scale.X * presentationScale.X,
                instance.Scale.Y * presentationScale.Y,
                instance.Scale.Z * presentationScale.Z);
            instance.RotationDegrees = rotationDegrees;
            instance.Position = position;
            instance.SetMeta("fo1_asset_role", role);
            instance.SetMeta(
                "fo1_cutaway_exempt",
                role is "entrance-corpse" or "vault-frame" or "vault-transition");
            if (role == "room")
            {
                var hiddenFloorSurfaces = HideRoomFloorSurface(instance, placementId);
                instance.SetMeta("fo1_hidden_donor_floor_surfaces", hiddenFloorSurfaces);
            }
            else if (role is "vault-airlock" or "vault-hall" or "vault-frame")
            {
                var vaultMaterialSurfaces = ApplyVaultEnvironmentMaterialPass(
                    instance,
                    placementId);
                instance.SetMeta(
                    "fo1_vault_environment_material_surfaces",
                    vaultMaterialSurfaces);
            }
            if (role is "large-rock" or "small-rock" or "stalagmite" &&
                wallRelief.CohesiveCaveMaterial is not null)
            {
                unifiedCaveMaterialSurfaces += ApplyConnectedWallSurfaceMaterial(
                    instance,
                    wallRelief.CohesiveCaveMaterial,
                    placementId);
                instance.SetMeta("fo1_cave_material_unified", true);
            }
            container.AddChild(instance);
            var placedBounds = WorldBounds(instance);
            if (grounding.Roles.TryGetValue(role, out var groundingRole))
            {
                var seatDepth = Math.Clamp(
                    placedBounds.Size.Y * groundingRole.SeatDepthHeightFraction,
                    groundingRole.MinimumSeatDepthMeters,
                    groundingRole.MaximumSeatDepthMeters);
                var targetBottom = grounding.FloorHeightMeters - seatDepth;
                instance.GlobalPosition = instance.GlobalPosition +
                    Vector3.Up * (targetBottom - placedBounds.Position.Y);
                var groundedBounds = WorldBounds(instance);
                var groundError = MathF.Abs(groundedBounds.Position.Y - targetBottom);
                instance.SetMeta("fo1_grounded_to_floor", true);
                instance.SetMeta("fo1_ground_floor_y_meters", grounding.FloorHeightMeters);
                instance.SetMeta("fo1_ground_bottom_y_meters", groundedBounds.Position.Y);
                instance.SetMeta("fo1_ground_seat_depth_meters", seatDepth);
                instance.SetMeta("fo1_ground_error_meters", groundError);
                groundedInstances++;
                groundedRoleCounts[role] = groundedRoleCounts.GetValueOrDefault(role) + 1;
                minimumGroundSeatDepthMeters = MathF.Min(minimumGroundSeatDepthMeters, seatDepth);
                maximumGroundSeatDepthMeters = MathF.Max(maximumGroundSeatDepthMeters, seatDepth);
                maximumGroundErrorMeters = MathF.Max(maximumGroundErrorMeters, groundError);
            }
            else
                instance.Position += Vector3.Up * (position.Y - placedBounds.Position.Y);
            if (role is "vault-airlock" or "vault-hall")
                AddVaultLight(parent, placementId, position, role == "vault-airlock");
            instanceCount++;
            meshInstances += prototype.Meshes;
            surfaceInstances += prototype.Surfaces;
            roleCounts[role] = roleCounts.GetValueOrDefault(role) + 1;
        }
        foreach (var prototype in prototypes.Values)
            prototype.Root.Free();

        var declared = composition.GetProperty("coverage");
        if (instanceCount != declared.GetProperty("instances").GetInt32())
            throw new InvalidOperationException(
                $"Fallout cave instance coverage drift: {instanceCount} != " +
                declared.GetProperty("instances").GetInt32());
        foreach (var role in declared.GetProperty("roles").EnumerateObject())
        {
            if (roleCounts.GetValueOrDefault(role.Name) != role.Value.GetInt32())
                throw new InvalidOperationException($"Fallout cave role coverage drift: {role.Name}");
        }
        if (groundedInstances != declared.GetProperty("groundedInstances").GetInt32())
            throw new InvalidOperationException(
                $"Fallout cave grounded-instance coverage drift: {groundedInstances}");
        foreach (var role in declared.GetProperty("groundingRoles").EnumerateObject())
        {
            if (groundedRoleCounts.GetValueOrDefault(role.Name) != role.Value.GetInt32())
                throw new InvalidOperationException(
                    $"Fallout cave grounding-role coverage drift: {role.Name}");
        }
        if (!float.IsFinite(minimumGroundSeatDepthMeters) ||
            maximumGroundErrorMeters > grounding.MaximumRuntimeErrorMeters)
            throw new InvalidOperationException(
                $"Fallout cave rock grounding failed: instances={groundedInstances} " +
                $"seat={minimumGroundSeatDepthMeters:F4}-{maximumGroundSeatDepthMeters:F4}m " +
                $"error={maximumGroundErrorMeters:F6}m");
        if (instanceCount < Fo1OwnedCaveKitNumericContracts.GeometryInt100 ||
            !roleCounts.ContainsKey("large-rock") || !roleCounts.ContainsKey("small-rock") ||
            !roleCounts.ContainsKey("stalagmite") ||
            declared.GetProperty("sourceWallObjects").GetInt32() < Fo1OwnedCaveKitNumericContracts.GeometryInt100 ||
            wallRelief.Placements < 1)
            throw new InvalidOperationException(
                $"Fallout owned cave composition is incomplete: instances={instanceCount}");

        return new Coverage(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))).ToLowerInvariant(),
            prototypes.Count,
            instanceCount,
            meshInstances,
            surfaceInstances,
            materialBindings + floor.MaterialBindings,
            roleCounts,
            floor.Hexes,
            floor.Triangles,
            floor.MeshInstances,
            groundedInstances,
            grounding.FloorHeightMeters,
            minimumGroundSeatDepthMeters,
            maximumGroundSeatDepthMeters,
            maximumGroundErrorMeters,
            grounding.MaximumRuntimeErrorMeters,
            unifiedCaveMaterialSurfaces,
            0);
    }

    private static int BuildVaultPortal(
        Node3D container,
        JsonElement composition,
        JsonElement caveKit,
        RuntimeMaterialLoader.LoadedTextures textures,
        StandardMaterial3D? cohesiveCaveMaterial)
    {
        var portal = composition.GetProperty("vaultPortal");
        if (portal.GetProperty("schema").GetString() !=
            "opennv-fo1-owned-vault-portal/v1")
            throw new InvalidOperationException("Unexpected Fallout Vault portal contract.");
        var diffuseId = TextureId(caveKit, portal.GetProperty("diffusePath").GetString()!);
        var normalId = TextureId(caveKit, portal.GetProperty("normalPath").GetString()!);
        var material = cohesiveCaveMaterial ?? new StandardMaterial3D
        {
            ResourceName = "FO1 exact-axis embedded Vault 13 rock portal",
            AlbedoTexture = textures.TwoDimensional[diffuseId],
            AlbedoColor = ReadColor(portal.GetProperty("albedoColor")),
            Roughness = portal.GetProperty("roughness").GetSingle(),
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            TextureRepeat = true,
            NormalEnabled = true,
            NormalTexture = textures.TwoDimensional[normalId],
            NormalScale = portal.GetProperty("normalScale").GetSingle(),
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
        };
        var instance = new MeshInstance3D
        {
            Name = "CAVE_vault-portal_embedded-v13ent-door",
            Mesh = BuildVaultPortalMesh(portal),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        instance.SetMeta("fo1_asset_role", "vault-portal");
        instance.SetMeta("fo1_cutaway_exempt", false);
        instance.SetMeta("fo1_source_tactical_visibility", "hide-vault-portal");
        instance.SetMeta("fo1_cave_material_unified", cohesiveCaveMaterial is not null);
        instance.SetMeta(
            "fo1_source_door_serial",
            portal.GetProperty("source").GetProperty("doorSerial").GetInt32());
        container.AddChild(instance);
        return 1;
    }

    private static ArrayMesh BuildVaultPortalMesh(JsonElement portal)
    {
        var origin = ReadVector(portal.GetProperty("originMeters"));
        var caveward = ReadVector(portal.GetProperty("cavewardVector")).Normalized();
        var lateral = ReadVector(portal.GetProperty("lateralVector")).Normalized();
        var floor = portal.GetProperty("floorHeightMeters").GetSingle();
        var frontRelief = portal.GetProperty("frontReliefMeters").GetSingle();
        var depth = portal.GetProperty("depthMeters").GetSingle();
        var innerRadius = portal.GetProperty("innerRadiusMeters").GetSingle();
        var outerHalfWidth = portal.GetProperty("outerHalfWidthMeters").GetSingle();
        var outerTop = portal.GetProperty("outerTopHeightMeters").GetSingle();
        var outerBottom = portal.GetProperty("outerBottomHeightMeters").GetSingle();
        var radialNoise = portal.GetProperty("radialNoiseMeters").GetSingle();
        var segments = portal.GetProperty("segments").GetInt32();
        var repeat = portal.GetProperty("textureRepeatMeters").GetSingle();
        if (!origin.IsFinite() || !caveward.IsFinite() || !lateral.IsFinite() ||
            MathF.Abs(caveward.Dot(lateral)) > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point001f ||
            floor is < Fo1OwnedCaveKitNumericContracts.GeometryFloatNEgativE0Point10f or > 0.0f || frontRelief is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point8f ||
            depth is < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f or > 4.0f || innerRadius is < Fo1OwnedCaveKitNumericContracts.GeometryFloat1Point2f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat2Point5f ||
            outerHalfWidth is < 4.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat14Point0f ||
            outerTop <= innerRadius * 2.0f + 1.0f ||
            outerBottom is < Fo1OwnedCaveKitNumericContracts.GeometryFloatNEgativE0Point3f or > Fo1OwnedCaveKitNumericContracts.GeometryFloatNEgativE0Point05f ||
            radialNoise is < 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point6f || segments is < Fo1OwnedCaveKitNumericContracts.GeometryInt16 or > Fo1OwnedCaveKitNumericContracts.GeometryInt64 || repeat <= Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f)
            throw new InvalidOperationException("Fallout Vault portal geometry is invalid.");

        Vector3 Point(float x, float distance, float height) =>
            origin + lateral * x + caveward * distance + Vector3.Up * height;
        var centerY = floor + innerRadius;
        var frontInner = new Vector3[segments];
        var backInner = new Vector3[segments];
        var frontOuter = new Vector3[segments];
        var backOuter = new Vector3[segments];
        var localInner = new Vector2[segments];
        var localOuter = new Vector2[segments];
        for (var index = 0; index < segments; index++)
        {
            var angle = 2.0f * MathF.PI * index / segments;
            var sine = MathF.Sin(angle);
            var cosine = MathF.Cos(angle);
            var radiusNoise = Noise(Fo1OwnedCaveKitNumericContracts.GeometryInt130013, index, Fo1OwnedCaveKitNumericContracts.GeometryInt19) * radialNoise;
            var reliefNoise = Noise(Fo1OwnedCaveKitNumericContracts.GeometryInt271828, index, Fo1OwnedCaveKitNumericContracts.GeometryInt31) * radialNoise * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point22f;
            localInner[index] = new Vector2(
                sine * innerRadius,
                centerY + cosine * innerRadius);
            var horizontalDistance = MathF.Abs(sine) < Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point0001f
                ? float.PositiveInfinity
                : outerHalfWidth / MathF.Abs(sine);
            var verticalDistance = cosine >= 0.0f
                ? (outerTop - centerY) / MathF.Max(cosine, Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point0001f)
                : (outerBottom - centerY) / MathF.Min(cosine, Fo1OwnedCaveKitNumericContracts.GeometryFloatNEgativE0Point0001f);
            var boundaryDistance = MathF.Min(horizontalDistance, verticalDistance);
            localOuter[index] = new Vector2(
                sine * boundaryDistance,
                centerY + cosine * boundaryDistance);
            frontInner[index] = Point(localInner[index].X, frontRelief, localInner[index].Y);
            backInner[index] = Point(localInner[index].X, -depth, localInner[index].Y);
            frontOuter[index] = Point(
                localOuter[index].X,
                frontRelief + reliefNoise,
                localOuter[index].Y + radiusNoise * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point16f);
            backOuter[index] = Point(localOuter[index].X, -depth, localOuter[index].Y);
        }

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (var index = 0; index < segments; index++)
        {
            var next = (index + 1) % segments;
            Vector2 FaceUv(Vector2 point) => new(point.X / repeat, point.Y / repeat);
            AddEnvelopeQuad(
                tool,
                frontOuter[index],
                frontOuter[next],
                frontInner[next],
                frontInner[index],
                FaceUv(localOuter[index]),
                FaceUv(localOuter[next]),
                FaceUv(localInner[next]),
                FaceUv(localInner[index]),
                caveward);
            AddEnvelopeQuad(
                tool,
                backOuter[next],
                backOuter[index],
                backInner[index],
                backInner[next],
                FaceUv(localOuter[next]),
                FaceUv(localOuter[index]),
                FaceUv(localInner[index]),
                FaceUv(localInner[next]),
                -caveward);

            var middleAngle = 2.0f * MathF.PI * (index + Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f) / segments;
            var innerNormal = -(
                lateral * MathF.Sin(middleAngle) + Vector3.Up * MathF.Cos(middleAngle));
            var outerNormal = -innerNormal;
            var arc0 = 2.0f * MathF.PI * innerRadius * index / segments / repeat;
            var arc1 = 2.0f * MathF.PI * innerRadius * (index + 1) / segments / repeat;
            AddEnvelopeQuad(
                tool,
                frontInner[index],
                frontInner[next],
                backInner[next],
                backInner[index],
                new Vector2(arc0, 0.0f),
                new Vector2(arc1, 0.0f),
                new Vector2(arc1, depth / repeat),
                new Vector2(arc0, depth / repeat),
                innerNormal.Normalized());
            AddEnvelopeQuad(
                tool,
                frontOuter[next],
                frontOuter[index],
                backOuter[index],
                backOuter[next],
                new Vector2(arc1, 0.0f),
                new Vector2(arc0, 0.0f),
                new Vector2(arc0, depth / repeat),
                new Vector2(arc1, depth / repeat),
                outerNormal.Normalized());
        }
        tool.Index();
        tool.GenerateTangents();
        return tool.Commit() ?? throw new InvalidOperationException(
            "Could not build the Fallout embedded Vault portal.");
    }

    private static EnvelopeCoverage BuildCaveEnvelope(
        Node3D container,
        JsonElement composition,
        JsonElement caveKit,
        RuntimeMaterialLoader.LoadedTextures textures,
        bool[] floorBacked,
        StandardMaterial3D? cohesiveCaveMaterial)
    {
        var envelope = composition.GetProperty("envelope");
        if (envelope.GetProperty("schema").GetString() !=
            "opennv-fo1-owned-cave-topology-envelope/v1")
            throw new InvalidOperationException("Unexpected Fallout cave envelope contract.");
        var diffuseId = TextureId(caveKit, envelope.GetProperty("diffusePath").GetString()!);
        var normalId = TextureId(caveKit, envelope.GetProperty("normalPath").GetString()!);
        var envelopeAlbedo = ReadColor(envelope.GetProperty("albedoColor"));
        var material = cohesiveCaveMaterial ?? new StandardMaterial3D
        {
            ResourceName = "FO1 source-bound cave envelope",
            AlbedoTexture = textures.TwoDimensional[diffuseId],
            AlbedoColor = envelopeAlbedo,
            Roughness = envelope.GetProperty("roughness").GetSingle(),
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            TextureRepeat = true,
            NormalEnabled = true,
            NormalTexture = textures.TwoDimensional[normalId],
            NormalScale = envelope.GetProperty("normalScale").GetSingle(),
            EmissionEnabled = true,
            EmissionTexture = textures.TwoDimensional[diffuseId],
            Emission = new Color(Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point16f, Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point135f, Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point095f),
            EmissionEnergyMultiplier = Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point22f,
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
        };
        var meshes = BuildCaveEnvelopeMeshes(envelope, floorBacked);
        var roof = new MeshInstance3D
        {
            Name = "CAVE_terrain-envelope-source-roof",
            Mesh = meshes.Roof,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        roof.SetMeta("fo1_asset_role", "terrain-envelope");
        roof.SetMeta("fo1_source_tactical_visibility", "hide-roof-envelope");
        container.AddChild(roof);
        var boundary = new MeshInstance3D
        {
            Name = "CAVE_terrain-envelope-source-boundary",
            Mesh = meshes.Boundary,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        boundary.SetMeta("fo1_asset_role", "terrain-envelope");
        boundary.SetMeta("fo1_source_tactical_visibility", "hide-boundary-envelope");
        container.AddChild(boundary);
        return new EnvelopeCoverage(1, 2, 2);
    }

    private static EnvelopeMeshes BuildCaveEnvelopeMeshes(JsonElement envelope, bool[] floorBacked)
    {
        if (floorBacked.Length != Fo1HexMath.Width * Fo1HexMath.Height ||
            envelope.GetProperty("topology").GetString() !=
                "all non-default V13ENT floor-backed 200x200 movement hexes")
            throw new InvalidOperationException("Fallout cave envelope topology is invalid.");
        var floor = envelope.GetProperty("floorHeightMeters").GetSingle();
        var ceiling = envelope.GetProperty("ceilingHeightMeters").GetSingle();
        var relief = envelope.GetProperty("ceilingReliefMeters").GetSingle();
        var repeat = envelope.GetProperty("textureRepeatMeters").GetSingle();
        if (floor is < Fo1OwnedCaveKitNumericContracts.GeometryFloatNEgativE0Point20f or > 0.0f || ceiling is < 4.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat12Point0f ||
            relief is < 0.0f or > 2.0f || repeat <= Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f)
            throw new InvalidOperationException("Fallout cave envelope geometry is invalid.");

        float CeilingHeight(Vector3 point)
        {
            var x = Mathf.RoundToInt(point.X * Fo1OwnedCaveKitNumericContracts.GeometryFloat1000Point0f);
            var z = Mathf.RoundToInt(point.Z * Fo1OwnedCaveKitNumericContracts.GeometryFloat1000Point0f);
            var broad = Noise(Fo1OwnedCaveKitNumericContracts.GeometryInt13013, x / Fo1OwnedCaveKitNumericContracts.GeometryInt2000, z / Fo1OwnedCaveKitNumericContracts.GeometryInt2000) * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point62f;
            var detail = Noise(Fo1OwnedCaveKitNumericContracts.GeometryInt31337, x, z) * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point38f;
            return ceiling + relief * (broad + detail);
        }

        var roofTool = new SurfaceTool();
        roofTool.Begin(Mesh.PrimitiveType.Triangles);
        var boundaryTool = new SurfaceTool();
        boundaryTool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var tile in Enumerable.Range(0, floorBacked.Length).Where(tile => floorBacked[tile]))
        {
            var corners = Fo1HexMath.Corners(tile);
            var roofCorners = corners
                .Select(point => new Vector3(point.X, CeilingHeight(point), point.Z))
                .ToArray();
            var roofCenter = roofCorners.Aggregate(Vector3.Zero, (sum, point) => sum + point) /
                roofCorners.Length;
            for (var edge = 0; edge < Fo1HexMath.DirectionCount; edge++)
            {
                var next = (edge + 1) % Fo1HexMath.DirectionCount;
                AddEnvelopeTriangle(
                    roofTool,
                    roofCenter,
                    roofCorners[next],
                    roofCorners[edge],
                    repeat,
                    Vector3.Down);
                var neighbor = Fo1HexMath.NeighborAcrossEdge(tile, edge);
                if (neighbor >= 0 && floorBacked[neighbor])
                    continue;
                var firstBottom = new Vector3(corners[edge].X, floor, corners[edge].Z);
                var secondBottom = new Vector3(corners[next].X, floor, corners[next].Z);
                var outward = ((corners[edge] + corners[next]) * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f - Fo1HexMath.Center(tile))
                    .Normalized();
                AddEnvelopeQuad(
                    boundaryTool,
                    firstBottom,
                    secondBottom,
                    roofCorners[next],
                    roofCorners[edge],
                    new Vector2(0.0f, floor / repeat),
                    new Vector2(firstBottom.DistanceTo(secondBottom) / repeat, floor / repeat),
                    new Vector2(firstBottom.DistanceTo(secondBottom) / repeat, roofCorners[next].Y / repeat),
                    new Vector2(0.0f, roofCorners[edge].Y / repeat),
                    outward);
            }
        }

        roofTool.Index();
        roofTool.GenerateTangents();
        boundaryTool.Index();
        boundaryTool.GenerateTangents();
        return new EnvelopeMeshes(
            roofTool.Commit() ?? throw new InvalidOperationException(
                "Could not build the Fallout source-bound cave roof."),
            boundaryTool.Commit() ?? throw new InvalidOperationException(
                "Could not build the Fallout source-bound cave boundary."));
    }

    private static void AddEnvelopeTriangle(
        SurfaceTool tool,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        float repeat,
        Vector3 normal)
    {
        foreach (var point in new[] { first, second, third })
        {
            tool.SetNormal(normal);
            tool.SetUV(new Vector2(point.X / repeat, point.Z / repeat));
            tool.AddVertex(point);
        }
    }

    private static void AddEnvelopeQuad(
        SurfaceTool tool,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Vector3 fourth,
        Vector2 firstUv,
        Vector2 secondUv,
        Vector2 thirdUv,
        Vector2 fourthUv,
        Vector3 normal)
    {
        foreach (var vertex in new[]
        {
            (Position: first, Uv: firstUv),
            (Position: second, Uv: secondUv),
            (Position: third, Uv: thirdUv),
            (Position: first, Uv: firstUv),
            (Position: third, Uv: thirdUv),
            (Position: fourth, Uv: fourthUv),
        })
        {
            tool.SetNormal(normal);
            tool.SetUV(vertex.Uv);
            tool.AddVertex(vertex.Position);
        }
    }

    private static int HideRoomFloorSurface(Node3D room, string placementId)
    {
        const string floorMaterialIdentity = "CaveRoomMid01:8@24";
        var hidden = 0;
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(room))
        {
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            {
                var importedIdentity = RuntimeMaterialLoader.SourceSurfaceIdentity(mesh, surface);
                var activeIdentity = mesh.GetActiveMaterial(surface)?.ResourceName;
                var sourceIdentity = mesh.Mesh!.SurfaceGetMaterial(surface)?.ResourceName;
                if (!string.Equals(importedIdentity, floorMaterialIdentity, StringComparison.Ordinal) &&
                    !string.Equals(activeIdentity, floorMaterialIdentity, StringComparison.Ordinal) &&
                    !string.Equals(sourceIdentity, floorMaterialIdentity, StringComparison.Ordinal))
                    continue;
                mesh.SetSurfaceOverrideMaterial(surface, new StandardMaterial3D
                {
                    ResourceName = $"FO1 hidden donor floor {placementId}",
                    AlbedoColor = new Color(0.0f, 0.0f, 0.0f, 0.0f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                });
                hidden++;
            }
        }
        if (hidden != 1)
            throw new InvalidOperationException(
                $"Fallout cave room floor identity drift: {placementId} hidden={hidden}");
        return hidden;
    }

    private static void HideOwnedWallDressingSurfaces(
        Node3D instance,
        IReadOnlySet<string> identities,
        string placementId)
    {
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(instance))
        {
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            {
                var importedIdentity = RuntimeMaterialLoader.SourceSurfaceIdentity(mesh, surface);
                var activeIdentity = mesh.GetActiveMaterial(surface)?.ResourceName;
                var sourceIdentity = mesh.Mesh!.SurfaceGetMaterial(surface)?.ResourceName;
                var identity = identities.Contains(importedIdentity ?? string.Empty)
                    ? importedIdentity
                    : identities.Contains(activeIdentity ?? string.Empty)
                        ? activeIdentity
                    : identities.Contains(sourceIdentity ?? string.Empty)
                        ? sourceIdentity
                        : null;
                if (identity is null)
                    continue;
                mesh.SetSurfaceOverrideMaterial(surface, new StandardMaterial3D
                {
                    ResourceName = $"FO1 hidden owned cave-wall surface {placementId} {identity}",
                    AlbedoColor = new Color(0.0f, 0.0f, 0.0f, 0.0f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                });
                hidden.Add(identity);
            }
        }
        if (!hidden.SetEquals(identities))
            throw new InvalidOperationException(
                $"Fallout owned cave-wall hidden surface drift: {placementId} " +
                $"hidden={string.Join(',', hidden.Order())}");
        instance.SetMeta("fo1_hidden_owned_wall_surfaces", hidden.Count);
    }

    private static int ApplyConnectedWallSurfaceMaterial(
        Node3D instance,
        StandardMaterial3D connectedWallMaterial,
        string placementId)
    {
        var unified = 0;
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(instance))
        {
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            {
                var source = mesh.GetActiveMaterial(surface);
                if (source is null || source.ResourceName.StartsWith(
                        "FO1 hidden owned cave-wall surface",
                        StringComparison.Ordinal))
                    continue;
                mesh.SetSurfaceOverrideMaterial(surface, connectedWallMaterial);
                unified++;
            }
        }
        if (unified == 0)
            throw new InvalidOperationException(
                $"Fallout cave wall dressing has no material surfaces: {placementId}");
        instance.SetMeta("fo1_world_triplanar_wall_surfaces", unified);
        return unified;
    }

    private static int ApplyVaultEnvironmentMaterialPass(Node3D vault, string placementId)
    {
        var treated = 0;
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(vault))
        {
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D source)
                    continue;
                var material = source.Duplicate() as StandardMaterial3D
                    ?? throw new InvalidOperationException(
                        "Could not duplicate a Vault 13 airlock material.");
                var metallic = source.Metallic >=
                    Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point20f;
                var dustFraction = metallic
                    ? Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point11f
                    : Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point18f;
                material.ResourceName =
                    $"FO1 Vault environment family {placementId} {source.ResourceName}";
                material.AlbedoColor = new Color(
                    source.AlbedoColor.R * Mathf.Lerp(
                        1.0f,
                        Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point58f,
                        dustFraction),
                    source.AlbedoColor.G * Mathf.Lerp(
                        1.0f,
                        Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point52f,
                        dustFraction),
                    source.AlbedoColor.B * Mathf.Lerp(
                        1.0f,
                        Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point40f,
                        dustFraction),
                    source.AlbedoColor.A);
                material.Roughness = MathF.Max(
                    metallic
                        ? Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point72f
                        : Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point92f,
                    source.Roughness);
                material.Metallic = source.Metallic *
                    (1.0f - dustFraction *
                        Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point25f);
                if (!source.EmissionEnabled && source.AlbedoTexture is not null)
                {
                    material.EmissionEnabled = true;
                    material.EmissionTexture = source.AlbedoTexture;
                    material.Emission = metallic
                        ? new Color(
                            Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point11f,
                            Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point135f,
                            Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point095f)
                        : new Color(
                            Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point095f,
                            Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point08f,
                            Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point05f);
                    material.EmissionEnergyMultiplier =
                        Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point16f;
                }
                mesh.SetSurfaceOverrideMaterial(surface, material);
                treated++;
            }
        }
        if (treated == 0)
            throw new InvalidOperationException(
                $"Fallout Vault environment material coverage drift: {placementId}");
        return treated;
    }

    private static void AddVaultLight(
        Node3D parent,
        string placementId,
        Vector3 position,
        bool threshold)
    {
        parent.AddChild(new OmniLight3D
        {
            Name = $"VAULT_CORRIDOR_LIGHT_{placementId}",
            Position = position + Vector3.Up * (threshold ? Fo1OwnedCaveKitNumericContracts.GeometryFloat2Point35f : Fo1OwnedCaveKitNumericContracts.GeometryFloat1Point95f),
            LightColor = threshold
                ? new Color(Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point76f, Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point69f, Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point56f)
                : new Color(Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point92f, Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point77f, Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point52f),
            LightEnergy = threshold ? Fo1OwnedCaveKitNumericContracts.GeometryFloat1Point9f : Fo1OwnedCaveKitNumericContracts.GeometryFloat1Point65f,
            OmniRange = threshold ? Fo1OwnedCaveKitNumericContracts.GeometryFloat8Point5f : Fo1OwnedCaveKitNumericContracts.GeometryFloat6Point5f,
            ShadowEnabled = false,
        });
    }

    private static FloorCoverage BuildContinuousFloor(
        Node3D parent,
        JsonElement composition,
        JsonElement caveKit,
        RuntimeMaterialLoader.LoadedTextures textures,
        bool[] floorBacked)
    {
        if (floorBacked.Length != Fo1HexMath.Width * Fo1HexMath.Height)
            throw new InvalidOperationException("Fallout continuous floor received an invalid hex topology.");
        var floor = composition.GetProperty("recipe").GetProperty("floor");
        if (floor.GetProperty("schema").GetString() != "opennv-fo1-owned-continuous-floor/v1" ||
            floor.GetProperty("topology").GetString() !=
                "all non-default V13ENT floor-backed 200x200 movement hexes")
            throw new InvalidOperationException("Unexpected Fallout continuous-floor contract.");
        var diffuseId = TextureId(caveKit, floor.GetProperty("diffusePath").GetString()!);
        var normalId = TextureId(caveKit, floor.GetProperty("normalPath").GetString()!);
        var height = floor.GetProperty("heightMeters").GetSingle();
        var repeat = floor.GetProperty("textureRepeatMeters").GetSingle();
        var roughness = floor.GetProperty("roughness").GetSingle();
        var normalScale = floor.GetProperty("normalScale").GetSingle();
        var emissionEnergy = floor.GetProperty("emissionEnergy").GetSingle();
        var color = ReadColor(floor.GetProperty("albedoColor"));
        if (height is < Fo1OwnedCaveKitNumericContracts.GeometryFloatNEgativE0Point10f or > 0.0f || repeat <= Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f ||
            roughness is < 0.0f or > 1.0f || normalScale is < 0.0f or > 2.0f ||
            emissionEnergy is < 0.0f or > 1.0f)
            throw new InvalidOperationException("Fallout continuous-floor material values are invalid.");

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var hexes = 0;
        foreach (var tile in Enumerable.Range(0, floorBacked.Length).Where(tile => floorBacked[tile]))
        {
            var center = Fo1HexMath.Center(tile);
            center.Y = height;
            var corners = Fo1HexMath.Corners(tile);
            for (var index = 0; index < corners.Length; index++)
            {
                var current = corners[index];
                var next = corners[(index + 1) % corners.Length];
                current.Y = height;
                next.Y = height;
                AddFloorVertex(tool, center, repeat);
                AddFloorVertex(tool, next, repeat);
                AddFloorVertex(tool, current, repeat);
            }
            hexes++;
        }
        if (hexes == 0)
            throw new InvalidOperationException("Fallout continuous floor has no source-backed hexes.");
        tool.Index();
        tool.GenerateTangents();
        var mesh = tool.Commit() ?? throw new InvalidOperationException(
            "Could not build the Fallout continuous cave floor.");
        var material = new ShaderMaterial
        {
            ResourceName = "FO1 normalized dark dusty gravel floor",
            Shader = new Shader { Code = DustyGravelFloorShader },
        };
        material.SetShaderParameter("albedo_texture", textures.TwoDimensional[diffuseId]);
        material.SetShaderParameter("normal_texture", textures.TwoDimensional[normalId]);
        material.SetShaderParameter("albedo_tint", color);
        material.SetShaderParameter("repeat_meters", repeat);
        material.SetShaderParameter("roughness_value", roughness);
        material.SetShaderParameter("normal_strength", normalScale);
        material.SetShaderParameter("emission_energy", emissionEnergy);
        var instance = new MeshInstance3D
        {
            Name = "FO1_OWNED_CONTINUOUS_CAVE_FLOOR",
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(instance);
        var triangles = hexes * Fo1OwnedCaveKitNumericContracts.GeometryInt6;
        var declared = composition.GetProperty("coverage");
        if (hexes != declared.GetProperty("continuousFloorHexes").GetInt32() ||
            triangles != declared.GetProperty("continuousFloorTriangles").GetInt32())
            throw new InvalidOperationException(
                $"Fallout continuous-floor coverage drift: {hexes}/{triangles}.");
        return new FloorCoverage(hexes, triangles, 1, 1);
    }

    private static void AddFloorVertex(SurfaceTool tool, Vector3 vertex, float repeat)
    {
        tool.SetNormal(Vector3.Up);
        tool.SetUV(new Vector2(vertex.X / repeat, vertex.Z / repeat));
        tool.AddVertex(vertex);
    }

    private static void VerifyBounds(JsonElement expected, Aabb actual, string id)
    {
        var expectedPosition = ReadVector(expected.GetProperty("positionMeters"));
        var expectedSize = ReadVector(expected.GetProperty("sizeMeters"));
        if (expectedPosition.DistanceTo(actual.Position) > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point002f ||
            expectedSize.DistanceTo(actual.Size) > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point002f)
            throw new InvalidOperationException(
                $"Fallout cave-kit bounds drift: {id} expected={expectedPosition}/{expectedSize} " +
                $"actual={actual.Position}/{actual.Size}");
    }

    private static GroundingContract ReadGroundingContract(JsonElement composition)
    {
        var recipe = composition.GetProperty("recipe");
        var floorHeight = recipe.GetProperty("floor").GetProperty("heightMeters").GetSingle();
        var source = recipe.GetProperty("grounding");
        if (source.GetProperty("schema").GetString() !=
            "opennv-fo1-owned-cave-grounding/v1")
            throw new InvalidOperationException("Unexpected Fallout cave grounding contract.");
        var maximumError = source.GetProperty("maximumRuntimeErrorMeters").GetSingle();
        var roles = new Dictionary<string, GroundingRole>(StringComparer.Ordinal);
        foreach (var property in source.GetProperty("roles").EnumerateObject())
        {
            var value = property.Value;
            var fraction = value.GetProperty("seatDepthHeightFraction").GetSingle();
            var minimum = value.GetProperty("minimumSeatDepthMeters").GetSingle();
            var maximum = value.GetProperty("maximumSeatDepthMeters").GetSingle();
            if (fraction is <= 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point40f || minimum <= 0.0f ||
                minimum > maximum || maximum > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point50f)
                throw new InvalidOperationException(
                    $"Fallout cave grounding role is invalid: {property.Name}");
            roles.Add(property.Name, new GroundingRole(fraction, minimum, maximum));
        }
        var expectedRoles = new HashSet<string>(StringComparer.Ordinal)
        {
            "large-rock",
            "small-rock",
            "stalagmite",
        };
        if (!float.IsFinite(floorHeight) || floorHeight is < Fo1OwnedCaveKitNumericContracts.GeometryFloatNEgativE0Point10f or > 0.0f ||
            maximumError is <= 0.0f or > Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point005f ||
            !expectedRoles.SetEquals(roles.Keys))
            throw new InvalidOperationException("Fallout cave grounding coverage is invalid.");
        return new GroundingContract(floorHeight, maximumError, roles);
    }

    private static Texture2D LoadVerifiedImageTexture(
        string sourcePath,
        string expectedSha256,
        int expectedWidth,
        int expectedHeight)
    {
        var path = VerifiedGltfLoader.ResolvePath(sourcePath);
        VerifiedGltfLoader.VerifyHash(path, expectedSha256);
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty() || image.GetWidth() != expectedWidth ||
            image.GetHeight() != expectedHeight)
            throw new InvalidOperationException($"Fallout FRM relief texture is invalid: {path}");
        return ImageTexture.CreateFromImage(image);
    }

    private static float Noise(int seed, int column, int row)
    {
        var value = unchecked((uint)(seed * Fo1OwnedCaveKitNumericContracts.GeometryInt73856093 ^ column * Fo1OwnedCaveKitNumericContracts.GeometryInt19349663 ^ row * Fo1OwnedCaveKitNumericContracts.GeometryInt83492791));
        value ^= value >> Fo1OwnedCaveKitNumericContracts.GeometryInt13;
        value *= Fo1OwnedCaveKitNumericContracts.GeometryUint1274126177u;
        return (value & 0xffffu) / Fo1OwnedCaveKitNumericContracts.GeometryFloat32767Point5f - 1.0f;
    }

    private static string TextureId(JsonElement caveKit, string requestedPath)
    {
        var matches = caveKit.GetProperty("textures").EnumerateArray()
            .Where(row => string.Equals(
                row.GetProperty("requestedPath").GetString(),
                requestedPath,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"Fallout cave texture identity drift: {requestedPath}");
        return matches[0].GetProperty("id").GetString()!;
    }

    private static Aabb WorldBounds(Node3D root)
    {
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var count = 0;
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(root))
        {
            var bounds = mesh.GetAabb();
            foreach (var x in new[] { bounds.Position.X, bounds.End.X })
                foreach (var y in new[] { bounds.Position.Y, bounds.End.Y })
                    foreach (var z in new[] { bounds.Position.Z, bounds.End.Z })
                    {
                        var point = mesh.ToGlobal(new Vector3(x, y, z));
                        minimum = minimum.Min(point);
                        maximum = maximum.Max(point);
                    }
            count++;
        }
        if (count == 0)
            throw new InvalidOperationException("Fallout cave-kit model has no renderable bounds.");
        return new Aabb(minimum, maximum - minimum);
    }

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("Fallout cave transform vector must contain three values.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Vector2 ReadVector2(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 2)
            throw new InvalidOperationException(
                "Fallout cave image coordinate must contain two values.");
        return new Vector2(values[0], values[1]);
    }

    private static Color ReadColor(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4 || values.Any(value => value is < 0.0f or > 1.0f))
            throw new InvalidOperationException("Fallout cave color must contain four normalized values.");
        return new Color(values[0], values[1], values[2], values[3]);
    }

    private sealed record Prototype(
        Node3D Root,
        string Role,
        Aabb Bounds,
        int Meshes,
        int Surfaces);

    private sealed record ReliefArtifact(
        int Width,
        int Height,
        Vector2 FrameOffset,
        StandardMaterial3D FrontMaterial,
        IReadOnlyList<Vector2[]> Contours);

    private readonly record struct WallVolumeRing(
        float HeightFraction,
        float RadiusMultiplier,
        float CenterJitterFraction);

    private sealed record WallVolumeProfile(
        float TextureRepeatMeters,
        float Roughness,
        float NormalScale,
        float RadiusScale,
        float HeightScale,
        float TriplanarSharpness,
        StandardMaterial3D Material);

    private readonly record struct WallNoiseBlend(
        float RingWavelengthBase,
        float RingWavelengthHeightScale,
        float MacroWeight,
        float RingMacroWeight,
        float MicroRadialWeight,
        float MicroJitterWeight,
        float VerticalMacroWeight,
        float VerticalMicroWeight,
        float PeriodicPrimaryWeight,
        float PeriodicSecondaryWeight,
        int PeriodicSecondaryFrequencyMultiplier,
        int PeriodicSecondaryFrequencyOffset,
        float PeriodicSecondaryPhaseScale);

    private sealed record WallSurfaceDressing(
        bool Enabled,
        IReadOnlySet<string> Profiles,
        string AssetRole,
        float SpacingMeters,
        int MinimumInstancesPerContour,
        float MinimumContourPerimeterMeters,
        int MaximumInstances,
        Vector3 Scale,
        float EmbedBehindContourMeters,
        float GroundSinkMeters,
        float YawOffsetDegrees,
        float YawJitterDegrees,
        float UniformScaleJitterFraction,
        float VerticalScaleJitterFraction,
        IReadOnlySet<string> HiddenSurfaceIdentities,
        Prototype? Prototype);

    private readonly record struct WallVolumeSource(
        int Serial,
        int Tile,
        Vector3 Center,
        float Radius,
        float Height);

    private sealed record WallVolumeComponent(
        string Id,
        string Profile,
        IReadOnlyList<WallVolumeSource> Sources);

    private readonly record struct WallContourKey(int X, int Z);

    private readonly record struct WallDressingAnchor(
        int Index,
        int ContourIndex,
        float DistanceMeters,
        Vector2 Position,
        Vector2 Outward,
        int IdentityOne,
        int IdentityTwo);

    private readonly record struct MoldedWallCoverage(
        int BoundarySegments,
        int ClosedContours,
        int Vertices,
        IReadOnlyList<WallDressingAnchor> DressingAnchors);

    private readonly record struct ReliefCoverage(
        int Placements,
        int MeshInstances,
        int SurfaceInstances,
        int MaterialBindings,
        int UnifiedCaveMaterialSurfaces,
        StandardMaterial3D? CohesiveCaveMaterial);

    private readonly record struct GroundingRole(
        float SeatDepthHeightFraction,
        float MinimumSeatDepthMeters,
        float MaximumSeatDepthMeters);

    private sealed record GroundingContract(
        float FloorHeightMeters,
        float MaximumRuntimeErrorMeters,
        IReadOnlyDictionary<string, GroundingRole> Roles);

    private readonly record struct FloorCoverage(
        int Hexes,
        int Triangles,
        int MeshInstances,
        int MaterialBindings);

    private readonly record struct EnvelopeMeshes(ArrayMesh Roof, ArrayMesh Boundary);

    private readonly record struct EnvelopeCoverage(
        int Instances,
        int MeshInstances,
        int SurfaceInstances);

    internal readonly record struct Coverage(
        string ManifestSha256,
        int Assets,
        int Instances,
        int MeshInstances,
        int SurfaceInstances,
        int MaterialBindings,
        IReadOnlyDictionary<string, int> Roles,
        int ContinuousFloorHexes,
        int ContinuousFloorTriangles,
        int ContinuousFloorMeshInstances,
        int GroundedInstances,
        float GroundingFloorHeightMeters,
        float MinimumGroundSeatDepthMeters,
        float MaximumGroundSeatDepthMeters,
        float MaximumGroundErrorMeters,
        float GroundingToleranceMeters,
        int UnifiedCaveMaterialSurfaces,
        int LitMaterials)
    {
        internal static Coverage Empty => new(
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            new Dictionary<string, int>(StringComparer.Ordinal),
            0,
            0,
            0,
            0,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            0,
            0);
    }
}
