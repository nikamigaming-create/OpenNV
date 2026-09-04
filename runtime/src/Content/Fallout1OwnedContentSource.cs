using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal sealed record Fallout1OwnedResource(string LogicalPath, string Source, byte[] Bytes);

internal sealed class Fallout1OwnedContentSource
{
    internal const string ProfileSchema = "opennv-fo1-owned-profile/v1";
    private const string Campaign = "Fallout1";
    private const string MasterArchive = "master.dat";
    private const string CritterArchive = "critter.dat";
    private const int Sha256Characters = 64;

    private readonly string _looseRoot;
    private readonly IReadOnlyDictionary<string, RegisteredLooseFile> _loose;
    private readonly IReadOnlyList<(string Name, FalloutDat1Archive Archive)> _archives;

    private Fallout1OwnedContentSource(
        string profileId,
        string installRoot,
        string looseRoot,
        IReadOnlyDictionary<string, RegisteredLooseFile> loose,
        IReadOnlyList<(string, FalloutDat1Archive)> archives)
    {
        ProfileId = profileId;
        InstallRoot = installRoot;
        _looseRoot = looseRoot;
        _loose = loose;
        _archives = archives;
    }

    internal string ProfileId { get; }
    internal string InstallRoot { get; }
    internal int LooseFileCount => _loose.Count;
    internal IReadOnlyList<string> OverlayOrder =>
        ["loose:data", CritterArchive, MasterArchive];

