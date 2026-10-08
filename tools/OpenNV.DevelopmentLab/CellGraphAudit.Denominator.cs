using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAudit
{
    private static readonly HashSet<string> PlacedSignatures = ["REFR", "ACHR", "ACRE", "PGRE", "PMIS"];

    private sealed class SourceGraphRow
    {
        public string Identity { get; init; } = "";
        public string Signature { get; init; } = "";
        public string Winner { get; init; } = "";
        public string? Sha256 { get; set; }
        public uint Flags { get; init; }
        public bool Deleted { get; init; }
        public bool InitiallyDisabled { get; init; }
        public string? ParentCell { get; set; }
        public string? ParentWorldspace { get; set; }
        public string? Base { get; set; }
        public string Disposition { get; set; } = "unbound";
        public object? Definition { get; set; }
        public object? SourceSemantics { get; set; }
        public object[] SourceFields { get; set; } = [];
        public string Projection { get; set; } = "not-selected";
        public string RuntimeOwner { get; set; } = "unverified; source bytes and a historical snapshot are not runtime admission";
        public List<object> Failures { get; } = [];
    }

    private sealed class SourceGraphInventory
    {
        internal readonly Dictionary<FalloutFormKey, FalloutPluginRecord> Winners = new(FalloutFormKeyComparer.Instance);
        internal readonly Dictionary<FalloutFormKey, SourceGraphRow> Rows = new(FalloutFormKeyComparer.Instance);
        internal readonly Dictionary<FalloutFormKey, FalloutCellDefinition> Definitions = new(FalloutFormKeyComparer.Instance);
        internal readonly Dictionary<FalloutFormKey, List<FalloutPluginRecord>> CellChildren = new(FalloutFormKeyComparer.Instance);
        internal readonly List<(FalloutFormKey Cell, FalloutFormKey Door, FalloutFormKey TargetCell, FalloutFormKey TargetDoor, bool Exterior)> Edges = [];
        internal readonly List<object> Failures = [];
    }

    // EffectiveRecords intentionally excludes deleted winners. Construct this
    // denominator from the same exact winner owner without resurrecting one.
    private static SourceGraphInventory DiscoverSourceGraph(FalloutPluginStack records)
    {
        var graph = new SourceGraphInventory();
        var navigationTargets = new SourceNavigationTargets(records);
        foreach (var declared in records.Plugins.SelectMany(plugin => plugin.Plugin.Records))
            if (declared.Signature != "TES4" && !graph.Winners.ContainsKey(declared.FormKey) &&
                records.TryGetWinner(declared.FormKey, out var winner)) graph.Winners.Add(winner.FormKey, winner);
        foreach (var record in graph.Winners.Values)
        {
            FalloutFormKey? cell = null, world = null;
            var groupErrors = new List<object>();
            try { cell = FalloutCellSceneReader.ParentCell(record); }
            catch (Exception error) { groupErrors.Add(new { lane = "source-cell-group", error = error.Message }); }
            try { world = FalloutCellSceneReader.ParentWorldspace(record); }
            catch (Exception error) { groupErrors.Add(new { lane = "source-world-group", error = error.Message }); }
            if (record.Signature is not ("CELL" or "WRLD" or "NAVM" or "LAND" or "ADDN") && !PlacedSignatures.Contains(record.Signature) &&
                cell is null && world is null && groupErrors.Count == 0) continue;
            var row = new SourceGraphRow { Identity = record.FormKey.ToString(), Signature = record.Signature,
                Winner = record.Plugin.Name, Flags = record.Flags, Deleted = record.IsDeleted,
                InitiallyDisabled = (record.Flags & 0x800) != 0, ParentCell = cell?.ToString(), ParentWorldspace = world?.ToString(),
                Disposition = record.IsDeleted ? "winning-deletion" : "winning-source-record" };
            row.Failures.AddRange(groupErrors); graph.Rows.Add(record.FormKey, row);
            if (cell is { } cellParent)
            {
                if (!graph.CellChildren.TryGetValue(cellParent, out var children)) graph.CellChildren.Add(cellParent, children = []);
                children.Add(record);
            }
            try
            {
                row.Sha256 = Hash(record.ReadData());
                var fields = record.ReadSubrecords().ToArray();
                row.SourceFields = fields.Select((field, index) => (object)new { index, field.Signature,
                    bytes = field.Data.Length, sha256 = Hash(field.Data.Span) }).ToArray();
                if (record.IsDeleted)
                {
                    row.Projection = "excluded-by-winning-deletion";
                    row.RuntimeOwner = "deleted source disposition; no live entity may be substituted";
                    continue;
                }
                if (record.Signature == "CELL")
                {
                    var definition = FalloutCellSceneReader.ReadDefinition(records, record.FormKey);
                    graph.Definitions.Add(record.FormKey, definition);
                    row.Definition = new { formKey = definition.FormKey.ToString(), definition.EditorId, definition.Flags,
                        coordinates = definition.Coordinates is { } grid ? new { grid.X, grid.Y } : null,
                        worldspace = definition.Worldspace?.ToString() };
                    row.Disposition = (definition.Flags & FalloutCellSceneReader.InteriorCellFlag) != 0 ? "interior-cell" : "exterior-cell";
                }
                else if (record.Signature == "WRLD")
                {
                    var parentFields = fields.Where(field => field.Signature == "WNAM").ToArray();
                    var flags = fields.Where(field => field.Signature == "PNAM").ToArray();
                    if (flags.Length > 1 || flags.Any(field => field.Data.Length != 2))
                        throw new InvalidDataException("WRLD PNAM parent flags are ambiguous or malformed.");
                    FalloutFormKey? parent = null;
                    if (parentFields.Length > 1 || parentFields.Any(field => field.Data.Length != 4))
                        throw new InvalidDataException("WRLD WNAM parent is ambiguous or malformed.");
                    if (parentFields.Length == 1) parent = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(parentFields[0].Data.Span));
                    if (parent is { } owner && (!records.TryGetEffective(owner, out var target) || target.Signature != "WRLD"))
                        throw new InvalidDataException("WRLD parent is absent, deleted, or not WRLD: " + owner);
                    row.SourceSemantics = new { parent = parent?.ToString(), parentFlags = flags.Length == 0 ? (ushort?)null :
                        BinaryPrimitives.ReadUInt16LittleEndian(flags[0].Data.Span), lodWorldName = FalloutExteriorLod.WorldName(records, record.FormKey),
                        environmentWaterWeatherAndLodResidency = "unverified; all field extents remain accounted for" };
                    row.Disposition = "worldspace";
                }
                else if (PlacedSignatures.Contains(record.Signature))
                {
                    var names = fields.Where(field => field.Signature == "NAME").ToArray();
                    if (names.Length != 1 || names[0].Data.Length != 4) throw new InvalidDataException("Placed NAME has no exact single FormID.");
                    var basis = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(names[0].Data.Span)); row.Base = basis.ToString();
                    if (cell is null) throw new InvalidDataException("Winning placed reference has no source CELL group.");
                    var primitive = FalloutReferencePrimitive.Read(record);
                    if (!records.TryGetEffective(basis, out _) && primitive is null)
                        throw new InvalidDataException("Placed base is missing or deleted without an exact source primitive: " + basis);
                    row.Disposition = row.InitiallyDisabled ? "initially-disabled-reference" : "source-reference";
                    row.SourceSemantics = new { primitive, placementAndBaseProjection = "must join the existing whole CELL scene reader",
                        enableAlternative = "both independent root states through reference owner; native alternatives unverified" };
                }
                else if (record.Signature == "NAVM")
                {
                    var mesh = FalloutNavigationMesh.Read(record);
                    var sharedAdjacencyValidated = false;
                    try
                    {
                        ValidateNavigationDeclarations([mesh], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mesh.Cell.ToString() });
                        sharedAdjacencyValidated = true;
                    }
                    catch (Exception error) { row.Failures.Add(new { lane = "navm-source-adjacency", error = error.Message }); }
                    if (cell != mesh.Cell) throw new InvalidDataException("NAVM DATA CELL differs from its source CELL ancestry.");
                    var externalTargets = navigationTargets.Inspect(record, mesh, row.Failures);
                    row.SourceSemantics = new { cell = mesh.Cell.ToString(), mesh.Version, vertices = mesh.Vertices.Length,
                        triangles = mesh.Triangles.Length, edges = mesh.Edges.Select(edge => new { edge.Type, target = edge.Mesh.ToString(), edge.Triangle,
                            winningTarget = records.TryGetEffective(edge.Mesh, out var target) && target.Signature == "NAVM" }),
                        doors = mesh.Doors.Select(door => new { target = door.Door.ToString(), door.Triangle }),
                        sourceDisabledIsNotInspectionExclusion = row.InitiallyDisabled, sharedAdjacencyValidated,
                        adjacencyTrianglesExcludedByFlags = Enumerable.Range(0, mesh.Triangles.Length)
                            .Where(index => (mesh.Triangles[index].Flags & 8) != 0).ToArray(),
                        externalTargetDeclarations = externalTargets,
                        externalTargetTriangleBoundsUninspected = externalTargets.Where(target => target.TargetTriangles is null)
                            .Select(target => new { target.SourceEntry, target.Target, target.Triangle }).ToArray(),
                        adjacencyScope = "Exact shared internal reciprocal/shared-edge and local external-index rules plus decoded winning-target triangle bounds; flag-excluded source adjacency remains uninspected. Native activation, directed path execution and contacts remain unverified." };
                    row.Disposition = "source-cell-navm";
                }
                else if (record.Signature == "LAND")
                {
                    row.Disposition = "source-cell-land";
                    if (cell is null) throw new InvalidDataException("Winning LAND has no source CELL group.");
                }
                else if (record.Signature == "ADDN")
                {
                    row.Disposition = "source-addon";
                    row.Projection = "source-addon-catalog";
                    row.SourceSemantics = new { catalog = "component.addonCatalog", sourceOwner = "FalloutAddonNodes",
                        indexModelAndSound = "exact selected catalog provenance; catalog/resource refusals retained independently",
                        nativeInstances = "unverified", composedCollision = "uninspected", audio = "SNAM native owner unbound" };
                }
                else
                {
                    row.Disposition = "unowned-cell-or-world-child";
                    row.Failures.Add(new { lane = "cell-world-child-owner", error = "No CellGraph semantic reader for winning child signature " + record.Signature });
                }
            }
            catch (Exception error) { row.Failures.Add(new { lane = "source-graph-record", error = error.Message }); }
        }
        foreach (var pair in graph.Rows)
        {
            var row = pair.Value;
            if (row.ParentCell is { } parentText)
            {
                var parent = ParseForm(parentText);
                if (!graph.Winners.TryGetValue(parent, out var owner) || owner.Signature != "CELL")
                    row.Failures.Add(new { lane = "reference-cell-owner", error = "Winning child binds missing/non-CELL group owner: " + parentText });
                else if (!row.Deleted && owner.IsDeleted)
                    row.Failures.Add(new { lane = "reference-cell-owner", error = "Live winning child is owned by a deleted CELL: " + parentText });
            }
            if (row.ParentWorldspace is { } worldText)
            {
                var world = ParseForm(worldText);
                if (!graph.Winners.TryGetValue(world, out var owner) || owner.Signature != "WRLD" || !row.Deleted && owner.IsDeleted)
                    row.Failures.Add(new { lane = "cell-world-owner", error = "Winning graph binds missing/deleted/non-WRLD group owner: " + worldText });
            }
            if (row.Deleted || !PlacedSignatures.Contains(row.Signature)) continue;
            try
            {
                var record = graph.Winners[pair.Key];
                var teleport = FalloutCellSceneReader.ReadTeleport(record);
                if (teleport is null) continue;
                var target = records.GetEffective(teleport.Door);
                if (target.Signature != "REFR") throw new InvalidDataException("XTEL target is not REFR.");
                var originCell = FalloutCellSceneReader.ParentCell(record) ?? throw new InvalidDataException("XTEL source has no CELL.");
                var targetCell = FalloutCellSceneReader.ParentCell(target) ?? throw new InvalidDataException("XTEL target has no CELL.");
                var definition = FalloutCellSceneReader.ReadDefinition(records, targetCell);
                graph.Edges.Add((originCell, record.FormKey, targetCell, target.FormKey, (definition.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0));
            }
            catch (Exception error) { row.Failures.Add(new { lane = "portal-discovery", error = error.Message }); }
        }
        if (!graph.Rows.Values.Any(row => row.Signature == "CELL" && !row.Deleted))
            graph.Failures.Add(new { lane = "source-cell-denominator", error = "The selected source stack has no effective winning CELL; no empty readiness is admitted." });
        return graph;
    }

    private static (object Report, bool InvariantsPassed, int FailureEvents, int RuntimeOwnersUnverified) SourceGraphSummary(SourceGraphInventory graph, IReadOnlySet<FalloutFormKey> selected,
        ComponentSummary summary, bool completeSource)
    {
        var projected = summary.Bound;
        var completedCellIdentities = summary.CompletedCells;
        foreach (var pair in graph.Rows)
        {
            var row = pair.Value;
            if (row.Deleted) continue;
            if (row.Signature == "CELL" && selected.Contains(pair.Key))
                row.Projection = completedCellIdentities.Contains(row.Identity) ? "source-scene-report" : "failed-scene-projection";
            else if (PlacedSignatures.Contains(row.Signature) && row.ParentCell is { } cell && selected.Contains(ParseForm(cell)))
                row.Projection = projected.Contains(row.Identity) ? "source-reference-report" : "failed-scene-projection";
        }
        var rows = graph.Rows.Values.ToArray();
        var winningReferenceIdentities = rows.Where(row => !row.Deleted && PlacedSignatures.Contains(row.Signature)).Select(row => row.Identity).ToHashSet(StringComparer.Ordinal);
        var sourceFailureEvents = rows.Sum(row => row.Failures.Count) + graph.Failures.Count;
        var runtimeOwnersUnverified = rows.Count(row => !row.Deleted);
        var identityUnique = graph.Rows.Count == rows.Select(row => row.Identity).Distinct(StringComparer.Ordinal).Count();
        var effectiveCellsSelected = !completeSource || rows.Where(row => row.Signature == "CELL" && !row.Deleted).All(row => selected.Contains(ParseForm(row.Identity)));
        var deletedExcluded = rows.Where(row => row.Deleted).All(row => row.Projection == "excluded-by-winning-deletion");
        var projectedAreWinning = projected.IsSubsetOf(winningReferenceIdentities);
        var report = new
        {
            scope = completeSource ? "all-winning-cell-world-reference-graphs" : "explicit-interior-diagnostic-component",
            denominator = new { winningCells = rows.Count(row => row.Signature == "CELL"),
                effectiveCells = rows.Count(row => row.Signature == "CELL" && !row.Deleted),
                deletedCells = rows.Count(row => row.Signature == "CELL" && row.Deleted),
                winningWorldspaces = rows.Count(row => row.Signature == "WRLD"), deletedWorldspaces = rows.Count(row => row.Signature == "WRLD" && row.Deleted),
                winningReferences = rows.Count(row => PlacedSignatures.Contains(row.Signature)),
                deletedReferences = rows.Count(row => PlacedSignatures.Contains(row.Signature) && row.Deleted),
                initiallyDisabledReferences = rows.Count(row => PlacedSignatures.Contains(row.Signature) && !row.Deleted && row.InitiallyDisabled),
                winningNavMeshes = rows.Count(row => row.Signature == "NAVM"), deletedNavMeshes = rows.Count(row => row.Signature == "NAVM" && row.Deleted),
                initiallyDisabledNavMeshes = rows.Count(row => row.Signature == "NAVM" && !row.Deleted && row.InitiallyDisabled),
                winningLandscapes = rows.Count(row => row.Signature == "LAND"), deletedLandscapes = rows.Count(row => row.Signature == "LAND" && row.Deleted),
                winningCellAndWorldChildren = rows.Count(row => row.ParentCell is not null || row.ParentWorldspace is not null),
                rowsWithSourceFailures = rows.Count(row => row.Failures.Count != 0), sourceFailureEvents,
                failedProjectionRows = rows.Count(row => row.Projection == "failed-scene-projection"),
                runtimeOwnersUnverified, selectedCells = selected.Count },
            invariants = new { winnerIdentityUnique = identityUnique, everyEffectiveCellSelected = effectiveCellsSelected,
                noDeletedWinnerProjected = deletedExcluded, everyProjectedReferenceHasWinningSource = projectedAreWinning },
            rows = rows.OrderBy(row => row.Identity, StringComparer.Ordinal), graphFailures = graph.Failures,
            finiteDomain = "All winning source graph rows and authored declaration alternatives; combinations, arbitrary source script states and native outcomes are independent unverified owners.",
            readiness = "unverified; this source inventory never certifies gameplay, native execution, or final pixels"
        };
        return (report, identityUnique && effectiveCellsSelected && deletedExcluded && projectedAreWinning, sourceFailureEvents, runtimeOwnersUnverified);
    }
}
