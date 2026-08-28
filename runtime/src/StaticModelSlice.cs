using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class StaticModelSliceNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloat0Point04f = 0.04f;
    internal const float PresentationFloat0Point08f = 0.08f;
    internal const float PresentationFloat0Point12f = 0.12f;
    internal const float PresentationFloat0Point1f = 0.1f;
    internal const float PresentationFloat0Point65f = 0.65f;
    internal const float PresentationFloat1Point18f = 1.18f;
    internal const float PresentationFloat1Point2f = 1.2f;
    internal const float PresentationFloat1Point8f = 1.8f;
    internal const float PresentationFloat16Point0f = 16.0f;
    internal const float PresentationFloat2Point2f = 2.2f;
    internal const float PresentationFloat9Point0f = 9.0f;
}

internal static class StaticModelSlice
{
    internal static LoadedStaticModel Load(
        string modelPath,
        string sidecarPath,
        Node3D parent,
        RuntimeConfiguration configuration,
        string? materialManifestPath = null,
        string? materialManifestSha256 = null,
        bool classicDiorama = false)
    {
        var loaded = VerifiedGltfLoader.Load(modelPath, sidecarPath);
        loaded.CollisionScene?.Free();
        var model = loaded.Scene;
        model.Name = "RetailStaticModel";
        parent.AddChild(model);

        var meshes = Descendants<MeshInstance3D>(model).ToArray();
        if (meshes.Length == 0)
            throw new InvalidOperationException("Imported glTF contains no MeshInstance3D nodes.");
        var surfaces = meshes.Sum(mesh => mesh.Mesh?.GetSurfaceCount() ?? 0);
        var vertices = meshes.Sum(mesh =>
            mesh.Mesh is not ArrayMesh arrayMesh
                ? 0
                : Enumerable.Range(0, arrayMesh.GetSurfaceCount()).Sum(arrayMesh.SurfaceGetArrayLen));
        if (surfaces == 0 || vertices == 0)
            throw new InvalidOperationException("Imported glTF contains no renderable surfaces or vertices.");

        var materialBindings = 0;
        if (materialManifestPath is not null)
        {
            if (materialManifestSha256 is null)
                throw new InvalidOperationException(
                    "Static material manifest requires its SHA-256.");
            var manifestPath = VerifiedGltfLoader.ResolvePath(materialManifestPath);
            VerifiedGltfLoader.VerifyHash(manifestPath, materialManifestSha256);
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var materialManifest = document.RootElement;
            if (materialManifest.GetProperty("schema").GetString() !=
                "opennv-static-material-manifest/v1")
                throw new InvalidOperationException(
                    $"Unexpected static material manifest: {manifestPath}");
            var textures = RuntimeMaterialLoader.LoadTextures(
                materialManifest,
                configuration.Renderer);
            materialBindings = RuntimeMaterialLoader.Apply(
                model,
                materialManifest.GetProperty("asset"),
                textures,
                configuration.Renderer,
                configuration.ContentCompiler.RetailGrass);
        }

        var view = BuildReferenceView(
            parent,
            model,
            configuration.DiagnosticPreview,
            configuration.Renderer,
            classicDiorama);
        return new LoadedStaticModel(
            loaded.SourceSha256,
            meshes.Length,
            surfaces,
            vertices,
            materialBindings,
            view.Projection,
            view.Bounds);
    }

    private static ReferenceView BuildReferenceView(
        Node3D parent,
        Node3D model,
        DiagnosticPreviewConfiguration configuration,
        RendererConfiguration renderer,
        bool classicDiorama)
    {
        var bounds = WorldBounds(model);
        var center = bounds.GetCenter();
        var extent = MathF.Max(
            MathF.Max(bounds.Size.X, bounds.Size.Y),
            MathF.Max(bounds.Size.Z, configuration.MinimumNearMeters));
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = configuration.BackgroundColorRgba.Color(),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = configuration.AmbientColorRgba.Color(),
            AmbientLightEnergy = configuration.AmbientEnergy,
            TonemapMode = RuntimeRendering.ParseToneMapper(renderer.ToneMapper),
        };
        parent.AddChild(new WorldEnvironment { Environment = environment });
        parent.AddChild(new DirectionalLight3D
        {
            RotationDegrees = configuration.LightRotationDegrees.Vector3(),
            LightEnergy = configuration.LightEnergy,
            ShadowEnabled = true,
        });
        var cameraTarget = center;
        var cameraPosition = center + new Vector3(extent * StaticModelSliceNumericContracts.PresentationFloat1Point2f, extent * StaticModelSliceNumericContracts.PresentationFloat0Point65f, extent * StaticModelSliceNumericContracts.PresentationFloat1Point8f);
        var cameraSize = 1.0f;
        if (classicDiorama)
        {
            var viewportSize = parent.GetViewport().GetVisibleRect().Size;
            var aspect = viewportSize.Y > 0.0f ? viewportSize.X / viewportSize.Y : StaticModelSliceNumericContracts.PresentationFloat16Point0f / StaticModelSliceNumericContracts.PresentationFloat9Point0f;
            var framingHeight = MathF.Max(bounds.Size.Y, bounds.Size.X / MathF.Max(aspect, StaticModelSliceNumericContracts.PresentationFloat0Point1f));
            var frontZ = bounds.Position.Z + MathF.Min(bounds.Size.Z * StaticModelSliceNumericContracts.PresentationFloat0Point08f, framingHeight * StaticModelSliceNumericContracts.PresentationFloat0Point12f);
            cameraTarget = new Vector3(center.X, center.Y, frontZ);
            cameraPosition = cameraTarget + new Vector3(
                framingHeight * StaticModelSliceNumericContracts.PresentationFloat0Point08f,
                framingHeight * StaticModelSliceNumericContracts.PresentationFloat0Point04f,
                -framingHeight * StaticModelSliceNumericContracts.PresentationFloat2Point2f);
            cameraSize = framingHeight * StaticModelSliceNumericContracts.PresentationFloat1Point18f;
        }
        var camera = new Camera3D
        {
            Position = classicDiorama
                ? cameraPosition
                : center + new Vector3(
                    extent * configuration.CameraOffsetExtentMultipliers[0],
                    extent * configuration.CameraOffsetExtentMultipliers[1],
                    extent * configuration.CameraOffsetExtentMultipliers[2]),
            Projection = classicDiorama
                ? Camera3D.ProjectionType.Orthogonal
                : Camera3D.ProjectionType.Perspective,
            Size = cameraSize,
            Near = MathF.Max(configuration.MinimumNearMeters, extent / configuration.NearExtentDivisor),
            Far = MathF.Max(configuration.MinimumFarMeters, extent * configuration.FarExtentMultiplier),
            Current = true,
        };
        parent.AddChild(camera);
        camera.LookAt(cameraTarget, Vector3.Up);
        return new ReferenceView(
            classicDiorama ? "orthogonal" : "perspective",
            bounds);
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
            throw new InvalidOperationException("Static model contains no bounds.");
        return new Aabb(minimum, maximum - minimum);
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

    private readonly record struct ReferenceView(string Projection, Aabb Bounds);

    internal readonly record struct LoadedStaticModel(
        string SourceSha256,
        int Meshes,
        int Surfaces,
        int Vertices,
        int MaterialBindings,
        string Projection,
        Aabb Bounds);
}
