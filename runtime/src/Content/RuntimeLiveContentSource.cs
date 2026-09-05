using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

/// <summary>
/// Reads the selected installation directly. It never writes or restores a
/// derived retail-content inventory.
/// </summary>
internal sealed class RuntimeLiveContentSource : IDisposable
{
    internal const string FalloutNewVegasGame = "fallout-new-vegas";
    internal const string Fallout3Game = "fallout-3";

    private readonly IReadOnlyList<string> _archivePaths;
    private readonly ConcurrentDictionary<string, Lazy<FalloutBsaArchive>> _openArchives =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte[]> _archivePayloads =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _archiveWinners =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LoosePayload> _loosePayloads =
        new(StringComparer.OrdinalIgnoreCase);
    private RuntimeLiveContentSource(
        string contentRoot,
        IReadOnlyList<FalloutPluginSource> pluginSources,
        IReadOnlyList<string> archivePaths,
        string game,
        string campaign,
        string stackId,
        string edition,
        string engineBuild,
        string contentVersion)
    {
        ContentRoot = contentRoot;
        PluginSources = pluginSources;
        _archivePaths = archivePaths;
        Game = game;
        Campaign = campaign;
        StackId = stackId;
        Edition = edition;
        EngineBuild = engineBuild;
        ContentVersion = contentVersion;
        SaveCompatibilityId = $"{edition}:{stackId}";
        SupportedCampaigns = [campaign];
        RequiredSemanticExtensions = [];
        CleanRoomSemanticCapabilities = [];
        ArchiveWarmup = Task.Run(() =>
        {
            Parallel.ForEach(
                _archivePaths,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 8),
                },
                path => _ = GetArchive(path));
            foreach (var archivePath in _archivePaths)
            {
                foreach (var logicalPath in GetArchive(archivePath).MemberPaths)
                    _archiveWinners[logicalPath] = archivePath;
            }
        });
    }

    internal static RuntimeLiveContentSource? Current { get; private set; }
    internal IReadOnlyList<FalloutPluginSource> PluginSources { get; }
    internal string Game { get; }
    internal string StackId { get; }
    internal string SaveCompatibilityId { get; }
    internal string Edition { get; }
    internal string EngineBuild { get; }
    internal string ContentVersion { get; }
    internal IReadOnlyList<string> SupportedCampaigns { get; }
    internal IReadOnlyList<string> RequiredSemanticExtensions { get; }
    internal IReadOnlyList<string> CleanRoomSemanticCapabilities { get; }
    internal string Campaign { get; }
    internal string ContentRoot { get; }
    internal Task ArchiveWarmup { get; }
    // Opt-in diagnostics. Ordinary reads do not hash, copy or journal payloads.
    internal Action<string, string, ReadOnlyMemory<byte>>? ResourceReadObserver { get; set; }

    internal IEnumerable<(string Identity, ReadOnlyMemory<byte> Bytes)> CachedResources() =>
        _archivePayloads.Select(pair => (pair.Key, (ReadOnlyMemory<byte>)pair.Value))
            .Concat(_loosePayloads.Select(pair => (pair.Key, (ReadOnlyMemory<byte>)pair.Value.Data)));

    internal (string File, long Offset, int StoredBytes, bool Compressed) ResourceExtent(string identity)
    {
        var split = identity.IndexOf("::", StringComparison.Ordinal);
        if (split < 0) return (identity, 0, checked((int)new FileInfo(identity).Length), false);
        var file = identity[..split];
        var extent = GetArchive(file).StoredExtent(identity[(split + 2)..]);
        return (file, extent.Offset, extent.Bytes, extent.Compressed);
    }
    internal static void Configure(string selectedRoot, string expectedCampaign)
    {
        Current?.Dispose();
        Current = null;
        var installation = NativeGameInstallation.Detect(selectedRoot);
        if (installation.Game is not (NativeGame.FalloutNewVegas or NativeGame.Fallout3))
            throw new InvalidDataException(
                "The direct ESM/BSA runtime accepts Fallout: New Vegas or Fallout 3 installations.");

        var campaign = installation.Game == NativeGame.Fallout3
            ? Fallout3Game
            : FalloutNewVegasGame;
        if (!string.Equals(campaign, expectedCampaign, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Selected installation is {campaign}, not requested campaign {expectedCampaign}.");

        var pluginFiles = ResolvePluginOrder(installation.ContentRoot, installation.Game);
        var plugins = pluginFiles.Select(path =>
        {
            var info = new FileInfo(path);
            return new FalloutPluginSource(
                info.Name,
                info.FullName,
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds());
        }).ToArray();
        if (plugins.Length == 0)
            throw new InvalidDataException("The selected Data folder contains no live ESM/ESP files.");

        var archives = ResolveArchiveOrder(
            installation.ContentRoot,
            plugins,
            installation.Game);
        if (archives.Count == 0)
            throw new InvalidDataException("The selected Data folder contains no live BSA files.");

        var edition = campaign;
        var build = installation.Game == NativeGame.Fallout3 ? "1.7.0.4" : "1.4.0.525";
        var identity = ComputeLiveIdentity(plugins, archives);
        Current = new RuntimeLiveContentSource(
            installation.ContentRoot,
            plugins,
            archives,
            campaign,
            campaign,
            identity,
            edition,
            build,
            build);
    }

    internal static void Clear()
    {
        Current?.Dispose();
        Current = null;
    }

    internal bool TryRead(string logicalPath, string? preferredArchive, out byte[] data, out string source)
    {
        if (TryResolveLoose(logicalPath, out var loosePath))
        {
            var info = new FileInfo(loosePath);
            var mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
            var payload = _loosePayloads.AddOrUpdate(
                loosePath,
                _ => new LoosePayload(info.Length, mtime, File.ReadAllBytes(loosePath)),
                (_, existing) => existing.Bytes == info.Length && existing.MtimeMilliseconds == mtime
                    ? existing
                    : new LoosePayload(info.Length, mtime, File.ReadAllBytes(loosePath)));
            data = payload.Data;
            source = loosePath;
            ResourceReadObserver?.Invoke(logicalPath, source, data);
            return true;
        }
        var canonical = FalloutBsaArchive.CanonicalPath(logicalPath);
        if (TryGetIndexedArchive(canonical, preferredArchive, out var indexedArchive))
        {
            var archive = GetArchive(indexedArchive);
            source = $"{indexedArchive}::{canonical}";
            data = _archivePayloads.GetOrAdd(source, _ => archive.Read(canonical));
            ResourceReadObserver?.Invoke(logicalPath, source, data);
            return true;
        }
        data = [];
        source = string.Empty;
        return false;
    }

    internal bool TryResolve(string logicalPath, string? preferredArchive, out string source)
    {
        if (TryResolveLoose(logicalPath, out source))
            return true;
        var canonical = FalloutBsaArchive.CanonicalPath(logicalPath);
        if (TryGetIndexedArchive(canonical, preferredArchive, out var indexedArchive))
        {
            source = $"{indexedArchive}::{canonical}";
            return true;
        }
        source = string.Empty;
        return false;
    }

    internal IReadOnlyList<string> ResourcePathsUnder(string logicalDirectory)
    {
        var prefix = FalloutBsaArchive.CanonicalPath(logicalDirectory).TrimEnd('\\') + "\\";
        ArchiveWarmup.GetAwaiter().GetResult();
        var paths = _archiveWinners.Keys.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directory = Path.GetFullPath(Path.Combine(ContentRoot, prefix.Replace('\\', Path.DirectorySeparatorChar)));
        var rootPrefix = Path.TrimEndingDirectorySeparator(ContentRoot) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(rootPrefix, PathComparison))
            throw new InvalidDataException("Owned resource directory escapes the installation.");
        if (Directory.Exists(directory))
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                paths.Add(FalloutBsaArchive.CanonicalPath(Path.GetRelativePath(ContentRoot, file)));
        return paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private bool TryGetIndexedArchive(
        string canonical,
        string? preferredArchive,
        out string archivePath)
    {
        if (!string.IsNullOrWhiteSpace(preferredArchive))
        {
            var preferredPath = CandidateArchives(preferredArchive).First();
            if (GetArchive(preferredPath).Contains(canonical))
            {
                archivePath = preferredPath;
                return true;
            }
        }
        if (ArchiveWarmup.IsCompletedSuccessfully &&
            _archiveWinners.TryGetValue(canonical, out archivePath!))
            return true;
        foreach (var candidate in CandidateArchives(preferredArchive))
        {
            if (GetArchive(candidate).Contains(canonical))
            {
                archivePath = candidate;
                return true;
            }
        }
        archivePath = string.Empty;
        return false;
    }

    private bool TryResolveLoose(string logicalPath, out string path)
    {
        var relative = FalloutBsaArchive.CanonicalPath(logicalPath)
            .Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(ContentRoot, relative));
        var prefix = Path.TrimEndingDirectorySeparator(ContentRoot) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, PathComparison) || !File.Exists(candidate))
        {
            path = string.Empty;
            return false;
        }
        path = candidate;
        return true;
    }

    private IEnumerable<string> CandidateArchives(string? preferredArchive)
    {
        if (!string.IsNullOrWhiteSpace(preferredArchive))
        {
            if (Path.GetFileName(preferredArchive) != preferredArchive)
                throw new InvalidDataException("A preferred BSA name must not contain a path.");
            var preferred = _archivePaths.Where(path => string.Equals(
                Path.GetFileName(path), preferredArchive, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (preferred.Length != 1)
                throw new InvalidDataException($"The requested live BSA is missing or ambiguous: {preferredArchive}");
            yield return preferred[0];
        }
        for (var index = _archivePaths.Count - 1; index >= 0; --index)
        {
            if (string.Equals(Path.GetFileName(_archivePaths[index]), preferredArchive,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            yield return _archivePaths[index];
        }
    }

    private FalloutBsaArchive GetArchive(string path)
        => _openArchives.GetOrAdd(
            path,
            static candidate => new Lazy<FalloutBsaArchive>(
                () => new FalloutBsaArchive(candidate),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    public void Dispose()
    {
        foreach (var archive in _openArchives.Values)
        {
            if (archive.IsValueCreated)
                archive.Value.Dispose();
        }
    }

    private static IReadOnlyList<string> ResolvePluginOrder(string dataRoot, NativeGame game)
    {
        var available = Directory.EnumerateFiles(dataRoot)
            .Where(path => Path.GetExtension(path) is var extension &&
                (extension.Equals(".esm", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".esp", StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(path => Path.GetFileName(path)!, Path.GetFullPath, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var master = game == NativeGame.Fallout3 ? "Fallout3.esm" : "FalloutNV.esm";
        if (available.Remove(master, out var masterPath))
            result.Add(masterPath);

        var profileName = game == NativeGame.Fallout3 ? "Fallout3" : "FalloutNV";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pluginsPath = string.IsNullOrWhiteSpace(local)
            ? string.Empty
            : Path.Combine(local, profileName, "plugins.txt");
        if (File.Exists(pluginsPath))
        {
            foreach (var raw in File.ReadLines(pluginsPath))
            {
                var name = raw.Trim().TrimStart('*');
                if (name.Length == 0 || name.StartsWith('#') || !available.Remove(name, out var path))
                    continue;
                result.Add(path);
            }
        }
        result.AddRange(available.Values
            .Where(path => Path.GetExtension(path).Equals(".esm", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => new FileInfo(path).LastWriteTimeUtc)
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase));
        return result;
    }

    private static IReadOnlyList<string> ResolveArchiveOrder(
        string dataRoot,
        IReadOnlyList<FalloutPluginSource> plugins,
        NativeGame game)
    {
        var available = Directory.EnumerateFiles(dataRoot)
            .Where(path => Path.GetExtension(path).Equals(".bsa", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => Path.GetFileName(path)!, Path.GetFullPath, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var profileName = game == NativeGame.Fallout3 ? "Fallout3" : "FalloutNV";
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var configuredIni = string.IsNullOrWhiteSpace(documents)
            ? string.Empty
            : Path.Combine(documents, "My Games", profileName, "Fallout.ini");
        var defaultIni = Path.Combine(
            Directory.GetParent(dataRoot)?.FullName ?? dataRoot,
            "Fallout_default.ini");
        var iniPath = File.Exists(configuredIni) ? configuredIni : defaultIni;
        if (File.Exists(iniPath))
        {
            foreach (var line in File.ReadLines(iniPath))
            {
                var split = line.IndexOf('=');
                if (split <= 0 ||
                    !line[..split].Trim().StartsWith("sArchiveList", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var name in line[(split + 1)..].Split(',', StringSplitOptions.TrimEntries |
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    if (available.Remove(name, out var path))
                        result.Add(path);
                }
            }
        }
        foreach (var plugin in plugins)
        {
            var stem = Path.GetFileNameWithoutExtension(plugin.Name);
            foreach (var name in available.Keys.Where(name =>
                         Path.GetFileNameWithoutExtension(name).Equals(stem, StringComparison.OrdinalIgnoreCase) ||
                         Path.GetFileNameWithoutExtension(name).StartsWith(
                             stem + " - ", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                     .ToArray())
            {
                result.Add(available[name]);
                available.Remove(name);
            }
        }
        result.AddRange(available.Values.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase));
        return result;
    }

    private static string ComputeLiveIdentity(
        IReadOnlyList<FalloutPluginSource> plugins,
        IReadOnlyList<string> archives)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var plugin in plugins)
            hash.AppendData(Encoding.UTF8.GetBytes(
                $"P\0{plugin.Name}\0{plugin.RegisteredBytes}\0{plugin.RegisteredMtimeUnixMilliseconds}\n"));
        foreach (var path in archives)
        {
            var info = new FileInfo(path);
            hash.AppendData(Encoding.UTF8.GetBytes(
                $"B\0{info.Name}\0{info.Length}\0{new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds()}\n"));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly record struct LoosePayload(
        long Bytes,
        long MtimeMilliseconds,
        byte[] Data);

}
