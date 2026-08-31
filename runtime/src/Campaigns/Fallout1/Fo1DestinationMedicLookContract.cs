using System.Security.Cryptography;
using System.Text.Json;

using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Fallout1;

/// <summary>One hash-bound SCRIPT_MEDIC look-at message; dialogue and combat stay outside this boundary.</summary>
internal sealed record Fo1DestinationMedicLookContract(
    string Path, string Sha256, int Serial, int Tile, string Pid, string Fid,
    string PrototypeSha256, string ArtSha256, string MessageText, int MessageId,
    IReadOnlyList<int> SourceWalkMaskRoute, ClassicScriptProgram Program)
{
    private const string Schema = "opennv-fo1-destination-medic-look/v1";
    private const string GenericDoorSchema = "opennv-fo1-destination-generic-door/v1";
    private const string ScriptMedic = "SCRIPT_MEDIC";
    private const string LookAtProcedure = "look_at_p_proc";
    private const string DisplayMessageOnly = "display-message-only";

    internal static Fo1DestinationMedicLookContract Load(
        string path,
        Fo1DestinationPresentationContract destination,
        Fo1ExitGridTransitionContract transition,
        Fo1DestinationGenericDoorContract genericDoor)
    {
        var resolved = VerifiedGltfLoader.ResolvePath(path);
        var bytes = File.ReadAllBytes(resolved);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (Required(root, "schema") != Schema ||
            Required(root, "status") != "compiled-owned-map-scripted-medic-look-at")
            throw new InvalidOperationException("Unexpected Fallout destination Medic look descriptor.");
        var inputs = root.GetProperty("inputs");
        var presentation = inputs.GetProperty("presentation");
        var presentationMap = inputs.GetProperty("presentationMap");
        var genericDoorInput = inputs.GetProperty("genericDoor");
        if (Required(presentation, "path") != destination.Catalog.CampaignPath ||
            Required(presentation, "sha256") != destination.Catalog.CampaignSha256 ||
            Required(presentationMap, "path") != destination.Catalog.Maps.Single().Path ||
            Required(presentationMap, "sha256") != destination.Catalog.Maps.Single().Sha256 ||
            Required(genericDoorInput, "path") != genericDoor.Path ||
            Required(genericDoorInput, "sha256") != genericDoor.Sha256)
            throw new InvalidOperationException("Fallout Medic look descriptor prerequisite join drifted.");
        var target = root.GetProperty("destination");
        if (Required(target, "mapId") != destination.Map.Id ||
            Required(target, "sourceFile") != transition.DestinationMapName ||
            Required(target, "sourceMapSha256") != transition.DestinationMapSha256 ||
            target.GetProperty("elevation").GetInt32() != transition.DestinationElevation)
            throw new InvalidOperationException("Fallout Medic look descriptor MAP join drifted.");
        var actor = root.GetProperty("actor");
        var prototypeSha256 = Required(actor.GetProperty("prototype"), "sha256");
        var artSha256 = Required(actor.GetProperty("art"), "sha256");
        if (!Hash(prototypeSha256) || !Hash(artSha256) || actor.GetProperty("scriptIndex").GetInt32() < 0 ||
            string.IsNullOrWhiteSpace(Required(actor, "sid")))
            throw new InvalidOperationException("Fallout Medic look descriptor actor identity is invalid.");
        var semantics = root.GetProperty("semantics");
        if (Required(semantics, "procedure") != LookAtProcedure ||
            Required(semantics, "result") != DisplayMessageOnly ||
            Required(semantics, "dialogue") != "unimplemented-fail-closed" ||
            Required(semantics, "combat") != "not-proven-by-look-at-only" ||
            Required(semantics, "actionPoints") != "not-source-backed")
            throw new InvalidOperationException("Fallout Medic look descriptor has unsupported behavior.");
        var messageText = Required(semantics, "messageText");
        var messageId = semantics.GetProperty("messageId").GetInt32();
        if (messageId < 0)
            throw new InvalidOperationException("Fallout Medic look descriptor message ID is invalid.");
        var program = ClassicScriptProgram.Parse(root.GetProperty("effectProgram"));
        var execution = program.ExecuteWithActions(
            LookAtProcedure,
            new ClassicScriptState(),
            new ClassicScriptContext(false, false, default));
        if (!execution.Executed || !execution.ScriptOverrides ||
            execution.DisplayMessages.Count != 1 ||
            execution.DisplayMessages[0] != new ClassicScriptMessage(null, messageId))
            throw new InvalidOperationException(
                "Fallout Medic look descriptor does not execute its source message.");
        var route = root.GetProperty("sourceWalkMaskRoute").GetProperty("pathTiles")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var tile = actor.GetProperty("tile").GetInt32();
        if (route.Length == 0 || route[0] != genericDoor.Door.Tile ||
            route.Any(value => value is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height) ||
            !Fo1HexMath.AreNeighbors(route[^1], tile) ||
            route.Zip(route.Skip(1)).Any(pair => !Fo1HexMath.AreNeighbors(pair.First, pair.Second)))
            throw new InvalidOperationException("Fallout Medic look descriptor route is not source-adjacent.");
        return new Fo1DestinationMedicLookContract(
            resolved, sha256, actor.GetProperty("serial").GetInt32(), tile, Required(actor, "pid"),
            Required(actor, "fid"), prototypeSha256, artSha256, messageText, messageId, route,
            program);
    }

    internal object Report(bool viewed) => new
    {
        schema = Schema,
        path = Path,
        sha256 = Sha256,
        actor = new { Serial, Tile, Pid, Fid, PrototypeSha256, ArtSha256 },
        semantics = new
        {
            procedure = LookAtProcedure,
            messageId = MessageId,
            messageText = MessageText,
            result = DisplayMessageOnly,
            dialogue = "unimplemented-fail-closed",
            combat = "not-proven-by-look-at-only",
            actionPoints = "not-source-backed"
        },
        sourceWalkMaskRoute = SourceWalkMaskRoute,
        viewed,
    };

    private static string Required(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! :
        throw new InvalidOperationException($"Fallout Medic look descriptor is missing {name}.");

    private static bool Hash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
