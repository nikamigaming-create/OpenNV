using System.Diagnostics;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal sealed class PreparedGameplayPrewarm
{
    private readonly Task<PrewarmResult> _ready;

    private PreparedGameplayPrewarm(Task<PrewarmResult> ready)
    {
        _ready = ready;
    }

    internal static PreparedGameplayPrewarm Start(
        LegalAssetPreparer.PreparedContent prepared)
    {
        var roots = new[] { prepared.CellScenePath }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => VerifiedGltfLoader.ResolvePath(path!))
            .Distinct(PathComparer)
            .ToArray();
        if (roots.Length == 0)
            throw new InvalidOperationException(
                "Prepared gameplay prewarm requires a compiled content root.");
        var cacheBoundaryPaths = new[]
            {
                prepared.CellScenePath,
                prepared.ActorScenesPath,
                prepared.OpeningManifestPath,
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => VerifiedGltfLoader.ResolvePath(path!))
            .Distinct(PathComparer)
            .ToArray();
        var cacheRoot = FindCommonDirectory(cacheBoundaryPaths);
        return new PreparedGameplayPrewarm(
            Task.Run(() => ReadDependencyClosure(roots, cacheRoot)));
    }

    internal async Task<PrewarmResult> WaitAsync() => await _ready;

    private static PrewarmResult ReadDependencyClosure(
        IReadOnlyList<string> roots,
        string cacheRoot)
    {
        var elapsed = Stopwatch.StartNew();
        var pending = new Stack<string>(roots.Reverse());
        var visited = new HashSet<string>(PathComparer);
        long bytesRead = 0;
        while (pending.TryPop(out var path))
        {
            var resolved = Path.GetFullPath(path);
            if (!IsWithinCache(resolved, cacheRoot) || !visited.Add(resolved))
                continue;
            if (!File.Exists(resolved))
                throw new FileNotFoundException(
                    "Prepared gameplay dependency is absent.",
                    resolved);
            var extension = Path.GetExtension(resolved);
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = File.ReadAllBytes(resolved);
                bytesRead += bytes.LongLength;
                using var document = JsonDocument.Parse(bytes);
                EnqueueFileReferences(
                    document.RootElement,
                    Path.GetDirectoryName(resolved)!,
                    cacheRoot,
                    pending);
                continue;
            }
            using var stream = File.OpenRead(resolved);
            bytesRead += stream.Length;
            stream.CopyTo(Stream.Null);
        }
        elapsed.Stop();
        return new PrewarmResult(visited.Count, bytesRead, elapsed.ElapsedMilliseconds);
    }

    private static void EnqueueFileReferences(
        JsonElement value,
        string containingDirectory,
        string cacheRoot,
        Stack<string> pending)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    if (property.NameEquals("linkedCells"))
                        continue;
                    EnqueueFileReferences(
                        property.Value,
                        containingDirectory,
                        cacheRoot,
                        pending);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                    EnqueueFileReferences(item, containingDirectory, cacheRoot, pending);
                break;
            case JsonValueKind.String:
                var candidate = ResolveCandidate(value.GetString(), containingDirectory);
                if (candidate is not null &&
                    IsWithinCache(candidate, cacheRoot) &&
                    File.Exists(candidate))
                    pending.Push(candidate);
                break;
        }
    }

    private static string? ResolveCandidate(string? value, string containingDirectory)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains("://", StringComparison.Ordinal))
            return null;
        try
        {
            return Path.GetFullPath(
                Path.IsPathRooted(value)
                    ? value
                    : Path.Combine(containingDirectory, value));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string FindCommonDirectory(IReadOnlyList<string> paths)
    {
        var common = Path.GetDirectoryName(paths[0])
            ?? throw new InvalidOperationException("Prepared gameplay root has no directory.");
        foreach (var path in paths.Skip(1))
        {
            while (!IsWithinCache(path, common))
                common = Directory.GetParent(common)?.FullName
                    ?? throw new InvalidOperationException(
                        "Prepared gameplay roots do not share a cache directory.");
        }
        return common;
    }

    private static bool IsWithinCache(string path, string cacheRoot)
    {
        var relative = Path.GetRelativePath(cacheRoot, path);
        return relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal readonly record struct PrewarmResult(
        int FileCount,
        long ByteCount,
        long ElapsedMilliseconds);
}
