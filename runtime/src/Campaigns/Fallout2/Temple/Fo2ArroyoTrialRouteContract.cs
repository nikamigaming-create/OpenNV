using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Campaigns.Fallout1;
using OpenNV.Runtime.Campaigns.Classic;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2TrialRouteStep(
    int Elevation,
    int Tile,
    int? ExitSerial,
    int Rotation);

internal sealed record Fo2TrialRoutePath(
    IReadOnlyList<Fo2TrialRouteStep> Steps,
    int StepCount,
    string Sha256);

internal sealed record Fo2TrialDialogueBranch(
    int MinimumIntelligence,
    string RequiredTaggedSkill,
    IReadOnlyList<int> SelectedMessageIds,
    IReadOnlyList<string> VisitedNodes,
    IReadOnlyDictionary<int, string> Messages,
    int GlobalVariable10,
    int LocalVariable12,
    int LocalVariable13,
    int MapVariable20);

internal sealed record Fo2TrialCameron(
    int Serial,
    int Tile,
    int Elevation,
    int Rotation,
    string Fid,
    string Pid,
    string Sid,
    int ScriptIndex,
    string PrototypeSha256,
    string ProgramLogicalPath,
    string ProgramSha256,
    string MessageLogicalPath,
    string MessageSha256,
    int MessageListId,
    IReadOnlyList<int> ReleaseActorTiles,
    int ReleaseDoorSerial,
    int ReleaseDoorTile,
    int ReleaseDoorElevation,
    string ReleaseDoorPid,
    string ReleaseDoorPrototypeSha256,
    int ReleaseDoorScriptIndex,
    bool ReleaseDoorOpened,
    bool ReleaseDoorUnlocked,
    bool ReleaseFinalVisible,
    ClassicDoorSource ReleaseDoorPresentation,
    Fo2TrialDialogueBranch TaggedSpeechBranch);

internal sealed record Fo2TrialKlintGate(
    int ActorSerial,
    int ActorScriptIndex,
    string ActorProgramLogicalPath,
    string ActorProgramSha256,
    int GateSerial,
    int SourceTile,
    int DestinationTile,
    int Elevation,
    string GatePid,
    string PrototypeSha256,
    int RequiredGlobalVariable10,
    int PostMoveWalkableHexes,
    string PostMoveWalkMaskSha256);

internal sealed record Fo2TrialVillageTransition(
    Fo2TrialRoutePath Path,
    int ExitSerial,
    int SourceTile,
    int TargetMapIndex,
    string TargetMapSha256,
    string TargetMapName,
    int TargetTile,
    int TargetElevation,
    int TargetRotation);

internal sealed record Fo2TrialVillageArrival(
    string Mode,
    string MapLogicalPath,
    string MapSource,
    string MapSha256,
    int MapBytes,
    int MapIndex,
    int Elevation,
    int ArrivalTile,
    int ArrivalRotation,
    int WalkableHexes,
    string WalkMaskSha256,
    IReadOnlyList<int> LegalNeighborTiles,
    int FirstActionFromTile,
    int FirstActionToTile,
    int FirstActionRotation);

internal sealed class Fo2ArroyoTrialRouteContract
{
    internal const string Schema = "opennv-fo2-arroyo-trial-route/v1";
    private const string Status = "compiled-owned-bounded-trial-route";

    private Fo2ArroyoTrialRouteContract(
        string path,
        string sha256,
        string sourceProfileId,
        string arroyoSourceSha256,
        string templeTransitionSha256,
        string globalCatalogSha256,
        int globalVariableIndex,
        Fo2TrialCameron cameron,
        Fo2TrialRoutePath approach,
        Fo2TrialRoutePath returnToTemple,
        IReadOnlyDictionary<int, string> walkMaskSha256,
        Fo2TrialKlintGate klintGate,
        Fo2TrialVillageTransition village,
        Fo2TrialVillageArrival villageArrival)
    {
        Path = path;
        Sha256 = sha256;
        SourceProfileId = sourceProfileId;
        ArroyoSourceSha256 = arroyoSourceSha256;
        TempleTransitionSha256 = templeTransitionSha256;
        GlobalCatalogSha256 = globalCatalogSha256;
        GlobalVariableIndex = globalVariableIndex;
        Cameron = cameron;
        ApproachCameron = approach;
        ReturnToTemple = returnToTemple;
        WalkMaskSha256 = walkMaskSha256;
        KlintGate = klintGate;
        Village = village;
        VillageArrival = villageArrival;
    }

