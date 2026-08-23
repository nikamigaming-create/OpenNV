using Godot;

namespace OpenNV.Runtime;

internal static class StaticModelSlice
{
    internal static LoadedStaticModel Load(string modelPath, string sidecarPath, Node3D parent)
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

        BuildReferenceView(parent, meshes[0]);
        return new LoadedStaticModel(loaded.SourceSha256, meshes.Length, surfaces, vertices);
    }

    private static void BuildReferenceView(Node3D parent, MeshInstance3D referenceMesh)
    {
        var bounds = referenceMesh.Mesh!.GetAabb();
        var center = bounds.GetCenter();
        var extent = MathF.Max(MathF.Max(bounds.Size.X, bounds.Size.Y), MathF.Max(bounds.Size.Z, 1.0f));
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.03f, 0.035f, 0.04f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.35f, 0.38f, 0.42f),
            AmbientLightEnergy = 0.65f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        parent.AddChild(new WorldEnvironment { Environment = environment });
        parent.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50.0f, -30.0f, 0.0f),
            LightEnergy = 1.4f,
            ShadowEnabled = true,
        });
        var camera = new Camera3D
        {
            Position = center + new Vector3(extent * 1.2f, extent * 0.65f, extent * 1.8f),
            Near = MathF.Max(0.01f, extent / 10_000.0f),
            Far = MathF.Max(100.0f, extent * 20.0f),
            Current = true,
        };
        parent.AddChild(camera);
        camera.LookAt(center, Vector3.Up);
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

    internal readonly record struct LoadedStaticModel(string SourceSha256, int Meshes, int Surfaces, int Vertices);
}
