using System.Text.Json;
using Godot;

using OpenNV.Runtime.SceneGraph;


using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Content;

internal static class StaticCellCompileLoader
{
    private const string PlacementStatus = "compiled-static-reference";
    private const string LightPlacementStatus = "compiled-point-light-reference";
    private const string LandscapePlacementStatus = "compiled-landscape-reference";
    private const string NonvisualPlacementStatus = "accounted-nonvisual-reference";
    private const string PlacementReadiness = "static-presentation-runtime-pending";
    private const string StaticModelPresentation = "static-model";
    private const string PointLightPresentation = "point-light";
    private const string LandscapePresentation = "landscape";
    private const string NonvisualPresentation = "nonvisual";
    private const string StaticNifAsset = "static-nif";
    private const string LandscapeAsset = "landscape";
    private const string LandscapeCollisionSource = "LAND-height-grid";
    private const string LandscapeCollisionBlock = "LAND";
    private const int VectorComponents = 3;
    private const int QuaternionComponents = 4;
    private const int DirectionComponents = 2;
    private const int SingleLandscapeCollisionBlock = 1;
    private const int SingleLandscapeCollisionBody = 1;
    private const uint DefaultRenderLayer = 1u;

    internal static LoadedStaticCell Load(
        string compilePath,
        Node3D parent,
        RuntimeConfiguration configuration,
        bool buildCollision)
    {
        var artifact = StaticCellCompileArtifact.Load(compilePath, configuration);
        var compileRoot = artifact.CompileRoot;
        var cell = artifact.Cell;
        var assets = artifact.Assets;
        var textures = artifact.Textures;
        var unitsToMeters = cell.GetProperty("worldUnitsToMeters").GetSingle();
        if (!Mathf.IsEqualApprox(unitsToMeters, configuration.World.GameUnitsToMeters))
            throw new InvalidOperationException(
                "Static CELL unit scale disagrees with the runtime configuration.");
        var loadedTextures = RuntimeMaterialLoader.LoadTextures(
            textures,
            configuration.Renderer,
            "textureId",
            compileRoot);
        var prototypes = new Dictionary<string, CompiledPrototype>(StringComparer.Ordinal);
        var materialBindings = 0;
        Node3D? root = null;
        try
        {
            foreach (var asset in assets)
            {
                var assetId = asset.GetProperty("assetId").GetString()!;
                var assetOutputs = asset.GetProperty("outputs");
                foreach (var descriptor in assetOutputs.EnumerateObject())
                    StaticCellCompileArtifact.VerifyNestedOutput(
                        compileRoot,
                        descriptor.Value);
                var modelPath = StaticCellCompileArtifact.ResolveContainedPath(
                    compileRoot,
                    assetOutputs.GetProperty("gltf").GetProperty("file").GetString()!);
                var sidecarPath = StaticCellCompileArtifact.ResolveContainedPath(
                    compileRoot,
                    assetOutputs.GetProperty("sidecar").GetProperty("file").GetString()!);
                var loaded = VerifiedGltfLoader.Load(modelPath, sidecarPath);
                if (!loaded.SourceSha256.Equals(
                        asset.GetProperty("sourceSha256").GetString(),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Static CELL asset source hash differs: {assetId}");
                var collisionExpected = asset.GetProperty("coverage")
                    .GetProperty("collisionExported")
                    .GetBoolean();
                var assetKind = asset.GetProperty("assetKind").GetString();
                var landscapeCollision = false;
                if (assetKind == StaticNifAsset &&
                    collisionExpected != (loaded.CollisionScene is not null))
                    throw new InvalidOperationException(
                        $"Static CELL authored collision differs: {assetId}");
                if (assetKind == LandscapeAsset)
                {
                    var collision = asset.GetProperty("collision");
                    var blocks = collision.GetProperty("blockTypes").EnumerateArray()
                        .Select(value => value.GetString())
                        .ToArray();
                    if (collisionExpected || loaded.CollisionScene is not null ||
                        !collision.GetProperty("enabled").GetBoolean() ||
                        collision.GetProperty("source").GetString() != LandscapeCollisionSource ||
                        blocks.Length != SingleLandscapeCollisionBlock ||
                        blocks[0] != LandscapeCollisionBlock)
                        throw new InvalidOperationException(
                            $"Static CELL landscape collision differs: {assetId}");
                    landscapeCollision = true;
                }
                else if (assetKind != StaticNifAsset)
                    throw new InvalidOperationException(
                        $"Static CELL asset kind differs: {assetId}");
                materialBindings += RuntimeMaterialLoader.Apply(
                    loaded.Scene,
                    asset,
                    loadedTextures,
                    configuration.Renderer,
                    configuration.ContentCompiler.RetailGrass);
                SetRenderLayer(loaded.Scene, DefaultRenderLayer);
                prototypes.Add(
                    assetId,
                    new CompiledPrototype(loaded, assetKind, landscapeCollision));
            }

            var sourceCell = cell.GetProperty("cell");
            var formKey = sourceCell.GetProperty("formKey").GetString()!;
            var editorId = sourceCell.GetProperty("editorId").GetString() ?? "";
            root = new Node3D
            {
                Name = $"STATIC_CELL_{SafeNodeName(formKey)}",
                Scale = Vector3.One * unitsToMeters,
            };
            parent.AddChild(root);
            AddCellEnvironment(root, sourceCell, configuration, unitsToMeters, formKey);

            var placements = 0;
            var collisionMeshes = 0;
            var authoredLights = 0;
            var authoredLandscapes = 0;
            var surfaces = 0;
            var vertices = 0;
            foreach (var placement in cell.GetProperty("placements").EnumerateArray())
            {
                var presentationKind = placement.GetProperty("presentationKind").GetString();
                var presentationStatus = placement.GetProperty("presentationStatus").GetString();
                if (placement.GetProperty("readinessStatus").GetString() != PlacementReadiness ||
                    placement.GetProperty("blockerReasons").GetArrayLength() != 0)
                    throw new InvalidOperationException(
                        $"Static CELL placement is not loadable: " +
                        placement.GetProperty("childFormKey").GetString());
                if (presentationKind == NonvisualPresentation)
                {
                    if (presentationStatus != NonvisualPlacementStatus ||
                        placement.GetProperty("assetId").ValueKind != JsonValueKind.Null ||
                        placement.GetProperty("light").ValueKind != JsonValueKind.Null ||
                        placement.GetProperty("landscape").ValueKind != JsonValueKind.Null ||
                        placement.GetProperty("positionGodotUnits").ValueKind != JsonValueKind.Null ||
                        placement.GetProperty("rotationGodotQuaternion").ValueKind != JsonValueKind.Null ||
                        placement.GetProperty("scale").ValueKind != JsonValueKind.Null)
                        throw new InvalidOperationException(
                            $"Static CELL nonvisual outcome differs: " +
                            placement.GetProperty("childFormKey").GetString());
                    placements++;
                    continue;
                }
                var placementNode = new Node3D
                {
                    Name = $"REFR_{placement.GetProperty("childRuntimeFormId").GetString()}",
                    Position = ReadVector(placement.GetProperty("positionGodotUnits")),
                    Basis = new Basis(ReadQuaternion(
                        placement.GetProperty("rotationGodotQuaternion"))),
                    Scale = Vector3.One * placement.GetProperty("scale").GetSingle(),
                };
                root.AddChild(placementNode);
                if (presentationKind is StaticModelPresentation or LandscapePresentation)
                {
                    var assetId = placement.GetProperty("assetId").GetString()!;
                    if (!prototypes.TryGetValue(assetId, out var prototype))
                        throw new InvalidOperationException(
                            $"Static CELL placement references an unknown asset: {assetId}");
                    var expectedStatus = presentationKind == StaticModelPresentation
                        ? PlacementStatus
                        : LandscapePlacementStatus;
                    var expectedAssetKind = presentationKind == StaticModelPresentation
                        ? StaticNifAsset
                        : LandscapeAsset;
                    if (presentationStatus != expectedStatus ||
                        prototype.AssetKind != expectedAssetKind)
                        throw new InvalidOperationException(
                            $"Static CELL placement asset kind differs: {assetId}");
                    var instance = prototype.Gltf.Scene.Duplicate(
                        (int)Node.DuplicateFlags.Default) as Node3D
                        ?? throw new InvalidOperationException(
                            $"Could not duplicate static CELL asset: {assetId}");
                    placementNode.AddChild(instance);
                    SetRenderLayer(instance, DefaultRenderLayer);
                    CountGeometry(instance, ref surfaces, ref vertices);
                    if (buildCollision &&
                        prototype.Gltf.CollisionScene is Node3D collisionPrototype)
                    {
                        var collision = collisionPrototype.Duplicate(
                            (int)Node.DuplicateFlags.Default) as Node3D
                            ?? throw new InvalidOperationException(
                                $"Could not duplicate authored collision: {assetId}");
                        collision.Name = $"AUTHORED_COLLISION_{assetId}";
                        placementNode.AddChild(collision);
                        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(collision))
                        {
                            mesh.Visible = false;
                            mesh.CreateTrimeshCollision();
                            foreach (var body in NodeTraversal.Descendants<StaticBody3D>(mesh))
                                body.CollisionLayer = DefaultRenderLayer;
                            collisionMeshes++;
                        }
                    }
                    else if (buildCollision && prototype.LandscapeCollision)
                    {
                        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(instance))
                        {
                            mesh.CreateTrimeshCollision();
                            var bodies = NodeTraversal.Descendants<StaticBody3D>(mesh).ToArray();
                            if (bodies.Length != SingleLandscapeCollisionBody)
                                throw new InvalidOperationException(
                                    $"Static CELL landscape collision body differs: {assetId}");
                            foreach (var body in bodies)
                                body.CollisionLayer = DefaultRenderLayer;
                            collisionMeshes += bodies.Length;
                        }
                    }
                    if (presentationKind == LandscapePresentation)
                    {
                        if (placement.GetProperty("landscape").ValueKind !=
                            JsonValueKind.Object)
                            throw new InvalidOperationException(
                                $"Static CELL landscape placement has no contract: {assetId}");
                        authoredLandscapes++;
                    }
                }
                else if (presentationKind == PointLightPresentation &&
                    presentationStatus == LightPlacementStatus &&
                    placement.GetProperty("assetId").ValueKind == JsonValueKind.Null)
                {
                    AddPointLight(placementNode, placement, configuration);
                    authoredLights++;
                }
                else
                    throw new InvalidOperationException(
                        $"Static CELL presentation contract differs: " +
                        placement.GetProperty("childFormKey").GetString());
                placements++;
            }
            var lighting = sourceCell.GetProperty("lighting");
            if (lighting.ValueKind == JsonValueKind.Object)
                RuntimeMaterialLoader.ApplyRetailAmbientDirectionalLighting(
                    root,
                    ReadByteColor(lighting.GetProperty("ambient_rgb")),
                    ReadByteColor(lighting.GetProperty("fog_rgb")),
                    lighting.GetProperty("fog_near").GetSingle(),
                    lighting.GetProperty("fog_far").GetSingle(),
                    lighting.GetProperty("fog_power").GetSingle(),
                    unitsToMeters);
            if (lighting.ValueKind == JsonValueKind.Object)
                RuntimeMaterialLoader.ApplyRetailLandscapeLighting(
                    root,
                    ReadByteColor(lighting.GetProperty("ambient_rgb")),
                    ReadByteColor(lighting.GetProperty("fog_rgb")),
                    lighting.GetProperty("fog_near").GetSingle(),
                    lighting.GetProperty("fog_far").GetSingle(),
                    lighting.GetProperty("fog_power").GetSingle(),
                    unitsToMeters);
            return new LoadedStaticCell(
                artifact.ManifestPath,
                artifact.ManifestSha256,
                root,
                formKey,
                editorId,
                assets.Count,
                textures.Count,
                materialBindings,
                placements,
                authoredLights,
                authoredLandscapes,
                collisionMeshes,
                surfaces,
                vertices);
        }
        catch
        {
            root?.QueueFree();
            throw;
        }
        finally
        {
            foreach (var prototype in prototypes.Values)
            {
                prototype.Gltf.Scene.Free();
                prototype.Gltf.CollisionScene?.Free();
            }
        }
    }