    internal string Path { get; }
    internal string Sha256 { get; }
    internal string SourceProfileId { get; }
    internal string ArroyoSourceSha256 { get; }
    internal string TempleTransitionSha256 { get; }
    internal string GlobalCatalogSha256 { get; }
    internal int GlobalVariableIndex { get; }
    internal Fo2TrialCameron Cameron { get; }
    internal Fo2TrialRoutePath ApproachCameron { get; }
    internal Fo2TrialRoutePath ReturnToTemple { get; }
    internal IReadOnlyDictionary<int, string> WalkMaskSha256 { get; }
    internal Fo2TrialKlintGate KlintGate { get; }
    internal Fo2TrialVillageTransition Village { get; }
    internal Fo2TrialVillageArrival VillageArrival { get; }

    internal static Fo2ArroyoTrialRouteContract Load(
        string configuredPath,
        Fo2ArroyoCavesPresentationCatalog arroyo,
        Fo2TempleTransitionCatalog transitions)
    {
        var path = Fo2TemplePresentationCatalog.ResolvePath(
            configuredPath,
            Directory.GetCurrentDirectory());
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (RequiredString(root, "schema") != Schema ||
            RequiredString(root, "status") != Status ||
            RequiredString(root, "campaign") != "Fallout2")
            throw new InvalidOperationException("Unexpected Fallout 2 trial-route contract.");
        var profile = root.GetProperty("sourceProfile");
        if (RequiredString(profile, "sourceProfileId") != arroyo.SourceProfileId)
            throw new InvalidOperationException(
                "Fallout 2 trial-route source profile differs from the active cache.");
        var source = root.GetProperty("arroyoSource");
        var temple = root.GetProperty("templeTransitions");
        if (RequiredHash(source, "sha256") != arroyo.SourceManifestSha256 ||
            RequiredHash(source, "mapSha256") != arroyo.MapSha256 ||
            RequiredHash(temple, "sha256") != transitions.ManifestSha256 ||
            RequiredHash(temple, "mapSha256") != transitions.SourceMapSha256)
            throw new InvalidOperationException(
                "Fallout 2 trial-route map or transition provenance drifted.");

        var global = root.GetProperty("globalState");
        var globalVariableIndex = global.GetProperty("index").GetInt32();
        if (RequiredString(global, "name") != "GVAR_START_ARROYO_TRIAL" ||
            globalVariableIndex < 0 ||
            global.GetProperty("initialValue").GetInt32() != 0)
            throw new InvalidOperationException("Fallout 2 trial global identity drifted.");
        var cameron = LoadCameron(root.GetProperty("cameron"));
        var movement = root.GetProperty("movement");
        if (movement.GetProperty("wallEdgeCollisionImplemented").GetBoolean() ||
            movement.GetProperty("multihexExpansionImplemented").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 bounded trial route may not claim unresolved collision parity.");
        var approach = LoadPath(movement.GetProperty("approachCameron"));
        var returnToTemple = LoadPath(movement.GetProperty("returnToTempleExit"));
        if (approach.Steps[0] != new Fo2TrialRouteStep(0, arroyo.ArrivalTile, null, 0) ||
            approach.Steps[^1].Elevation != cameron.Elevation ||
            !Fo1HexMath.Neighbors(cameron.Tile).Contains(approach.Steps[^1].Tile) ||
            returnToTemple.Steps[0] != approach.Steps[^1] ||
            returnToTemple.Steps[^1].Elevation != arroyo.LiveExit.SourceElevation ||
            returnToTemple.Steps[^1].Tile != arroyo.LiveExit.SourceTile)
            throw new InvalidOperationException(
                "Fallout 2 Cameron approach/return endpoints drifted.");
        var walkMasks = movement.GetProperty("walkMasks").EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("elevation").GetInt32(),
                row => RequiredHash(row, "sha256"));
        if (!walkMasks.Keys.Order().SequenceEqual(new[] { 0, 1, 2 }))
            throw new InvalidOperationException(
                "Fallout 2 trial route lacks one of ARCAVES' three elevations.");

        var gate = LoadGate(root.GetProperty("klintGate"));
        var village = LoadVillage(root.GetProperty("villageTransition"), transitions);
        var villageArrival = LoadVillageArrival(
            root.GetProperty("villageArrival"),
            village);
        if (gate.RequiredGlobalVariable10 != cameron.TaggedSpeechBranch.GlobalVariable10 ||
            village.Path.Steps[0].Tile != arroyo.LiveExit.TargetTile ||
            village.Path.Steps[^1].Tile != village.SourceTile)
            throw new InvalidOperationException(
                "Fallout 2 trial outcome, Klint gate, and village route are disconnected.");
        return new Fo2ArroyoTrialRouteContract(
            path,
            Fo2TemplePresentationCatalog.Sha256(bytes),
            arroyo.SourceProfileId,
            arroyo.SourceManifestSha256,
            transitions.ManifestSha256,
            RequiredHash(global, "sha256"),
            globalVariableIndex,
            cameron,
            approach,
            returnToTemple,
            walkMasks,
            gate,
            village,
            villageArrival);
    }

