using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2TempleScriptProgram(
    int ScriptsListIndex,
    string IndexSemantics,
    string Program,
    string LogicalPath,
    string Source,
    long Bytes,
    string Sha256);

internal sealed record Fo2TempleLiveScriptRecord(
    int Type,
    int Extent,
    int Slot,
    string Sid,
    int Bytes,
    int ObjectSerial,
    int ObjectTile,
    int ScriptIndex,
    Fo2TempleScriptProgram Program);

internal sealed record Fo2TempleDestinationMap(
    int MapIndex,
    string LookupName,
    string MapName,
    string LogicalPath,
    string Source,
    long Bytes,
    string Sha256,
    int Version,
    string HeaderName,
    IReadOnlySet<int> PresentElevations);

internal sealed record Fo2TempleExitGrid(
    int Serial,
    int ObjectId,
    int Tile,
    int Elevation,
    uint Flags,
    string Fid,
    string Pid,
    string ArtFilename,
    bool SourceBlocking,
    int TargetMapIndex,
    int TargetTile,
    int TargetElevation,
    int TargetRotation);

internal sealed class Fo2TempleTransitionCatalog
{
    private const string TransitionSchema = "opennv-fo2-temple-transitions/v1";
    private const string SourceProfileSchema = "opennv-fo2-owned-profile/v1";

    private Fo2TempleTransitionCatalog(
        string manifestPath,
        string manifestSha256,
        string sourceMapSha256,
        string sourceMapName,
        Fo2TempleScriptProgram headerProgram,
        IReadOnlyList<Fo2TempleLiveScriptRecord> liveScriptRecords,
        string liveScriptRecordsSha256,
        IReadOnlyList<Fo2TempleExitGrid> exits,
        IReadOnlyDictionary<int, Fo2TempleDestinationMap> destinationMaps,
        int verifiedResources)
    {
        ManifestPath = manifestPath;
        ManifestSha256 = manifestSha256;
        SourceMapSha256 = sourceMapSha256;
        SourceMapName = sourceMapName;
        HeaderProgram = headerProgram;
        LiveScriptRecords = liveScriptRecords;
        LiveScriptRecordsSha256 = liveScriptRecordsSha256;
        Exits = exits;
        DestinationMaps = destinationMaps;
        VerifiedResources = verifiedResources;
    }

    internal string ManifestPath { get; }
    internal string ManifestSha256 { get; }
    internal string SourceMapSha256 { get; }
    internal string SourceMapName { get; }
    internal Fo2TempleScriptProgram HeaderProgram { get; }
    internal IReadOnlyList<Fo2TempleLiveScriptRecord> LiveScriptRecords { get; }
    internal string LiveScriptRecordsSha256 { get; }
    internal IReadOnlyList<Fo2TempleExitGrid> Exits { get; }
    internal IReadOnlyDictionary<int, Fo2TempleDestinationMap> DestinationMaps { get; }
    internal int VerifiedResources { get; }

