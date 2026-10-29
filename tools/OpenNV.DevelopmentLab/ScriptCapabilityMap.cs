using System.Text.Json;
using OpenNV.Runtime.World.Cells;

// Compact executable view of the existing source audit. It reports only the
// inspection evidence that exists; static signatures never certify execution.
internal static class ScriptCapabilityMap
{
    private sealed class Group
    {
        internal long Statements, Uninspected, Declarations, ControlFlow, Errors;
        internal HashSet<string> Owners { get; } = new(StringComparer.Ordinal);
        internal SortedSet<string> Examples { get; } = new(StringComparer.Ordinal);
        internal SortedSet<string> Reasons { get; } = new(StringComparer.Ordinal);
    }

    private sealed class EventGroup
    {
        internal long Statements;
        internal HashSet<string> Owners { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, Group> Operations { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static object[] FailedSourceBodies(string root, long expected)
    {
        using var failures = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "failures.json")));
        var failed = failures.RootElement.EnumerateArray()
            .Where(row => row.GetProperty("lane").GetString() == "source-parser")
            .Select(row => (Scope: row.GetProperty("scope").GetString() ?? throw new InvalidDataException("Parser failure has no scope."),
                Error: row.GetProperty("error").GetString() ?? throw new InvalidDataException("Parser failure has no error.")))
            .ToArray();
        if (failed.LongLength != expected || failed.Select(row => row.Scope).Distinct(StringComparer.Ordinal).Count() != failed.Length)
            throw new InvalidDataException("Parser failures differ from the source audit denominator.");
        var programs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var wanted = failed.Select(row =>
        {
            var separator = row.Scope.LastIndexOf("/SCTX:", StringComparison.Ordinal);
            if (separator < 0) throw new InvalidDataException("Parser failure lacks its source-field identity.");
            return row.Scope[..separator];
        }).ToHashSet(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(root, "programs.jsonl")))
        {
            using var document = JsonDocument.Parse(line);
            var row = document.RootElement;
            var scope = row.GetProperty("scopeIdentity").GetString() ?? throw new InvalidDataException("Program has no scope identity.");
            if (wanted.Contains(scope) && !programs.TryAdd(scope, row.Clone()))
                throw new InvalidDataException("Parser failure maps to more than one program scope.");
        }
        if (programs.Count != wanted.Count) throw new InvalidDataException("Parser failure has no program scope.");
        return failed.Select(row =>
        {
            var scope = row.Scope[..row.Scope.LastIndexOf("/SCTX:", StringComparison.Ordinal)];
            var program = programs[scope];
            return (object)new { sourceBody = row.Scope, programScope = scope, error = row.Error,
                compiledBytes = program.GetProperty("compiledBytes").GetInt64(),
                compiledBodies = program.GetProperty("compiledBodies").GetInt32(),
                references = program.GetProperty("references").GetInt32(),
                extentDisposition = program.GetProperty("extentDisposition").GetString(),
                compiledExecution = "unknown" };
        }).ToArray();
    }

    internal static int Run(string directory, string repository)
    {
        var root = Path.GetFullPath(directory);
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "summary.json")));
        var report = summary.RootElement;
        if (report.GetProperty("schema").GetString() != "opennv-private-quest-graph-audit/v2")
            throw new InvalidDataException("The script map requires a compatible complete quest-graph audit.");
        var expected = report.GetProperty("statements").GetInt64();
        var expectedErrors = report.GetProperty("unboundStatements").GetInt64();
        var expectedUnknown = report.GetProperty("uninspectedCommands").GetInt64();
        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        var events = new Dictionary<string, EventGroup>(StringComparer.OrdinalIgnoreCase);
        long total = 0, errors = 0, unknown = 0;
        foreach (var line in File.ReadLines(Path.Combine(root, "statements.jsonl")))
        {
            using var row = JsonDocument.Parse(line);
            var entry = row.RootElement;
            var source = entry.GetProperty("source").GetString() ?? throw new InvalidDataException("Audit statement has no source.");
            var eventName = entry.GetProperty("Event").GetString();
            if (string.IsNullOrWhiteSpace(eventName)) throw new InvalidDataException("Audit statement has no event context.");
            var statement = entry.GetProperty("statement");
            var operation = statement.GetProperty("Operation").GetString();
            var ownership = statement.GetProperty("Ownership").GetString();
            if (operation is null || string.IsNullOrWhiteSpace(ownership))
                throw new InvalidDataException("Audit statement lacks operation or inspection disposition.");
            if (operation.Length == 0) operation = "<empty-operation>";
            if (!groups.TryGetValue(operation, out var group)) groups.Add(operation, group = new());
            if (!events.TryGetValue(eventName, out var eventGroup)) events.Add(eventName, eventGroup = new());
            if (!eventGroup.Operations.TryGetValue(operation, out var eventOperation))
                eventGroup.Operations.Add(operation, eventOperation = new());
            ++total; ++eventGroup.Statements; eventGroup.Owners.Add(source);
            void Add(Group destination)
            {
                ++destination.Statements; destination.Owners.Add(source);
                destination.Examples.Add(source);
                if (destination.Examples.Count > 3) destination.Examples.Remove(destination.Examples.Max!);
                switch (ownership)
                {
                    case "statement-dispatch-not-inspected": ++destination.Uninspected; break;
                    case "expression-declarations-and-signatures": ++destination.Declarations; break;
                    case "control-flow": ++destination.ControlFlow; break;
                    case "unbound":
                        var error = statement.GetProperty("Error").GetString();
                        if (string.IsNullOrWhiteSpace(error)) throw new InvalidDataException("Unbound statement has no observed reason.");
                        ++destination.Errors; destination.Reasons.Add(error); break;
                    default: throw new InvalidDataException("Unknown statement inspection disposition: " + ownership);
                }
            }
            Add(group); Add(eventOperation);
            switch (ownership)
            {
                case "statement-dispatch-not-inspected": ++unknown; break;
                case "unbound": ++errors; break;
            }
        }
        if (total != expected || errors != expectedErrors || unknown != expectedUnknown ||
            events.Values.Sum(group => group.Statements) != total ||
            events.Values.Any(group => group.Operations.Values.Sum(operation => operation.Statements) != group.Statements) ||
            events.Values.Sum(group => group.Operations.Values.Sum(operation => operation.Errors)) != errors ||
            events.Values.Sum(group => group.Operations.Values.Sum(operation => operation.Uninspected)) != unknown)
            throw new InvalidDataException("Statement map does not match the source audit denominator.");
        var dispatch = ScriptDispatchIndex.Read(Path.GetFullPath(repository));
        var failedSourceBodies = FailedSourceBodies(root, report.GetProperty("parsedSourceFailures").GetInt64());
        var output = new
        {
            schema = "opennv-script-capability-map/v1",
            sourceCompatibilityId = report.GetProperty("SaveCompatibilityId").GetString(),
            auditBuild = report.GetProperty("auditBuild").GetString(),
            auditedRuntimeBuild = report.GetProperty("runtimeBuild").GetString(),
            currentRuntimeBuild = typeof(FalloutReferenceScripts).Module.ModuleVersionId,
            sameRuntimeBuild = report.GetProperty("runtimeBuild").GetGuid() == typeof(FalloutReferenceScripts).Module.ModuleVersionId,
            total, errors, dispatchNotInspected = unknown,
            sourceBodies = report.GetProperty("sourceBodies").GetInt64(),
            sourceParseFailures = report.GetProperty("parsedSourceFailures").GetInt64(),
            inspectionFailureBodies = report.GetProperty("inspectionFailureBodies").GetInt64(),
            failedSourceBodies,
            compiledExecution = "unknown", runtimeEffects = "unknown", retailParity = "unknown",
            boundary = "Literal dispatch declarations are source evidence only; guards, host ownership, execution and retail behavior remain unverified. Dynamic dispatch and expressions are not covered by this index.",
            dispatchSourceHashes = dispatch.Sources,
            nonliteralDispatchGuards = dispatch.DynamicGuards,
            events = events.OrderByDescending(pair => pair.Value.Statements).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new { name = pair.Key, statements = pair.Value.Statements, sourceBodies = pair.Value.Owners.Count,
                    operations = pair.Value.Operations.OrderByDescending(operation => operation.Value.Uninspected + operation.Value.Errors)
                        .ThenBy(operation => operation.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(operation => new { operation = operation.Key, statements = operation.Value.Statements,
                            sourceBodies = operation.Value.Owners.Count, dispatchNotInspected = operation.Value.Uninspected,
                            declarationSignatures = operation.Value.Declarations, controlFlow = operation.Value.ControlFlow,
                            inspectionErrors = operation.Value.Errors, reasons = operation.Value.Reasons,
                            exampleSources = operation.Value.Examples,
                            literalDispatchSites = dispatch.Operations.TryGetValue(operation.Key, out var sites) ? sites : [] }) }),
            operations = groups.OrderByDescending(pair => pair.Value.Uninspected + pair.Value.Errors)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new { operation = pair.Key, statements = pair.Value.Statements, sourceBodies = pair.Value.Owners.Count,
                    dispatchNotInspected = pair.Value.Uninspected, declarationSignatures = pair.Value.Declarations,
                    controlFlow = pair.Value.ControlFlow, inspectionErrors = pair.Value.Errors,
                    exampleSources = pair.Value.Examples, reasons = pair.Value.Reasons,
                    literalDispatchSites = dispatch.Operations.TryGetValue(pair.Key, out var sites) ? sites : [] })
        };
        Console.WriteLine(JsonSerializer.Serialize(output));
        return 0;
    }
}
