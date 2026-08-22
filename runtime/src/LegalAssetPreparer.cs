using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class LegalAssetPreparer
{
    private const string CacheSchema = "opennv-legal-asset-cache/v1";

    internal static PreparedContent Prepare(
        string selectedDataRoot,
        IReadOnlyDictionary<string, string> options)
    {
        var dataRoot = ResolvePath(selectedDataRoot);
        if (!Directory.Exists(dataRoot))
            throw new DirectoryNotFoundException($"Data folder does not exist: {dataRoot}");

        var contentTool = ResolveContentTool(options);
        if (!File.Exists(contentTool))
            throw new FileNotFoundException("The packaged legal-content helper is missing.", contentTool);
        var cacheRoot = options.TryGetValue("cache-root", out var configuredCache)
            ? ResolvePath(configuredCache)
            : ProjectSettings.GlobalizePath("user://cache/static-nif-v1");
        var output = new Godot.Collections.Array();
        var exitCode = OS.Execute(
            contentTool,
            ["--data-root", dataRoot, "--cache-root", cacheRoot],
            output,
            true,
            false);
        if (exitCode != 0)
        {
            var processOutput = string.Join(System.Environment.NewLine, output.Select(value => value.AsString()));
            throw new InvalidOperationException($"Legal-content helper exited with code {exitCode}: {processOutput}");
        }

        var manifestPath = Path.Combine(cacheRoot, "install-manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != CacheSchema ||
            root.GetProperty("status").GetString() != "prepared-static-geometry-slice")
            throw new InvalidOperationException($"Unexpected legal-asset cache manifest: {manifestPath}");
        var manifestDataRoot = ResolvePath(root.GetProperty("install").GetProperty("dataRoot").GetString()!);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!dataRoot.Equals(manifestDataRoot, comparison))
            throw new InvalidOperationException("Legal-asset cache manifest belongs to a different Data folder.");
        var outputs = root.GetProperty("outputs");
        var prepared = new PreparedContent(
            outputs.GetProperty("model").GetString()!,
            outputs.GetProperty("sidecar").GetString()!);
        ValidateCompilerProvenance(prepared.SidecarPath, contentTool);
        return prepared;
    }

    private static void ValidateCompilerProvenance(string sidecarPath, string contentTool)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(sidecarPath));
        var compiler = document.RootElement.GetProperty("compiler");
        if (compiler.GetProperty("name").GetString() != "OpenNV.Content packaged direct exporter v1")
            throw new InvalidOperationException("Legal-asset sidecar was not produced by the packaged content helper.");
        using var stream = File.OpenRead(contentTool);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(compiler.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Legal-asset sidecar compiler hash does not match the packaged content helper.");
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

    private static string ResolvePath(string path) =>
        path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    internal readonly record struct PreparedContent(string ModelPath, string SidecarPath);
}
