using System.Text.Json;

internal static class RuntimeKnowledgeGraphContracts
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "opennv-knowledge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var reference = Path.Combine(root, "reference");
            var repository = Path.Combine(root, "ours");
            var files = new[]
            {
                "runtime/src/World/Cells/FalloutReferenceScripts.cs",
                "runtime/src/Content/FalloutQuestScripts.cs",
                "runtime/src/Campaigns/NewVegas/Opening/RuntimeNativeOpeningStageDriver.Commands.cs",
            };
            for (var index = 0; index < files.Length; ++index)
            {
                var path = Path.Combine(repository, files[index]);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, index < 2
                    ? "class Owner" + index + " { void Run() { void Call() { if (operation == \"example\") throw new NotSupportedException(\"missing host\"); } } }"
                    : "class Driver { void ApplyNativeSourceCommand() { if (operation == \"example\") return; } }");
            }
            Directory.CreateDirectory(Path.Combine(reference, "build"));
            Directory.CreateDirectory(Path.Combine(reference, "include"));
            File.WriteAllText(Path.Combine(reference, "script.cpp"), "// synthetic reference metadata fixture");
            File.WriteAllText(Path.Combine(reference, "include/SCRIPT_OUTPUT.h"),
                "SCRIPT_OUTPUT_FUNCTION_EXAMPLE = 4097,\nSCRIPT_OUTPUT_FLOW_RETURN = 30,\nSCRIPT_OUTPUT_FLOW_COUNT = 16,\n");
            var function = new { address = "0x1000", symbol = "?Example@Synthetic@@", size = 4 };
            File.WriteAllText(Path.Combine(reference, "build/units.json"), JsonSerializer.Serialize(new
            {
                totals = new { units = 2, functions = 1, bytes = 4, eh_tails = 1, eh_tail_bytes = 2 },
                units = new object[]
                {
                    new { source = "script.cpp", compiler = "fixture", functions = new[] { function },
                        eh_tails = new[] { new { address = "0x2000", size = 2, parent = "0x1000" } } },
                    new { source = "script.cpp", compiler = "variant", functions = new[] { function } },
                },
            }));
            var output = Path.Combine(root, "graph");
            RuntimeKnowledgeGraph.Build(reference, repository, output, [], ["build/units.json", "script.cpp", "include/SCRIPT_OUTPUT.h"], files, "fixture", "fixture");
            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "summary.json")));
            var inventory = summary.RootElement.GetProperty("referenceInventory");
            Require(inventory.GetProperty("functionEntries").GetInt32() == 2 &&
                inventory.GetProperty("uniqueFunctionAddresses").GetInt32() == 1 && inventory.GetProperty("ehTails").GetInt32() == 1,
                "Build variants or EH tails were collapsed into a false function count.");
            Require(inventory.GetProperty("opcodeDeclarations").GetInt32() == 2,
                "Opcode declarations were omitted or enum counts were admitted as commands.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(Path.Combine(output, "nodes.jsonl")))
            {
                using var row = JsonDocument.Parse(line);
                Require(ids.Add(row.RootElement.GetProperty("id").GetString()!), "Graph node IDs collide.");
            }
            foreach (var line in File.ReadLines(Path.Combine(output, "edges.jsonl")))
            {
                using var row = JsonDocument.Parse(line);
                Require(ids.Contains(row.RootElement.GetProperty("from").GetString()!) &&
                    ids.Contains(row.RootElement.GetProperty("to").GetString()!) &&
                    !row.RootElement.GetProperty("relation").GetString()!.Contains("implemented", StringComparison.Ordinal),
                    "Graph lost an endpoint or upgraded a source name into implementation evidence.");
            }
            Require(summary.RootElement.GetProperty("ownInventory").GetProperty("unsupportedGuards").GetInt32() == 2,
                "Unreached unsupported guards disappeared.");
            File.AppendAllText(Path.Combine(output, "edges.jsonl"), "{}\n");
            var changedGraphRefused = false;
            try { RuntimeKnowledgeGraph.Query(output, "example"); }
            catch (InvalidDataException) { changedGraphRefused = true; }
            Require(changedGraphRefused, "A modified graph was admitted by the query.");
            try { RuntimeKnowledgeGraph.Build(reference, repository, output, [], [], [], "fixture", "fixture"); }
            catch (IOException) { Console.WriteLine("Runtime knowledge graph contracts passed."); return; }
            throw new InvalidOperationException("An existing graph was overwritten.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Require(bool value, string error)
    {
        if (!value) throw new InvalidOperationException(error);
    }
}
