using Godot;

namespace OpenNV.Runtime;

internal static class StaticModelSlice
{
    internal static LoadedStaticModel Load(
        string modelPath,
        string sidecarPath,
        Node3D parent,
        RuntimeConfiguration configuration)
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

        BuildReferenceView(
            parent,
            meshes[0],
            configuration.DiagnosticPreview,
            configuration.Renderer);
        return new LoadedStaticModel(loaded.SourceSha256, meshes.Length, surfaces, vertices);
    }

    private static void BuildReferenceView(
        Node3D parent,
        MeshInstance3D referenceMesh,
        DiagnosticPreviewConfiguration configuration,
        RendererConfiguration renderer)
    {
        var bounds = referenceMesh.Mesh!.GetAabb();
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
        var camera = new Camera3D
        {
            Position = center + new Vector3(
                extent * configuration.CameraOffsetExtentMultipliers[0],
                extent * configuration.CameraOffsetExtentMultipliers[1],
                extent * configuration.CameraOffsetExtentMultipliers[2]),
            Near = MathF.Max(configuration.MinimumNearMeters, extent / configuration.NearExtentDivisor),
            Far = MathF.Max(configuration.MinimumFarMeters, extent * configuration.FarExtentMultiplier),
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
