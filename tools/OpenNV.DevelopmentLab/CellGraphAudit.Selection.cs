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
        WriteReport(Path.Combine(options.Output, "audit-start.private.json"), new
        {
            schema = "opennv-cell-graph-audit-start/v1", source.StackId, source.SaveCompatibilityId,
            auditBuild = typeof(CellGraphAudit).Module.ModuleVersionId,
            runtimeBuild = typeof(FalloutNifFile).Module.ModuleVersionId,
            configurationSha256, options.CompleteSource,
            winningCellDenominator = "not-yet-discovered", remainingSourceGraph = "uninspected", readinessPassed = false
        });
        var graph = DiscoverSourceGraph(records);
        var seed = options.Seed;
        var directory = options.Output;
        var metadataName = options.Metadata;
        var graphErrors = new List<object>();
        var metadata = new List<FalloutCellDefinition>();
        var metadataUnbound = new List<FalloutFormKey>();
        foreach (var record in metadataName is null ? [] : records.EffectiveRecords("CELL"))
            try
            {
                if (!record.ReadSubrecords().Any(field => field.Signature == "EDID" &&
                    FalloutDialogueTopic.Text(field.Data.Span).Contains(metadataName!, StringComparison.OrdinalIgnoreCase))) continue;
                if (graph.Definitions.TryGetValue(record.FormKey, out var definition)) metadata.Add(definition);
                else metadataUnbound.Add(record.FormKey);
            }
            catch (Exception error)
            {
                metadataUnbound.Add(record.FormKey);
                graphErrors.Add(new { cell = record.FormKey.ToString(), lane = "metadata-cell-reader", error = error.Message,
                    effectiveSourceReferences = SourceReferenceIdentities(records, record.FormKey) });
            }
        var metadataInterior = metadata.Where(cell => (cell.Flags & FalloutCellSceneReader.InteriorCellFlag) != 0).Select(cell => cell.FormKey).ToHashSet();
        var selected = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        var queue = new Queue<FalloutFormKey>();
        var outgoing = graph.Edges.GroupBy(edge => edge.Cell).ToDictionary(group => group.Key, group => group.ToArray(), FalloutFormKeyComparer.Instance);
        if (options.CompleteSource)
        {
            foreach (var row in graph.Rows.Where(pair => pair.Value.Signature == "CELL" && !pair.Value.Deleted)) selected.Add(row.Key);
            if (seed is { } anchor && !selected.Contains(anchor))
                graphErrors.Add(new { cell = anchor.ToString(), lane = "observation-seed", error = "Seed is absent, deleted or not an effective CELL in the complete winning graph." });
        }
        else
        {
            queue.Enqueue(seed ?? throw new ArgumentException("Component selection has no seed."));
            foreach (var cell in metadataInterior) queue.Enqueue(cell);
            foreach (var cell in metadataUnbound) queue.Enqueue(cell);
            while (queue.TryDequeue(out var cell))
            {
                if (!selected.Add(cell)) continue;
                if (!graph.Definitions.TryGetValue(cell, out var definition) ||
                    (definition.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0)
                {
                    graphErrors.Add(new { cell = cell.ToString(), lane = "cell-reader", error = "Interior component seed/selection is exterior or has no bound effective CELL definition.",
                        effectiveSourceReferences = SourceReferenceIdentities(records, cell) });
                    continue;
                }
                foreach (var edge in outgoing.GetValueOrDefault(cell) ?? [])
                    if (!edge.Exterior) queue.Enqueue(edge.TargetCell);
            }
        }
        var edges = graph.Edges.Where(edge => selected.Contains(edge.Cell)).ToArray();
        var adjacent = new Dictionary<FalloutFormKey, HashSet<FalloutFormKey>>(FalloutFormKeyComparer.Instance);
        foreach (var edge in edges.Where(edge => options.CompleteSource || !edge.Exterior))
        {
            if (!adjacent.TryGetValue(edge.Cell, out var from)) adjacent.Add(edge.Cell, from = new(FalloutFormKeyComparer.Instance));
            if (!adjacent.TryGetValue(edge.TargetCell, out var to)) adjacent.Add(edge.TargetCell, to = new(FalloutFormKeyComparer.Instance));
            from.Add(edge.TargetCell); to.Add(edge.Cell);
        }
        var connected = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        if (seed is { } connectedSeed) queue.Enqueue(connectedSeed);
        while (queue.TryDequeue(out var cell))
        {
            if (!connected.Add(cell)) continue;
            foreach (var neighbor in adjacent.GetValueOrDefault(cell) ?? []) queue.Enqueue(neighbor);
        }
        var auditResources = new CellAuditResources(source, units, records);
        auditResources.ReadAddonDeclarations();
        var alternatives = DiscoverAlternativeDeclarations(source, records, graph, selected, auditResources);
        var reports = new List<object>();
        var accumulator = new ComponentSummary(records, selected);
        foreach (var cell in selected.OrderBy(cell => cell.ToString(), StringComparer.Ordinal))
        {
            var path = Path.Combine(directory, "cell-" + cell.OwnerPlugin.Replace('.', '-') + "-" + cell.ObjectId.ToString("x6") +
                ".private.json" + (options.CompressReports ? ".gz" : ""));
            Console.WriteLine(JsonSerializer.Serialize(new { audit = "cell-component-progress", cell = cell.ToString(), selected = selected.Count,
                completed = reports.Count, elapsedSeconds = clock.Elapsed.TotalSeconds }));
            try
            {
                RunCell(source, records, cell, path, options with { Snapshot = cell == seed ? options.Snapshot : null,
                    SampleNative = cell == seed ? options.SampleNative : null }, units, configurationSha256, clock, auditResources, graph);
                using var report = ReadReport(path); var data = report.RootElement;
                accumulator.Observe(data);
                reports.Add(new { cell = cell.ToString(), report = path, denominator = data.GetProperty("denominator").Clone(),
                    runtimeAuditBuild = data.GetProperty("runtimeAuditBuild").Clone(),
                    failures = data.GetProperty("failures").GetArrayLength(), navFailures = data.GetProperty("navFailures").GetArrayLength(),
                    referenceFailureIdentities = data.GetProperty("references").EnumerateArray().Where(reference => reference.GetProperty("failures").GetArrayLength() != 0)
                        .Select(reference => reference.GetProperty("identity").GetString()).ToArray(),
                    resourceFailurePaths = data.GetProperty("resources").EnumerateArray().Where(resource => resource.GetProperty("failures").GetArrayLength() != 0)
                        .Select(resource => resource.GetProperty("path").GetString()).ToArray() });
            }
            catch (Exception error)
            {
                graphErrors.Add(new { cell = cell.ToString(), lane = "cell-audit", error = error.Message,
                    effectiveSourceReferences = SourceReferenceIdentities(records, cell) });
            }
        }
        var summary = accumulator.Build();
        var graphSummary = SourceGraphSummary(graph, selected, accumulator, options.CompleteSource);
        var sourceGraph = graphSummary.Report;
        var resourceHashConsistency = accumulator.ResourceHashesMatch(auditResources.Resources);
        var sourceAccountingPassed = auditResources.AddonCatalogPassed && resourceHashConsistency && graphErrors.Count == 0 && graphSummary.FailureEvents == 0 && graphSummary.InvariantsPassed &&
            alternatives.ActorAnimations is { DeclarationsPassed: true } &&
            alternatives.IncomingAnimations is { DeclarationsPassed: true } &&
            alternatives.Failures.Count == 0 && alternatives.Records.All(row => row.Failures.Count == 0) &&
            alternatives.Resources.All(resource => resource.Failures.Count == 0) &&
            accumulator.PackageErrorEvents == 0 && accumulator.InvariantsPassed && accumulator.InspectedReportsPassed;
        var runtimeOwnersUnverified = graphSummary.RuntimeOwnersUnverified;
        // A source report, even with a historical snapshot, cannot finish an
        // actual native/state-combination/final-pixel owner.
        var ready = options.CompleteSource && sourceAccountingPassed && runtimeOwnersUnverified == 0;
        var component = new { schema = "opennv-development-cell-component-audit/v2", source.StackId, source.SaveCompatibilityId,
            runtimeAuditBuild = typeof(FalloutNifFile).Module.ModuleVersionId, configurationSha256, units,
            seed = seed?.ToString(), selection = new { options.CompleteSource, metadataName, metadataInteriorCount = metadataInterior.Count,
                metadataUnboundCells = metadataUnbound.Select(cell => cell.ToString()), totalSelectedCells = selected.Count,
                totalSelectedInteriors = selected.Count(cell => graph.Definitions.TryGetValue(cell, out var definition) &&
                    (definition.Flags & FalloutCellSceneReader.InteriorCellFlag) != 0) + selected.Count(cell => !graph.Definitions.ContainsKey(cell)),
                xtelWeakComponentFromSeed = connected.Count,
                disconnectedMetadataInteriors = metadataInterior.Where(cell => !connected.Contains(cell)).Select(cell => cell.ToString()).Order().ToArray(),
                metadataExteriorCells = metadata.Where(cell => (cell.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0),
                policy = options.CompleteSource ? "Every effective winning CELL, including exterior/disconnected/empty/disabled source graphs. Seed/metadata only classify observations; they do not filter the denominator." :
                    "Explicit diagnostic interior component. Exterior boundary rows remain source-accounted; this restricted scope cannot establish complete readiness." },
            metadataCells = metadata, edges = edges.Select(edge => new { cell = edge.Cell.ToString(), door = edge.Door.ToString(),
                destinationCell = edge.TargetCell.ToString(), destinationDoor = edge.TargetDoor.ToString(), exteriorBoundary = !options.CompleteSource && edge.Exterior,
                exteriorDestination = edge.Exterior }),
            sourceGraph, addonCatalog = auditResources.AddonCoverage, alternatives, summary, graphErrors, reports, elapsedSeconds = clock.Elapsed.TotalSeconds,
            reuse = new { recentDecodedModelsMaximum = 64, recentModelBudgetProxyBytes = 128L * 1024 * 1024,
                recentNavigationCellsMaximum = 64, recentNavigationBudgetProxyBytes = 64L * 1024 * 1024,
                policy = "Bounded recent decoded NIF/collision assets; full neutral winner/resource/failure metadata and per-cell reports remain uncapped.",
                measuredPeakMemoryAndFullSweepDuration = "unverified" },
            coverage = new { sourceAccountingPassed, resourceHashConsistency, completeSourceGraphSelected = options.CompleteSource, runtimeOwnersUnverified,
                alternativeSemanticFieldsUninspected = alternatives.Records.Sum(row => row.UninspectedSemanticFieldSlots.Length),
                selectionCombinations = "This exact stack/order/settings only; other valid launcher selections remain uninspected dimensions.",
                nativeStateCombinations = "unverified", nativePhysics = "unverified", gameplay = "not-executed", finalPixels = "unverified", readinessPassed = ready } };
        var componentPath = Path.Combine(directory, "component.private.json" + (options.CompressReports ? ".gz" : ""));
        WriteReport(componentPath, component);
        // A compact receipt keeps the complete multi-million-field report
        // reviewable without loading that report into another object graph.
        WriteReport(Path.Combine(directory, "summary.json"), new
        {
            schema = "opennv-cell-graph-audit-summary/v1", source.StackId, source.SaveCompatibilityId,
            auditBuild = typeof(CellGraphAudit).Module.ModuleVersionId,
            runtimeBuild = typeof(FalloutNifFile).Module.ModuleVersionId, configurationSha256,
            completeSourceSelected = options.CompleteSource, inventoryFinished = true,
            selectedCells = selected.Count, completedCellReports = reports.Count,
            winningGraphRows = graph.Rows.Count,
            winningCells = graph.Rows.Values.Count(row => row.Signature == "CELL"),
            deletedCells = graph.Rows.Values.Count(row => row.Signature == "CELL" && row.Deleted),
            winningReferences = graph.Rows.Values.Count(row => PlacedSignatures.Contains(row.Signature)),
            deletedReferences = graph.Rows.Values.Count(row => PlacedSignatures.Contains(row.Signature) && row.Deleted),
            initiallyDisabledReferences = graph.Rows.Values.Count(row => PlacedSignatures.Contains(row.Signature) && !row.Deleted && row.InitiallyDisabled),
            graphErrors = graphErrors.Count, sourceGraphFailureEvents = graphSummary.FailureEvents,
            graphInvariantsPassed = graphSummary.InvariantsPassed, resourceHashConsistency,
            sourceAccountingPassed, runtimeOwnersUnverified, readinessPassed = ready,
            componentPath, elapsedSeconds = clock.Elapsed.TotalSeconds,
            boundary = "The compact counts refer to the complete private component and CELL reports. Source accounting does not certify native/state behavior, physics, gameplay or pixels."
        });
        Console.WriteLine(JsonSerializer.Serialize(new { audit = "cell-component", componentPath, selected = selected.Count,
            completeSourceReports = reports.Count, graphErrors = graphErrors.Count, sourceAccountingPassed, runtimeOwnersUnverified, elapsedSeconds = clock.Elapsed.TotalSeconds }));
        Console.WriteLine(JsonSerializer.Serialize(new { audit = "cell-graph-result", exitCode = ready ? 0 : 1,
            outcome = ready ? "Every required graph/source/runtime coverage owner is accounted for." : "Issue and coverage inventory produced; retained failures or required owners remain unverified.",
            sourceAccountingPassed, componentPath, parity = "unverified", nativePhysics = "unverified", gameplay = "not-executed" }));
        return ready;
    }

    private static string[] SourceReferenceIdentities(FalloutPluginStack records, FalloutFormKey cell) =>
        records.EffectiveCellChildren(cell, PlacedSignatures).Select(record => record.FormKey.ToString()).Order(StringComparer.Ordinal).ToArray();
}