    private static void AddPointLight(
        Node3D parent,
        JsonElement placement,
        RuntimeConfiguration configuration)
    {
        var light = placement.GetProperty("light");
        if (light.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Static CELL point light has no contract.");
        parent.AddChild(new OmniLight3D
        {
            Name = $"LIGH_{placement.GetProperty("childRuntimeFormId").GetString()}",
            LightColor = ReadByteColor(light.GetProperty("colorRgb")),
            LightEnergy = MathF.Max(
                configuration.Renderer.MinimumPointLightEnergy,
                light.GetProperty("intensity").GetSingle() *
                configuration.Renderer.PointLightEnergyScale),
            OmniRange = RetailLighting.PointShaderRadius(
                light.GetProperty("effectiveRadiusMeters").GetSingle()),
            OmniAttenuation = RetailLighting.GodotOmniDecayForRetailRemap,
            ShadowEnabled = configuration.Renderer.AuthoredPointLightShadows,
            LightCullMask = DefaultRenderLayer,
        });
    }

    private static void AddCellEnvironment(
        Node3D parent,
        JsonElement cell,
        RuntimeConfiguration configuration,
        float unitsToMeters,
        string formKey)
    {
        var lighting = cell.GetProperty("lighting");
        if (lighting.ValueKind != JsonValueKind.Object)
            return;
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = configuration.Renderer.BackgroundColorRgba.Color(),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = ReadByteColor(lighting.GetProperty("ambient_rgb")),
            AmbientLightEnergy = configuration.Renderer.AmbientEnergyScale,
            TonemapMode = RuntimeRendering.ParseToneMapper(configuration.Renderer.ToneMapper),
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = ReadByteColor(lighting.GetProperty("fog_rgb")),
            FogLightEnergy = configuration.Renderer.FogLightEnergy,
            FogDensity = configuration.Renderer.FogDensity,
            FogDepthBegin = lighting.GetProperty("fog_near").GetSingle() * unitsToMeters,
            FogDepthEnd = lighting.GetProperty("fog_far").GetSingle() * unitsToMeters,
            FogDepthCurve = lighting.GetProperty("fog_power").GetSingle(),
        };
        parent.AddChild(new WorldEnvironment { Environment = environment });
        var rotation = lighting.GetProperty("directional_rotation")
            .EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        if (rotation.Length != DirectionComponents)
            throw new InvalidOperationException(
                "Static CELL directional rotation must contain two values.");
        var surfaceToLight = RetailLighting.SurfaceToLightFromXcllDegrees(
            rotation[0],
            rotation[1]);
        parent.AddChild(new DirectionalLight3D
        {
            Name = $"STATIC_CELL_{SafeNodeName(formKey)}_Directional",
            Transform = new Transform3D(
                RetailLighting.DirectionalLightBasis(surfaceToLight),
                Vector3.Zero),
            LightColor = ReadByteColor(lighting.GetProperty("directional_rgb")),
            LightEnergy = lighting.GetProperty("directional_fade").GetSingle() *
                configuration.Renderer.DirectionalEnergyScale,
            ShadowEnabled = configuration.ActorReview.DirectionalShadows,
            LightCullMask = DefaultRenderLayer,
        });
    }

