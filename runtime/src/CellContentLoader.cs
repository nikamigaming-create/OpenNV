using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class CellContentLoader
{
    private const string CellSceneSchema = "opennv-cell-scene/v7";

    internal static LoadedContent Load(
        string scenePath,
        Node3D parent,
        GameplaySession session,
        bool useXr,
        string? actorScenePath,
        string? actorScenesManifestPath,
        bool proofEnableActor,
        bool buildCollision,
        uint renderLayer)
    {
        var resolvedScenePath = VerifiedGltfLoader.ResolvePath(scenePath);
        using var document = JsonDocument.Parse(File.ReadAllText(resolvedScenePath));
        var source = document.RootElement;
        if (source.GetProperty("schema").GetString() != CellSceneSchema ||
            source.GetProperty("status").GetString() != "geometry-structure")
            throw new InvalidOperationException($"Unexpected OpenNV cell content: {resolvedScenePath}");

        var prototypes = new Dictionary<string, VerifiedGltfLoader.LoadedGltf>(StringComparer.Ordinal);
        var collisionAssets = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var textures = RuntimeMaterialLoader.LoadTextures(source);
            var materialBindings = 0;
            var defaultCompiler = source.GetProperty("compiler");
            foreach (var asset in source.GetProperty("assets").EnumerateArray())
            {
                var assetId = asset.GetProperty("id").GetString()!;
                var loaded = VerifiedGltfLoader.Load(
                    asset.GetProperty("model").GetString()!,
                    asset.GetProperty("sidecar").GetString()!);
                if (!loaded.SourceSha256.Equals(
                        asset.GetProperty("sourceSha256").GetString(),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Cell asset source hash mismatch: {assetId}");
                var compiler = asset.TryGetProperty("compiler", out var assetCompiler)
                    ? assetCompiler
                    : defaultCompiler;
                if (!loaded.CompilerName.Equals(compiler.GetProperty("name").GetString(), StringComparison.Ordinal) ||
                    !loaded.CompilerSha256.Equals(
                        compiler.GetProperty("sha256").GetString(),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Cell asset compiler provenance mismatch: {assetId}");
                materialBindings += RuntimeMaterialLoader.Apply(loaded.Scene, asset, textures);
                SetRenderLayer(loaded.Scene, renderLayer);
                var collision = asset.GetProperty("collision");
                if (collision.GetProperty("enabled").GetBoolean())
                {
                    if (loaded.CollisionScene is null &&
                        collision.GetProperty("source").GetString() != "LAND-height-grid")
                        throw new InvalidOperationException($"Authored collision payload is missing: {assetId}");
                    collisionAssets.Add(assetId);
                }
                prototypes.Add(assetId, loaded);
            }

            var coordinates = source.GetProperty("coordinates");
            var unitScale = coordinates.GetProperty("unitsToMeters").GetSingle();
            var originGameUnits = ReadVector(coordinates.GetProperty("originGameUnits"));
            var cell = source.GetProperty("cell");
            var formId = cell.GetProperty("formId").GetString()!;
            var editorId = cell.GetProperty("editorId").GetString()!;
            var root = new Node3D
            {
                Name = $"CELL_{formId}_{editorId}",
                Scale = Vector3.One * unitScale,
            };
            parent.AddChild(root);

            var loadedReferences = 0;
            var doors = new Dictionary<string, DoorInstance>(StringComparer.OrdinalIgnoreCase);
            var pickups = new Dictionary<string, PickupInstance>(StringComparer.OrdinalIgnoreCase);
            var containers = new Dictionary<string, ContainerInstance>(StringComparer.OrdinalIgnoreCase);
            var collisionMeshes = 0;
            var surfaces = 0;
            var vertices = 0;
            foreach (var reference in source.GetProperty("references").EnumerateArray())
            {
                if (reference.GetProperty("initiallyDisabled").GetBoolean())
                    continue;
                var referenceFormId = reference.GetProperty("formId").GetString()!;
                var yaw = reference.GetProperty("yawGodotRadians").GetSingle();
                var rotation = ReadQuaternion(reference.GetProperty("rotationGodotQuaternion"));
                var interaction = reference.GetProperty("interaction");
                var interactionType = interaction.ValueKind == JsonValueKind.Object
                    ? interaction.GetProperty("type").GetString()
                    : null;
                if (useXr && interactionType == "pickup" &&
                    interaction.GetProperty("itemFormId").GetString() == "0008f216")
                    continue;
                if (interactionType == "pickup" && session.IsReferenceRemoved(referenceFormId))
                    continue;
                var assetId = reference.GetProperty("assetId").GetString()!;
                Node3D placement;
                if (interactionType == "door")
                {
                    var destination = reference.GetProperty("teleportDestinationFormId");
                    var door = new DoorInstance { Name = $"DOOR_{referenceFormId}" };
                    door.Configure(
                        referenceFormId,
                        yaw,
                        destination.ValueKind == JsonValueKind.String ? destination.GetString() : null);
                    door.SetOpen(session.IsDoorOpen(referenceFormId));
                    doors.Add(referenceFormId, door);
                    placement = door;
                }
                else if (interactionType == "pickup")
                {
                    PickupInstance.WeaponProfile? weapon = null;
                    if (interaction.TryGetProperty("weapon", out var weaponSource))
                    {
                        weapon = new PickupInstance.WeaponProfile(
                            weaponSource.GetProperty("damage").GetInt32(),
                            weaponSource.GetProperty("clipSize").GetInt32(),
                            weaponSource.GetProperty("ammoFormId").ValueKind == JsonValueKind.String
                                ? weaponSource.GetProperty("ammoFormId").GetString()
                                : null);
                    }
                    var pickup = new PickupInstance();
                    pickup.Configure(
                        referenceFormId,
                        interaction.GetProperty("itemFormId").GetString()!,
                        interaction.GetProperty("itemEditorId").GetString()!,
                        interaction.GetProperty("itemRecordType").GetString()!,
                        interaction.GetProperty("count").GetInt32(),
                        weapon);
                    pickup.Basis = new Basis(rotation);
                    pickups.Add(referenceFormId, pickup);
                    placement = pickup;
                }
                else if (interactionType == "container")
                {
                    var entries = interaction.GetProperty("items")
                        .EnumerateArray()
                        .Select(item => new ContainerInstance.Entry(
                            item.GetProperty("itemFormId").GetString()!,
                            item.GetProperty("itemEditorId").GetString()!,
                            item.GetProperty("itemRecordType").GetString()!,
                            item.GetProperty("count").GetInt32(),
                            item.GetProperty("resolved").GetBoolean()))
                        .ToArray();
                    var container = new ContainerInstance();
                    container.Configure(
                        referenceFormId,
                        reference.GetProperty("baseEditorId").GetString()!,
                        entries);
                    container.Basis = new Basis(rotation);
                    containers.Add(referenceFormId, container);
                    placement = container;
                }
                else
                {
                    placement = new Node3D
                    {
                        Name = $"REFR_{referenceFormId}",
                        Basis = new Basis(rotation),
                    };
                }
                placement.Position = ReadVector(reference.GetProperty("positionGodotUnits"));
                root.AddChild(placement);

                var instance = prototypes[assetId].Scene.Duplicate((int)Node.DuplicateFlags.Default) as Node3D
                    ?? throw new InvalidOperationException($"Could not duplicate cell asset: {assetId}");
                placement.AddChild(instance);
                foreach (var mesh in Descendants<MeshInstance3D>(instance))
                {
                    mesh.Layers = renderLayer;
                    if (mesh.Mesh is null)
                        continue;
                    surfaces += mesh.Mesh.GetSurfaceCount();
                    if (mesh.Mesh is ArrayMesh arrayMesh)
                        vertices += Enumerable.Range(0, arrayMesh.GetSurfaceCount()).Sum(arrayMesh.SurfaceGetArrayLen);
                }
                if (buildCollision && prototypes[assetId].CollisionScene is Node3D collisionPrototype)
                {
                    var collisionInstance = collisionPrototype.Duplicate((int)Node.DuplicateFlags.Default) as Node3D
                        ?? throw new InvalidOperationException($"Could not duplicate authored collision: {assetId}");
                    collisionInstance.Name = $"AUTHORED_COLLISION_{assetId}";
                    placement.AddChild(collisionInstance);
                    foreach (var collisionMesh in Descendants<MeshInstance3D>(collisionInstance))
                    {
                        collisionMesh.Visible = false;
                        collisionMesh.CreateTrimeshCollision();
                        foreach (var body in Descendants<StaticBody3D>(collisionMesh))
                            body.CollisionLayer = renderLayer;
                        collisionMeshes++;
                    }
                }
                else if (buildCollision && (collisionAssets.Contains(assetId) || interactionType is not null))
                {
                    foreach (var mesh in Descendants<MeshInstance3D>(instance))
                    {
                        mesh.CreateTrimeshCollision();
                        foreach (var body in Descendants<StaticBody3D>(mesh))
                            body.CollisionLayer = renderLayer;
                        collisionMeshes++;
                    }
                }
                loadedReferences++;
            }

            var proofDoor = source.GetProperty("proof").GetProperty("doorReferenceFormId").GetString()!;
            if (!doors.ContainsKey(proofDoor))
                throw new InvalidOperationException($"Cell proof door was not loaded: {proofDoor}");
            var acceptedCellFormIds = cell.TryGetProperty("sourceCellFormIds", out var sourceCells)
                ? sourceCells.EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(new[] { formId }, StringComparer.OrdinalIgnoreCase);
            var actors = new List<CellActorLoader.PlacedActor>();
            var actorPaths = actorScenesManifestPath is not null
                ? CellActorLoader.LoadManifest(actorScenesManifestPath, acceptedCellFormIds)
                : actorScenePath is not null
                    ? new[] { actorScenePath }
                    : Array.Empty<string>();
            foreach (var path in actorPaths)
            {
                var placedActor = CellActorLoader.Load(
                    path,
                    acceptedCellFormIds,
                    root,
                    proofEnableActor);
                if (placedActor is not null)
                {
                    SetRenderLayer(placedActor.Value.Placement, renderLayer);
                    actors.Add(placedActor.Value);
                }
            }

            Node3D? heldWeapon = null;
            var muzzlePosition = Vector3.Zero;
            StartingLoadout? startingLoadout = null;
            if (source.TryGetProperty("vr", out var vr))
            {
                var loadout = vr.GetProperty("startingLoadout");
                startingLoadout = new StartingLoadout(
                    loadout.GetProperty("weaponFormId").GetString()!,
                    loadout.GetProperty("weaponEditorId").GetString()!,
                    loadout.GetProperty("ammoFormId").GetString()!,
                    loadout.GetProperty("ammoEditorId").GetString()!,
                    loadout.GetProperty("damage").GetInt32(),
                    loadout.GetProperty("clipSize").GetInt32(),
                    loadout.GetProperty("reserveRounds").GetInt32());
                if (useXr)
                {
                    var heldAssetId = loadout.GetProperty("modelAssetId").GetString()!;
                    heldWeapon = prototypes[heldAssetId].Scene.Duplicate((int)Node.DuplicateFlags.Default) as Node3D
                        ?? throw new InvalidOperationException("Could not duplicate VR held weapon asset.");
                    muzzlePosition = ReadVector(loadout.GetProperty("muzzlePositionGodotUnits"));
                }
            }

            var lighting = ReadLighting(source.GetProperty("lighting"));
            return new LoadedContent(
                resolvedScenePath,
                root,
                formId,
                editorId,
                cell.GetProperty("interior").GetBoolean(),
                acceptedCellFormIds,
                originGameUnits,
                unitScale,
                prototypes.Count,
                textures.TwoDimensional.Count,
                materialBindings,
                loadedReferences,
                doors,
                pickups,
                containers,
                actors,
                collisionMeshes,
                surfaces,
                vertices,
                proofDoor,
                heldWeapon,
                muzzlePosition,
                startingLoadout,
                lighting);
        }
        finally
        {
            foreach (var prototype in prototypes.Values)
            {
                prototype.Scene.Free();
                prototype.CollisionScene?.Free();
            }
        }
    }

    private static LightingContract ReadLighting(JsonElement lighting)
    {
        var calibration = lighting.GetProperty("calibration");
        var direction = lighting.GetProperty("directionalRotationDegrees")
            .EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        if (direction.Length != 2)
            throw new InvalidOperationException("CELL directional rotation must contain two values.");
        var lights = lighting.GetProperty("lights")
            .EnumerateArray()
            .Where(light => !light.GetProperty("initiallyDisabled").GetBoolean())
            .Select(light => new LightContract(
                light.GetProperty("formId").GetString()!,
                light.GetProperty("baseEditorId").GetString()!,
                ReadVector(light.GetProperty("positionGodotUnits")),
                ReadColor(light.GetProperty("color")),
                light.GetProperty("intensity").GetSingle(),
                light.GetProperty("radiusMeters").GetSingle()))
            .ToArray();
        return new LightingContract(
            lighting.TryGetProperty("mode", out var mode) ? mode.GetString()! : "interior-xcll",
            ReadColor(lighting.GetProperty("ambientColor")),
            ReadColor(lighting.GetProperty("directionalColor")),
            ReadColor(lighting.GetProperty("fogColor")),
            lighting.GetProperty("fogNearGameUnits").GetSingle(),
            lighting.GetProperty("fogFarGameUnits").GetSingle(),
            lighting.GetProperty("fogPower").GetSingle(),
            new Vector2(direction[0], direction[1]),
            lighting.GetProperty("directionalFade").GetSingle(),
            calibration.GetProperty("ambientEnergy").GetSingle(),
            calibration.GetProperty("omniEnergyScale").GetSingle(),
            calibration.GetProperty("directionalEnergyScale").GetSingle(),
            lights);
    }

    private static Vector3 ReadVector(JsonElement array)
    {
        var values = array.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("Cell scene vector must contain three values.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Color ReadColor(JsonElement array)
    {
        var values = array.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("Cell scene color must contain three values.");
        return new Color(values[0], values[1], values[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement array)
    {
        var values = array.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4)
            throw new InvalidOperationException("Cell scene quaternion must contain four values.");
        return new Quaternion(values[0], values[1], values[2], values[3]).Normalized();
    }

    private static void SetRenderLayer(Node root, uint layer)
    {
        foreach (var mesh in Descendants<MeshInstance3D>(root))
            mesh.Layers = layer;
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

    internal readonly record struct LoadedContent(
        string ScenePath,
        Node3D Root,
        string FormId,
        string EditorId,
        bool Interior,
        IReadOnlySet<string> SourceCellFormIds,
        Vector3 OriginGameUnits,
        float UnitsToMeters,
        int Assets,
        int Textures,
        int MaterialBindings,
        int References,
        IReadOnlyDictionary<string, DoorInstance> Doors,
        IReadOnlyDictionary<string, PickupInstance> Pickups,
        IReadOnlyDictionary<string, ContainerInstance> Containers,
        IReadOnlyList<CellActorLoader.PlacedActor> Actors,
        int CollisionMeshes,
        int Surfaces,
        int Vertices,
        string ProofDoorFormId,
        Node3D? HeldWeapon,
        Vector3 MuzzlePosition,
        StartingLoadout? StartingLoadout,
        LightingContract Lighting);

    internal readonly record struct StartingLoadout(
        string WeaponFormId,
        string WeaponEditorId,
        string AmmoFormId,
        string AmmoEditorId,
        int Damage,
        int ClipSize,
        int ReserveRounds);

    internal readonly record struct LightingContract(
        string Mode,
        Color AmbientColor,
        Color DirectionalColor,
        Color FogColor,
        float FogNearGameUnits,
        float FogFarGameUnits,
        float FogPower,
        Vector2 DirectionalRotationDegrees,
        float DirectionalFade,
        float AmbientEnergy,
        float OmniEnergyScale,
        float DirectionalEnergyScale,
        IReadOnlyList<LightContract> Lights);

    internal readonly record struct LightContract(
        string FormId,
        string EditorId,
        Vector3 PositionGodotUnits,
        Color Color,
        float Intensity,
        float RadiusMeters);
}
