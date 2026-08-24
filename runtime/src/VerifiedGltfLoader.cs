using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class VerifiedGltfLoader
{
    private const string SidecarSchemaV1 = "opennv-static-nif-gltf/v1";
    private const string SidecarSchemaV2 = "opennv-static-nif-gltf/v2";
    private const string LandscapeSidecarSchema = "opennv-landscape-gltf/v1";

    internal static LoadedGltf Load(string modelPath, string sidecarPath)
    {
        var sidecarFile = ResolvePath(sidecarPath);
        using var document = JsonDocument.Parse(File.ReadAllText(sidecarFile));
        var root = document.RootElement;
        var schema = root.GetProperty("schema").GetString();
        if (schema != SidecarSchemaV1 && schema != SidecarSchemaV2 && schema != LandscapeSidecarSchema)
            throw new InvalidOperationException($"Unexpected sidecar schema: {sidecarPath}");
        if (root.GetProperty("status").GetString() != "geometry-only")
            throw new InvalidOperationException($"Static slice requires geometry-only status: {sidecarPath}");

        var modelFile = ResolvePath(modelPath);
        var outputs = root.GetProperty("outputs");
        var gltf = outputs.GetProperty("gltf");
        VerifyHash(modelFile, gltf.GetProperty("sha256").GetString()!);
        var buffer = outputs.GetProperty("buffer");
        var bufferFile = Path.Combine(Path.GetDirectoryName(modelFile)!, buffer.GetProperty("file").GetString()!);
        VerifyHash(bufferFile, buffer.GetProperty("sha256").GetString()!);

        var scene = LoadScene(modelFile);
        Node3D? collisionScene = null;
        if (outputs.TryGetProperty("collisionGltf", out var collisionGltf))
        {
            var collisionFile = Path.Combine(
                Path.GetDirectoryName(modelFile)!,
                collisionGltf.GetProperty("file").GetString()!);
            VerifyHash(collisionFile, collisionGltf.GetProperty("sha256").GetString()!);
            var collisionBuffer = outputs.GetProperty("collisionBuffer");
            var collisionBufferFile = Path.Combine(
                Path.GetDirectoryName(modelFile)!,
                collisionBuffer.GetProperty("file").GetString()!);
            VerifyHash(collisionBufferFile, collisionBuffer.GetProperty("sha256").GetString()!);
            collisionScene = LoadScene(collisionFile);
        }
        var compiler = root.GetProperty("compiler");
        var dynamicBodies = ReadDynamicBodies(root);
        return new LoadedGltf(
            scene,
            collisionScene,
            dynamicBodies,
            root.GetProperty("source").GetProperty("sha256").GetString()!,
            compiler.GetProperty("name").GetString()!,
            compiler.GetProperty("sha256").GetString()!);
    }

    private static IReadOnlyList<DynamicBodyContract> ReadDynamicBodies(JsonElement root)
    {
        var coverage = root.GetProperty("coverage");
        if (!coverage.TryGetProperty("dynamicPhysicsBodies", out var bodies))
            return Array.Empty<DynamicBodyContract>();
        return bodies.EnumerateArray().Select(body => new DynamicBodyContract(
            body.GetProperty("targetName").GetString()!,
            body.GetProperty("shapeType").GetString()!,
            body.GetProperty("shapeTransformPolicy").GetString()!,
            ReadVector3(body.GetProperty("sourceBodyTranslationHavokUnits")),
            ReadQuaternion(body.GetProperty("sourceBodyRotation")),
            body.GetProperty("mass").GetSingle(),
            body.GetProperty("friction").GetSingle(),
            body.GetProperty("restitution").GetSingle(),
            body.GetProperty("linearDamping").GetSingle(),
            body.GetProperty("angularDamping").GetSingle(),
            body.GetProperty("motionSystem").GetInt32(),
            body.GetProperty("qualityType").GetInt32(),
            body.GetProperty("layer").GetInt32(),
            body.GetProperty("hulls").EnumerateArray().Select(hull => new ConvexHullContract(
                hull.GetProperty("radiusGameUnits").GetSingle(),
                hull.GetProperty("pointsGodotGameUnits").EnumerateArray()
                    .Select(ReadVector3)
                    .ToArray()))
                .ToArray()))
            .ToArray();
    }

    private static Vector3 ReadVector3(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("Dynamic physics vector must contain three values.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4)
            throw new InvalidOperationException("Dynamic physics quaternion must contain four values.");
        return new Quaternion(values[0], values[1], values[2], values[3]);
    }

    private static Node3D LoadScene(string modelFile)
    {
        var gltfDocument = new GltfDocument();
        var state = new GltfState();
        var error = gltfDocument.AppendFromFile(modelFile, state, 0, Path.GetDirectoryName(modelFile)!);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Godot glTF import failed ({error}): {modelFile}");
        return gltfDocument.GenerateScene(state) as Node3D
            ?? throw new InvalidOperationException($"Godot generated no Node3D scene from glTF: {modelFile}");
    }

    internal static string ResolvePath(string path) =>
        path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    internal static void VerifyHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Provenance hash mismatch: {path}");
    }

    internal readonly record struct LoadedGltf(
        Node3D Scene,
        Node3D? CollisionScene,
        IReadOnlyList<DynamicBodyContract> DynamicPhysicsBodies,
        string SourceSha256,
        string CompilerName,
        string CompilerSha256);

    internal readonly record struct DynamicBodyContract(
        string TargetName,
        string ShapeType,
        string ShapeTransformPolicy,
        Vector3 SourceBodyTranslationHavokUnits,
        Quaternion SourceBodyRotation,
        float Mass,
        float Friction,
        float Restitution,
        float LinearDamping,
        float AngularDamping,
        int MotionSystem,
        int QualityType,
        int Layer,
        IReadOnlyList<ConvexHullContract> Hulls);

    internal readonly record struct ConvexHullContract(
        float RadiusGameUnits,
        IReadOnlyList<Vector3> PointsGodotGameUnits);
}
