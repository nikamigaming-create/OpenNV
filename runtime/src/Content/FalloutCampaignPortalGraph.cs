using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutCampaignPortalIssue(
    FalloutFormKey Reference,
    FalloutFormKey? Cell,
    FalloutFormKey? Destination,
    string Winner,
    long HeaderOffset,
    string ErrorType,
    string Error);

internal sealed class FalloutCampaignPortalGraph
{
    private sealed record Door(FalloutPluginRecord Source, FalloutFormKey Cell);
    private sealed record Link(Door Source, Door Destination);
    private sealed record Failure(FalloutCampaignPortalIssue Issue, Exception Cause);

    private readonly IReadOnlyDictionary<FalloutFormKey, FalloutPluginRecord> _cells;
    private readonly IReadOnlyDictionary<FalloutFormKey, IReadOnlyList<Link>> _links;
    private readonly IReadOnlyDictionary<FalloutFormKey, IReadOnlyList<Failure>> _issuesByCell;
    internal IReadOnlyList<FalloutCampaignPortalIssue> Issues { get; }

    internal FalloutCampaignPortalGraph(FalloutPluginStack records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _cells = records.EffectiveRecords("CELL").ToDictionary(record => record.FormKey,
            FalloutFormKeyComparer.Instance);
        var doors = new Dictionary<FalloutFormKey, Door>(FalloutFormKeyComparer.Instance);
        var links = new Dictionary<FalloutFormKey, List<Link>>(FalloutFormKeyComparer.Instance);
        var failures = new List<Failure>();
        foreach (var record in records.EffectiveRecords("REFR"))
        {
            FalloutFormKey? sourceCell = null;
            FalloutFormKey? destinationKey = null;
            try
            {
                sourceCell = FalloutCellSceneReader.ParentCell(record);
                var teleport = FalloutCellSceneReader.ReadTeleport(record);
                if (teleport is null) continue;
                destinationKey = teleport.Door;
                if (teleport.Flags != 0)
                    throw new NotSupportedException(
                        $"{Context(record)} has unowned XTEL flags 0x{teleport.Flags:x8}.");
                var source = RequireDoor(record.FormKey, record);
                var destination = RequireDoor(teleport.Door, record);
                if (!links.TryGetValue(source.Cell, out var outgoing)) links.Add(source.Cell, outgoing = []);
                outgoing.Add(new(source, destination));
            }
            catch (Exception error) when (error is InvalidDataException or FalloutPluginFormatException or
                NotSupportedException or KeyNotFoundException)
            {
                failures.Add(new(new(record.FormKey, sourceCell, destinationKey, record.Plugin.Name,
                    record.HeaderOffset, error.GetType().Name, error.Message), error));
            }
        }
        _links = links.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Link>)pair.Value.AsReadOnly(),
            FalloutFormKeyComparer.Instance);
        Issues = failures.Select(failure => failure.Issue).ToList().AsReadOnly();
        _issuesByCell = failures.Where(failure => failure.Issue.Cell is not null)
            .GroupBy(failure => failure.Issue.Cell!.Value, FalloutFormKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Failure>)group.ToList().AsReadOnly(),
                FalloutFormKeyComparer.Instance);

        Door RequireDoor(FalloutFormKey key, FalloutPluginRecord origin)
        {
            if (doors.TryGetValue(key, out var cached)) return cached;
            if (!records.TryGetEffective(key, out var door) || door.Signature != "REFR")
                throw new InvalidDataException($"{Context(origin)} XTEL does not bind a winning REFR door {key}.");
            var names = door.ReadSubrecords().Where(field => field.Signature == "NAME").ToArray();
            if (names.Length != 1 || names[0].Data.Length != sizeof(uint))
                throw new InvalidDataException($"{Context(door)} XTEL door NAME must contain exactly one FormID.");
            var basis = door.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(names[0].Data.Span));
            if (basis is null || !records.TryGetEffective(basis.Value, out var definition) || definition.Signature != "DOOR")
                throw new InvalidDataException($"{Context(door)} XTEL reference does not bind a winning DOOR base ({basis}).");
            var cell = FalloutCellSceneReader.ParentCell(door);
            if (cell is null || !_cells.ContainsKey(cell.Value))
                throw new InvalidDataException($"{Context(door)} XTEL reference has no winning CELL ancestry ({cell}).");
            var result = new Door(door, cell.Value);
            doors.Add(key, result);
            return result;
        }
    }

    internal IReadOnlyList<FalloutFormKey> Find(
        FalloutFormKey fromCell, FalloutFormKey targetCell, Func<FalloutFormKey, bool> canUse)
    {
        ArgumentNullException.ThrowIfNull(canUse);
        var from = RequireCell(fromCell);
        var target = RequireCell(targetCell);
        if (FalloutFormKeyComparer.Instance.Equals(fromCell, targetCell)) return [];
        var visited = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance) { fromCell };
        var previous = new Dictionary<FalloutFormKey, Link>(FalloutFormKeyComparer.Instance);
        var pending = new Queue<FalloutFormKey>();
        var blocked = new List<string>();
        pending.Enqueue(fromCell);
        while (pending.TryDequeue(out var cell))
        {
            if (_issuesByCell.TryGetValue(cell, out var failures))
            {
                var detail = $"Directed source XTEL route from {Context(from)} to {Context(target)} " +
                    $"reaches an unadmitted source CELL {cell}: " +
                    string.Join("; ", failures.Select(failure => failure.Issue.Error));
                if (failures[0].Cause is NotSupportedException)
                    throw new NotSupportedException(detail, failures[0].Cause);
                throw new InvalidDataException(detail, failures[0].Cause);
            }
            if (!_links.TryGetValue(cell, out var outgoing)) continue;
            foreach (var link in outgoing)
            {
                if (visited.Contains(link.Destination.Cell)) continue;
                if (!Admitted(link.Source, link) || !Admitted(link.Destination, link)) continue;
                previous.Add(link.Destination.Cell, link);
                if (FalloutFormKeyComparer.Instance.Equals(link.Destination.Cell, targetCell))
                {
                    var route = new List<FalloutFormKey>();
                    var current = targetCell;
                    while (!FalloutFormKeyComparer.Instance.Equals(current, fromCell))
                    {
                        var step = previous[current];
                        route.Add(step.Source.Source.FormKey);
                        current = step.Source.Cell;
                    }
                    route.Reverse();
                    return route.AsReadOnly();
                }
                visited.Add(link.Destination.Cell);
                pending.Enqueue(link.Destination.Cell);
            }
        }
        throw new NotSupportedException(
            $"No usable directed source XTEL route from {Context(from)} to {Context(target)}; " +
            $"explored CELLs=[{string.Join(", ", visited)}]; rejected doors=[{string.Join("; ", blocked)}]. " +
            $"Retained source graph Issues={Issues.Count}. " +
            "No exterior travel, reverse link, lock bypass or state change is inferred.");

        bool Admitted(Door door, Link link)
        {
            try
            {
                if (canUse(door.Source.FormKey)) return true;
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
            {
                throw new NotSupportedException(
                    $"Source XTEL eligibility is unavailable for {Context(door.Source)} on " +
                    $"{link.Source.Source.FormKey} -> {link.Destination.Source.FormKey}: {error.Message}", error);
            }
            blocked.Add(Context(door.Source) + " on " + link.Source.Source.FormKey + " -> " + link.Destination.Source.FormKey);
            return false;
        }
    }

    private FalloutPluginRecord RequireCell(FalloutFormKey key) =>
        _cells.TryGetValue(key, out var cell) ? cell :
            throw new InvalidDataException($"Source XTEL route endpoint {key} is not a winning CELL.");

    private static string Context(FalloutPluginRecord record) =>
        $"{record.Plugin.Name} {record.Signature} {record.FormKey} at 0x{record.HeaderOffset:x}";
}
