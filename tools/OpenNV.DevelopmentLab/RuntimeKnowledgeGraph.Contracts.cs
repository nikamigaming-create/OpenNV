internal static partial class RuntimeKnowledgeGraph
{
    private sealed record Contract(string Id, string Label, string Method, string ReferenceOwner,
        string OwnerFile, string TestFile, string Boundary);

    private static readonly Contract[] ReviewedContracts =
    [
        new("compiled-framing", "SCDA instruction framing and operand decoding", "ParseOutput", "ScriptRunner",
            "runtime/src/Content/FalloutCompiledScript.cs",
            "contract-tests/FalloutPluginRuntimeProbe/CompiledScriptContracts.cs",
            "SCDA framing, block/expression/command operand decoding across 26,257 owned FNV+FO3 compiled bodies"),
        new("compiled-vm-execution", "SCDA compiled VM execution", "Run", "ScriptRunner",
            "runtime/src/Content/FalloutCompiledScriptVm.cs",
            "contract-tests/FalloutPluginRuntimeProbe/CompiledScriptContracts.cs",
            "postfix expression evaluation, branch skip counts, variable assignment and command dispatch"),
        new("compiled-expression-eval", "SCDA postfix expression evaluation", "Eval", "ExpressionEvaluator",
            "runtime/src/Content/FalloutCompiledScriptVm.cs",
            "contract-tests/FalloutPluginRuntimeProbe/CompiledScriptContracts.cs",
            "postfix stack evaluation with integer/float promotion"),
        new("effect-integer-assignment", "Effect-local integer assignment", "ParseBlock", "ScriptRunner",
            "runtime/src/Content/FalloutScriptEffectLocalStorage.cs",
            "contract-tests/FalloutPluginRuntimeProbe/ScriptIntegerCellContracts.cs",
            "finite in-range scalar writes and cold cells; overflow and event parity unverified"),
    ];

    private static void ContractReference(Graph graph, string function, string symbol)
    {
        foreach (var contract in ReviewedContracts)
            if (symbol.StartsWith("?" + contract.Method + "@" + contract.ReferenceOwner + "@@", StringComparison.Ordinal))
            {
                var id = graph.Node("contract:" + contract.Id, "reviewed-contract", contract.Label,
                    new
                    {
                        document = "docs/compiled-script-contracts.md",
                        contract.Boundary,
                        provenance = "neutral-behavior-contract-no-reference-code-import"
                    });
                graph.Edge(function, "informs-contract", id, "reviewed-static-behavior-not-independent-byte-match");
            }
    }

    private static void ContractOwners(Graph graph, string[] files)
    {
        foreach (var contract in ReviewedContracts)
        {
            if (!files.Contains(contract.OwnerFile, StringComparer.Ordinal) || !files.Contains(contract.TestFile, StringComparer.Ordinal)) continue;
            var id = graph.Node("contract:" + contract.Id, "reviewed-contract", contract.Label,
                new { document = "docs/compiled-script-contracts.md", contract.Boundary });
            graph.Edge(id, "implemented-contract-owner", "opennv-file:" + contract.OwnerFile, "reviewed-bounded-contract-only");
            graph.Edge(id, "contract-test-source", "opennv-file:" + contract.TestFile, "source-reference-not-test-result");
        }
    }
}
