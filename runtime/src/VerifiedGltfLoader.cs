using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class VerifiedGltfLoader
{
    private const string SidecarSchema = "opennv-static-nif-gltf/v1";

    internal static LoadedGltf Load(string modelPath, string sidecarPath)
    {
        var sidecarFile = ResolvePath(sidecarPath);
        using var document = JsonDocument.Parse(File.ReadAllText(sidecarFile));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != SidecarSchema)
            throw new InvalidOperationException($"Unexpected sidecar schema: {sidecarPath}");
        if (root.GetProperty("status").GetString() != "geometry-only")
            throw new InvalidOperationException($"Static slice requires geometry-only status: {sidecarPath}");

        var modelFile = ResolvePath(modelPath);
        var gltf = root.GetProperty("outputs").GetProperty("gltf");
        VerifyHash(modelFile, gltf.GetProperty("sha256").GetString()!);
        var buffer = root.GetProperty("outputs").GetProperty("buffer");
        var bufferFile = Path.Combine(Path.GetDirectoryName(modelFile)!, buffer.GetProperty("file").GetString()!);
        VerifyHash(bufferFile, buffer.GetProperty("sha256").GetString()!);

        var gltfDocument = new GltfDocument();
        var state = new GltfState();
        var error = gltfDocument.AppendFromFile(modelFile, state, 0, Path.GetDirectoryName(modelFile)!);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Godot glTF import failed ({error}): {modelFile}");
        var scene = gltfDocument.GenerateScene(state) as Node3D
            ?? throw new InvalidOperationException($"Godot generated no Node3D scene from glTF: {modelFile}");
        var compiler = root.GetProperty("compiler");
        return new LoadedGltf(
            scene,
            root.GetProperty("source").GetProperty("sha256").GetString()!,
            compiler.GetProperty("name").GetString()!,
            compiler.GetProperty("sha256").GetString()!);
    }

    internal static string ResolvePath(string path) =>
        path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    private static void VerifyHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Provenance hash mismatch: {path}");
    }

    internal readonly record struct LoadedGltf(
        Node3D Scene,
        string SourceSha256,
        string CompilerName,
        string CompilerSha256);
}
