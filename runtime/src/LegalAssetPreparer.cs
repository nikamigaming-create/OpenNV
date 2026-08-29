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
        var dataRoot = ResolveDataRoot(selectedDataRoot, configuration);

        var contentTool = ResolveContentTool(options, configuration)
            ?? throw new FileNotFoundException(
                "Neither the packaged nor source-checkout legal-content helper is available.");
        var compiler = ReadContentToolCompilerIdentity(contentTool);
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
        var (exitCode, output) = ExecuteContentTool(contentTool, arguments);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Legal-content helper exited with code {exitCode}: {output}");
        }

        return OpenPreparedCache(cacheRoot, dataRoot, compiler, configuration);
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
            var contentTool = ResolveContentTool(options, configuration)
                ?? throw new FileNotFoundException(
                    "Neither the packaged nor source-checkout legal-content helper is available.");
            var compiler = ReadContentToolCompilerIdentity(contentTool);
            var dataRoot = ReadManifestDataRoot(manifestPath);
            try
            {
                prepared = OpenPreparedCache(cacheRoot, dataRoot, compiler, configuration);
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
        CompilerIdentity expectedCompiler,
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
        var modelPath = outputs.GetProperty("model").GetString()!;
        var sidecarPath = outputs.GetProperty("sidecar").GetString()!;
        VerifyHash(modelPath, outputs.GetProperty("modelSha256").GetString()!);
        VerifyHash(sidecarPath, outputs.GetProperty("sidecarSha256").GetString()!);
        var prepared = new PreparedContent(
            modelPath,
            sidecarPath,
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
                : null,
            outputs.GetProperty("openingManifest").GetString()!,
            outputs.GetProperty("openingManifestSha256").GetString()!);
        ValidateCompilerProvenance(prepared.SidecarPath, expectedCompiler);
        if (prepared.CellScenePath is not null)
        {
            if (prepared.CellSceneSha256 is null)
                throw new InvalidOperationException("Cell scene has no install-manifest hash.");
            VerifyHash(prepared.CellScenePath, prepared.CellSceneSha256);
            ValidateCellCompilerProvenance(prepared.CellScenePath, expectedCompiler);
        }
        if (prepared.ActorScenesPath is not null)
        {
            if (prepared.ActorScenesSha256 is null)
                throw new InvalidOperationException("Actor scene set has no install-manifest hash.");
            VerifyHash(prepared.ActorScenesPath, prepared.ActorScenesSha256);
        }
        VerifyHash(prepared.OpeningManifestPath, prepared.OpeningManifestSha256);
        ValidateOpeningManifest(prepared.OpeningManifestPath, configuration);
        return prepared;
    }

    private static string ReadManifestDataRoot(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return ResolvePath(document.RootElement.GetProperty("install").GetProperty("dataRoot").GetString()!);
    }

    private static void ValidateCompilerProvenance(
        string sidecarPath,
        CompilerIdentity expectedCompiler)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(sidecarPath));
        ValidateCompilerIdentity(
            document.RootElement.GetProperty("compiler"),
            expectedCompiler,
            "Legal-asset sidecar");
    }

    private static void ValidateCellCompilerProvenance(
        string cellScenePath,
        CompilerIdentity expectedCompiler)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(cellScenePath));
        ValidateCompilerIdentity(
            document.RootElement.GetProperty("compiler"),
            expectedCompiler,
            "Cell scene");
    }

    private static void ValidateCompilerIdentity(
        JsonElement source,
        CompilerIdentity expected,
        string label)
    {
        var actual = new CompilerIdentity(
            source.GetProperty("name").GetString()!,
            source.GetProperty("sha256").GetString()!);
        if (!actual.Name.Equals(expected.Name, StringComparison.Ordinal) ||
            !actual.Sha256.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{label} compiler identity differs from the active legal-content helper.");
    }

    private static void VerifyHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Legal-asset cache hash mismatch: {path}");
    }

    private static void ValidateOpeningManifest(
        string manifestPath,
        RuntimeConfiguration configuration)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "opennv-owned-opening-manifest/v1" ||
            root.GetProperty("status").GetString() != "compiled-owned-opening-graph")
            throw new InvalidOperationException("Prepared opening manifest has an unexpected contract.");
        configuration.VerifyCompiledConfigurationDescriptor(root.GetProperty("configuration"));
        if (root.GetProperty("blockers").GetArrayLength() != 0)
            throw new InvalidOperationException("Prepared opening manifest contains unresolved entry blockers.");
        var ui = root.GetProperty("ui");
        if (!ui.TryGetProperty("gameplayPresentation", out var gameplayPresentation) ||
            gameplayPresentation.ValueKind != JsonValueKind.Object ||
            !gameplayPresentation.TryGetProperty("schema", out var gameplaySchema) ||
            gameplaySchema.GetString() != "opennv-owned-gameplay-ui/v1")
            throw new InvalidOperationException(
                "Prepared opening manifest lacks the current owned gameplay UI contract.");
        if (!gameplayPresentation.TryGetProperty("physicalDevice", out var physicalDevice) ||
            physicalDevice.ValueKind != JsonValueKind.Object ||
            physicalDevice.GetProperty("schema").GetString() !=
                "opennv-owned-physical-pipboy/v1" ||
            physicalDevice.GetProperty("screenSurface").GetString() != "pipboyscreen:0")
            throw new InvalidOperationException(
                "Prepared opening manifest lacks the owned physical Pip-Boy contract.");
        VerifyHash(
            physicalDevice.GetProperty("source").GetString()!,
            physicalDevice.GetProperty("sourceSha256").GetString()!);
        VerifyHash(
            physicalDevice.GetProperty("materialManifest").GetString()!,
            physicalDevice.GetProperty("materialManifestSha256").GetString()!);
        var requiredLayoutTiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["hud"] = ["QuestReminder", "Messages", "Info", "ReticleCenter"],
            ["status"] = [],
            ["items"] = ["IM_MainRect"],
            ["data"] = ["MM_MainRect"],
        };
        var roles = gameplayPresentation.GetProperty("roles")
            .EnumerateArray()
            .ToDictionary(
                role => role.GetProperty("role").GetString()!,
                role => role.GetProperty("layout")
                    .EnumerateArray()
                    .Select(tile => tile.GetProperty("tile").GetString()!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        if (requiredLayoutTiles.Any(required =>
            !roles.TryGetValue(required.Key, out var tiles) ||
            required.Value.Any(tile => !tiles.Contains(tile))))
            throw new InvalidOperationException(
                "Prepared opening manifest lacks required owned gameplay UI layout tiles.");
    }

    private static ContentToolInvocation? ResolveContentTool(
        IReadOnlyDictionary<string, string> options,
        RuntimeConfiguration configuration)
    {
        if (options.TryGetValue("content-tool", out var configuredTool))
        {
            var resolved = ResolvePath(configuredTool);
            if (Path.GetExtension(resolved).Equals(".py", StringComparison.OrdinalIgnoreCase))
                return File.Exists(resolved)
                    ? new ContentToolInvocation(
                        configuration.LegalAssets.SourceContentTool.Executable,
                        new[] { resolved },
                        false,
                        configuration.LegalAssets.SourceContentTool.CompilerName)
                    : null;
            return File.Exists(resolved)
                ? new ContentToolInvocation(
                    resolved,
                    Array.Empty<string>(),
                    true,
                    configuration.LegalAssets.PackagedCompilerName)
                : null;
        }
        var executableDirectory = Path.GetDirectoryName(OS.GetExecutablePath())
            ?? throw new InvalidOperationException("Cannot resolve the OpenNV executable directory.");
        var packaged = Path.Combine(
            executableDirectory,
            OperatingSystem.IsWindows() ? "OpenNV.Content.exe" : "OpenNV.Content");
        if (File.Exists(packaged))
            return new ContentToolInvocation(
                packaged,
                Array.Empty<string>(),
                true,
                configuration.LegalAssets.PackagedCompilerName);
        var sourceScript = Path.GetFullPath(Path.Combine(
            ProjectSettings.GlobalizePath("res://"),
            configuration.LegalAssets.SourceContentTool.Script));
        return File.Exists(sourceScript)
            ? new ContentToolInvocation(
                configuration.LegalAssets.SourceContentTool.Executable,
                new[] { sourceScript },
                false,
                configuration.LegalAssets.SourceContentTool.CompilerName)
            : null;
    }

    private static CompilerIdentity ReadContentToolCompilerIdentity(
        ContentToolInvocation contentTool)
    {
        var (exitCode, output) = ExecuteContentTool(
            contentTool,
            new[] { "--compiler-identity" });
        const string Prefix = "OPENNV_CONTENT_COMPILER_IDENTITY ";
        var payload = output.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SingleOrDefault(value => value.StartsWith(Prefix, StringComparison.Ordinal));
        if (exitCode != 0 || payload is null)
            throw new InvalidOperationException(
                $"Legal-content helper identity query failed with code {exitCode}: {output}");
        using var document = JsonDocument.Parse(payload[Prefix.Length..]);
        var identity = new CompilerIdentity(
            document.RootElement.GetProperty("name").GetString()!,
            document.RootElement.GetProperty("sha256").GetString()!,
            document.RootElement.TryGetProperty("artifactSha256", out var artifactSha256)
                ? artifactSha256.GetString()
                : null);
        if (!identity.Name.Equals(contentTool.CompilerName, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Legal-content helper reported an unexpected compiler name.");
        if (contentTool.Packaged)
        {
            if (identity.ArtifactSha256 is null)
                throw new InvalidOperationException(
                    "Packaged legal-content helper omitted its binary hash.");
            using var stream = File.OpenRead(contentTool.Executable);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!actual.Equals(identity.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Packaged legal-content helper reported an incorrect binary hash.");
        }
        return identity;
    }

    private static (int ExitCode, string Output) ExecuteContentTool(
        ContentToolInvocation contentTool,
        IEnumerable<string> arguments)
    {
        var output = new Godot.Collections.Array();
        var allArguments = contentTool.PrefixArguments.Concat(arguments).ToArray();
        var exitCode = OS.Execute(
            contentTool.Executable,
            allArguments,
            output,
            true,
            false);
        return (
            exitCode,
            string.Join(
                System.Environment.NewLine,
                output.Select(value => value.AsString())));
    }

    private static string ResolveCacheRoot(
        IReadOnlyDictionary<string, string> options,
        RuntimeConfiguration configuration) =>
        options.TryGetValue("cache-root", out var configuredCache)
            ? ResolvePath(configuredCache)
            : ProjectSettings.GlobalizePath(configuration.LegalAssets.DefaultCacheRoot);

    private static string ResolveDataRoot(
        string selectedRoot,
        RuntimeConfiguration configuration)
    {
        var root = ResolvePath(selectedRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Selected game folder does not exist: {root}");
        var ownedData = configuration.LegalAssets.OwnedData;
        if (ContainsFile(root, ownedData.MasterFile))
            return root;
        var dataDirectories = Directory.EnumerateDirectories(root)
            .Where(path => Path.GetFileName(path).Equals(
                ownedData.DataDirectoryName,
                StringComparison.OrdinalIgnoreCase))
            .Where(path => ContainsFile(path, ownedData.MasterFile))
            .ToArray();
        if (dataDirectories.Length == 1)
            return Path.GetFullPath(dataDirectories[0]);
        throw new DirectoryNotFoundException(
            "Select either the configured game installation folder or its data folder. " +
            $"No configured master file was found at '{root}' or in its data child.");
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
        string? ActorScenesSha256,
        string OpeningManifestPath,
        string OpeningManifestSha256);

    private sealed record ContentToolInvocation(
        string Executable,
        IReadOnlyList<string> PrefixArguments,
        bool Packaged,
        string CompilerName);

    private sealed record CompilerIdentity(
        string Name,
        string Sha256,
        string? ArtifactSha256 = null);
}
