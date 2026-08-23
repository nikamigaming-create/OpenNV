using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class StaticModelSlice
{
    internal static LoadedStaticModel Load(
        string modelPath,
        string sidecarPath,
        Node3D parent,
        string? materialManifestPath = null,
        string? materialManifestSha256 = null,
        bool classicDiorama = false)
    {
        var loaded = VerifiedGltfLoader.Load(modelPath, sidecarPath);
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
                throw new InvalidOperationException("Static material manifest requires its SHA-256.");
            var manifestPath = VerifiedGltfLoader.ResolvePath(materialManifestPath);
            VerifiedGltfLoader.VerifyHash(manifestPath, materialManifestSha256);
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var materialManifest = document.RootElement;
            if (materialManifest.GetProperty("schema").GetString() != "opennv-static-material-manifest/v1")
                throw new InvalidOperationException($"Unexpected static material manifest: {manifestPath}");
            var textures = RuntimeMaterialLoader.LoadTextures(materialManifest);
            materialBindings = RuntimeMaterialLoader.Apply(
                model,
                materialManifest.GetProperty("asset"),
                textures);
        }

        var view = BuildReferenceView(parent, model, classicDiorama);
        return new LoadedStaticModel(
            loaded.SourceSha256,
            meshes.Length,
            surfaces,
            vertices,
            materialBindings,
            view.Projection,
            view.Bounds);
    }

    private static ReferenceView BuildReferenceView(Node3D parent, Node3D model, bool classicDiorama)
    {
        var bounds = WorldBounds(model);
        var center = bounds.GetCenter();
        var extent = MathF.Max(MathF.Max(bounds.Size.X, bounds.Size.Y), MathF.Max(bounds.Size.Z, 1.0f));
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.03f, 0.035f, 0.04f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.35f, 0.38f, 0.42f),
            AmbientLightEnergy = classicDiorama ? 0.85f : 0.65f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        parent.AddChild(new WorldEnvironment { Environment = environment });
        parent.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50.0f, -30.0f, 0.0f),
            LightEnergy = 1.4f,
            ShadowEnabled = true,
        });
        var cameraTarget = center;
        var cameraPosition = center + new Vector3(extent * 1.2f, extent * 0.65f, extent * 1.8f);
        var cameraSize = 1.0f;
        if (classicDiorama)
        {
            var viewportSize = parent.GetViewport().GetVisibleRect().Size;
            var aspect = viewportSize.Y > 0.0f ? viewportSize.X / viewportSize.Y : 16.0f / 9.0f;
            var framingHeight = MathF.Max(bounds.Size.Y, bounds.Size.X / MathF.Max(aspect, 0.1f));
            var frontZ = bounds.Position.Z + MathF.Min(bounds.Size.Z * 0.08f, framingHeight * 0.12f);
            cameraTarget = new Vector3(center.X, center.Y, frontZ);
            cameraPosition = cameraTarget + new Vector3(
                framingHeight * 0.08f,
                framingHeight * 0.04f,
                -framingHeight * 2.2f);
            cameraSize = framingHeight * 1.18f;
        }
        var camera = new Camera3D
        {
            Position = cameraPosition,
            Projection = classicDiorama
                ? Camera3D.ProjectionType.Orthogonal
                : Camera3D.ProjectionType.Perspective,
            Size = cameraSize,
            Near = MathF.Max(0.01f, extent / 10_000.0f),
            Far = MathF.Max(100.0f, extent * 20.0f),
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
