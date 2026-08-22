using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class CellSceneLoader
{
    private const string CellSceneSchema = "opennv-cell-scene/v2";

    internal static LoadedCell Load(
        string scenePath,
        Node3D parent,
        bool openProofDoor,
        string? proofDoorOverride = null)
    {
        var resolvedScenePath = VerifiedGltfLoader.ResolvePath(scenePath);
        using var document = JsonDocument.Parse(File.ReadAllText(resolvedScenePath));
        var source = document.RootElement;
        if (source.GetProperty("schema").GetString() != CellSceneSchema ||
            source.GetProperty("status").GetString() != "geometry-structure")
            throw new InvalidOperationException($"Unexpected OpenNV cell scene: {resolvedScenePath}");

        var prototypes = new Dictionary<string, VerifiedGltfLoader.LoadedGltf>(StringComparer.Ordinal);
        try
        {
            var textures = RuntimeMaterialLoader.LoadTextures(source);
            var materialBindings = 0;
            var compiler = source.GetProperty("compiler");
            var compilerName = compiler.GetProperty("name").GetString()!;
            var compilerSha256 = compiler.GetProperty("sha256").GetString()!;
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
                if (!loaded.CompilerName.Equals(compilerName, StringComparison.Ordinal) ||
                    !loaded.CompilerSha256.Equals(compilerSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Cell asset compiler provenance mismatch: {assetId}");
                materialBindings += RuntimeMaterialLoader.Apply(loaded.Scene, asset, textures);
                prototypes.Add(assetId, loaded);
            }

            var coordinates = source.GetProperty("coordinates");
            var unitScale = coordinates.GetProperty("unitsToMeters").GetSingle();
            var cell = source.GetProperty("cell");
            var root = new Node3D
            {
                Name = $"CELL_{cell.GetProperty("formId").GetString()}_{cell.GetProperty("editorId").GetString()}",
                Scale = Vector3.One * unitScale,
            };
            parent.AddChild(root);

            var loadedReferences = 0;
            var doors = new Dictionary<string, DoorInstance>(StringComparer.OrdinalIgnoreCase);
            var collisionMeshes = 0;
            var surfaces = 0;
            var vertices = 0;
            foreach (var reference in source.GetProperty("references").EnumerateArray())
            {
                if (reference.GetProperty("initiallyDisabled").GetBoolean())
                    continue;
                var formId = reference.GetProperty("formId").GetString()!;
                var yaw = reference.GetProperty("yawRadians").GetSingle();
                Node3D placement;
                if (reference.GetProperty("baseRecordType").GetString() == "DOOR")
                {
                    var door = new DoorInstance { Name = $"DOOR_{formId}" };
                    door.Configure(yaw);
                    doors.Add(formId, door);
                    placement = door;
                }
                else
                {
                    placement = new Node3D
                    {
                        Name = $"REFR_{formId}",
                        Rotation = new Vector3(0.0f, yaw, 0.0f),
                    };
                }
                placement.Position = ReadVector(reference.GetProperty("positionGodotUnits"));
                root.AddChild(placement);

                var assetId = reference.GetProperty("assetId").GetString()!;
                var instance = prototypes[assetId].Scene.Duplicate((int)Node.DuplicateFlags.Default) as Node3D
                    ?? throw new InvalidOperationException($"Could not duplicate cell asset: {assetId}");
                placement.AddChild(instance);
                foreach (var mesh in Descendants<MeshInstance3D>(instance))
                {
                    if (mesh.Mesh is null)
                        continue;
                    surfaces += mesh.Mesh.GetSurfaceCount();
                    if (mesh.Mesh is ArrayMesh arrayMesh)
                        vertices += Enumerable.Range(0, arrayMesh.GetSurfaceCount()).Sum(arrayMesh.SurfaceGetArrayLen);
                    mesh.CreateTrimeshCollision();
                    collisionMeshes++;
                }
                loadedReferences++;
            }

            var proofDoor = proofDoorOverride ??
                source.GetProperty("proof").GetProperty("doorReferenceFormId").GetString()!;
            if (!doors.ContainsKey(proofDoor))
                throw new InvalidOperationException($"Cell proof door was not loaded: {proofDoor}");
            if (openProofDoor)
                doors[proofDoor].SetOpen(true);
            var spawn = source.GetProperty("spawn");
            var authoredLights = source.GetProperty("lighting").GetProperty("lights").GetArrayLength();
            var player = BuildView(
                parent,
                spawn.GetProperty("yawRadians").GetSingle(),
                source.GetProperty("lighting"),
                unitScale);
            return new LoadedCell(
                root,
                cell.GetProperty("formId").GetString()!,
                cell.GetProperty("editorId").GetString()!,
                prototypes.Count,
                textures.Count,
                materialBindings,
                loadedReferences,
                doors.Count,
                authoredLights,
                collisionMeshes,
                surfaces,
                vertices,
                proofDoor,
                openProofDoor,
                doors[proofDoor],
                player);
        }
        finally
        {
            foreach (var prototype in prototypes.Values)
                prototype.Scene.Free();
        }
    }

    private static CellPlayer BuildView(Node3D parent, float yaw, JsonElement lighting, float unitScale)
    {
        var calibration = lighting.GetProperty("calibration");
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.015f, 0.018f, 0.022f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = ReadColor(lighting.GetProperty("ambientColor")),
            AmbientLightEnergy = calibration.GetProperty("ambientEnergy").GetSingle(),
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        parent.AddChild(new WorldEnvironment { Environment = environment });
        var direction = lighting.GetProperty("directionalRotationDegrees")
            .EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        if (direction.Length != 2)
            throw new InvalidOperationException("CELL directional rotation must contain two values.");
        parent.AddChild(new DirectionalLight3D
        {
            Name = "CELL_XCLL_Directional",
            RotationDegrees = new Vector3(direction[0], direction[1], 0.0f),
            LightColor = ReadColor(lighting.GetProperty("directionalColor")),
            LightEnergy = lighting.GetProperty("directionalFade").GetSingle() *
                calibration.GetProperty("directionalEnergyScale").GetSingle(),
            ShadowEnabled = true,
        });
        foreach (var light in lighting.GetProperty("lights").EnumerateArray())
        {
            if (light.GetProperty("initiallyDisabled").GetBoolean())
                continue;
            parent.AddChild(new OmniLight3D
            {
                Name = $"LIGH_{light.GetProperty("formId").GetString()}_{light.GetProperty("baseEditorId").GetString()}",
                Position = ReadVector(light.GetProperty("positionGodotUnits")) * unitScale,
                LightColor = ReadColor(light.GetProperty("color")),
                LightEnergy = MathF.Max(
                    0.1f,
                    light.GetProperty("intensity").GetSingle() *
                    calibration.GetProperty("omniEnergyScale").GetSingle()),
                OmniRange = light.GetProperty("radiusMeters").GetSingle(),
                ShadowEnabled = false,
            });
        }
        var player = new CellPlayer();
        player.Configure(yaw);
        parent.AddChild(player);
        return player;
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

    internal static DoorRay BuildProofRay(DoorInstance door)
    {
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var meshCount = 0;
        foreach (var mesh in Descendants<MeshInstance3D>(door))
        {
            var bounds = mesh.GetAabb();
            foreach (var x in new[] { bounds.Position.X, bounds.End.X })
                foreach (var y in new[] { bounds.Position.Y, bounds.End.Y })
                    foreach (var z in new[] { bounds.Position.Z, bounds.End.Z })
                    {
                        var point = door.ToLocal(mesh.ToGlobal(new Vector3(x, y, z)));
                        minimum = minimum.Min(point);
                        maximum = maximum.Max(point);
                    }
            meshCount++;
        }
        if (meshCount == 0)
            throw new InvalidOperationException("Proof door contains no renderable mesh.");

        var size = maximum - minimum;
        var center = (minimum + maximum) / 2.0f;
        var normal = size.X <= size.Z ? Vector3.Right : Vector3.Back;
        var thickness = MathF.Min(size.X, size.Z);
        var reach = MathF.Max(thickness * 2.0f, 12.0f);
        return new DoorRay(
            door.ToGlobal(center - normal * reach),
            door.ToGlobal(center + normal * reach),
            size,
            normal);
    }

    internal static RayHit CastProofRay(PhysicsDirectSpaceState3D space, DoorInstance door, DoorRay ray)
    {
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(ray.From, ray.To));
        if (hit.Count == 0)
            return new RayHit(false, false, "");
        var collider = hit["collider"].AsGodotObject() as Node;
        return new RayHit(
            true,
            collider is not null && door.IsAncestorOf(collider),
            collider?.GetPath().ToString() ?? "unknown");
    }

    internal static FloorHit CastSpawnFloor(PhysicsDirectSpaceState3D space)
    {
        var query = PhysicsRayQueryParameters3D.Create(
            new Vector3(0.0f, 2.0f, 0.0f),
            new Vector3(0.0f, -2.0f, 0.0f),
            1);
        var hit = space.IntersectRay(query);
        if (hit.Count == 0)
            return new FloorHit(false, float.NaN, "");
        var collider = hit["collider"].AsGodotObject() as Node;
        return new FloorHit(
            true,
            hit["position"].AsVector3().Y,
            collider?.GetPath().ToString() ?? "unknown");
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

    internal readonly record struct LoadedCell(
        Node3D Root,
        string FormId,
        string EditorId,
        int Assets,
        int Textures,
        int MaterialBindings,
        int References,
        int Doors,
        int AuthoredLights,
        int CollisionMeshes,
        int Surfaces,
        int Vertices,
        string ProofDoorFormId,
        bool ProofDoorOpen,
        DoorInstance ProofDoor,
        CellPlayer Player);

    internal readonly record struct DoorRay(Vector3 From, Vector3 To, Vector3 LocalSize, Vector3 LocalNormal);

    internal readonly record struct RayHit(bool Hit, bool HitProofDoor, string ColliderPath);

    internal readonly record struct FloorHit(bool Hit, float Y, string ColliderPath);
}
