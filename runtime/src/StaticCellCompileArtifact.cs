using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime;

internal static class StaticCellCompileArtifact
{
    private const string ManifestFileName = "manifest.json";
    private const string ManifestSchema = "opennv-static-cell-compile-manifest/v1";
    private const string CellSchema = "opennv-static-cell-compile/v1";
    private const string CompileStatus = "static-assets-compiled-runtime-pending";
    private const string RuntimePendingStatus = "pending";

    internal static VerifiedArtifact Load(
        string compilePath,
        RuntimeConfiguration configuration)
    {
        var manifestPath = ResolveManifestPath(compilePath);
        var compileRoot = Path.GetDirectoryName(manifestPath)!;
        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = manifestDocument.RootElement;
        if (manifest.GetProperty("schema").GetString() != ManifestSchema ||
            manifest.GetProperty("status").GetString() != CompileStatus)
            throw new InvalidOperationException(
                $"Static CELL compile is not promotable to a runtime load: {manifestPath}");
        configuration.VerifyCompiledConfigurationDescriptor(
            manifest.GetProperty("runtimeConfiguration"));

        var outputs = manifest.GetProperty("outputs");
        var cellPath = VerifyTopLevelOutput(compileRoot, outputs, "cell", "cell-static.json");
        var assetsPath = VerifyTopLevelOutput(compileRoot, outputs, "assets", "assets.jsonl");
        var texturesPath = VerifyTopLevelOutput(compileRoot, outputs, "textures", "textures.jsonl");
        var blockersPath = VerifyTopLevelOutput(compileRoot, outputs, "blockers", "blockers.jsonl");
        var assets = ReadJsonLines(assetsPath);
        var textures = ReadJsonLines(texturesPath);
        var blockers = ReadJsonLines(blockersPath);
        if (blockers.Count != 0)
            throw new InvalidOperationException("Static CELL compile retains blockers.");

        using var cellDocument = JsonDocument.Parse(File.ReadAllText(cellPath));
        var cell = cellDocument.RootElement;
        if (cell.GetProperty("schema").GetString() != CellSchema ||
            cell.GetProperty("status").GetString() != CompileStatus ||
            cell.GetProperty("runtimeStatus").GetString() != RuntimePendingStatus ||
            cell.GetProperty("parityStatus").GetString() != RuntimePendingStatus)
            throw new InvalidOperationException("Static CELL document has invalid promotion state.");
        VerifyCounts(manifest.GetProperty("counts"), cell, assets, textures, blockers);
        VerifyTextureFiles(compileRoot, textures);
        return new VerifiedArtifact(
            manifestPath,
            FileSha256(manifestPath),
            compileRoot,
            cell.Clone(),
            assets,
            textures);
    }

    internal static string VerifyNestedOutput(string root, JsonElement descriptor)
        => VerifyOutput(
            root,
            descriptor.GetProperty("file").GetString()!,
            descriptor.GetProperty("bytes").GetInt64(),
            descriptor.GetProperty("sha256").GetString()!);

    internal static string ResolveContainedPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException(
                $"Static CELL output path must be relative: {relativePath}");
        var fullRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var relative = Path.GetRelativePath(fullRoot, path);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Static CELL output escapes its compile root: {relativePath}");
        if (!File.Exists(path))
            throw new FileNotFoundException("Static CELL output is missing.", path);
        return path;
    }

    private static string ResolveManifestPath(string compilePath)
    {
        var resolved = VerifiedGltfLoader.ResolvePath(compilePath);
        return Directory.Exists(resolved)
            ? Path.Combine(resolved, ManifestFileName)
            : resolved;
    }

    private static string VerifyTopLevelOutput(
        string root,
        JsonElement outputs,
        string name,
        string expectedFile)
    {
        var descriptor = outputs.GetProperty(name);
        if (descriptor.GetProperty("file").GetString() != expectedFile)
            throw new InvalidOperationException($"Static CELL output name differs: {name}");
        return VerifyNestedOutput(root, descriptor);
    }

    private static string VerifyOutput(
        string root,
        string relativePath,
        long expectedBytes,
        string expectedSha256)
    {
        var path = ResolveContainedPath(root, relativePath);
        if (new FileInfo(path).Length != expectedBytes)
            throw new InvalidOperationException($"Static CELL output byte count differs: {path}");
        VerifiedGltfLoader.VerifyHash(path, expectedSha256);
        return path;
    }

    private static IReadOnlyList<JsonElement> ReadJsonLines(string path)
    {
        var rows = new List<JsonElement>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var document = JsonDocument.Parse(line);
            rows.Add(document.RootElement.Clone());
        }
        return rows;
    }

    private static void VerifyCounts(
        JsonElement counts,
        JsonElement cell,
        IReadOnlyList<JsonElement> assets,
        IReadOnlyList<JsonElement> textures,
        IReadOnlyList<JsonElement> blockers)
    {
        var placements = cell.GetProperty("placements").GetArrayLength();
        if (counts.GetProperty("assets").GetInt32() != assets.Count ||
            counts.GetProperty("textures").GetInt32() != textures.Count ||
            counts.GetProperty("blockers").GetInt32() != blockers.Count ||
            counts.GetProperty("sourceChildren").GetInt32() != placements ||
            counts.GetProperty("compiledPlacements").GetInt32() != placements ||
            cell.GetProperty("blockerCount").GetInt32() != blockers.Count)
            throw new InvalidOperationException("Static CELL compile counts differ.");
    }

    private static void VerifyTextureFiles(
        string root,
        IReadOnlyList<JsonElement> textures)
    {
        foreach (var texture in textures)
        {
            VerifyOutput(
                root,
                texture.GetProperty("png").GetString()!,
                texture.GetProperty("pngBytes").GetInt64(),
                texture.GetProperty("pngSha256").GetString()!);
            foreach (var face in texture.GetProperty("cubeFaces").EnumerateArray())
                VerifyOutput(
                    root,
                    face.GetProperty("png").GetString()!,
                    face.GetProperty("bytes").GetInt64(),
                    face.GetProperty("pngSha256").GetString()!);
        }
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal readonly record struct VerifiedArtifact(
        string ManifestPath,
        string ManifestSha256,
        string CompileRoot,
        JsonElement Cell,
        IReadOnlyList<JsonElement> Assets,
        IReadOnlyList<JsonElement> Textures);
}
