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

internal enum FalloutCampaignPortalStepKind { ExteriorTravel, ActivateDoor }

// An exterior step is an itinerary request in a shared authored coordinate
// space. Native NAVM/capsule movement must still prove traversal and arrival.
// A null Reference on the last travel step means the caller's original target;
// no arbitrary CELL-center waypoint is manufactured here.
internal sealed record FalloutCampaignPortalStep(FalloutCampaignPortalStepKind Kind,
    FalloutFormKey FromCell, FalloutFormKey ToCell, FalloutFormKey? Reference, FalloutFormKey? Worldspace);

internal sealed class FalloutCampaignPortalGraph
{
    private sealed record Door(FalloutPluginRecord Source, FalloutFormKey Cell);
    private sealed record Link(Door Source, Door Destination);
    private sealed record Failure(FalloutCampaignPortalIssue Issue, Exception Cause);
    private readonly record struct Region(FalloutFormKey Source, FalloutFormKey Partition);
    private sealed class RegionComparer : IEqualityComparer<Region>
    {
        internal static RegionComparer Instance { get; } = new();
        public bool Equals(Region left, Region right) =>
            FalloutFormKeyComparer.Instance.Equals(left.Source, right.Source) &&
            FalloutFormKeyComparer.Instance.Equals(left.Partition, right.Partition);
        public int GetHashCode(Region value) => HashCode.Combine(
            FalloutFormKeyComparer.Instance.GetHashCode(value.Source),
            FalloutFormKeyComparer.Instance.GetHashCode(value.Partition));
    }

    private readonly IReadOnlyDictionary<FalloutFormKey, FalloutPluginRecord> _cells;
    private readonly IReadOnlyDictionary<FalloutFormKey, IReadOnlyList<Link>> _links;
    private readonly IReadOnlyDictionary<FalloutFormKey, IReadOnlyList<Failure>> _issuesByCell;
    private readonly IReadOnlyDictionary<FalloutFormKey, FalloutFormKey> _sourceScopes;
    private readonly IReadOnlyDictionary<FalloutFormKey, FalloutFormKey?> _worldspaces;
    private readonly IReadOnlyDictionary<FalloutFormKey, Failure> _cellFailures;
    internal IReadOnlyList<FalloutCampaignPortalIssue> Issues { get; }