    private static Color ReadByteColor(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetByte()).ToArray();
        if (values.Length != VectorComponents)
            throw new InvalidOperationException("Static CELL color must contain three values.");
        return new Color(
            values[0] / (float)byte.MaxValue,
            values[1] / (float)byte.MaxValue,
            values[2] / (float)byte.MaxValue);
    }

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != VectorComponents)
            throw new InvalidOperationException("Static CELL vector must contain three values.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != QuaternionComponents)
            throw new InvalidOperationException("Static CELL quaternion must contain four values.");
        return new Quaternion(values[0], values[1], values[2], values[3]).Normalized();
    }

    private static void CountGeometry(Node root, ref int surfaces, ref int vertices)
    {
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(root))
        {
            if (mesh.Mesh is null)
                continue;
            surfaces += mesh.Mesh.GetSurfaceCount();
            if (mesh.Mesh is ArrayMesh arrayMesh)
                vertices += Enumerable.Range(0, arrayMesh.GetSurfaceCount())
                    .Sum(arrayMesh.SurfaceGetArrayLen);
        }
    }

    private static void SetRenderLayer(Node root, uint layer)
    {
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(root))
            mesh.Layers = layer;
    }

    private static string SafeNodeName(string value) =>
        string.Concat(value.Select(character => char.IsLetterOrDigit(character) ? character : '_'));

    private readonly record struct CompiledPrototype(
        VerifiedGltfLoader.LoadedGltf Gltf,
        string AssetKind,
        bool LandscapeCollision);

    internal readonly record struct LoadedStaticCell(
        string ManifestPath,
        string ManifestSha256,
        Node3D Root,
        string FormKey,
        string EditorId,
        int Assets,
        int Textures,
        int MaterialBindings,
        int Placements,
        int AuthoredLights,
        int AuthoredLandscapes,
        int CollisionMeshes,
        int Surfaces,
        int Vertices);
}
