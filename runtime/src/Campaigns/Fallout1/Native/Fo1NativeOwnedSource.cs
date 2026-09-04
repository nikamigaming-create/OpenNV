using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Fallout1.Native;

internal sealed class Fo1NativeOwnedSource : IFalloutClassicOwnedSource
{
    private const string ProfileSchema = "opennv-fo1-owned-profile/v1";
    private const int Sha256Characters = 64;
    private readonly IReadOnlyList<FalloutDat1Archive> _archives;
    private readonly IReadOnlyDictionary<string, string> _loose;

    private Fo1NativeOwnedSource(
        string profileId,
        IReadOnlyList<FalloutDat1Archive> archives,
        IReadOnlyDictionary<string, string> loose)
    {
        ProfileId = profileId;
        _archives = archives;
        _loose = loose;
    }

    public string ProfileId { get; }

    internal static Fo1NativeOwnedSource Load(string profilePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.GetFullPath(profilePath)));
        var root = document.RootElement;
        var profileId = root.GetProperty("sourceProfileId").GetString() ?? string.Empty;
        if (root.GetProperty("schema").GetString() != ProfileSchema ||
            root.GetProperty("campaign").GetString() != "Fallout1" ||
            profileId.Length != Sha256Characters)
            throw new InvalidDataException("The Fallout 1 owned profile identity is invalid.");

        var byName = new Dictionary<string, FalloutDat1Archive>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in root.GetProperty("install").GetProperty("archives").EnumerateArray())
        {
            var file = row.GetProperty("file").GetString() ?? string.Empty;
            var path = row.GetProperty("source").GetString() ?? string.Empty;
            if (!Path.GetFileName(path).Equals(file, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) || new FileInfo(path).Length != row.GetProperty("bytes").GetInt64())
                throw new InvalidDataException($"A registered Fallout 1 DAT changed or is missing: {file}.");
            var archive = new FalloutDat1Archive(path);
            var format = row.GetProperty("formatIdentity");
            var header = format.GetProperty("headerValues").EnumerateArray()
                .Select(value => value.GetUInt32()).ToArray();
            if (format.GetProperty("format").GetString() != "fallout-dat1" ||
                archive.Entries.Count != format.GetProperty("entries").GetInt32() ||
                archive.DirectoryBytes != format.GetProperty("directoryBytes").GetInt64() ||
                archive.DirectorySha256() != format.GetProperty("directorySha256").GetString() ||
                !archive.HeaderValues.SequenceEqual(header) || !byName.TryAdd(file, archive))
                throw new InvalidDataException($"A registered Fallout 1 DAT1 index differs: {file}.");
        }
        var order = new[] { "critter.dat", "master.dat" };
        if (byName.Count != order.Length || order.Any(name => !byName.ContainsKey(name)))
            throw new InvalidDataException("Fallout 1 requires critter.dat and master.dat.");

        var looseDescriptor = root.GetProperty("install").GetProperty("loose");
        var looseRoot = Path.GetFullPath(looseDescriptor.GetProperty("root").GetString() ?? string.Empty);
        var loose = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in looseDescriptor.GetProperty("files").EnumerateArray())
        {
            var logicalPath = FalloutDat1Archive.CanonicalPath(
                row.GetProperty("logicalPath").GetString() ?? string.Empty);
            var path = Path.GetFullPath(row.GetProperty("source").GetString() ?? string.Empty);
            if (!path.StartsWith(looseRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) || new FileInfo(path).Length != row.GetProperty("bytes").GetInt64() ||
                !loose.TryAdd(logicalPath, path))
                throw new InvalidDataException($"A registered Fallout 1 loose file differs: {logicalPath}.");
        }
        if (loose.Count != looseDescriptor.GetProperty("count").GetInt32())
            throw new InvalidDataException("The Fallout 1 loose-file inventory count differs.");
        return new Fo1NativeOwnedSource(
            profileId,
            order.Select(name => byName[name]).ToArray(),
            loose);
    }

    public byte[] Read(string logicalPath, out int sourceIndex)
    {
        var canonical = FalloutDat1Archive.CanonicalPath(logicalPath);
        if (_loose.TryGetValue(canonical, out var loosePath))
        {
            sourceIndex = 0;
            return File.ReadAllBytes(loosePath);
        }
        for (var index = 0; index < _archives.Count; ++index)
            if (_archives[index].Contains(canonical))
            {
                sourceIndex = index + 1;
                return _archives[index].Read(canonical);
            }
        throw new FileNotFoundException($"No active Fallout 1 source contains {canonical}.");
    }

    public IReadOnlyList<string> EffectiveLogicalPaths(string prefix, string extension)
    {
        var canonicalPrefix = FalloutDat1Archive.CanonicalPath(prefix).TrimEnd('\\') + "\\";
        return _loose.Keys
            .Concat(_archives.SelectMany(archive => archive.Entries.Keys))
            .Where(path => path.StartsWith(canonicalPrefix, StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Dispose() { }
}
