using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

// Private planning denominator only: declarations and graph edges do not prove
// player access, successful streaming, ordinary gameplay or matched pixels.
internal static class OwnedLocationInventoryProbe
{
    internal static void Run(string dataRoot, string output)
    {
        var workspace = FindWorkspace();
        var local = Path.GetFullPath(Path.Combine(workspace, "local")) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(output);
        if (!destination.StartsWith(local, StringComparison.OrdinalIgnoreCase) ||
            !destination.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Location inventory output must be a JSON file under workspace local/.");
        if (File.Exists(destination)) throw new IOException("Location inventory output must be fresh.");

        // Respect the actual selected stack when invoked inside an existing
        // probe session. Standalone use resolves the installation's load order,
        // rather than silently replacing it with a vanilla list.
        var current = RuntimeLiveContentSource.Current;
        using var opened = current is null
            ? RuntimeLiveContentSource.Open(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame) : null;
        var content = current ?? opened!;
        var requested = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
        if (!requested.Equals(Path.TrimEndingDirectorySeparator(content.ContentRoot), StringComparison.OrdinalIgnoreCase) &&
            !requested.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(content.ContentRoot)!), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selected live stack does not belong to the requested installation.");
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var sources = content.PluginSources.Select(source => new
        {
            source.Name, bytes = new FileInfo(source.AbsolutePath).Length,
            sha256 = FileHash(source.AbsolutePath)
        }).ToArray();
        var cells = records.EffectiveRecords("CELL").OrderBy(record => record.FormKey.ToString(), StringComparer.Ordinal).ToArray();
        var cellKeys = cells.Select(record => record.FormKey).ToHashSet(FalloutFormKeyComparer.Instance);
        var exterior = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        var cellRows = new List<object>();
        var markerRows = new List<object>();
        var edges = new List<Edge>();
        var issues = new List<object>();
        var provenance = new Dictionary<FalloutFormKey, object>(FalloutFormKeyComparer.Instance);
        void Bind(FalloutPluginRecord record) => provenance.TryAdd(record.FormKey, new
        {
            sourceId = record.FormKey.ToString(), record.Signature, winnerPlugin = record.Plugin.Name,
            recordSha256 = Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant()
        });
        foreach (var record in cells)
        {
            Bind(record);
            try
            {
                var cell = FalloutCellSceneReader.ReadDefinition(records, record.FormKey);
                var interior = (cell.Flags & FalloutCellSceneReader.InteriorCellFlag) != 0;
                if (!interior) exterior.Add(record.FormKey);
                if (cell.Worldspace is { } world) Bind(records.GetEffective(world));
                cellRows.Add(new
                {
                    sourceId = record.FormKey.ToString(), cell.EditorId, name = Text(record, "FULL"),
                    kind = interior ? "interior" : "exterior", worldspace = cell.Worldspace?.ToString(),
                    ownerPlugin = record.FormKey.OwnerPlugin, winnerPlugin = record.Plugin.Name,
                    visitStatus = "unvisited", matchStatus = "unmatched", playerReachability = "unproven"
                });
            }
            catch (Exception error) when (SourceFailure(error))
            {
                cellRows.Add(new { sourceId = record.FormKey.ToString(), kind = "unresolved",
                    visitStatus = "unvisited", matchStatus = "unmatched", playerReachability = "unproven" });
                Issue(record, "cell-definition", error);
            }
        }
        var markerCandidates = 0;
        var teleportCandidates = 0;
        foreach (var reference in records.EffectiveRecords("REFR").OrderBy(record => record.FormKey.ToString(), StringComparer.Ordinal))
        {
            bool marker;
            bool teleport;
            try
            {
                var fields = reference.ReadSubrecords().ToArray();
                marker = fields.Any(field => field.Signature == "XMRK");
                teleport = fields.Any(field => field.Signature == "XTEL");
            }
            catch (Exception error) when (SourceFailure(error)) { Issue(reference, "reference-fields", error); continue; }
            if (marker)
            {
                ++markerCandidates; Bind(reference);
                try
                {
                    var source = FalloutMapMarker.Read(reference);
                    var parent = FalloutCellSceneReader.ParentCell(reference);
                    var world = FalloutCellSceneReader.ParentWorldspace(reference);
                    markerRows.Add(new
                    {
                        sourceId = source.Reference.ToString(), source.Name, source.Type, source.Flags,
                        reputation = source.Reputation?.ToString(), parentCell = parent?.ToString(),
                        worldspace = world?.ToString(), parentResolved = parent is { } cell && cellKeys.Contains(cell),
                        ownerPlugin = reference.FormKey.OwnerPlugin, winnerPlugin = reference.Plugin.Name,
                        visitStatus = "unvisited", matchStatus = "unmatched", playerReachability = "unproven"
                    });
                    if (parent is null || !cellKeys.Contains(parent.Value))
                        throw new InvalidDataException("Map marker has no winning parent CELL.");
                }
                catch (Exception error) when (SourceFailure(error)) { Issue(reference, "map-marker", error); }
            }
            if (!teleport) continue;
            ++teleportCandidates; Bind(reference);
            FalloutFormKey? from = null;
            FalloutFormKey? target = null;
            try
            {
                from = RequireDoor(reference);
                var link = FalloutCellSceneReader.ReadTeleport(reference) ?? throw new InvalidDataException("XTEL field produced no destination.");
                target = link.Door;
                var destinationRecord = records.GetEffective(link.Door);
                Bind(destinationRecord);
                var to = RequireDoor(destinationRecord);
                edges.Add(new(reference.FormKey.ToString(), from.Value.ToString(), target.Value.ToString(),
                    to.ToString(), link.Flags, link.Flags == 0 ? "resolved-source-edge" : "source-edge-runtime-flags-unqualified", null));
            }
            catch (Exception error) when (SourceFailure(error))
            {
                edges.Add(new(reference.FormKey.ToString(), from?.ToString(), target?.ToString(), null,
                    null, "unresolved", error.Message));
                Issue(reference, "xtel-edge", error);
            }
        }
        // Directed topology only. Every exterior is an explicitly declared seed;
        // no exterior adjacency, lock, enable, quest or player traversal is inferred.
        var reached = exterior.Select(key => key.ToString()).ToHashSet(StringComparer.Ordinal);
        var outgoing = edges.Where(edge => edge.ToCell is not null && edge.Flags == 0)
            .GroupBy(edge => edge.FromCell!).ToDictionary(group => group.Key, group => group.Select(edge => edge.ToCell!).ToArray());
        var pending = new Queue<string>(reached.Order(StringComparer.Ordinal));
        while (pending.TryDequeue(out var from))
            if (outgoing.TryGetValue(from, out var destinations))
                foreach (var to in destinations) if (reached.Add(to)) pending.Enqueue(to);
        var reachability = cells.Select(record => new
        {
            cell = record.FormKey.ToString(), seed = exterior.Contains(record.FormKey),
            sourceXtelReachableFromAnyExteriorSeed = reached.Contains(record.FormKey.ToString()),
            playerReachability = "unproven"
        }).ToArray();
        var partitions = content.PluginSources.Select((plugin, index) => new
        {
            plugin = plugin.Name, loadOrder = index, role = index == 0 ? "base-master" : "additional-content-or-plugin",
            // Plugin identity is the source partition: do not guess whether an
            // arbitrary ESP/ESM is official DLC from a location's name.
            ownedCells = cells.Count(record => record.FormKey.OwnerPlugin.Equals(plugin.Name, StringComparison.OrdinalIgnoreCase)),
            winningCells = cells.Count(record => record.Plugin.Name.Equals(plugin.Name, StringComparison.OrdinalIgnoreCase))
        }).ToArray();
        // A changing owned input invalidates the complete inventory before output.
        for (var index = 0; index < sources.Length; ++index)
            if (sources[index].sha256 != FileHash(content.PluginSources[index].AbsolutePath))
                throw new IOException("Owned plugin changed during location inventory.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, new
        {
            schema = "opennv-owned-location-inventory/v1", generatedUtc = DateTime.UtcNow,
            boundary = "Private source declarations only; no assets, transforms, native visit, matched retail evidence or playable-location claim.",
            content.Game, content.Campaign, content.StackId, content.SaveCompatibilityId, plugins = sources, partitions,
            denominatorPolicy = "Every non-deleted winning CELL; actual winning REFR XMRK candidates; no inferred location count or hidden/visible draw count.",
            denominators = new { winningCells = cells.Length, decodedCells = cells.Length - issues.Count(issue => issue is CellIssue),
                actualMapMarkerCandidates = markerCandidates, decodedMapMarkers = markerRows.Count,
                sourceXtelCandidates = teleportCandidates, resolvedXtelEdges = edges.Count(edge => edge.ToCell is not null),
                unresolvedXtelEdges = edges.Count(edge => edge.ToCell is null), verifiedVisits = 0, matchedLocations = 0 },
            cells = cellRows, mapMarkers = markerRows, xtelEdges = edges,
            sourceGraph = new { seedPolicy = "all decoded exterior CELLs; zero-flag directed XTEL only; no walk adjacency or access admission",
                reachability }, issues, winningRecords = provenance.Values
        }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"OPENNV_OWNED_LOCATION_INVENTORY cells={cells.Length} markerCandidates={markerCandidates} xtelCandidates={teleportCandidates} " +
            $"issues={issues.Count} visits=0 matched=0 output={destination}");

        void Issue(FalloutPluginRecord record, string lane, Exception error)
        {
            if (lane == "cell-definition") issues.Add(new CellIssue(record.FormKey.ToString(), lane, error.GetType().Name, error.Message));
            else issues.Add(new { sourceId = record.FormKey.ToString(), lane, errorType = error.GetType().Name, error = error.Message });
        }
        FalloutFormKey RequireDoor(FalloutPluginRecord record)
        {
            if (record.Signature != "REFR") throw new InvalidDataException("XTEL endpoint is not a winning REFR.");
            var names = record.ReadSubrecords().Where(field => field.Signature == "NAME").ToArray();
            if (names.Length != 1 || names[0].Data.Length != 4) throw new InvalidDataException("XTEL endpoint has invalid NAME.");
            var basis = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(names[0].Data.Span));
            var definition = records.GetEffective(basis); Bind(definition);
            if (definition.Signature != "DOOR") throw new InvalidDataException("XTEL endpoint base is not DOOR.");
            var cell = FalloutCellSceneReader.ParentCell(record);
            return cell is { } key && cellKeys.Contains(key) ? key : throw new InvalidDataException("XTEL endpoint has no winning CELL ancestry.");
        }
    }

    private sealed record Edge(string Reference, string? FromCell, string? DestinationReference, string? ToCell,
        uint? Flags, string Status, string? Error);
    private sealed record CellIssue(string SourceId, string Lane, string ErrorType, string Error);
    private static bool SourceFailure(Exception error) => error is IOException or InvalidDataException or
        NotSupportedException or KeyNotFoundException or InvalidOperationException or FalloutPluginFormatException;
    private static string Text(FalloutPluginRecord record, string signature)
    {
        var fields = record.ReadSubrecords().Where(field => field.Signature == signature).ToArray();
        if (fields.Length > 1) throw new InvalidDataException($"Duplicate {signature} on {record.FormKey}.");
        return fields.Length == 0 ? "" : FalloutDialogueTopic.Text(fields[0].Data.Span);
    }
    private static string FileHash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }
    private static string FindWorkspace()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(directory.FullName, "runtime")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Location inventory requires the OpenNV workspace.");
    }
}