    internal static Fo2TempleTransitionCatalog Load(
        string transitionManifestPath,
        Fo2TemplePresentationCatalog presentation)
    {
        var manifestPath = Path.GetFullPath(transitionManifestPath);
        var manifestBytes = File.ReadAllBytes(manifestPath);
        using var document = JsonDocument.Parse(manifestBytes);
        var root = document.RootElement;
        var policy = root.GetProperty("runtimePolicy");
        if (RequiredString(root, "schema") != TransitionSchema ||
            RequiredString(root, "status") != "compiled-owned-transition-records" ||
            RequiredString(root, "campaign") != "Fallout2" ||
            RequiredString(root, "slice") != "TempleOfTrials" ||
            root.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean() ||
            RequiredString(policy, "exitGridTransition") != "source-instance-values-only" ||
            policy.GetProperty("headerMapProgramExecution").GetBoolean() ||
            policy.GetProperty("objectProgramExecution").GetBoolean() ||
            policy.GetProperty("doorTransition").GetBoolean() ||
            policy.GetProperty("runtimeReady").GetBoolean())
            throw new InvalidOperationException("Unexpected Fallout 2 Temple transition manifest.");

        var source = root.GetProperty("sourceManifest");
        var sourcePath = Path.GetFullPath(RequiredString(source, "file"));
        if (!sourcePath.Equals(presentation.SourceManifestPath, StringComparison.OrdinalIgnoreCase) ||
            VerifyFile(sourcePath, RequiredHash(source, "sha256"), null) !=
                presentation.SourceManifestSha256)
            throw new InvalidOperationException(
                "Fallout 2 Temple transition/source manifest binding drifted.");
        var profileDescriptor = root.GetProperty("sourceProfile");
        var profilePath = Path.GetFullPath(RequiredString(profileDescriptor, "file"));
        var profileSha256 = VerifyFile(
            profilePath,
            RequiredHash(profileDescriptor, "sha256"),
            null);
        using (var profileDocument = JsonDocument.Parse(File.ReadAllBytes(profilePath)))
        {
            var profile = profileDocument.RootElement;
            if (RequiredString(profile, "schema") != SourceProfileSchema ||
                RequiredString(profile, "status") != "registered-owned-install" ||
                RequiredString(profile, "campaign") != "Fallout2" ||
                RequiredString(profile, "sourceProfileId") != presentation.SourceProfileId ||
                RequiredString(profileDescriptor, "sourceProfileId") != presentation.SourceProfileId ||
                profileSha256 != RequiredHash(profileDescriptor, "sha256"))
                throw new InvalidOperationException(
                    "Fallout 2 Temple transition/profile binding drifted.");
        }
        var sourceMap = root.GetProperty("sourceMap");
        if (sourceMap.GetProperty("mapIndex").GetInt32() != Fo2TemplePresentationCatalog.MapIndex ||
            RequiredString(sourceMap, "logicalPath") != "maps\\artemple.map" ||
            RequiredHash(sourceMap, "sha256") != presentation.MapSha256)
            throw new InvalidOperationException(
                "Fallout 2 Temple transition source MAP binding drifted.");

        var scriptsList = root.GetProperty("scriptsList");
        if (RequiredString(scriptsList, "logicalPath") != "scripts\\scripts.lst" ||
            scriptsList.GetProperty("entries").GetInt32() <= 0)
            throw new InvalidOperationException("Fallout 2 scripts.lst identity drifted.");
        var header = root.GetProperty("headerMapProgram");
        var headerProgram = ReadProgram(header);
        if (header.GetProperty("storedScriptIndex").GetInt32() != 745 ||
            header.GetProperty("executionImplemented").GetBoolean() ||
            headerProgram.ScriptsListIndex != 744 ||
            headerProgram.IndexSemantics != "MAP-header-one-based-to-scripts-list" ||
            !headerProgram.Program.Equals("ARTemple.int", StringComparison.OrdinalIgnoreCase) ||
            !headerProgram.LogicalPath.Equals("scripts\\artemple.int", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 2 Temple header MAP-program identity drifted.");

        var liveRecords = root.GetProperty("liveMapScriptRecords").EnumerateArray()
            .Select(ReadLiveScriptRecord).ToArray();
        if (liveRecords.Length == 0 ||
            liveRecords.Select(row => row.Sid).Distinct(StringComparer.Ordinal).Count() !=
                liveRecords.Length ||
            liveRecords.Any(row => row.Program.IndexSemantics !=
                "MAP-object-direct-scripts-list-index" ||
                row.Program.ScriptsListIndex != row.ScriptIndex))
            throw new InvalidOperationException(
                "Fallout 2 Temple live MAP-script records drifted.");
        var liveRecordsSha256 = RequiredHash(root, "liveMapScriptRecordsSha256");
        if (ScriptRecordsSha256(liveRecords) != liveRecordsSha256)
            throw new InvalidOperationException(
                "Fallout 2 Temple live MAP-script record hash drifted.");
        var scriptedSourceObjects = presentation.ObjectPlacements
            .Where(row => row.ScriptIndex >= 0).ToArray();
        if (scriptedSourceObjects.Length != liveRecords.Length ||
            liveRecords.Any(record => !scriptedSourceObjects.Any(sourceObject =>
                sourceObject.Serial == record.ObjectSerial &&
                sourceObject.Tile == record.ObjectTile &&
                sourceObject.Sid == record.Sid &&
                sourceObject.ScriptIndex == record.ScriptIndex)))
            throw new InvalidOperationException(
                "Fallout 2 Temple live MAP-script/source-object join drifted.");

        var sourceDoors = presentation.ObjectPlacements.Count(row =>
            row.ObjectType == 2 && row.PrototypeSubtype == 0);
        var doors = root.GetProperty("doors");
        if (doors.GetProperty("count").GetInt32() != sourceDoors ||
            doors.GetProperty("sourceObjects").GetArrayLength() != sourceDoors ||
            doors.GetProperty("runtimeImplemented").GetBoolean() ||
            sourceDoors != 0)
            throw new InvalidOperationException(
                "Fallout 2 Temple door evidence changed; door runtime remains fail-closed.");

        var destinations = root.GetProperty("destinationMaps").EnumerateArray()
            .Select(ReadDestinationMap)
            .ToDictionary(row => row.MapIndex);
        if (destinations.Count == 0 || destinations.Values.Any(row =>
                row.MapIndex < 0 || row.Version != 20 ||
                row.HeaderName.Length == 0 || row.PresentElevations.Count == 0 ||
                !row.LogicalPath.Equals(
                    $"maps\\{row.MapName}.map",
                    StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Fallout 2 Temple destination MAP identities drifted.");
        var exits = root.GetProperty("exitGrids").EnumerateArray()
            .Select(ReadExitGrid).OrderBy(row => row.Serial).ToArray();
        if (exits.Length == 0 ||
            exits.Select(row => row.Serial).Distinct().Count() != exits.Length ||
            exits.Select(row => row.Tile).Distinct().Count() != exits.Length)
            throw new InvalidOperationException(
                "Fallout 2 Temple exit-grid identities are absent or duplicated.");
        foreach (var exit in exits)
        {
            var sourceObject = presentation.ObjectPlacements.SingleOrDefault(row =>
                row.Serial == exit.Serial) ?? throw new InvalidOperationException(
                    $"Fallout 2 Temple exit-grid source object is absent: {exit.Serial}");
            if (sourceObject.ObjectId != exit.ObjectId ||
                sourceObject.Tile != exit.Tile ||
                sourceObject.Elevation != exit.Elevation ||
                sourceObject.Flags != exit.Flags ||
                sourceObject.Fid != exit.Fid ||
                sourceObject.Pid != exit.Pid ||
                sourceObject.ArtFilename != exit.ArtFilename ||
                sourceObject.ObjectType != 5 ||
                sourceObject.InstanceValues.Count != 4 ||
                sourceObject.InstanceValues[0] != exit.TargetMapIndex ||
                sourceObject.InstanceValues[1] != exit.TargetTile ||
                sourceObject.InstanceValues[2] != exit.TargetElevation ||
                sourceObject.InstanceValues[3] != exit.TargetRotation ||
                sourceObject.Blocking(0x10) != exit.SourceBlocking ||
                exit.SourceBlocking ||
                exit.Tile is < 0 or >= 40000 ||
                exit.Elevation != 0 ||
                exit.TargetTile is < 0 or >= 40000 ||
                exit.TargetRotation is < 0 or >= Fo1HexMath.DirectionCount ||
                !destinations.TryGetValue(exit.TargetMapIndex, out var destination) ||
                !destination.PresentElevations.Contains(exit.TargetElevation))
                throw new InvalidOperationException(
                    $"Fallout 2 Temple exit-grid/source binding drifted: {exit.Serial}");
        }

        var resources = root.GetProperty("resources").EnumerateArray().Select(row =>
            $"{RequiredString(row, "logicalPath")}|{RequiredHash(row, "sha256")}|" +
            row.GetProperty("bytes").GetInt64()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredPrograms = liveRecords.Select(row => row.Program).Append(headerProgram);
        if (!requiredPrograms.All(program => resources.Contains(
                $"{program.LogicalPath}|{program.Sha256}|{program.Bytes}")) ||
            !destinations.Values.All(destination => resources.Contains(
                $"{destination.LogicalPath}|{destination.Sha256}|{destination.Bytes}")) ||
            !resources.Contains(
                $"scripts\\scripts.lst|{RequiredHash(scriptsList, "sha256")}|" +
                scriptsList.GetProperty("bytes").GetInt64()))
            throw new InvalidOperationException(
                "Fallout 2 Temple transition resource closure failed.");

        return new Fo2TempleTransitionCatalog(
            manifestPath,
            Sha256(manifestBytes),
            presentation.MapSha256,
            Path.GetFileName(RequiredString(sourceMap, "logicalPath")),
            headerProgram,
            liveRecords,
            liveRecordsSha256,
            exits,
            destinations,
            resources.Count);
    }

    internal static Fo2TempleTransitionCatalog LoadFromPresentationOutput(
        Fo2TemplePresentationCatalog presentation)
    {
        var cacheBytes = File.ReadAllBytes(presentation.ManifestPath);
        using var cacheDocument = JsonDocument.Parse(cacheBytes);
        var descriptor = cacheDocument.RootElement
            .GetProperty("outputs")
            .GetProperty("templeTransitions");
        var cacheRoot = Path.GetDirectoryName(presentation.ManifestPath) ??
            throw new InvalidOperationException("Fallout 2 Temple cache has no parent directory.");
        var relativeFile = RequiredString(descriptor, "file");
        if (Path.IsPathRooted(relativeFile))
            throw new InvalidOperationException(
                "Fallout 2 Temple transition output must be relative to its cache.");
        var transitionPath = Path.GetFullPath(Path.Combine(cacheRoot, relativeFile));
        var relative = Path.GetRelativePath(cacheRoot, transitionPath);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}"))
            throw new InvalidOperationException(
                "Fallout 2 Temple transition output escapes its cache root.");
        var descriptorHash = RequiredHash(descriptor, "sha256");
        var descriptorSourceHash = RequiredHash(descriptor, "sourceManifestSha256");
        var descriptorProfileHash = RequiredHash(descriptor, "sourceProfileSha256");
        var descriptorProfileId = RequiredString(descriptor, "sourceProfileId");
        if (descriptorSourceHash != presentation.SourceManifestSha256 ||
            descriptorProfileHash != presentation.SourceProfileSha256 ||
            descriptorProfileId != presentation.SourceProfileId)
            throw new InvalidOperationException(
                "Fallout 2 Temple transition descriptor does not join the cache source/profile.");
        var transitionBytes = Fo2TemplePresentationCatalog.VerifyFile(
            transitionPath,
            descriptorHash,
            expectedBytes: null,
            "Fallout 2 Temple transition output");
        using var transitionDocument = JsonDocument.Parse(transitionBytes);
        var transition = transitionDocument.RootElement;
        var source = transition.GetProperty("sourceManifest");
        var profile = transition.GetProperty("sourceProfile");
        if (RequiredString(transition, "schema") != TransitionSchema ||
            RequiredString(transition, "status") != "compiled-owned-transition-records" ||
            Path.GetFullPath(RequiredString(source, "file")) != presentation.SourceManifestPath ||
            RequiredHash(source, "sha256") != presentation.SourceManifestSha256 ||
            Path.GetFullPath(RequiredString(profile, "file")) != presentation.SourceProfilePath ||
            RequiredHash(profile, "sha256") != presentation.SourceProfileSha256 ||
            RequiredString(profile, "sourceProfileId") != presentation.SourceProfileId)
            throw new InvalidOperationException(
                "Fallout 2 Temple transition output does not bind the cache source/profile.");
        return Load(transitionPath, presentation);
    }

    private static Fo2TempleScriptProgram ReadProgram(JsonElement source) => new(
        source.GetProperty("scriptsListIndex").GetInt32(),
        RequiredString(source, "indexSemantics"),
        RequiredString(source, "program"),
        RequiredString(source, "logicalPath"),
        RequiredString(source, "source"),
        source.GetProperty("bytes").GetInt64(),
        RequiredHash(source, "sha256"));

    private static Fo2TempleLiveScriptRecord ReadLiveScriptRecord(JsonElement source) => new(
        source.GetProperty("type").GetInt32(),
        source.GetProperty("extent").GetInt32(),
        source.GetProperty("slot").GetInt32(),
        RequiredString(source, "sid"),
        source.GetProperty("bytes").GetInt32(),
        source.GetProperty("objectSerial").GetInt32(),
        source.GetProperty("objectTile").GetInt32(),
        source.GetProperty("scriptIndex").GetInt32(),
        ReadProgram(source.GetProperty("program")));

    private static Fo2TempleDestinationMap ReadDestinationMap(JsonElement source)
    {
        var header = source.GetProperty("header");
        return new Fo2TempleDestinationMap(
            source.GetProperty("mapIndex").GetInt32(),
            RequiredString(source, "lookupName"),
            RequiredString(source, "mapName"),
            RequiredString(source, "logicalPath"),
            RequiredString(source, "source"),
            source.GetProperty("bytes").GetInt64(),
            RequiredHash(source, "sha256"),
            header.GetProperty("version").GetInt32(),
            RequiredString(header, "name"),
            source.GetProperty("presentElevations").EnumerateArray()
                .Select(value => value.GetInt32()).ToHashSet());
    }

    private static Fo2TempleExitGrid ReadExitGrid(JsonElement source)
    {
        var destination = source.GetProperty("destination");
        return new Fo2TempleExitGrid(
            source.GetProperty("serial").GetInt32(),
            source.GetProperty("objectId").GetInt32(),
            source.GetProperty("tile").GetInt32(),
            source.GetProperty("elevation").GetInt32(),
            RequiredFlags(source, "flags"),
            RequiredString(source, "fid"),
            RequiredString(source, "pid"),
            RequiredString(source, "artFilename"),
            source.GetProperty("sourceBlocking").GetBoolean(),
            destination.GetProperty("mapIndex").GetInt32(),
            destination.GetProperty("tile").GetInt32(),
            destination.GetProperty("elevation").GetInt32(),
            destination.GetProperty("rotation").GetInt32());
    }

    private static string ScriptRecordsSha256(
        IReadOnlyList<Fo2TempleLiveScriptRecord> records)
    {
        var lines = records.Select(row => string.Join(
            '|',
            row.Type,
            row.Extent,
            row.Slot,
            row.Sid,
            row.Bytes,
            row.ObjectSerial,
            row.ObjectTile,
            row.ScriptIndex,
            row.Program.Sha256));
        return Sha256(Encoding.ASCII.GetBytes(string.Join('\n', lines)));
    }

    private static string VerifyFile(string path, string expectedSha256, long? expectedBytes)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Fallout 2 transition input is missing.", path);
        var bytes = File.ReadAllBytes(path);
        if (expectedBytes.HasValue && bytes.LongLength != expectedBytes.Value)
            throw new InvalidOperationException(
                $"Fallout 2 transition input byte count drifted: {path}");
        var actual = Sha256(bytes);
        if (actual != expectedSha256)
            throw new InvalidOperationException(
                $"Fallout 2 transition input SHA-256 drifted: {path}");
        return actual;
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Fallout 2 transition string is empty: {property}");
        return value;
    }

    private static string RequiredHash(JsonElement source, string property)
    {
        var value = RequiredString(source, property).ToLowerInvariant();
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException(
                $"Fallout 2 transition SHA-256 is invalid: {property}");
        return value;
    }

    private static uint RequiredFlags(JsonElement source, string property)
    {
        var value = RequiredString(source, property);
        if (value.Length != 8 || !uint.TryParse(
                value,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var flags))
            throw new InvalidOperationException(
                $"Fallout 2 transition flags are invalid: {property}");
        return flags;
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
