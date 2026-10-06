using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class CampaignGoalsOwnedProbe
{
    internal static void Run(string mod, string root, string game, string questEditorId,
        string doorPlugin, uint doorId, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quest = FalloutDialogueTopic.Find(records, "QUST", questEditorId);
        var targets = FalloutQuestTargets.Read(records, quest.FormKey);
        if (targets.Count == 0) throw new InvalidDataException("Selected owned quest has no source objective targets.");
        var source = records.GetEffective(new(doorPlugin, doorId));
        var teleport = FalloutCellSceneReader.ReadTeleport(source) ??
            throw new InvalidDataException($"Selected owned reference {source.FormKey} has no XTEL.");
        var destination = records.GetEffective(teleport.Door);
        var fromCell = FalloutCellSceneReader.ParentCell(source) ??
            throw new InvalidDataException($"Selected owned XTEL source {source.FormKey} has no CELL.");
        var targetCell = FalloutCellSceneReader.ParentCell(destination) ??
            throw new InvalidDataException($"Selected owned XTEL destination {destination.FormKey} has no CELL.");
        if (FalloutFormKeyComparer.Instance.Equals(fromCell, targetCell))
            throw new InvalidDataException("Selected owned portal must cross CELLs for this source-topology audit.");
        var observed = new[] { quest, source, destination, records.GetEffective(fromCell), records.GetEffective(targetCell) }
            .Concat(targets.Select(target => records.GetEffective(target.Reference)))
            .DistinctBy(record => record.FormKey, FalloutFormKeyComparer.Instance).ToArray();
        var hashes = observed.ToDictionary(record => record.FormKey, Hash, FalloutFormKeyComparer.Instance);
        var graph = new FalloutCampaignPortalGraph(records);
        // This predicate isolates the selected source edge, not gameplay access.
        // No lock, enable-parent, target-condition or player subject is invented.
        var selectedEdge = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance)
        {
            source.FormKey, destination.FormKey
        };
        var route = graph.Find(fromCell, targetCell, selectedEdge.Contains);
        if (route.Count != 1 || route[0] != source.FormKey ||
            !graph.Find(fromCell, targetCell, selectedEdge.Contains).SequenceEqual(route))
            throw new InvalidDataException("Selected owned source portal did not retain its exact directed edge.");
        if (observed.Any(record => hashes[record.FormKey] != Hash(record)))
            throw new InvalidDataException("Owned quest or portal source data changed during read-only admission.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-campaign-goals/v1",
            quest = new
            {
                form = quest.FormKey.ToString(), winner = quest.Plugin.Name, masters = quest.Plugin.Masters,
                sourceSha256 = hashes[quest.FormKey]
            },
            targets = targets.Select(target => new
            {
                target.Objective, target.Ordinal, reference = target.Reference.ToString(), target.Flags,
                referenceWinner = records.GetEffective(target.Reference).Plugin.Name,
                conditions = target.Conditions.Select(condition => new
                {
                    condition.Flags,
                    comparison = float.IsFinite(condition.Comparison) ? (float?)condition.Comparison : null,
                    comparisonBits = BitConverter.SingleToUInt32Bits(condition.Comparison),
                    condition.Function, condition.Argument1, condition.Argument2, condition.RunOn, condition.Reference,
                    winner = condition.Owner.Plugin.Name
                })
            }),
            portal = new
            {
                source = source.FormKey.ToString(), sourceWinner = source.Plugin.Name,
                sourceSha256 = hashes[source.FormKey], sourceFlags = source.Flags,
                sourceHasEnableParent = source.ReadSubrecords().Any(field => field.Signature == "XESP"),
                sourceHasLock = source.ReadSubrecords().Any(field => field.Signature == "XLOC"),
                destination = destination.FormKey.ToString(), destinationWinner = destination.Plugin.Name,
                destinationSha256 = hashes[destination.FormKey],
                fromCell = fromCell.ToString(), targetCell = targetCell.ToString(), teleport.Flags,
                orderedSourceDoors = route.Select(door => door.ToString()).ToArray()
            },
            portalGraphIssueCount = graph.Issues.Count,
            portalGraphIssues = graph.Issues.Select(issue => new
            {
                reference = issue.Reference.ToString(), cell = issue.Cell?.ToString(),
                destination = issue.Destination?.ToString(), issue.Winner, issue.HeaderOffset, issue.ErrorType, issue.Error
            }),
            wholePortalGraphAdmitted = graph.Issues.Count == 0,
            sourceUnchanged = true,
            stateMutation = false,
            conditionsEvaluated = false,
            gameplayEligibility = "unverified; selected-edge predicate is source topology only",
            boundary = "winning-source-objective-targets-and-directed-portal; displayed-goals/ordinary-travel/campaign-completion/retail-parity-unverified",
            recording = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"OPENNV_OWNED_CAMPAIGN_GOALS_PASS quest={quest.FormKey} targets={targets.Count} " +
            $"sourceDoor={source.FormKey} directedSourceEdge=true sourceUnchanged=true stateMutation=false " +
            $"sourceGraphIssues={graph.Issues.Count} " +
            "conditionAndAccessEvaluation=unverified nativeTraversalAndCampaign=unverified recording=false");
    }

    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
}