    private static Fo2TrialCameron LoadCameron(JsonElement value)
    {
        var branch = value.GetProperty("taggedSpeechBranch");
        var result = branch.GetProperty("result");
        var messages = branch.GetProperty("messages").EnumerateObject()
            .ToDictionary(
                row => int.Parse(row.Name, System.Globalization.CultureInfo.InvariantCulture),
                row => row.Value.GetString() ?? "");
        var selected = ReadInts(branch.GetProperty("selectedMessageIds"));
        if (selected.Any(id => !messages.TryGetValue(id, out var text) ||
                string.IsNullOrWhiteSpace(text)))
            throw new InvalidOperationException(
                "Fallout 2 Cameron selected dialogue text is incomplete.");
        var release = value.GetProperty("release");
        var program = value.GetProperty("program");
        var messageCatalog = value.GetProperty("messageCatalog");
        return new Fo2TrialCameron(
            value.GetProperty("serial").GetInt32(),
            value.GetProperty("tile").GetInt32(),
            value.GetProperty("elevation").GetInt32(),
            value.GetProperty("rotation").GetInt32(),
            RequiredString(value, "fid"),
            RequiredString(value, "pid"),
            RequiredString(value, "sid"),
            value.GetProperty("scriptIndex").GetInt32(),
            RequiredHash(value, "prototypeSha256"),
            RequiredString(program, "logicalPath"),
            RequiredHash(program, "sha256"),
            RequiredString(messageCatalog, "logicalPath"),
            RequiredHash(messageCatalog, "sha256"),
            messageCatalog.GetProperty("messageListId").GetInt32(),
            ReadInts(release.GetProperty("actorTiles")),
            release.GetProperty("doorSerial").GetInt32(),
            release.GetProperty("doorTile").GetInt32(),
            release.GetProperty("doorElevation").GetInt32(),
            RequiredString(release, "doorPid"),
            RequiredHash(release, "doorPrototypeSha256"),
            release.GetProperty("doorScriptIndex").GetInt32(),
            release.GetProperty("doorOpened").GetBoolean(),
            release.GetProperty("doorUnlocked").GetBoolean(),
            release.GetProperty("finalVisible").GetBoolean(),
            ClassicDoorSource.Load(
                release.GetProperty("doorPresentation"),
                RequiredHash(release, "doorPrototypeSha256"),
                RequiredHash(release.GetProperty("doorPresentation").GetProperty("art"), "sha256")),
            new Fo2TrialDialogueBranch(
                branch.GetProperty("minimumIntelligence").GetInt32(),
                RequiredString(branch, "requiredTaggedSkill"),
                selected,
                branch.GetProperty("visitedNodes").EnumerateArray()
                    .Select(row => row.GetString() ?? "").ToArray(),
                messages,
                result.GetProperty("globalVariable10").GetInt32(),
                result.GetProperty("localVariable12").GetInt32(),
                result.GetProperty("localVariable13").GetInt32(),
                result.GetProperty("mapVariable20").GetInt32()));
    }

