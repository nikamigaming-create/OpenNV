using System.Text.Json;
using System.Text.RegularExpressions;

internal static partial class RuntimeKnowledgeGraph
{
    private static object Reference(Graph graph, string root, string[] files, string revision)
    {
        var known = files.ToHashSet(StringComparer.Ordinal);
        foreach (var relative in files.Order(StringComparer.Ordinal))
        {
            var path = InputPath(root, relative);
            var file = graph.Node("reference-file:" + relative, "reference-file", relative,
                new { path = relative, sha256 = Hash(path), bytes = new FileInfo(path).Length });
            graph.Edge(revision, "contains", file, "git-file-inventory"); Classify(graph, file, relative);
        }
        var manifestPath = InputPath(root, "build/units.json");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var data = manifest.RootElement;
        long functions = 0, bytes = 0, tails = 0, tailBytes = 0, aliases = 0;
        var unitIndex = 0;
        var uniqueAddresses = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in data.GetProperty("units").EnumerateArray())
        {
            var source = entry.GetProperty("source").GetString()!;
            if (!known.Contains(source)) throw new InvalidDataException("Manifest unit has no source file: " + source);
            var unit = graph.Node("reference-unit:" + unitIndex++, "reference-unit", source,
                new { source, compiler = entry.GetProperty("compiler").GetString() });
            graph.Edge("reference-file:" + source, "builds", unit, "manifest");
            var unitAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var function in entry.GetProperty("functions").EnumerateArray())
            {
                var address = function.GetProperty("address").GetString()!;
                if (!unitAddresses.Add(address)) throw new InvalidDataException("Reference unit repeats a function address.");
                var symbol = function.GetProperty("symbol").GetString()!;
                var size = function.GetProperty("size").GetInt64();
                var id = graph.Node(unit + "/function:" + address, "reference-function", symbol,
                    new { address, symbol, bytes = size, source, mapping = "unverified" });
                graph.Edge(unit, "declares-function", id, "manifest");
                ContractReference(graph, id, symbol);
                var name = SymbolName(symbol);
                if (name is not null) graph.Edge(id, "declared-name", graph.Name(name), "symbol-spelling-not-semantics");
                ++functions; bytes += size;
                if (uniqueAddresses.TryGetValue(address, out var priorSize) && priorSize != size)
                    throw new InvalidDataException("Reference function address has conflicting extents.");
                uniqueAddresses[address] = size;
                if (function.TryGetProperty("fold_aliases", out var folded))
                    foreach (var alias in folded.EnumerateArray())
                    {
                        var aliasNode = graph.Node(id + "/alias:" + aliases++, "reference-fold-alias", alias.ToString());
                        graph.Edge(id, "declared-fold-alias", aliasNode, "manifest-not-independently-matched");
                    }
            }
            if (!entry.TryGetProperty("eh_tails", out var tailEntries)) continue;
            foreach (var tail in tailEntries.EnumerateArray())
            {
                var id = graph.Node(unit + "/tail:" + tails++, "reference-eh-tail", tail.GetProperty("address").GetString()!,
                    tail.Clone());
                graph.Edge(unit, "declares-eh-tail", id, "manifest");
                tailBytes += tail.GetProperty("size").GetInt64();
            }
        }
        var totals = data.GetProperty("totals");
        foreach (var (name, actual) in new[] { ("units", (long)unitIndex), ("functions", (long)uniqueAddresses.Count), ("bytes", uniqueAddresses.Values.Sum()),
                     ("eh_tails", tails), ("eh_tail_bytes", tailBytes) })
            if (totals.GetProperty(name).GetInt64() != actual)
                throw new InvalidDataException("Reference manifest denominator mismatch: " + name);
        var opcodeDeclarations = 0;
        const string opcodeHeader = "include/SCRIPT_OUTPUT.h";
        if (known.Contains(opcodeHeader))
        {
            foreach (var line in File.ReadLines(InputPath(root, opcodeHeader)))
            {
                var match = Regex.Match(line, @"^\s*(SCRIPT_OUTPUT_(?:FUNCTION|FLOW)_[A-Z0-9_]+)\s*=\s*(\d+)\s*,");
                if (!match.Success || match.Groups[1].Value.EndsWith("_COUNT", StringComparison.Ordinal)) continue;
                var label = match.Groups[1].Value;
                var value = ushort.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                var opcode = graph.Node("opcode:" + value, "compiled-opcode", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var declaration = graph.Node("reference-opcode:" + label, "reference-opcode-declaration", label,
                    new { value, source = opcodeHeader, applicability = "FNV-reference-only-FO3-unverified" });
                graph.Edge("reference-file:" + opcodeHeader, "declares", declaration, "enum-metadata");
                graph.Edge(declaration, "declares-opcode-value", opcode, "enum-metadata");
                var command = label[(label.StartsWith("SCRIPT_OUTPUT_FUNCTION_", StringComparison.Ordinal) ? 23 : 19)..]
                    .Replace("_", "", StringComparison.Ordinal);
                graph.Edge(declaration, "candidate-command-name", graph.Name(command), "normalized-enum-name-not-command-binding");
                ++opcodeDeclarations;
            }
        }
        return new
        {
            files = files.Length,
            units = unitIndex,
            functionEntries = functions,
            uniqueFunctionAddresses = uniqueAddresses.Count,
            entryBytes = bytes,
            uniqueFunctionBytes = uniqueAddresses.Values.Sum(),
            ehTails = tails,
            ehTailBytes = tailBytes,
            foldedAliases = aliases,
            opcodeDeclarations,
            denominator = "manifest-functions-and-EH-tails-kept-separate",
            byteMatch = "not-run"
        };
    }

    private static string? SymbolName(string symbol)
    {
        // Constructors/operators and unnamed functions remain inventoried with
        // no invented correspondence. MSVC decoration is not a C# signature.
        var match = Regex.Match(symbol, @"^\?([A-Za-z_][A-Za-z0-9_]*)@");
        if (!match.Success) return null;
        var name = match.Groups[1].Value;
        if (name.StartsWith("Cmd_", StringComparison.Ordinal) && name.EndsWith("_Execute", StringComparison.Ordinal))
            name = name[4..^8];
        return name;
    }
}
