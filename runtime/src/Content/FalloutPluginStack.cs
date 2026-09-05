using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPluginSource(
    string Name,
    string AbsolutePath,
    long? RegisteredBytes = null,
    long? RegisteredMtimeUnixMilliseconds = null,
    string? RegisteredSha256 = null);

internal readonly record struct FalloutPluginStackLoadMetrics(
    TimeSpan PluginHeaderScan,
    TimeSpan WinnerConstruction);

internal sealed class FalloutPluginContext
{
    private string? _sha256;

    internal FalloutPluginContext(
        FalloutPlugin plugin,
        int loadOrderIndex,
        long bytes,
        long mtimeUnixMilliseconds,
        string? registeredSha256)
    {
        Plugin = plugin;
        LoadOrderIndex = loadOrderIndex;
        Bytes = bytes;
        MtimeUnixMilliseconds = mtimeUnixMilliseconds;
        _sha256 = registeredSha256?.ToLowerInvariant();
    }

    internal FalloutPlugin Plugin { get; }
    internal int LoadOrderIndex { get; }
    internal long Bytes { get; }
    internal long MtimeUnixMilliseconds { get; }

    // Deliberately lazy: launcher manifests already bind and validate byte/mtime
    // provenance, so normal startup does not rehash every large plugin.
    internal string Sha256 => _sha256 ??= HashFile(Plugin.Path);

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

internal sealed class FalloutPluginStack : IDisposable
{
    private const int MaximumPluginCount = 256;
    private const int Sha256HexCharacterCount = SHA256.HashSizeInBytes * 2;

    private readonly ReadOnlyCollection<FalloutPluginContext> _plugins;
    private readonly IReadOnlyDictionary<FalloutFormKey, FalloutPluginRecord> _winners;
    private readonly IReadOnlyDictionary<string, int> _loadOrderIndices;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<FalloutFormKey>> _winnerKeysBySignature;
    private readonly object _signatureIndexLock = new();
    private readonly Dictionary<string, IReadOnlyList<FalloutPluginRecord>> _effectiveBySignature =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<FalloutPluginRecord>> _registrationBySignature =
        new(StringComparer.Ordinal);
    private readonly int _effectiveRecordCount;
    private readonly object _cellIndexLock = new();
    private readonly Dictionary<string, Dictionary<FalloutFormKey, IReadOnlyList<FalloutPluginRecord>>>
        _cellChildrenBySignatureAndCell = new(StringComparer.Ordinal);

