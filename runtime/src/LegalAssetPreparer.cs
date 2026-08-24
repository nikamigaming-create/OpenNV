using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class LegalAssetPreparer
{
    private const string CacheSchema = "opennv-legal-asset-cache/v1";

    internal static PreparedContent Prepare(
        string selectedDataRoot,
        IReadOnlyDictionary<string, string> options,
        RuntimeConfiguration configuration)
    {
        var dataRoot = ResolveDataRoot(selectedDataRoot);

        var contentTool = ResolveContentTool(options);
        if (!File.Exists(contentTool))
            throw new FileNotFoundException("The packaged legal-content helper is missing.", contentTool);
        var cacheRoot = ResolveCacheRoot(options, configuration);
        var arguments = new List<string>
        {
            "--data-root",
            dataRoot,
            "--cache-root",
            cacheRoot,
            "--cell-recipe",
            options.TryGetValue("cell-recipe", out var configuredRecipe)
                ? configuredRecipe
                : configuration.LegalAssets.DefaultCellRecipe,
        };
        var output = new Godot.Collections.Array();
        var exitCode = OS.Execute(
            contentTool,
            arguments.ToArray(),
            output,
            true,
            false);
        if (exitCode != 0)
        {
            var processOutput = string.Join(System.Environment.NewLine, output.Select(value => value.AsString()));
            throw new InvalidOperationException($"Legal-content helper exited with code {exitCode}: {processOutput}");
        }

        return OpenPreparedCache(cacheRoot, dataRoot, contentTool, configuration);
    }

    internal static bool TryRestore(
        IReadOnlyDictionary<string, string> options,
        RuntimeConfiguration configuration,
        out PreparedContent prepared,
        out string? error)
    {
        prepared = default;
        error = null;
        var cacheRoot = ResolveCacheRoot(options, configuration);
        var manifestPath = Path.Combine(cacheRoot, "install-manifest.json");
        if (!File.Exists(manifestPath))
            return false;

        try
        {
            var contentTool = ResolveContentTool(options);
            var dataRoot = ReadManifestDataRoot(manifestPath);
            try
            {
                prepared = OpenPreparedCache(cacheRoot, dataRoot, contentTool, configuration);
            }
            catch when (Directory.Exists(dataRoot))
            {
                prepared = Prepare(dataRoot, options, configuration);
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static PreparedContent OpenPreparedCache(
        string cacheRoot,
        string expectedDataRoot,
        string contentTool,
        RuntimeConfiguration configuration)
    {
        var manifestPath = Path.Combine(cacheRoot, "install-manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != CacheSchema ||
            root.GetProperty("status").GetString() != "prepared-legal-assets")
            throw new InvalidOperationException($"Unexpected legal-asset cache manifest: {manifestPath}");
        var manifestDataRoot = ResolvePath(root.GetProperty("install").GetProperty("dataRoot").GetString()!);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!expectedDataRoot.Equals(manifestDataRoot, comparison))
            throw new InvalidOperationException("Legal-asset cache manifest belongs to a different Data folder.");
        var outputs = root.GetProperty("outputs");
        var prepared = new PreparedContent(
            outputs.GetProperty("model").GetString()!,
            outputs.GetProperty("sidecar").GetString()!,
            outputs.TryGetProperty("cellScene", out var cellScene) && cellScene.ValueKind == JsonValueKind.String
                ? cellScene.GetString()
                : null,
            outputs.TryGetProperty("cellSceneSha256", out var cellSceneSha256) &&
            cellSceneSha256.ValueKind == JsonValueKind.String
                ? cellSceneSha256.GetString()
                : null,
            outputs.TryGetProperty("actorScenes", out var actorScenes) &&
            actorScenes.ValueKind == JsonValueKind.String
                ? actorScenes.GetString()
                : null,
            outputs.TryGetProperty("actorScenesSha256", out var actorScenesSha256) &&
            actorScenesSha256.ValueKind == JsonValueKind.String
                ? actorScenesSha256.GetString()
                : null);
        ValidateCompilerProvenance(prepared.SidecarPath, contentTool, configuration);
        if (prepared.CellScenePath is not null)
        {
            if (prepared.CellSceneSha256 is null)
                throw new InvalidOperationException("Cell scene has no install-manifest hash.");
            VerifyHash(prepared.CellScenePath, prepared.CellSceneSha256);
            ValidateCellCompilerProvenance(prepared.CellScenePath, contentTool, configuration);
        }
        if (prepared.ActorScenesPath is not null)
        {
            if (prepared.ActorScenesSha256 is null)
                throw new InvalidOperationException("Actor scene set has no install-manifest hash.");
            VerifyHash(prepared.ActorScenesPath, prepared.ActorScenesSha256);
        }
        return prepared;
    }

    private static string ReadManifestDataRoot(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return ResolvePath(document.RootElement.GetProperty("install").GetProperty("dataRoot").GetString()!);
    }

    private static void ValidateCompilerProvenance(
        string sidecarPath,
        string contentTool,
        RuntimeConfiguration configuration)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(sidecarPath));
        var compiler = document.RootElement.GetProperty("compiler");
        if (compiler.GetProperty("name").GetString() != configuration.LegalAssets.PackagedCompilerName)
            throw new InvalidOperationException("Legal-asset sidecar was not produced by the packaged content helper.");
        using var stream = File.OpenRead(contentTool);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(compiler.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Legal-asset sidecar compiler hash does not match the packaged content helper.");
    }

    private static void ValidateCellCompilerProvenance(
        string cellScenePath,
        string contentTool,
        RuntimeConfiguration configuration)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(cellScenePath));
        var compiler = document.RootElement.GetProperty("compiler");
        if (compiler.GetProperty("name").GetString() != configuration.LegalAssets.PackagedCompilerName)
            throw new InvalidOperationException("Cell scene was not produced by the packaged content helper.");
        using var stream = File.OpenRead(contentTool);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(compiler.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cell scene compiler hash does not match the packaged content helper.");
    }

    private static void VerifyHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Legal-asset cache hash mismatch: {path}");
    }

    private static string ResolveContentTool(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("content-tool", out var configuredTool))
            return ResolvePath(configuredTool);
        var executableDirectory = Path.GetDirectoryName(OS.GetExecutablePath())
            ?? throw new InvalidOperationException("Cannot resolve the OpenNV executable directory.");
        return Path.Combine(
            executableDirectory,
            OperatingSystem.IsWindows() ? "OpenNV.Content.exe" : "OpenNV.Content");
    }

    private static string ResolveCacheRoot(
        IReadOnlyDictionary<string, string> options,
        RuntimeConfiguration configuration) =>
        options.TryGetValue("cache-root", out var configuredCache)
            ? ResolvePath(configuredCache)
            : ProjectSettings.GlobalizePath(configuration.LegalAssets.DefaultCacheRoot);

    private static string ResolveDataRoot(string selectedRoot)
    {
        var root = ResolvePath(selectedRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Selected Fallout: New Vegas folder does not exist: {root}");
        if (ContainsFile(root, "FalloutNV.esm"))
            return root;
        var dataDirectories = Directory.EnumerateDirectories(root)
            .Where(path => Path.GetFileName(path).Equals("Data", StringComparison.OrdinalIgnoreCase))
            .Where(path => ContainsFile(path, "FalloutNV.esm"))
            .ToArray();
        if (dataDirectories.Length == 1)
            return Path.GetFullPath(dataDirectories[0]);
        throw new DirectoryNotFoundException(
            "Select either the Fallout: New Vegas installation folder or its Data folder. " +
            $"No FalloutNV.esm was found at '{root}' or in its Data child.");
    }

    private static bool ContainsFile(string directory, string expectedName) =>
        Directory.EnumerateFiles(directory)
            .Any(path => Path.GetFileName(path).Equals(expectedName, StringComparison.OrdinalIgnoreCase));

    private static string ResolvePath(string path) =>
        path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    internal readonly record struct PreparedContent(
        string ModelPath,
        string SidecarPath,
        string? CellScenePath,
        string? CellSceneSha256,
        string? ActorScenesPath,
        string? ActorScenesSha256);
}
