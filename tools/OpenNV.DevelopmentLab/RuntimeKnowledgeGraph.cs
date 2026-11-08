using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

// Private evidence graph. Neither names nor membership in a behavior family
// establish implementation, execution, compatibility, or retail parity.
internal static partial class RuntimeKnowledgeGraph
{
    internal sealed class Graph(string directory) : IDisposable
    {
        private readonly StreamWriter _nodes = new(Path.Combine(directory, "nodes.jsonl"));
        private readonly StreamWriter _edges = new(Path.Combine(directory, "edges.jsonl"));
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
        internal Dictionary<string, long> Kinds { get; } = new(StringComparer.Ordinal);
        internal long Edges { get; private set; }
        internal int Nodes => _ids.Count;

        internal string Node(string id, string kind, string label, object? evidence = null)
        {
            if (_ids.Add(id))
            {
                Kinds[kind] = Kinds.GetValueOrDefault(kind) + 1;
                _nodes.WriteLine(JsonSerializer.Serialize(new { id, kind, label, evidence }));
            }
            return id;
        }

        internal void Edge(string from, string relation, string to, string confidence)
        {
            if (!_ids.Contains(from) || !_ids.Contains(to)) throw new InvalidDataException("Graph edge has an absent endpoint.");
            ++Edges;
            _edges.WriteLine(JsonSerializer.Serialize(new { from, relation, to, confidence }));
        }

        internal string Name(string name) => Node("name:" + name.ToLowerInvariant(), "name", name.ToLowerInvariant());
        public void Dispose() { _nodes.Dispose(); _edges.Dispose(); }
    }

    internal static int Run(string reference, string output, string[] audits)
    {
        var repository = Directory.GetCurrentDirectory();
        reference = Path.GetFullPath(reference); output = Path.GetFullPath(output);
        var local = Path.GetFullPath(Path.Combine(repository, "local")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(local, StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(reference + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            audits.Any(audit => output.StartsWith(Path.GetFullPath(audit) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Knowledge graph output must be a fresh private local directory outside its inputs.");
        var referenceRevision = Git(reference, "rev-parse", "HEAD").Trim();
        var revision = Git(repository, "rev-parse", "HEAD").Trim();
        var referenceFiles = Git(reference, "ls-files", "-z").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var files = Git(repository, "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--",
            "runtime", "tools", "contract-tests", "docs/implementation-plan.md")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
        Build(reference, repository, output, audits, referenceFiles, files, referenceRevision, revision);
        Console.WriteLine(File.ReadAllText(Path.Combine(output, "summary.json")));
        return 0;
    }

    internal static void Build(string reference, string repository, string output, string[] audits,
        string[] referenceFiles, string[] files, string referenceRevision, string revision)
    {
        if (Directory.Exists(output)) throw new IOException("Use a fresh knowledge graph output directory.");
        Directory.CreateDirectory(output);
        using var graph = new Graph(output);
        var referenceRoot = graph.Node("reference", "revision", "FNV research reference", new
        {
            revision = referenceRevision,
            matching = "upstream-claim-not-independently-verified",
            executable = "FNV 1.4.0.525 only"
        });
        var ownRoot = graph.Node("opennv", "revision", "OpenNV working source", new
        {
            revision,
            identity = "per-file-hashes-include-working-changes",
            execution = "not-inferred-from-source"
        });
        foreach (var family in Families) graph.Node("family:" + family.Name, "behavior-family", family.Name);
        graph.Node("family:unclassified", "behavior-family", "unclassified");
        var referenceInventory = Reference(graph, reference, referenceFiles, referenceRoot);
        var ownInventory = OpenNV(graph, repository, files, ownRoot);
        ContractOwners(graph, files);
        var auditInventory = Audits(graph, repository, audits);
        // The summary is a completion marker; close writers before hashing.
        graph.Dispose();
        var summary = new
        {
            schema = "opennv-private-runtime-knowledge-graph/v1",
            referenceRevision,
            opennvRevision = revision,
            referenceInventory,
            ownInventory,
            auditInventory,
            nodes = graph.Nodes,
            edges = graph.Edges,
            kinds = graph.Kinds,
            nodesSha256 = Hash(Path.Combine(output, "nodes.jsonl")),
            edgesSha256 = Hash(Path.Combine(output, "edges.jsonl")),
            confidence = new
            {
                manifestIdentity = "declared-not-byte-match-verified",
                sourceSyntax = "observed",
                familyAndNameMappings = "candidate",
                testMentions = "not-test-results",
                parity = "unverified"
            },
            boundary = "All reference files and manifest function entries are retained, including unmatched names. " +
                "C# declarations, name references, dispatch sites, unsupported guards and audit failures are independent lanes. " +
                "A shared name or family is a search lead, never an implemented-by edge. Unknown mappings stay unknown. " +
                "Private graph includes source metadata and owned audit identities; do not publish it."
        };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start Git inventory.");
        var errors = process.StandardError.ReadToEndAsync();
        var result = process.StandardOutput.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Git inventory failed: " + errors.GetAwaiter().GetResult());
        return result;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string InputPath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidDataException("Graph input is missing or outside its source root: " + relative);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Graph inputs cannot be symbolic links: " + relative);
        return path;
    }

    internal static int Query(string directory, string term)
    {
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "summary.json")));
        var manifest = summary.RootElement;
        if (manifest.GetProperty("schema").GetString() != "opennv-private-runtime-knowledge-graph/v1" ||
            manifest.GetProperty("nodesSha256").GetString() != Hash(Path.Combine(directory, "nodes.jsonl")) ||
            manifest.GetProperty("edgesSha256").GetString() != Hash(Path.Combine(directory, "edges.jsonl")))
            throw new InvalidDataException("Knowledge graph is incomplete or changed after generation.");
        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(directory, "nodes.jsonl")))
        {
            using var document = JsonDocument.Parse(line);
            var row = document.RootElement;
            if (!(row.GetProperty("id").GetString()!.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                  row.GetProperty("label").GetString()!.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
            selected.Add(row.GetProperty("id").GetString()!); Console.WriteLine(line);
        }
        foreach (var line in File.ReadLines(Path.Combine(directory, "edges.jsonl")))
        {
            using var document = JsonDocument.Parse(line);
            var row = document.RootElement;
            if (selected.Contains(row.GetProperty("from").GetString()!) || selected.Contains(row.GetProperty("to").GetString()!))
                Console.WriteLine(line);
        }
        return 0;
    }
}