    internal static Fallout1OwnedContentSource Load(string profilePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.GetFullPath(profilePath)));
        var root = document.RootElement;
        var profileId = RequiredString(root, "sourceProfileId");
        if (RequiredString(root, "schema") != ProfileSchema ||
            RequiredString(root, "status") != "registered-owned-install" ||
            RequiredString(root, "campaign") != Campaign ||
            !IsSha256(profileId) ||
            RequiredString(root, "saveCompatibilityId") != $"fallout1:{profileId}" ||
            root.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean())
            throw new InvalidDataException("The Fallout 1 owned profile identity is invalid.");

        var install = root.GetProperty("install");
        var overlay = install.GetProperty("overlayOrderHighToLow").EnumerateArray()
            .Select(value => value.GetString()).ToArray();
        if (!overlay.SequenceEqual(new[] { "loose:data", CritterArchive, MasterArchive }))
            throw new InvalidDataException("The Fallout 1 overlay order differs from the admitted contract.");
        var installRoot = Path.GetFullPath(RequiredString(install, "root"));
        var byName = new Dictionary<string, FalloutDat1Archive>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in install.GetProperty("archives").EnumerateArray())
        {
            var file = RequiredString(row, "file").ToLowerInvariant();
            var source = Path.GetFullPath(RequiredString(row, "source"));
            if (file is not (MasterArchive or CritterArchive) ||
                !PathEquals(Path.GetDirectoryName(source), installRoot) ||
                !string.Equals(Path.GetFileName(source), file, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Fallout 1 archive source escapes the install root: {file}");
            if (!IsSha256(RequiredString(row, "sha256")))
                throw new InvalidDataException($"Fallout 1 archive hash is invalid: {file}");
            VerifyFileIdentity(source, row, $"Fallout 1 archive {file}", verifyHash: false);
            var archive = new FalloutDat1Archive(source);
            var format = row.GetProperty("formatIdentity");
            var headers = format.GetProperty("headerValues").EnumerateArray()
                .Select(value => value.GetUInt32()).ToArray();
            if (RequiredString(format, "format") != "fallout-dat1" ||
                format.GetProperty("entries").GetInt32() != archive.Entries.Count ||
                format.GetProperty("directoryBytes").GetInt64() != archive.DirectoryBytes ||
                RequiredString(format, "directorySha256") != archive.DirectorySha256() ||
                !headers.SequenceEqual(archive.HeaderValues) ||
                !byName.TryAdd(file, archive))
                throw new InvalidDataException($"Registered Fallout 1 DAT1 index differs: {file}");
        }
        if (byName.Count != 2 || !byName.ContainsKey(MasterArchive) || !byName.ContainsKey(CritterArchive))
            throw new InvalidDataException("Fallout 1 requires exactly master.dat and critter.dat.");

        var loose = install.GetProperty("loose");
        var looseRoot = Path.GetFullPath(RequiredString(loose, "root"));
        if (!PathEquals(Path.GetDirectoryName(looseRoot), installRoot) ||
            !string.Equals(Path.GetFileName(looseRoot), "data", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The registered Fallout 1 loose root must be install/Data.");
        var looseFiles = new Dictionary<string, RegisteredLooseFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in loose.GetProperty("files").EnumerateArray())
        {
            var logical = FalloutDat1Archive.CanonicalPath(RequiredString(row, "logicalPath"));
            var source = Path.GetFullPath(RequiredString(row, "source"));
            var expected = Path.GetFullPath(Path.Combine(looseRoot, logical.Replace('\\', Path.DirectorySeparatorChar)));
            var sha256 = RequiredString(row, "sha256");
            if (!PathEquals(source, expected) || !looseFiles.TryAdd(logical, new RegisteredLooseFile(
                    source,
                    row.GetProperty("bytes").GetInt64(),
                    row.GetProperty("lastWriteTimeUtcUnixMilliseconds").GetInt64(),
                    sha256)) || !IsSha256(sha256))
                throw new InvalidDataException($"Invalid or duplicate Fallout 1 loose row: {logical}");
        }
        if (loose.GetProperty("count").GetInt32() != looseFiles.Count)
            throw new InvalidDataException("The Fallout 1 loose inventory count differs.");

        return new Fallout1OwnedContentSource(
            profileId,
            installRoot,
            looseRoot,
            looseFiles,
            [(CritterArchive, byName[CritterArchive]), (MasterArchive, byName[MasterArchive])]);
    }

    internal static Fallout1OwnedContentSource LoadInstall(string installDirectory)
    {
        var installRoot = Path.GetFullPath(installDirectory);
        if (!Directory.Exists(installRoot))
            throw new DirectoryNotFoundException($"Fallout 1 install root does not exist: {installRoot}");
        var archives = new Dictionary<string, FalloutDat1Archive>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { MasterArchive, CritterArchive })
        {
            var path = Directory.EnumerateFiles(installRoot)
                .SingleOrDefault(candidate => Path.GetFileName(candidate).Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Fallout 1 requires {name} in the selected root.", installRoot);
            archives.Add(name, new FalloutDat1Archive(path));
        }
        var looseRoot = Path.Combine(installRoot, "DATA");
        var looseFiles = Directory.Exists(looseRoot)
            ? Directory.EnumerateFiles(looseRoot, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    path => FalloutDat1Archive.CanonicalPath(Path.GetRelativePath(looseRoot, path)),
                    path => new RegisteredLooseFile(path, new FileInfo(path).Length, 0, string.Empty),
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, RegisteredLooseFile>(StringComparer.OrdinalIgnoreCase);
        var profileId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\0", new[] { installRoot, archives[MasterArchive].DirectorySha256(), archives[CritterArchive].DirectorySha256() }))))
            .ToLowerInvariant();
        return new Fallout1OwnedContentSource(
            profileId,
            installRoot,
            looseRoot,
            looseFiles,
            [(CritterArchive, archives[CritterArchive]), (MasterArchive, archives[MasterArchive])]);
    }

    internal Fallout1OwnedResource Read(string logicalPath)
    {
        var canonical = FalloutDat1Archive.CanonicalPath(logicalPath);
        if (_loose.TryGetValue(canonical, out var loose))
        {
            if (loose.Sha256.Length != 0)
                VerifyRegisteredLoose(canonical, loose);
            return new Fallout1OwnedResource(canonical, $"loose:data:{canonical}", File.ReadAllBytes(loose.Source));
        }
        foreach (var (name, archive) in _archives)
            if (archive.Contains(canonical))
                return new Fallout1OwnedResource(canonical, $"dat1:{name}:{canonical}", archive.Read(canonical));
        throw new FileNotFoundException($"No registered Fallout 1 source contains {canonical}.");
    }

    internal string FirstArchiveMember(string archiveName)
    {
        var row = _archives.SingleOrDefault(value =>
            string.Equals(value.Name, archiveName, StringComparison.OrdinalIgnoreCase));
        if (row.Archive is null || row.Archive.Entries.Count == 0)
            throw new FileNotFoundException($"Registered Fallout 1 archive is empty or absent: {archiveName}");
        return row.Archive.Entries.Keys.OrderBy(value => value, StringComparer.Ordinal).First();
    }

    internal static string CreateProfileJson(string installDirectory)
    {
        var root = Path.GetFullPath(installDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Fallout 1 install root does not exist: {root}");
        var archiveRows = new List<object>();
        var identity = new StringBuilder("opennv-fo1-owned-profile/v1\0");
        foreach (var name in new[] { MasterArchive, CritterArchive })
        {
            var source = Directory.EnumerateFiles(root)
                .SingleOrDefault(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Fallout 1 requires {name} in the selected root.", root);
            var archive = new FalloutDat1Archive(source);
            var info = new FileInfo(source);
            var sha = Sha256(source);
            var directorySha = archive.DirectorySha256();
            identity.Append(name).Append('\0').Append(sha).Append('\0').Append(directorySha).Append('\0');
            archiveRows.Add(new
            {
                file = name,
                source = info.FullName,
                bytes = info.Length,
                lastWriteTimeUtcUnixMilliseconds = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                sha256 = sha,
                formatIdentity = new
                {
                    format = "fallout-dat1",
                    entries = archive.Entries.Count,
                    headerValues = archive.HeaderValues,
                    directoryBytes = archive.DirectoryBytes,
                    directorySha256 = directorySha,
                },
            });
        }

        var looseRoot = Path.Combine(root, "DATA");
        var looseRows = new List<object>();
        if (Directory.Exists(looseRoot))
        {
            foreach (var source in Directory.EnumerateFiles(looseRoot, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(source);
                if (info.LinkTarget is not null)
                    throw new InvalidDataException($"Fallout 1 loose links are not admitted: {source}");
                var logical = FalloutDat1Archive.CanonicalPath(Path.GetRelativePath(looseRoot, source));
                var sha = Sha256(source);
                identity.Append(logical).Append('\0').Append(sha).Append('\0');
                looseRows.Add(new
                {
                    logicalPath = logical,
                    source = info.FullName,
                    bytes = info.Length,
                    lastWriteTimeUtcUnixMilliseconds = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                    sha256 = sha,
                });
            }
        }
        var profileId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())))
            .ToLowerInvariant();
        return JsonSerializer.Serialize(new
        {
            schema = ProfileSchema,
            status = "registered-owned-install",
            campaign = Campaign,
            sourceProfileId = profileId,
            saveCompatibilityId = $"fallout1:{profileId}",
            retailOrDerivedAssetsPackaged = false,
            install = new
            {
                root,
                archives = archiveRows,
                loose = new { root = looseRoot, count = looseRows.Count, files = looseRows },
                overlayOrderHighToLow = new[] { "loose:data", CritterArchive, MasterArchive },
            },
            runtimeCompatibility = new
            {
                nativeResourceSource = true,
                mapProFrmClosure = true,
                fullMapObjectGraph = true,
                scripts = false,
                gameplay = false,
            },
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private void VerifyRegisteredLoose(string logical, RegisteredLooseFile row)
    {
        var expected = Path.GetFullPath(Path.Combine(_looseRoot, logical.Replace('\\', Path.DirectorySeparatorChar)));
        if (!PathEquals(expected, row.Source))
            throw new InvalidDataException($"Fallout 1 loose path escaped its root: {logical}");
        var info = new FileInfo(row.Source);
        if (!info.Exists || info.Length != row.Bytes ||
            new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() != row.LastWriteTimeUtcUnixMilliseconds ||
            Sha256(row.Source) != row.Sha256)
            throw new InvalidDataException($"Registered Fallout 1 loose file changed: {logical}");
    }

    private static void VerifyFileIdentity(string path, JsonElement row, string label, bool verifyHash)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != row.GetProperty("bytes").GetInt64() ||
            new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() !=
                row.GetProperty("lastWriteTimeUtcUnixMilliseconds").GetInt64() ||
            verifyHash && Sha256(path) != RequiredString(row, "sha256"))
            throw new InvalidDataException($"{label} changed or is missing.");
    }

    private static string RequiredString(JsonElement source, string property) =>
        source.GetProperty(property).GetString() is { Length: > 0 } value
            ? value
            : throw new InvalidDataException($"Fallout 1 profile string is empty: {property}");

    private static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool PathEquals(string? left, string right) => left is not null &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256(string value) => value.Length == Sha256Characters &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record RegisteredLooseFile(
        string Source,
        long Bytes,
        long LastWriteTimeUtcUnixMilliseconds,
        string Sha256);
}