    private static Fo2TrialKlintGate LoadGate(JsonElement value) => new(
        value.GetProperty("actorSerial").GetInt32(),
        value.GetProperty("acklintScriptIndex").GetInt32(),
        RequiredString(value, "actorProgramLogicalPath"),
        RequiredHash(value, "actorProgramSha256"),
        value.GetProperty("gateSerial").GetInt32(),
        value.GetProperty("sourceTile").GetInt32(),
        value.GetProperty("destinationTile").GetInt32(),
        value.GetProperty("elevation").GetInt32(),
        RequiredString(value, "gatePid"),
        RequiredHash(value, "prototypeSha256"),
        value.GetProperty("requiredGlobalVariable10").GetInt32(),
        value.GetProperty("postMoveWalkableHexes").GetInt32(),
        RequiredHash(value, "postMoveWalkMaskSha256"));

    private static Fo2TrialVillageTransition LoadVillage(
        JsonElement value,
        Fo2TempleTransitionCatalog transitions)
    {
        var path = LoadPath(value.GetProperty("path"));
        var destination = value.GetProperty("destination");
        var destinationMap = value.GetProperty("destinationMap");
        var result = new Fo2TrialVillageTransition(
            path,
            value.GetProperty("exitSerial").GetInt32(),
            value.GetProperty("sourceTile").GetInt32(),
            destination.GetProperty("mapIndex").GetInt32(),
            RequiredHash(destinationMap, "sha256"),
            RequiredString(destinationMap, "mapName"),
            destination.GetProperty("tile").GetInt32(),
            destination.GetProperty("elevation").GetInt32(),
            destination.GetProperty("rotation").GetInt32());
        var source = transitions.Exits.SingleOrDefault(row => row.Serial == result.ExitSerial);
        if (source is null || source.Tile != result.SourceTile ||
            source.TargetMapIndex != result.TargetMapIndex ||
            source.TargetTile != result.TargetTile ||
            source.TargetElevation != result.TargetElevation ||
            source.TargetRotation != result.TargetRotation ||
            !transitions.DestinationMaps.TryGetValue(result.TargetMapIndex, out var map) ||
            map.Sha256 != result.TargetMapSha256 || map.MapName != result.TargetMapName ||
            value.GetProperty("destinationPresentationLoaded").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 village exit differs from the owned transition catalog.");
        return result;
    }

    private static Fo2TrialVillageArrival LoadVillageArrival(
        JsonElement value,
        Fo2TrialVillageTransition transition)
    {
        var action = value.GetProperty("firstLegalAction");
        var result = new Fo2TrialVillageArrival(
            RequiredString(value, "mode"),
            RequiredString(value, "mapLogicalPath"),
            RequiredString(value, "mapSource"),
            RequiredHash(value, "mapSha256"),
            value.GetProperty("mapBytes").GetInt32(),
            value.GetProperty("mapIndex").GetInt32(),
            value.GetProperty("elevation").GetInt32(),
            value.GetProperty("arrivalTile").GetInt32(),
            value.GetProperty("arrivalRotation").GetInt32(),
            value.GetProperty("walkableHexes").GetInt32(),
            RequiredHash(value, "walkMaskSha256"),
            ReadInts(value.GetProperty("legalNeighborTiles")),
            action.GetProperty("fromTile").GetInt32(),
            action.GetProperty("toTile").GetInt32(),
            action.GetProperty("rotation").GetInt32());
        if (result.Mode != "nonvisual-owned-map-arrival-and-first-hex-action-v1" ||
            value.GetProperty("presentationLoaded").GetBoolean() ||
            RequiredString(action, "kind") != "adjacent-source-walkable-hex-step" ||
            result.MapIndex != transition.TargetMapIndex ||
            result.MapSha256 != transition.TargetMapSha256 ||
            result.ArrivalTile != transition.TargetTile ||
            result.Elevation != transition.TargetElevation ||
            result.ArrivalRotation != transition.TargetRotation ||
            result.FirstActionFromTile != result.ArrivalTile ||
            result.FirstActionToTile !=
                Fo1HexMath.TileInDirection(result.ArrivalTile, result.FirstActionRotation) ||
            !result.LegalNeighborTiles.Contains(result.FirstActionToTile) ||
            result.WalkableHexes <= 0)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG arrival/first-action identity drifted.");
        return result;
    }

    private static Fo2TrialRoutePath LoadPath(JsonElement value)
    {
        var steps = value.GetProperty("steps").EnumerateArray().Select(row =>
            new Fo2TrialRouteStep(
                row.GetProperty("elevation").GetInt32(),
                row.GetProperty("tile").GetInt32(),
                row.GetProperty("exitSerial").ValueKind == JsonValueKind.Null
                    ? null
                    : row.GetProperty("exitSerial").GetInt32(),
                row.GetProperty("rotation").GetInt32())).ToArray();
        var stepCount = value.GetProperty("stepCount").GetInt32();
        var sha256 = RequiredHash(value, "sha256");
        if (steps.Length != stepCount + 1 || steps.Length == 0 ||
            steps.Any(row => row.Elevation is < 0 or > 2 ||
                row.Tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
                row.Rotation is < 0 or >= Fo1HexMath.DirectionCount) ||
            PathSha256(steps) != sha256)
            throw new InvalidOperationException("Fallout 2 trial path identity drifted.");
        for (var index = 1; index < steps.Length; index++)
        {
            var prior = steps[index - 1];
            var current = steps[index];
            if (prior.Elevation == current.Elevation)
            {
                if (current.ExitSerial is not null ||
                    !Fo1HexMath.Neighbors(prior.Tile).Contains(current.Tile))
                    throw new InvalidOperationException(
                        "Fallout 2 trial path contains a non-source hex step.");
            }
            else if (current.ExitSerial is null)
                throw new InvalidOperationException(
                    "Fallout 2 trial elevation change lacks its owned exit-grid serial.");
        }
        return new Fo2TrialRoutePath(steps, stepCount, sha256);
    }

    private static string PathSha256(IReadOnlyList<Fo2TrialRouteStep> steps)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[sizeof(int) * 4];
        foreach (var step in steps)
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes, step.Elevation);
            BinaryPrimitives.WriteInt32BigEndian(bytes[4..], step.Tile);
            BinaryPrimitives.WriteInt32BigEndian(bytes[8..], step.ExitSerial ?? -1);
            BinaryPrimitives.WriteInt32BigEndian(bytes[12..], step.Rotation);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static int[] ReadInts(JsonElement value) => value.EnumerateArray()
        .Select(row => row.GetInt32()).ToArray();

    private static string RequiredString(JsonElement value, string property)
    {
        var result = value.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(result)
            ? result
            : throw new InvalidOperationException(
                $"Fallout 2 trial-route string is missing: {property}");
    }

    private static string RequiredHash(JsonElement value, string property)
    {
        var result = RequiredString(value, property);
        return result.Length == 64 && result.All(Uri.IsHexDigit)
            ? result.ToLowerInvariant()
            : throw new InvalidOperationException(
                $"Fallout 2 trial-route hash is invalid: {property}");
    }
}
