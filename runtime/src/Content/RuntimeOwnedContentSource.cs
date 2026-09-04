using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal sealed class RuntimeOwnedContentSource
{
    private const string ModStackSchema = "opennv-mod-stack/v2";
    private const string ModStackStatus = "registered-read-only-source-stack";
    internal const string FalloutNewVegasGame = "fallout-new-vegas";
    internal const string Fallout3Game = "fallout-3";
    private const int Sha256HexCharacterCount = SHA256.HashSizeInBytes * 2;
    private readonly IReadOnlyDictionary<string, string> _looseWinners;
    private readonly IReadOnlyList<string> _archivePaths;
    private readonly IReadOnlyList<FalloutPluginSource> _pluginSources;
    private readonly string _game;
    private readonly string _stackId;
    private readonly string _saveCompatibilityId;
    private readonly string _edition;
    private readonly string _engineBuild;
    private readonly string _contentVersion;
    private readonly IReadOnlyList<string> _supportedCampaigns;
    private readonly IReadOnlyList<string> _requiredSemanticExtensions;
    private readonly IReadOnlyList<string> _cleanRoomSemanticCapabilities;
    private readonly string _campaign;
    private readonly string _contentRoot;
    private readonly Dictionary<string, FalloutBsaArchive> _archives =
        new(StringComparer.OrdinalIgnoreCase);

    internal static RuntimeOwnedContentSource? Current { get; private set; }

    private RuntimeOwnedContentSource(
        IReadOnlyDictionary<string, string> looseWinners,
        IReadOnlyList<string> archivePaths,
        IReadOnlyList<FalloutPluginSource> pluginSources,
        string game,
        string stackId,
        string saveCompatibilityId,
        string edition,
        string engineBuild,
        string contentVersion,
        IReadOnlyList<string> supportedCampaigns,
        IReadOnlyList<string> requiredSemanticExtensions,
        IReadOnlyList<string> cleanRoomSemanticCapabilities,
        string campaign,
        string contentRoot)
    {
        _looseWinners = looseWinners;
        _archivePaths = archivePaths;
        _pluginSources = pluginSources;
        _game = game;
        _stackId = stackId;
        _saveCompatibilityId = saveCompatibilityId;
        _edition = edition;
        _engineBuild = engineBuild;
        _contentVersion = contentVersion;
        _supportedCampaigns = supportedCampaigns;
        _requiredSemanticExtensions = requiredSemanticExtensions;
        _cleanRoomSemanticCapabilities = cleanRoomSemanticCapabilities;
        _campaign = campaign;
        _contentRoot = contentRoot;
    }

    internal IReadOnlyList<FalloutPluginSource> PluginSources => _pluginSources;
    internal string Game => _game;
    internal string StackId => _stackId;
    internal string SaveCompatibilityId => _saveCompatibilityId;
    internal string Edition => _edition;
    internal string EngineBuild => _engineBuild;
    internal string ContentVersion => _contentVersion;
    internal IReadOnlyList<string> SupportedCampaigns => _supportedCampaigns;
    internal IReadOnlyList<string> RequiredSemanticExtensions => _requiredSemanticExtensions;
    internal IReadOnlyList<string> CleanRoomSemanticCapabilities => _cleanRoomSemanticCapabilities;
    internal string Campaign => _campaign;
    internal string ContentRoot => _contentRoot;

    /// <summary>
    /// Mounts the canonical v2 source stack. The primary root is read from the
    /// manifest only to preserve the old audit adapter's shape; all actual
    /// source selection remains owned by the sealed manifest.
    /// </summary>
    internal static void ConfigureSourceStack(
        string sourceStackPath,
        string expectedSourceStackSha256,
        string expectedStackId,
        string expectedCampaign)
    {
        var manifestPath = RequireFile(sourceStackPath, "source-stack manifest");
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var roots = document.RootElement.GetProperty("roots");
        var primary = roots.EnumerateArray()
            .SingleOrDefault(row => row.GetProperty("priority").GetInt32() == 0);
        if (primary.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("The source stack has no priority-zero primary root.");
        var primaryRoot = primary.GetProperty("root").GetString();
        if (string.IsNullOrWhiteSpace(primaryRoot))
            throw new InvalidDataException("The source stack primary root is empty.");
        Configure(
            primaryRoot,
            manifestPath,
            expectedSourceStackSha256,
            expectedStackId,
            expectedCampaign);
    }

    internal static void Configure(
        string dataRoot,
        string? modStackPath,
        string? expectedModStackSha256 = null,
        string? expectedStackId = null,
        string? expectedCampaign = null)
    {
        Current = null;
        var resolvedDataRoot = RequireDirectory(dataRoot, "owned Gamebryo Data root");
        var roots = new List<string> { resolvedDataRoot };
        var looseWinners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var archives = new List<string>();
        var plugins = new List<FalloutPluginSource>();
        if (!string.IsNullOrWhiteSpace(modStackPath))
        {
            var manifestPath = Path.GetFullPath(modStackPath);
            var manifestBytes = File.ReadAllBytes(manifestPath);
            var actualSha256 = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
            if (!IsSha256(expectedModStackSha256) ||
                !actualSha256.Equals(expectedModStackSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"The mod-stack manifest hash differs: expected={expectedModStackSha256} actual={actualSha256}");
            using var document = JsonDocument.Parse(manifestBytes);
            var root = document.RootElement;
            var metadata = ReadStackMetadata(root, expectedStackId, expectedCampaign);
            var rootsById = new Dictionary<string, string>(StringComparer.Ordinal);
            var expectedPriority = 0;
            foreach (var row in root.GetProperty("roots").EnumerateArray())
            {
                if (row.GetProperty("priority").GetInt32() != expectedPriority++)
                    throw new InvalidDataException("Mod source priorities must be contiguous and ordered.");
                var id = row.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(id) || rootsById.ContainsKey(id))
                    throw new InvalidDataException("Mod source identifiers must be unique.");
                var sourceRoot = RequireDirectory(row.GetProperty("root").GetString()!, "mod source root");
                rootsById.Add(id, sourceRoot);
                if (!roots.Contains(sourceRoot, PathComparer))
                    roots.Add(sourceRoot);
            }
            if (!root.TryGetProperty("looseFiles", out var looseRows) ||
                looseRows.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(
                    "The mod stack has no sealed loose-file inventory; register it again.");
            var expectedLooseIndex = 0;
            var looseNamesByRoot = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var row in looseRows.EnumerateArray())
            {
                if (row.GetProperty("index").GetInt32() != expectedLooseIndex++)
                    throw new InvalidDataException("Loose-file inventory order must be contiguous.");
                var owner = row.GetProperty("rootId").GetString()!;
                var logicalPath = row.GetProperty("path").GetString()!;
                if (!rootsById.TryGetValue(owner, out var sourceRoot) ||
                    logicalPath.Contains('\\', StringComparison.Ordinal))
                    throw new InvalidDataException("The mod stack contains an invalid loose-file row.");
                var canonical = FalloutBsaArchive.CanonicalPath(logicalPath);
                if (!looseNamesByRoot.TryGetValue(owner, out var names))
                    looseNamesByRoot.Add(owner, names = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (!names.Add(canonical))
                    throw new InvalidDataException(
                        $"The mod stack contains a case-colliding loose path: {owner}/{logicalPath}");
                var candidate = Path.GetFullPath(Path.Combine(
                    sourceRoot,
                    logicalPath.Replace('/', Path.DirectorySeparatorChar)));
                var sourcePrefix = Path.TrimEndingDirectorySeparator(sourceRoot) + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(sourcePrefix, PathComparison))
                    throw new InvalidDataException("A loose-file row escapes its source root.");
                VerifyDeclaredFile(row, candidate, "loose file", allowEmpty: true);
                looseWinners[canonical] = candidate;
            }
            if (root.TryGetProperty("orderSource", out var orderSource))
                ValidateOrderSource(orderSource);
            if (root.TryGetProperty("archiveOrderSource", out var archiveOrderSource))
                ValidateArchiveOrderSource(archiveOrderSource);
            ValidateDeclaredFiles(
                root.GetProperty("plugins"),
                rootsById,
                ".esm",
                ".esp",
                (row, file, path) => plugins.Add(new FalloutPluginSource(
                    file,
                    Path.GetFullPath(path),
                    row.GetProperty("bytes").GetInt64(),
                    row.GetProperty("mtimeMs").GetInt64())));
            var expectedArchiveIndex = 0;
            foreach (var row in root.GetProperty("archives").EnumerateArray())
            {
                if (row.GetProperty("index").GetInt32() != expectedArchiveIndex++)
                    throw new InvalidDataException("Mod archive order must be contiguous.");
                var owner = row.GetProperty("rootId").GetString()!;
                var file = row.GetProperty("file").GetString()!;
                if (!rootsById.TryGetValue(owner, out var sourceRoot) ||
                    Path.GetFileName(file) != file ||
                    !string.Equals(Path.GetExtension(file), ".bsa", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Mod stack contains an invalid BSA row.");
                var archivePath = Path.Combine(sourceRoot, file);
                VerifyDeclaredFile(row, archivePath, "BSA");
                archives.Add(archivePath);
            }
            if (!PathEquals(resolvedDataRoot, metadata.PrimaryRoot))
                throw new InvalidDataException(
                    "The supplied data-root does not match the source-stack primary root.");
            Current = new RuntimeOwnedContentSource(
                looseWinners,
                archives,
                plugins,
                metadata.Game,
                metadata.StackId,
                metadata.SaveCompatibilityId,
                metadata.Edition,
                metadata.EngineBuild,
                metadata.ContentVersion,
                metadata.SupportedCampaigns,
                metadata.RequiredSemanticExtensions,
                metadata.CleanRoomSemanticCapabilities,
                metadata.Campaign,
                metadata.PrimaryRoot);
            return;
        }
        throw new InvalidDataException("Native owned-data loading requires a sealed source-stack manifest.");
    }

    private sealed record StackMetadata(
        string Game,
        string StackId,
        string SaveCompatibilityId,
        string Edition,
        string EngineBuild,
        string ContentVersion,
        IReadOnlyList<string> SupportedCampaigns,
        IReadOnlyList<string> RequiredSemanticExtensions,
        IReadOnlyList<string> CleanRoomSemanticCapabilities,
        string Campaign,
        string PrimaryRoot);

    private static StackMetadata ReadStackMetadata(
        JsonElement root,
        string? expectedStackId,
        string? expectedCampaign)
    {
        if (root.GetProperty("schema").GetString() != ModStackSchema ||
            root.GetProperty("status").GetString() != ModStackStatus ||
            root.GetProperty("sourceOrder").GetString() != "low-to-high-last-wins")
            throw new InvalidDataException("Unsupported OpenNV v2 source-stack contract.");

        var edition = root.GetProperty("edition").GetString();
        var game = root.GetProperty("game").GetString();
        var engineBuild = root.GetProperty("engineBuild").GetString();
        var contentVersion = root.GetProperty("contentVersion").GetString();
        if (edition is not ("fallout-new-vegas" or "fallout-3" or "ttw") ||
            game is not (FalloutNewVegasGame or Fallout3Game) ||
            edition == "fallout-3" && game != Fallout3Game ||
            edition is "fallout-new-vegas" or "ttw" && game != FalloutNewVegasGame ||
            string.IsNullOrWhiteSpace(engineBuild) || string.IsNullOrWhiteSpace(contentVersion))
            throw new InvalidDataException("The source-stack edition metadata is invalid.");

        var expectedEngineBuild = edition == "fallout-3" ? "1.7.0.4" : "1.4.0.525";
        var expectedContentVersion = edition switch
        {
            "fallout-new-vegas" => "1.4.0.525",
            "fallout-3" => "1.7.0.4",
            "ttw" => "3.4",
            _ => throw new InvalidDataException("The source-stack edition is unsupported.")
        };
        if (engineBuild != expectedEngineBuild || contentVersion != expectedContentVersion)
            throw new InvalidDataException("The source-stack engine/content version is not admitted.");

        var supportedCampaigns = ReadStringArray(root, "supportedCampaigns");
        var expectedCampaigns = edition switch
        {
            "fallout-new-vegas" => new[] { FalloutNewVegasGame },
            "fallout-3" => new[] { Fallout3Game },
            "ttw" => new[] { Fallout3Game, FalloutNewVegasGame },
            _ => throw new InvalidDataException("The source-stack edition is unsupported.")
        };
        if (!supportedCampaigns.SequenceEqual(expectedCampaigns, StringComparer.Ordinal))
            throw new InvalidDataException("The source-stack supported-campaign declaration is not canonical.");

        var semantic = root.GetProperty("semanticExtensions");
        if (semantic.ValueKind != JsonValueKind.Object ||
            semantic.GetProperty("mode").GetString() != "clean-room")
            throw new InvalidDataException("The source-stack semantic-extension mode is invalid.");
        var required = ReadStringArray(semantic, "required");
        var capabilities = ReadStringArray(semantic, "cleanRoomCapabilities");
        var expectedRequired = edition == "ttw"
            ? new[] { "xnvse", "jip-ln", "showoff" }
            : Array.Empty<string>();
        var expectedCapabilities = edition == "ttw"
            ? new[] { "xnvse-semantics", "jip-ln-semantics", "showoff-semantics" }
            : Array.Empty<string>();
        if (!required.OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(expectedRequired.OrderBy(value => value, StringComparer.Ordinal)) ||
            !capabilities.OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(expectedCapabilities.OrderBy(value => value, StringComparer.Ordinal)))
            throw new InvalidDataException("The source-stack semantic-extension requirements are not canonical.");

        var stackId = root.GetProperty("stackId").GetString();
        if (!IsSha256(stackId) || expectedStackId is not null &&
            (!IsSha256(expectedStackId) || !stackId!.Equals(expectedStackId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The source-stack identity differs from the launch request.");
        var saveCompatibilityId = root.GetProperty("saveCompatibilityId").GetString();
        if (saveCompatibilityId != $"{edition}:{stackId}")
            throw new InvalidDataException("The source-stack save namespace is not stack-scoped.");
        if (expectedCampaign is not null && !supportedCampaigns.Contains(expectedCampaign, StringComparer.Ordinal))
            throw new InvalidDataException(
                $"The source stack edition {edition} does not support campaign {expectedCampaign}.");

        var rootRows = root.GetProperty("roots").EnumerateArray().ToArray();
        var primary = rootRows.SingleOrDefault(row => row.GetProperty("priority").GetInt32() == 0);
        var primaryRoot = primary.ValueKind == JsonValueKind.Undefined
            ? null
            : primary.GetProperty("root").GetString();
        if (string.IsNullOrWhiteSpace(primaryRoot) || !Path.IsPathFullyQualified(primaryRoot))
            throw new InvalidDataException("The source stack has no absolute primary root.");

        return new StackMetadata(
            game!,
            stackId!,
            saveCompatibilityId!,
            edition!,
            engineBuild!,
            contentVersion!,
            supportedCampaigns,
            required,
            capabilities,
            expectedCampaign ?? supportedCampaigns[0],
            Path.GetFullPath(primaryRoot));
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement parent, string property)
    {
        var values = parent.GetProperty(property).EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        if (values.Any(value => string.IsNullOrWhiteSpace(value)) ||
            values.Length != values.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidDataException($"The source-stack {property} list contains duplicates or empty IDs.");
        return values.Select(value => value!).ToArray();
    }

    internal static void Clear() => Current = null;

    internal bool TryRead(string logicalPath, string? preferredArchive, out byte[] data, out string source)
    {
        var canonical = FalloutBsaArchive.CanonicalPath(logicalPath);
        if (_looseWinners.TryGetValue(canonical, out var loosePath))
        {
            data = File.ReadAllBytes(loosePath);
            source = loosePath;
            return true;
        }
        for (var index = _archivePaths.Count - 1; index >= 0; --index)
        {
            var archive = GetArchive(_archivePaths[index]);
            if (!archive.Contains(canonical))
                continue;
            data = archive.Read(canonical);
            source = $"{_archivePaths[index]}::{canonical}";
            return true;
        }
        if (!string.IsNullOrWhiteSpace(preferredArchive))
        {
            var archiveName = Path.GetFileName(preferredArchive);
            if (!string.Equals(archiveName, preferredArchive, StringComparison.Ordinal))
                throw new InvalidDataException("A preferred BSA name must not contain a path.");
            var matchingArchives = _archivePaths
                .Where(candidate => string.Equals(
                    Path.GetFileName(candidate), archiveName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matchingArchives.Length == 0)
                throw new InvalidDataException(
                    $"The requested BSA is not active in the bound mod stack: {archiveName}");
            if (matchingArchives.Length != 1)
                throw new InvalidDataException(
                    $"The bound mod stack contains an ambiguous active BSA name: {archiveName}");
            var archivePath = matchingArchives[0];
            var archive = GetArchive(archivePath);
            if (archive.Contains(canonical))
            {
                data = archive.Read(canonical);
                source = $"{archivePath}::{canonical}";
                return true;
            }
        }
        data = [];
        source = string.Empty;
        return false;
    }

    internal bool TryResolve(string logicalPath, string? preferredArchive, out string source)
    {
        var canonical = FalloutBsaArchive.CanonicalPath(logicalPath);
        if (_looseWinners.TryGetValue(canonical, out var loosePath))
        {
            source = loosePath;
            return true;
        }
        for (var index = _archivePaths.Count - 1; index >= 0; --index)
        {
            var archive = GetArchive(_archivePaths[index]);
            if (!archive.Contains(canonical)) continue;
            source = $"{_archivePaths[index]}::{canonical}";
            return true;
        }
        if (!string.IsNullOrWhiteSpace(preferredArchive))
        {
            var archiveName = Path.GetFileName(preferredArchive);
            if (!string.Equals(archiveName, preferredArchive, StringComparison.Ordinal))
                throw new InvalidDataException("A preferred BSA name must not contain a path.");
            var matchingArchives = _archivePaths.Where(candidate => string.Equals(
                Path.GetFileName(candidate), archiveName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matchingArchives.Length != 1)
                throw new InvalidDataException(
                    $"The requested BSA is inactive or ambiguous: {archiveName}");
            var archivePath = matchingArchives[0];
            if (GetArchive(archivePath).Contains(canonical))
            {
                source = $"{archivePath}::{canonical}";
                return true;
            }
        }
        source = string.Empty;
        return false;
    }

    private FalloutBsaArchive GetArchive(string path)
    {
        if (!_archives.TryGetValue(path, out var archive))
        {
            archive = new FalloutBsaArchive(path);
            _archives.Add(path, archive);
        }
        return archive;
    }

    private static string RequireDirectory(string path, string label)
    {
        var resolved = Path.GetFullPath(path);
        if (!Directory.Exists(resolved))
            throw new DirectoryNotFoundException($"The {label} is missing: {resolved}");
        return resolved;
    }

    private static string RequireFile(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"A {label} path is required.", nameof(path));
        var resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved))
            throw new FileNotFoundException($"The {label} is missing.", resolved);
        return resolved;
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static void ValidateDeclaredFiles(
        JsonElement rows,
        IReadOnlyDictionary<string, string> rootsById,
        string firstExtension,
        string secondExtension,
        Action<JsonElement, string, string> add)
    {
        var expectedIndex = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.EnumerateArray())
        {
            if (row.GetProperty("index").GetInt32() != expectedIndex++)
                throw new InvalidDataException("Plugin order must be contiguous.");
            var owner = row.GetProperty("rootId").GetString()!;
            var file = row.GetProperty("file").GetString()!;
            var extension = Path.GetExtension(file);
            if (!rootsById.TryGetValue(owner, out var sourceRoot) ||
                Path.GetFileName(file) != file ||
                !(extension.Equals(firstExtension, StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(secondExtension, StringComparison.OrdinalIgnoreCase)) ||
                !names.Add(file))
                throw new InvalidDataException("Mod stack contains an invalid plugin row.");
            var pluginPath = Path.Combine(sourceRoot, file);
            VerifyDeclaredFile(row, pluginPath, "plugin");
            add(row, file, pluginPath);
        }
    }

    private static void VerifyDeclaredFile(
        JsonElement row,
        string path,
        string label,
        bool allowEmpty = false)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"A declared {label} is missing.", path);
        var info = new FileInfo(path);
        var declaredBytes = row.GetProperty("bytes").GetInt64();
        var declaredMtimeMs = row.GetProperty("mtimeMs").GetInt64();
        var actualMtimeMs = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
        if (declaredBytes < 0 || !allowEmpty && declaredBytes == 0 || declaredMtimeMs < 0 ||
            info.Length != declaredBytes || actualMtimeMs != declaredMtimeMs)
            throw new InvalidDataException(
                $"Declared {label} changed after registration: {path} " +
                $"(bytes registered={declaredBytes}, actual={info.Length}; " +
                $"mtimeMs registered={declaredMtimeMs}, actual={actualMtimeMs}).");
    }

    private static void ValidateOrderSource(JsonElement source)
    {
        if (source.ValueKind == JsonValueKind.Null)
            return;
        var kind = source.GetProperty("kind").GetString();
        if (kind is not ("official-default" or "fnv-profile" or "mo2-profile" or "ttw-profile" or "explicit-layer-order"))
            throw new InvalidDataException("Mod stack load-order provenance kind is unsupported.");
        ValidateProvenanceFiles(source, "Load-order");
    }

    private static void ValidateArchiveOrderSource(JsonElement source)
    {
        if (source.ValueKind == JsonValueKind.Null)
            return;
        if (source.GetProperty("kind").GetString() != "fallout-default-ini")
            throw new InvalidDataException("Mod stack archive-order provenance kind is unsupported.");
        var entries = source.GetProperty("entries");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in entries.EnumerateArray())
        {
            var key = row.GetProperty("key").GetString();
            var file = row.GetProperty("file").GetString();
            const string prefix = "SArchiveList";
            if (string.IsNullOrWhiteSpace(key) || key.Length < prefix.Length ||
                !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !key[prefix.Length..].All(char.IsDigit) ||
                string.IsNullOrWhiteSpace(file) || Path.GetFileName(file) != file ||
                !Path.GetExtension(file).Equals(".bsa", StringComparison.OrdinalIgnoreCase) ||
                !names.Add(file))
                throw new InvalidDataException("Mod stack archive-list provenance is invalid.");
        }
        if (names.Count == 0)
            throw new InvalidDataException("Mod stack archive-list provenance is empty.");
        ValidateProvenanceFiles(source, "Archive-order");
    }

    private static void ValidateProvenanceFiles(JsonElement source, string label)
    {
        var seen = new HashSet<string>(PathComparer);
        foreach (var row in source.GetProperty("files").EnumerateArray())
        {
            var path = row.GetProperty("path").GetString();
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new InvalidDataException("A load-order provenance path is not absolute.");
            var resolved = Path.GetFullPath(path);
            if (!seen.Add(resolved) || !File.Exists(resolved))
                throw new FileNotFoundException(
                    "A load-order provenance file is missing or duplicated.", resolved);
            var info = new FileInfo(resolved);
            var bytes = row.GetProperty("bytes").GetInt64();
            var mtime = row.GetProperty("mtimeMs").GetInt64();
            var sha256 = row.GetProperty("sha256").GetString();
            var actualMtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
            if (bytes < 0 || mtime < 0 || !IsSha256(sha256) ||
                info.Length != bytes || actualMtime != mtime)
                throw new InvalidDataException(
                    $"{label} provenance changed after registration: {resolved}");
            using var stream = new FileStream(
                resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!actualSha256.Equals(sha256, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"{label} provenance hash changed after registration: {resolved}");
        }
    }

    private static bool IsSha256(string? value) =>
        value?.Length == Sha256HexCharacterCount && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

}