    private FalloutPluginStack(
        IReadOnlyList<FalloutPluginContext> plugins,
        IDictionary<FalloutFormKey, FalloutPluginRecord> winners,
        IDictionary<string, int> loadOrderIndices)
    {
        _plugins = new ReadOnlyCollection<FalloutPluginContext>(plugins.ToArray());
        _winners = new ReadOnlyDictionary<FalloutFormKey, FalloutPluginRecord>(
            new Dictionary<FalloutFormKey, FalloutPluginRecord>(winners, FalloutFormKeyComparer.Instance));
        _loadOrderIndices = new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(loadOrderIndices, StringComparer.OrdinalIgnoreCase));
        _winnerKeysBySignature = new ReadOnlyDictionary<string, IReadOnlyList<FalloutFormKey>>(
            _winners
                .Where(pair => !pair.Value.IsDeleted)
                .GroupBy(pair => pair.Value.Signature, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<FalloutFormKey>)group.Select(pair => pair.Key).ToArray(),
                    StringComparer.Ordinal));
        _effectiveRecordCount = _winnerKeysBySignature.Values.Sum(keys => keys.Count);
    }

    internal IReadOnlyList<FalloutPluginContext> Plugins => _plugins;
    internal int WinnerRecordCount => _winners.Count;
    internal int EffectiveRecordCount => _effectiveRecordCount;

    internal static FalloutPluginStack Load(string dataRoot, IReadOnlyList<string> configuredNames)
    {
        var root = Path.GetFullPath(dataRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Plugin Data root does not exist: {root}");
        var sources = configuredNames
            .Select(name => new FalloutPluginSource(name, FindExactlyOneCaseInsensitiveFile(root, name)))
            .ToArray();
        return Load(sources);
    }

    internal static FalloutPluginStack Load(IReadOnlyList<FalloutPluginSource> sources)
        => Load(sources, out _);

    internal static FalloutPluginStack Load(
        IReadOnlyList<FalloutPluginSource> sources,
        out FalloutPluginStackLoadMetrics metrics)
        => Load(sources, loadAllSignatureIndexesForAudit: false, out metrics);

    internal static FalloutPluginStack Load(
        IReadOnlyList<FalloutPluginSource> sources,
        bool loadAllSignatureIndexesForAudit,
        out FalloutPluginStackLoadMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
            throw new FalloutPluginFormatException("Plugin load order is empty.");
        if (sources.Count > MaximumPluginCount)
            throw new FalloutPluginFormatException("Plugin load order exceeds the 8-bit FormID namespace.");
        if (sources.Any(source => string.IsNullOrWhiteSpace(source.Name)) ||
            sources.Select(source => source.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count)
            throw new FalloutPluginFormatException("Plugin load order contains an invalid or duplicate name.");

        var canonicalByFold = sources.ToDictionary(source => source.Name, source => source.Name, StringComparer.OrdinalIgnoreCase);
        var orderedPluginNames = sources.Select(source => source.Name).ToArray();
        var uniquePaths = new HashSet<string>(PathComparer);
        foreach (var source in sources)
        {
            ValidatePluginName(source.Name);
            if (!Path.IsPathFullyQualified(source.AbsolutePath))
                throw new FalloutPluginFormatException(
                    $"Layered plugin path must be absolute: {source.AbsolutePath}");
            var path = Path.GetFullPath(source.AbsolutePath);
            if (!Path.GetFileName(path).Equals(source.Name, StringComparison.OrdinalIgnoreCase))
                throw new FalloutPluginFormatException(
                    $"Layered plugin name/path differ: {source.Name} != {path}");
            if (!uniquePaths.Add(path))
                throw new FalloutPluginFormatException($"Layered plugin path is duplicated: {path}");
            ValidateRegisteredProvenance(source, path);
        }
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contexts = new List<FalloutPluginContext>(sources.Count);
        var winners = new Dictionary<FalloutFormKey, FalloutPluginRecord>(FalloutFormKeyComparer.Instance);
        var recordTypes = new Dictionary<FalloutFormKey, string>(FalloutFormKeyComparer.Instance);
        var loadOrderIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pluginHeaderScan = new Stopwatch();
        var winnerConstruction = new Stopwatch();
        var opened = new FalloutPlugin?[sources.Count];

        pluginHeaderScan.Start();
        try
        {
            Parallel.For(
                0,
                sources.Count,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6),
                },
                index => opened[index] = FalloutPlugin.Open(
                    Path.GetFullPath(sources[index].AbsolutePath),
                    sources[index].Name));
        }
        catch
        {
            foreach (var plugin in opened)
                plugin?.Dispose();
            throw;
        }
        finally
        {
            pluginHeaderScan.Stop();
        }

        try
        {
            for (var loadIndex = 0; loadIndex < sources.Count; loadIndex++)
            {
                var source = sources[loadIndex];
                var configuredName = source.Name;
                var path = Path.GetFullPath(source.AbsolutePath);
                var plugin = opened[loadIndex] ?? throw new InvalidOperationException(
                    $"Plugin did not finish opening: {configuredName}");
                opened[loadIndex] = null;
                var info = new FileInfo(path);
                contexts.Add(new FalloutPluginContext(
                    plugin,
                    loadIndex,
                    info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                    source.RegisteredSha256));
                var canonicalMasters = new List<string>(plugin.Masters.Count);
                foreach (var declaredMaster in plugin.Masters)
                {
                    if (!canonicalByFold.TryGetValue(declaredMaster, out var canonicalMaster))
                        throw new FalloutPluginFormatException(
                            $"{configuredName} requires master outside the configured stack: {declaredMaster}");
                    if (!loaded.Contains(canonicalMaster))
                        throw new FalloutPluginFormatException(
                            $"{configuredName} master must occur earlier in load order: {canonicalMaster}");
                    canonicalMasters.Add(canonicalMaster);
                }
                plugin.SetLoadOrderContext(canonicalMasters, loadIndex, orderedPluginNames);

                loadOrderIndices.Add(configuredName, loadIndex);
                loaded.Add(configuredName);
                winnerConstruction.Start();
                foreach (var record in plugin.Records)
                {
                    if (record.Signature == "TES4")
                        continue;
                    if (record.RawFormId == 0)
                        throw new FalloutPluginFormatException(
                            $"{configuredName} {record.Signature} has a zero FormID at 0x{record.HeaderOffset:x}.");
                    FalloutFormKey key;
                    try
                    {
                        key = record.FormKey;
                    }
                    catch (FalloutPluginFormatException error)
                    {
                        throw new FalloutPluginFormatException(
                            $"{configuredName} {record.Signature} {record.RawFormId:x8} at " +
                            $"0x{record.HeaderOffset:x} has invalid FormID namespace: {error.Message}",
                            error);
                    }
                    if (recordTypes.TryGetValue(key, out var earlierType) && earlierType != record.Signature)
                        throw new FalloutPluginFormatException(
                            $"Form {key} changes record type from {earlierType} to {record.Signature} " +
                            $"in {configuredName}.");
                    recordTypes[key] = record.Signature;
                    winners[key] = record;
                }
                winnerConstruction.Stop();
            }
            winnerConstruction.Start();
            var stack = new FalloutPluginStack(contexts, winners, loadOrderIndices);
            if (loadAllSignatureIndexesForAudit)
                stack.LoadAllSignatureIndexes();
            winnerConstruction.Stop();
            metrics = new FalloutPluginStackLoadMetrics(
                pluginHeaderScan.Elapsed,
                winnerConstruction.Elapsed);
            return stack;
        }
        catch
        {
            foreach (var context in contexts)
                context.Plugin.Dispose();
            foreach (var plugin in opened)
                plugin?.Dispose();
            throw;
        }
    }

    internal bool TryGetWinner(FalloutFormKey key, out FalloutPluginRecord record) =>
        _winners.TryGetValue(key, out record!);

    internal IReadOnlyList<FalloutPluginRecord> EffectiveRecords(string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        lock (_signatureIndexLock)
        {
            if (_effectiveBySignature.TryGetValue(signature, out var existing))
                return existing;
            var records = _winnerKeysBySignature.TryGetValue(signature, out var keys)
                ? keys.OrderBy(RuntimeFormId).Select(key => _winners[key]).ToArray()
                : [];
            _effectiveBySignature.Add(signature, records);
            return records;
        }
    }

    internal IReadOnlyList<FalloutPluginRecord> EffectiveRecordsInRegistrationOrder(string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        lock (_signatureIndexLock)
        {
            if (_registrationBySignature.TryGetValue(signature, out var existing)) return existing;
            var seen = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
            var records = new List<FalloutPluginRecord>();
            foreach (var plugin in _plugins)
                foreach (var record in plugin.Plugin.Records)
                    if (record.Signature == signature && seen.Add(record.FormKey) && TryGetEffective(record.FormKey, out var winner))
                        records.Add(winner);
            return _registrationBySignature[signature] = records.ToArray();
        }
    }

    private void LoadAllSignatureIndexes()
    {
        lock (_signatureIndexLock)
        {
            foreach (var pair in _winnerKeysBySignature)
                _effectiveBySignature.Add(
                    pair.Key,
                    pair.Value.OrderBy(RuntimeFormId).Select(key => _winners[key]).ToArray());
        }
    }

    internal IReadOnlyList<FalloutPluginRecord> EffectiveCellChildren(
        FalloutFormKey cell,
        IReadOnlySet<string> signatures)
    {
        var result = new List<FalloutPluginRecord>();
        foreach (var signature in signatures)
        {
            result.AddRange(CellChildrenForSignatureAndCell(signature, cell));
        }
        result.Sort((left, right) => RuntimeFormId(left.FormKey).CompareTo(RuntimeFormId(right.FormKey)));
        return result;
    }

    private IReadOnlyList<FalloutPluginRecord> CellChildrenForSignatureAndCell(
        string signature,
        FalloutFormKey cell)
    {
        lock (_cellIndexLock)
        {
            if (!_cellChildrenBySignatureAndCell.TryGetValue(signature, out var byCell))
            {
                byCell = new Dictionary<FalloutFormKey, IReadOnlyList<FalloutPluginRecord>>(
                    FalloutFormKeyComparer.Instance);
                _cellChildrenBySignatureAndCell.Add(signature, byCell);
            }
            if (byCell.TryGetValue(cell, out var existing))
                return existing;
            var records = _winnerKeysBySignature.TryGetValue(signature, out var keys)
                ? keys.Select(key => _winners[key])
                    .Where(record =>
                    {
                        var parent = FalloutCellSceneReader.ParentCell(record);
                        return parent is not null &&
                            FalloutFormKeyComparer.Instance.Equals(parent.Value, cell);
                    })
                    .OrderBy(record => RuntimeFormId(record.FormKey))
                    .ToArray()
                : [];
            byCell.Add(cell, records);
            return records;
        }
    }

    internal IReadOnlyList<FalloutPluginRecord> EffectiveRecords() =>
        _winners
            .Where(pair => !pair.Value.IsDeleted)
            .OrderBy(pair => RuntimeFormId(pair.Key))
            .Select(pair => pair.Value)
            .ToArray();

    internal bool TryGetEffective(FalloutFormKey key, out FalloutPluginRecord record)
    {
        if (_winners.TryGetValue(key, out record!) && !record.IsDeleted)
            return true;
        record = null!;
        return false;
    }

    internal FalloutPluginRecord GetEffective(FalloutFormKey key)
    {
        if (!_winners.TryGetValue(key, out var winner))
            throw new KeyNotFoundException($"No record exists for {key}.");
        if (winner.IsDeleted)
            throw new KeyNotFoundException($"The winning record for {key} is deleted by {winner.Plugin.Name}.");
        return winner;
    }

    internal uint RuntimeFormId(FalloutFormKey key)
    {
        if (string.IsNullOrWhiteSpace(key.OwnerPlugin) || key.ObjectId > FalloutFormKey.ObjectIdMask)
            throw new FalloutPluginFormatException($"Invalid FormKey object ID: {key}");
        if (!_loadOrderIndices.TryGetValue(key.OwnerPlugin, out var index))
            throw new KeyNotFoundException($"FormKey owner is outside the loaded stack: {key.OwnerPlugin}");
        return ((uint)index << FalloutFormKey.ObjectIdBits) | key.ObjectId;
    }

    internal FalloutFormKey RuntimeFormKey(uint runtimeFormId)
    {
        var loadOrderIndex = runtimeFormId >> FalloutFormKey.ObjectIdBits;
        var objectId = runtimeFormId & FalloutFormKey.ObjectIdMask;
        if (loadOrderIndex >= _plugins.Count)
            throw new KeyNotFoundException(
                $"Runtime FormID {runtimeFormId:x8} uses inactive load-order index {loadOrderIndex}.");
        return new FalloutFormKey(_plugins[(int)loadOrderIndex].Plugin.Name, objectId);
    }

    public void Dispose()
    {
        foreach (var context in _plugins)
            context.Plugin.Dispose();
    }

    private static string FindExactlyOneCaseInsensitiveFile(string root, string expectedName)
    {
        ValidatePluginName(expectedName);
        var matches = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
            throw new FileNotFoundException(
                $"Expected exactly one {expectedName} in {root}; found {matches.Length}.");
        return matches[0];
    }

    private static void ValidatePluginName(string name)
    {
        if (name != Path.GetFileName(name) ||
            !(name.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
              name.EndsWith(".esp", StringComparison.OrdinalIgnoreCase)))
            throw new FalloutPluginFormatException($"Plugin entry is not an ESM/ESP file name: {name}");
    }

    private static void ValidateRegisteredProvenance(FalloutPluginSource source, string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("Layered plugin is missing.", path);
        var mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
        if (source.RegisteredBytes is <= 0 || source.RegisteredMtimeUnixMilliseconds is < 0)
            throw new FalloutPluginFormatException($"Layered plugin provenance is invalid: {source.Name}");
        if (source.RegisteredBytes.HasValue && source.RegisteredBytes.Value != info.Length ||
            source.RegisteredMtimeUnixMilliseconds.HasValue &&
            source.RegisteredMtimeUnixMilliseconds.Value != mtime)
            throw new FalloutPluginFormatException(
                $"Layered plugin changed after registration: {path}");
        if (source.RegisteredSha256 is not null &&
            (source.RegisteredSha256.Length != Sha256HexCharacterCount ||
             !source.RegisteredSha256.All(Uri.IsHexDigit)))
            throw new FalloutPluginFormatException(
                $"Layered plugin registered SHA-256 is invalid: {source.Name}");
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed class FalloutFormKeyComparer : IEqualityComparer<FalloutFormKey>
    {
        internal static FalloutFormKeyComparer Instance { get; } = new();

        public bool Equals(FalloutFormKey left, FalloutFormKey right) =>
            left.ObjectId == right.ObjectId &&
            left.OwnerPlugin.Equals(right.OwnerPlugin, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(FalloutFormKey value) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.OwnerPlugin), value.ObjectId);
    }
}
