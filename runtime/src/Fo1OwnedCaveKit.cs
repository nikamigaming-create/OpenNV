using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

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

internal static class Fo1OwnedCaveKit
{
    private const string PresentationSchema = "opennv-fo1-3d-presentation/v1";
    private const string CompositionSchema = "opennv-fo1-owned-cave-composition/v1";

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
            var meshes = Descendants<MeshInstance3D>(loaded.Scene).Count(mesh => mesh.Mesh is not null);
            var surfaces = Descendants<MeshInstance3D>(loaded.Scene)
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
        var envelopeInstances = BuildCaveEnvelope(
            container,
            composition,
            caveKit,
            textures,
            floorBacked);
        instanceCount += envelopeInstances;
        meshInstances += envelopeInstances;
        surfaceInstances += envelopeInstances;
        roleCounts["terrain-envelope"] = envelopeInstances;
        var wallRelief = composition.TryGetProperty("connectedWallVolume", out _)
            ? BuildConnectedWallVolumes(container, composition, caveKit, textures, prototypes)
            : BuildFrmReliefWalls(
                container,
                composition,
                caveKit,
                textures,
                canonicalYawDegrees,
                canonicalPitchDegrees);
        instanceCount += wallRelief.Placements;
        meshInstances += wallRelief.MeshInstances;
        surfaceInstances += wallRelief.SurfaceInstances;
        materialBindings += wallRelief.MaterialBindings;
        roleCounts["wall-ribbon"] = wallRelief.Placements;
        var vaultPortalInstances = BuildVaultPortal(
            container,
            composition,
            caveKit,
            textures);
        instanceCount += vaultPortalInstances;
        meshInstances += vaultPortalInstances;
        surfaceInstances += vaultPortalInstances;
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
            else if (role == "vault-airlock")
            {
                var darkenedConcreteSurfaces = DarkenVaultAirlockConcrete(instance, placementId);
                instance.SetMeta(
                    "fo1_darkened_vault_concrete_surfaces",
                    darkenedConcreteSurfaces);
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
            grounding.MaximumRuntimeErrorMeters);
    }

    private static int BuildVaultPortal(
        Node3D container,
        JsonElement composition,
        JsonElement caveKit,
        RuntimeMaterialLoader.LoadedTextures textures)
    {
        var portal = composition.GetProperty("vaultPortal");
        if (portal.GetProperty("schema").GetString() !=
            "opennv-fo1-owned-vault-portal/v1")
            throw new InvalidOperationException("Unexpected Fallout Vault portal contract.");
        var diffuseId = TextureId(caveKit, portal.GetProperty("diffusePath").GetString()!);
        var normalId = TextureId(caveKit, portal.GetProperty("normalPath").GetString()!);
        var material = new StandardMaterial3D
        {
            ResourceName = "FO1 exact-axis embedded Vault 13 rock portal",
            AlbedoTexture = textures.TwoDimensional[diffuseId],
            AlbedoColor = ReadColor(portal.GetProperty("albedoColor")),
            Roughness = portal.GetProperty("roughness").GetSingle(),
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
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

    private static int BuildCaveEnvelope(
        Node3D container,
        JsonElement composition,
        JsonElement caveKit,
        RuntimeMaterialLoader.LoadedTextures textures,
        bool[] floorBacked)
    {
        var envelope = composition.GetProperty("envelope");
        if (envelope.GetProperty("schema").GetString() !=
            "opennv-fo1-owned-cave-topology-envelope/v1")
            throw new InvalidOperationException("Unexpected Fallout cave envelope contract.");
        var diffuseId = TextureId(caveKit, envelope.GetProperty("diffusePath").GetString()!);
        var normalId = TextureId(caveKit, envelope.GetProperty("normalPath").GetString()!);
        var envelopeAlbedo = ReadColor(envelope.GetProperty("albedoColor"));
        var material = new StandardMaterial3D
        {
            ResourceName = "FO1 source-bound cave envelope",
            AlbedoTexture = textures.TwoDimensional[diffuseId],
            AlbedoColor = envelopeAlbedo,
            Roughness = envelope.GetProperty("roughness").GetSingle(),
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            NormalEnabled = true,
            NormalTexture = textures.TwoDimensional[normalId],
            NormalScale = envelope.GetProperty("normalScale").GetSingle(),
            EmissionEnabled = true,
            EmissionTexture = textures.TwoDimensional[diffuseId],
            Emission = new Color(Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point16f, Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point135f, Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point095f),
            EmissionEnergyMultiplier = Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point22f,
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
        };
        var instance = new MeshInstance3D
        {
            Name = "CAVE_terrain-envelope_source-v13ent-threshold",
            Mesh = BuildCaveEnvelopeMesh(envelope, floorBacked),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        instance.SetMeta("fo1_asset_role", "terrain-envelope");
        instance.SetMeta("fo1_cutaway_exempt", false);
        container.AddChild(instance);
        return 1;
    }

    private static ArrayMesh BuildCaveEnvelopeMesh(JsonElement envelope, bool[] floorBacked)
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

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
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
                    tool,
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
                    tool,
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

        tool.Index();
        tool.GenerateTangents();
        return tool.Commit() ?? throw new InvalidOperationException(
            "Could not build the Fallout source-bound cave envelope.");
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
        foreach (var mesh in Descendants<MeshInstance3D>(room))
        {
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            {
                var activeIdentity = mesh.GetActiveMaterial(surface)?.ResourceName;
                var sourceIdentity = mesh.Mesh!.SurfaceGetMaterial(surface)?.ResourceName;
                if (!string.Equals(activeIdentity, floorMaterialIdentity, StringComparison.Ordinal) &&
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
        foreach (var mesh in Descendants<MeshInstance3D>(instance))
        {
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            {
                var activeIdentity = mesh.GetActiveMaterial(surface)?.ResourceName;
                var sourceIdentity = mesh.Mesh!.SurfaceGetMaterial(surface)?.ResourceName;
                var identity = identities.Contains(activeIdentity ?? string.Empty)
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

    private static int DarkenVaultAirlockConcrete(Node3D airlock, string placementId)
    {
        var concreteIdentities = new HashSet<string>(StringComparer.Ordinal)
        {
            "VURmGearExit01:1@9",
            "VURmGearExit01:9@36",
        };
        var darkened = 0;
        foreach (var mesh in Descendants<MeshInstance3D>(airlock))
        {
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D source ||
                    !concreteIdentities.Contains(source.ResourceName))
                    continue;
                var material = source.Duplicate() as StandardMaterial3D
                    ?? throw new InvalidOperationException(
                        "Could not duplicate a Vault 13 airlock material.");
                material.ResourceName = $"FO1 dark Vault concrete {placementId} {source.ResourceName}";
                material.AlbedoColor = new Color(
                    source.AlbedoColor.R * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point27f,
                    source.AlbedoColor.G * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point30f,
                    source.AlbedoColor.B * Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point32f,
                    source.AlbedoColor.A);
                material.Roughness = MathF.Max(Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point86f, source.Roughness);
                material.Metallic = MathF.Min(Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point08f, source.Metallic);
                mesh.SetSurfaceOverrideMaterial(surface, material);
                darkened++;
            }
        }
        if (darkened != concreteIdentities.Count)
            throw new InvalidOperationException(
                $"Fallout Vault airlock concrete identity drift: {placementId} " +
                $"darkened={darkened}");
        return darkened;
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
        var color = ReadColor(floor.GetProperty("albedoColor"));
        if (height is < Fo1OwnedCaveKitNumericContracts.GeometryFloatNEgativE0Point10f or > 0.0f || repeat <= Fo1OwnedCaveKitNumericContracts.GeometryFloat0Point5f ||
            roughness is < 0.0f or > 1.0f || normalScale is < 0.0f or > 2.0f)
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
        var material = new StandardMaterial3D
        {
            AlbedoTexture = textures.TwoDimensional[diffuseId],
            AlbedoColor = color,
            Roughness = roughness,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            NormalEnabled = true,
            NormalTexture = textures.TwoDimensional[normalId],
            NormalScale = normalScale,
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
        };
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
        var prototypeSurfaceIdentities = Descendants<MeshInstance3D>(dressing.Prototype!.Root)
            .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh?.GetSurfaceCount() ?? 0)
                .Select(surface => mesh.GetActiveMaterial(surface)?.ResourceName ??
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
            if (dressing.Profiles.Contains(component.Profile))
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
        if (dressingInstanceCount == 0)
            throw new InvalidOperationException(
                "Fallout cave-wall dressing produced no owned relief instances.");
        return new ReliefCoverage(
            componentCount,
            meshCount,
            surfaceCount,
            componentCount * 2);
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
            if (dressing.Profiles.Contains(component.Profile))
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
            placements * 3);
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
        foreach (var mesh in Descendants<MeshInstance3D>(root))
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

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
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
        int MaterialBindings);

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
        float GroundingToleranceMeters)
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
            0.0f);
    }
}
