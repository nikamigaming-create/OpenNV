using System.Text.Json;

internal static partial class RuntimeKnowledgeGraph
{
    private sealed class Operation
    {
        internal long Count, Errors, Uninspected;
        internal SortedSet<string> Reasons { get; } = new(StringComparer.Ordinal);
    }

    private static object Audits(Graph graph, string repository, string[] audits)
    {
        var dispatch = ScriptDispatchIndex.Read(repository);
        var dispatchSites = 0;
        foreach (var (operation, sites) in dispatch.Operations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var command = graph.Node("command:" + operation, "script-command", operation);
            graph.Edge(command, "command-name", graph.Name(operation), "literal-dispatch-name");
            foreach (var site in sites)
            {
                var id = graph.Node("dispatch:" + dispatchSites++, "dispatch-site", operation + " / " + site.Owner,
                    new
                    {
                        site,
                        sourceSha256 = dispatch.Sources.Single(value => value.File == site.File).Sha256,
                        execution = "unverified",
                        prerequisites = "not-validated"
                    });
                graph.Edge(command, "candidate-dispatch", id, "literal-guard-only");
                graph.Edge(id, "declared-in", "opennv-file:" + site.File, "syntax");
            }
        }
        var reports = new List<object>();
        var gapRows = new List<object>();
        for (var auditIndex = 0; auditIndex < audits.Length; ++auditIndex)
        {
            var directory = Path.GetFullPath(audits[auditIndex]);
            using var summary = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "summary.json")));
            var report = summary.RootElement;
            if (report.GetProperty("schema").GetString() != "opennv-private-quest-graph-audit/v2")
                throw new InvalidDataException("Knowledge graph requires a v2 quest audit.");
            var audit = graph.Node("audit:" + auditIndex, "owned-audit", Path.GetFileName(directory), new
            {
                sourceCompatibilityId = report.GetProperty("SaveCompatibilityId").GetString(),
                runtimeBuild = report.GetProperty("runtimeBuild").GetString(),
                summarySha256 = Hash(Path.Combine(directory, "summary.json")),
                statementsSha256 = Hash(Path.Combine(directory, "statements.jsonl")),
                failuresSha256 = Hash(Path.Combine(directory, "failures.json")),
                currentRuntimeIdentity = "not-assumed-equal-to-audited-runtime",
                directory,
            });
            var operations = new Dictionary<string, Operation>(StringComparer.OrdinalIgnoreCase);
            long statements = 0, errors = 0, uninspected = 0;
            foreach (var line in File.ReadLines(Path.Combine(directory, "statements.jsonl")))
            {
                using var document = JsonDocument.Parse(line);
                var row = document.RootElement.GetProperty("statement");
                var name = row.GetProperty("Operation").GetString()!.ToLowerInvariant();
                if (!operations.TryGetValue(name, out var operation)) operations.Add(name, operation = new());
                ++operation.Count; ++statements;
                var ownership = row.GetProperty("Ownership").GetString();
                if (ownership == "unbound")
                {
                    ++errors; ++operation.Errors;
                    operation.Reasons.Add(row.GetProperty("Error").GetString()!);
                }
                else if (ownership == "statement-dispatch-not-inspected") { ++uninspected; ++operation.Uninspected; }
                else if (ownership is not ("expression-declarations-and-signatures" or "control-flow"))
                    throw new InvalidDataException("Unknown statement ownership disposition.");
            }
            if (statements != report.GetProperty("statements").GetInt64() || errors != report.GetProperty("unboundStatements").GetInt64() ||
                uninspected != report.GetProperty("uninspectedCommands").GetInt64())
                throw new InvalidDataException("Knowledge graph audit statement denominator differs.");
            foreach (var (name, operation) in operations.OrderByDescending(pair => pair.Value.Errors).ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var command = graph.Node("command:" + name, "script-command", name);
                graph.Edge(command, "command-name", graph.Name(name), "audit-operation-name");
                var observed = graph.Node(audit + "/operation:" + name, "observed-operation", name, new
                {
                    statements = operation.Count,
                    errors = operation.Errors,
                    dispatchUninspected = operation.Uninspected,
                    reasons = operation.Reasons,
                    runtimeEffects = "unverified",
                    dispatchSites = dispatch.Operations.GetValueOrDefault(name)?.Count ?? 0,
                });
                graph.Edge(audit, "observes", observed, "audit-row-aggregation");
                graph.Edge(observed, "operation", command, "exact-normalized-operation");
                if (operation.Errors > 0) gapRows.Add(new
                {
                    priority = 1,
                    audit,
                    operation = name,
                    observedFailures = operation.Errors,
                    reasons = operation.Reasons,
                    disposition = "observed-inspection-failure-runtime-effect-unverified"
                });
            }
            using var failures = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "failures.json")));
            var index = 0;
            foreach (var failure in failures.RootElement.EnumerateArray())
            {
                var id = graph.Node(audit + "/failure:" + index++, "observed-failure", failure.GetProperty("lane").GetString()!, failure.Clone());
                graph.Edge(audit, "reports-failure", id, "retained-audit");
            }
            if (index != report.GetProperty("failures").GetInt32()) throw new InvalidDataException("Audit failure denominator differs.");
            var compiled = graph.Node(audit + "/compiled", "implementation-gap", "compiled script operands and execution",
                report.TryGetProperty("compiledInstructions", out var inventory) ? inventory.Clone() :
                (object)new { framing = "not-inspected-in-this-audit", execution = "unverified" });
            graph.Edge(audit, "requires", compiled, "audit-boundary");
            graph.Edge(compiled, "candidate-family", "family:scripts", "reviewed-work-domain");
            if (report.TryGetProperty("compiledInstructions", out var decoded))
                foreach (var row in decoded.GetProperty("opcodes").EnumerateArray())
                {
                    var value = row.GetProperty("opcode").GetUInt16();
                    var opcode = graph.Node("opcode:" + value, "compiled-opcode", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    var observed = graph.Node(audit + "/opcode:" + value, "observed-compiled-opcode", "opcode " + value,
                        new
                        {
                            opcode = value,
                            instructions = row.GetProperty("count").GetInt64(),
                            operands = "not-decoded",
                            execution = "not-implemented",
                            referenceApplicability = "FNV-reference-FO3-unverified"
                        });
                    graph.Edge(audit, "observes", observed, "SCDA-framing");
                    graph.Edge(observed, "has-opcode", opcode, "exact-numeric-identity-not-cross-game-semantics");
                }
            gapRows.Add(new
            {
                priority = 0,
                audit,
                operation = "compiled-script-execution",
                observedFailures = (long?)null,
                disposition = "missing-runtime-owner-not-inferred-from-source-parser-success"
            });
            reports.Add(new { audit, statements, errors, uninspected, failures = index });
        }
        return new
        {
            dispatchSites,
            dynamicGuards = dispatch.DynamicGuards,
            reports,
            gaps = gapRows,
            priorityMeaning = "compiled-owner-first-then-observed-inspection-failures; no-name-match-is-not-proof-of-missing-behavior"
        };
    }
}