    internal FalloutCampaignPortalGraph(FalloutPluginStack records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _cells = records.EffectiveRecords("CELL").ToDictionary(record => record.FormKey,
            FalloutFormKeyComparer.Instance);
        var doors = new Dictionary<FalloutFormKey, Door>(FalloutFormKeyComparer.Instance);
        var links = new Dictionary<FalloutFormKey, List<Link>>(FalloutFormKeyComparer.Instance);
        var failures = new List<Failure>();
        var scopes = new Dictionary<FalloutFormKey, FalloutFormKey>(FalloutFormKeyComparer.Instance);
        var worldspaces = new Dictionary<FalloutFormKey, FalloutFormKey?>(FalloutFormKeyComparer.Instance);
        var cellFailures = new Dictionary<FalloutFormKey, Failure>(FalloutFormKeyComparer.Instance);
        foreach (var cell in _cells.Values)
        {
            try
            {
                var definition = FalloutCellSceneReader.ReadDefinition(records, cell.FormKey);
                var world = (definition.Flags & 1) == 0 ? definition.Worldspace : null;
                if (world is { } key && (!records.TryGetEffective(key, out var owner) || owner.Signature != "WRLD"))
                    throw new InvalidDataException($"{Context(cell)} exterior ancestry is not a winning WRLD {key}.");
                scopes.Add(cell.FormKey, world ?? cell.FormKey);
                worldspaces.Add(cell.FormKey, world);
            }
            catch (Exception error) when (error is InvalidDataException or FalloutPluginFormatException or
                NotSupportedException or KeyNotFoundException)
            {
                var failure = new Failure(new(cell.FormKey, cell.FormKey, null, cell.Plugin.Name,
                    cell.HeaderOffset, error.GetType().Name, error.Message), error);
                failures.Add(failure);
                cellFailures.Add(cell.FormKey, failure);
            }
        }
        _sourceScopes = scopes;
        _worldspaces = worldspaces;
        _cellFailures = cellFailures;
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
            if (_cellFailures.TryGetValue(cell.Value, out var failure))
                throw new InvalidDataException($"{Context(door)} has an unadmitted CELL declaration: {failure.Issue.Error}", failure.Cause);
            var result = new Door(door, cell.Value);
            doors.Add(key, result);
            return result;
        }
    }

    internal IReadOnlyList<FalloutFormKey> Find(
        FalloutFormKey fromCell, FalloutFormKey targetCell, Func<FalloutFormKey, bool> canUse,
        Func<FalloutFormKey, FalloutFormKey>? region = null) =>
        FindLinks(fromCell, targetCell, canUse, region).Select(link => link.Source.Source.FormKey).ToList().AsReadOnly();

    internal IReadOnlyList<FalloutCampaignPortalStep> FindItinerary(
        FalloutFormKey fromCell, FalloutFormKey targetCell, Func<FalloutFormKey, bool> canUse,
        Func<FalloutFormKey, FalloutFormKey>? region = null)
    {
        var links = FindLinks(fromCell, targetCell, canUse, region);
        var steps = new List<FalloutCampaignPortalStep>();
        var current = fromCell;
        foreach (var link in links)
        {
            Travel(link.Source.Cell, link.Source.Source.FormKey);
            steps.Add(new(FalloutCampaignPortalStepKind.ActivateDoor, link.Source.Cell,
                link.Destination.Cell, link.Source.Source.FormKey, null));
            current = link.Destination.Cell;
        }
        Travel(targetCell, null);
        return steps.AsReadOnly();

        void Travel(FalloutFormKey destination, FalloutFormKey? reference)
        {
            if (FalloutFormKeyComparer.Instance.Equals(current, destination)) return;
            var world = _worldspaces[current];
            if (world is not { } sourceWorld || _worldspaces[destination] is not { } destinationWorld ||
                !FalloutFormKeyComparer.Instance.Equals(sourceWorld, destinationWorld))
                throw new InvalidDataException("Source itinerary requires an exterior transfer without a shared authored worldspace.");
            steps.Add(new(FalloutCampaignPortalStepKind.ExteriorTravel, current, destination, reference, world));
            current = destination;
        }
    }

    private IReadOnlyList<Link> FindLinks(
        FalloutFormKey fromCell, FalloutFormKey targetCell, Func<FalloutFormKey, bool> canUse,
        Func<FalloutFormKey, FalloutFormKey>? region)
    {
        ArgumentNullException.ThrowIfNull(canUse);
        var from = RequireCell(fromCell);
        var target = RequireCell(targetCell);
        // A caller may partition a source region further, but cannot use a
        // custom key to join unrelated interiors or different worldspaces.
        Region Scope(FalloutFormKey cell)
        {
            if (_cellFailures.TryGetValue(cell, out var failure))
                throw new InvalidDataException($"Source route CELL is unadmitted: {failure.Issue.Error}", failure.Cause);
            var source = _sourceScopes[cell];
            return new(source, region?.Invoke(cell) ?? source);
        }
        var links = _links.Values.SelectMany(value => value)
            .GroupBy(link => Scope(link.Source.Cell), RegionComparer.Instance).ToDictionary(group => group.Key,
                group => (IReadOnlyList<Link>)group.ToArray(), RegionComparer.Instance);
        var issues = _issuesByCell.Values.SelectMany(value => value)
            .Where(failure => _sourceScopes.ContainsKey(failure.Issue.Cell!.Value))
            .GroupBy(failure => Scope(failure.Issue.Cell!.Value), RegionComparer.Instance).ToDictionary(group => group.Key,
                group => (IReadOnlyList<Failure>)group.ToArray(), RegionComparer.Instance);
        var fromRegion = Scope(fromCell); var targetRegion = Scope(targetCell);
        if (RegionComparer.Instance.Equals(fromRegion, targetRegion)) return [];
        var visited = new HashSet<Region>(RegionComparer.Instance) { fromRegion };
        var previous = new Dictionary<Region, Link>(RegionComparer.Instance);
        var pending = new Queue<Region>();
        var blocked = new List<string>();
        var reachedFailures = new List<Failure>();
        pending.Enqueue(fromRegion);
        while (pending.TryDequeue(out var cell))
        {
            if (issues.TryGetValue(cell, out var failures))
            {
                // These source edges were excluded at construction. Retain
                // their diagnostics without poisoning other admitted edges in
                // the same exterior region. Never traverse an unknown edge.
                reachedFailures.AddRange(failures);
            }
            if (!links.TryGetValue(cell, out var outgoing)) continue;
            foreach (var link in outgoing)
            {
                var destinationCell = Scope(link.Destination.Cell);
                if (visited.Contains(destinationCell)) continue;
                if (!Admitted(link.Source, link) || !Admitted(link.Destination, link)) continue;
                previous.Add(destinationCell, link);
                if (RegionComparer.Instance.Equals(destinationCell, targetRegion))
                {
                    var route = new List<Link>();
                    var current = targetRegion;
                    while (!RegionComparer.Instance.Equals(current, fromRegion))
                    {
                        var step = previous[current];
                        route.Add(step);
                        current = Scope(step.Source.Cell);
                    }
                    route.Reverse();
                    return route.AsReadOnly();
                }
                visited.Add(destinationCell);
                pending.Enqueue(destinationCell);
            }
        }
        throw new NotSupportedException(
            $"No usable directed source XTEL route from {Context(from)} to {Context(target)}; " +
            $"explored CELLs=[{string.Join(", ", visited)}]; rejected doors=[{string.Join("; ", blocked)}]. " +
            $"Retained source graph Issues={Issues.Count}; reached excluded source edges=[" +
            string.Join("; ", reachedFailures.Select(failure => failure.Issue.Error)) + "]. " +
            "Exterior itinerary intent does not prove physical traversal. No reverse link, lock bypass or state change is inferred.");

        bool Admitted(Door door, Link link)
        {
            try
            {
                if (canUse(door.Source.FormKey)) return true;
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException)
            {
                blocked.Add($"Source XTEL eligibility is unavailable for {Context(door.Source)} on " +
                    $"{link.Source.Source.FormKey} -> {link.Destination.Source.FormKey}: {error.Message}");
                return false;
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
