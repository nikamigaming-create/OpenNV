using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class VerifiedGltfLoader
{
    private const string SidecarSchema = "opennv-static-nif-gltf/v1";
    private const string LandscapeSidecarSchema = "opennv-landscape-gltf/v1";

    internal static LoadedGltf Load(string modelPath, string sidecarPath)
    {
        var sidecarFile = ResolvePath(sidecarPath);
        using var document = JsonDocument.Parse(File.ReadAllText(sidecarFile));
        var root = document.RootElement;
        var schema = root.GetProperty("schema").GetString();
        if (schema != SidecarSchema && schema != LandscapeSidecarSchema)
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
        return new LoadedGltf(
            scene,
            collisionScene,
            root.GetProperty("source").GetProperty("sha256").GetString()!,
            compiler.GetProperty("name").GetString()!,
            compiler.GetProperty("sha256").GetString()!);
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
        string SourceSha256,
        string CompilerName,
        string CompilerSha256);
}
