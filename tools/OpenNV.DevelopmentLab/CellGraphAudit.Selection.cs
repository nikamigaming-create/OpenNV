using System.Diagnostics;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAudit
{
    internal static bool RunComponent(RuntimeLiveContentSource source, FalloutPluginStack records, Options options,
        float units, string configurationSha256)
    {
        var clock = Stopwatch.StartNew();
        var seed = options.Seed;
        var directory = options.Output;
        var metadataName = options.Metadata;
        var graphErrors = new List<object>();
        var metadata = new List<FalloutCellDefinition>();
        var metadataUnbound = new List<FalloutFormKey>();
        foreach (var record in metadataName is null ? [] : records.EffectiveRecords("CELL"))
        {
            try
            {
                if (record.ReadSubrecords().Any(field => field.Signature == "EDID" &&
                    FalloutDialogueTopic.Text(field.Data.Span).Contains(metadataName!, StringComparison.OrdinalIgnoreCase)))
                    metadata.Add(FalloutCellSceneReader.ReadDefinition(records, record.FormKey));
            }
            catch (Exception error)
            {
                metadataUnbound.Add(record.FormKey);
                graphErrors.Add(new { cell = record.FormKey.ToString(), lane = "metadata-cell-reader", error = error.Message,
                    effectiveSourceReferences = SourceReferenceIdentities(records, record.FormKey) });
            }
        }
        var metadataInterior = metadata.Where(cell => (cell.Flags & FalloutCellSceneReader.InteriorCellFlag) != 0).Select(cell => cell.FormKey).ToHashSet();
        var queue = new Queue<FalloutFormKey>(); queue.Enqueue(seed);
        foreach (var cell in metadataInterior) queue.Enqueue(cell);
        foreach (var cell in metadataUnbound) queue.Enqueue(cell);
        var selected = new HashSet<FalloutFormKey>();
        var edges = new List<(FalloutFormKey Cell, FalloutFormKey Door, FalloutFormKey TargetCell, FalloutFormKey TargetDoor, bool Exterior)>();
        while (queue.TryDequeue(out var cell))
        {
            if (!selected.Add(cell)) continue;
            FalloutCellScene scene;
            try
            {
                scene = FalloutCellSceneReader.Read(records, cell);
                if ((scene.Cell.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0)
                    throw new InvalidDataException("Cell-graph interior selection reached an exterior seed.");
            }
            catch (Exception error)
            {
                graphErrors.Add(new { cell = cell.ToString(), lane = "cell-reader", error = error.Message,
                    effectiveSourceReferences = SourceReferenceIdentities(records, cell) });
                continue;
            }
            foreach (var reference in scene.References.Where(reference => reference.Teleport is not null))
                try
                {
                    var targetRecord = records.GetEffective(reference.Teleport!.Door);
                    var targetCell = FalloutCellSceneReader.ParentCell(targetRecord) ?? throw new InvalidDataException("XTEL target has no source CELL.");
                    var target = FalloutCellSceneReader.ReadDefinition(records, targetCell);
                    var exterior = (target.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0;
                    edges.Add((cell, reference.FormKey, targetCell, targetRecord.FormKey, exterior));
                    if (!exterior) queue.Enqueue(targetCell);
                }
                catch (Exception error) { graphErrors.Add(new { cell = cell.ToString(), reference = reference.FormKey.ToString(), lane = "portal-discovery", error = error.Message }); }
        }
        var connected = new HashSet<FalloutFormKey>(); queue.Enqueue(seed);
        while (queue.TryDequeue(out var cell))
        {
            if (!connected.Add(cell)) continue;
            foreach (var edge in edges.Where(edge => !edge.Exterior && (edge.Cell == cell || edge.TargetCell == cell)))
                queue.Enqueue(edge.Cell == cell ? edge.TargetCell : edge.Cell);
        }
        var reports = new List<object>();
        var completedCells = new List<JsonElement>();
        foreach (var cell in selected.OrderBy(cell => cell.ToString(), StringComparer.Ordinal))
        {
            var path = Path.Combine(directory, "cell-" + cell.OwnerPlugin.Replace('.', '-') + "-" + cell.ObjectId.ToString("x6") + ".private.json");
            Console.WriteLine(JsonSerializer.Serialize(new { audit = "cell-component-progress", cell = cell.ToString(), selected = selected.Count,
                completed = reports.Count, elapsedSeconds = clock.Elapsed.TotalSeconds }));
            try
            {
                RunCell(source, records, cell, path, options with { Snapshot = cell == seed ? options.Snapshot : null,
                    SampleNative = cell == seed ? options.SampleNative : null }, units, configurationSha256, clock);
                using var report = JsonDocument.Parse(File.ReadAllBytes(path));
                var data = report.RootElement;
                completedCells.Add(data.Clone());
                reports.Add(new { cell = cell.ToString(), report = path, denominator = data.GetProperty("denominator").Clone(),
                    invariants = data.GetProperty("invariants").Clone(), runtimeAuditBuild = data.GetProperty("runtimeAuditBuild").Clone(),
                    failures = data.GetProperty("failures").Clone(), navFailures = data.GetProperty("navFailures").Clone(),
                    referenceFailures = data.GetProperty("references").EnumerateArray().Where(reference => reference.GetProperty("failures").GetArrayLength() != 0)
                        .Select(reference => new { identity = reference.GetProperty("identity").GetString(),
                            signature = reference.GetProperty("signature").GetString(), model = reference.GetProperty("model").GetString(),
                            failures = reference.GetProperty("failures").Clone() }).ToArray(),
                    resourceFailures = data.GetProperty("resources").EnumerateArray().Where(resource => resource.GetProperty("failures").GetArrayLength() != 0)
                        .Select(resource => new { path = resource.GetProperty("path").GetString(), failures = resource.GetProperty("failures").Clone() }).ToArray() });
            }
            catch (Exception error) { graphErrors.Add(new { cell = cell.ToString(), lane = "cell-audit", error = error.Message,
                effectiveSourceReferences = SourceReferenceIdentities(records, cell) }); }
        }
        var summary = SummarizeComponent(records, selected, completedCells);
        var summaryData = JsonSerializer.SerializeToElement(summary, Json);
        var component = new { schema = "opennv-development-cell-component-audit/v1", source.SaveCompatibilityId,
            runtimeAuditBuild = typeof(FalloutNifFile).Module.ModuleVersionId, configurationSha256, units,
            seed = seed.ToString(), selection = new { metadataName, metadataInteriorCount = metadataInterior.Count,
                metadataUnboundCells = metadataUnbound.Select(cell => cell.ToString()),
                totalSelectedInteriors = selected.Count, xtelWeakComponentFromSeed = connected.Count,
                disconnectedMetadataInteriors = metadataInterior.Where(cell => !connected.Contains(cell)).Select(cell => cell.ToString()).Order().ToArray(),
                metadataExteriorCells = metadata.Where(cell => (cell.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0),
                policy = "All metadata-selected authored interiors plus their outgoing interior XTEL closure. Weak adjacency is used only to classify seed connectivity; teleport links remain directed. Exterior target cells are boundary edges." },
            metadataCells = metadata, edges = edges.Select(edge => new { cell = edge.Cell.ToString(), door = edge.Door.ToString(),
                destinationCell = edge.TargetCell.ToString(), destinationDoor = edge.TargetDoor.ToString(), exteriorBoundary = edge.Exterior }),
            summary, graphErrors, reports, elapsedSeconds = clock.Elapsed.TotalSeconds,
            gameplay = "not-executed", nativeOtherCells = "unverified; only the selected detailed native snapshot has an observation join" };
        var componentPath = Path.Combine(directory, "component.private.json");
        File.WriteAllText(componentPath, JsonSerializer.Serialize(component, Json) + System.Environment.NewLine);
        Console.WriteLine(JsonSerializer.Serialize(new { audit = "cell-component", componentPath, selected = selected.Count,
            completeSourceReports = reports.Count, graphErrors = graphErrors.Count, elapsedSeconds = clock.Elapsed.TotalSeconds }));
        var passed = graphErrors.Count == 0 && summaryData.GetProperty("denominator").GetProperty("packageErrorEvents").GetInt32() == 0 &&
            summaryData.GetProperty("invariants").EnumerateObject().All(invariant => invariant.Value.GetBoolean()) && reports.All(report =>
        {
            var value = JsonSerializer.SerializeToElement(report);
            return value.GetProperty("referenceFailures").GetArrayLength() == 0 &&
                value.GetProperty("resourceFailures").GetArrayLength() == 0 && value.GetProperty("failures").GetArrayLength() == 0 &&
                value.GetProperty("navFailures").GetArrayLength() == 0;
        });
        Console.WriteLine(JsonSerializer.Serialize(new { audit = "cell-graph-result", exitCode = passed ? 0 : 1,
            outcome = passed ? "No issues found within the stated source audit scope." : "Issue inventory produced; retained source or evidence-binding divergences.",
            componentPath, parity = "unverified", nativePhysics = "unverified", gameplay = "not-executed" }));
        return passed;
    }

    private static string[] SourceReferenceIdentities(FalloutPluginStack records, FalloutFormKey cell) =>
        records.EffectiveCellChildren(cell, new HashSet<string> { "REFR", "ACHR", "ACRE", "PGRE", "PMIS" })
            .Select(record => record.FormKey.ToString()).Order(StringComparer.Ordinal).ToArray();
}
