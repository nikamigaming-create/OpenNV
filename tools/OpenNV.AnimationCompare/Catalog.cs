using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNV.Runtime.Content;

namespace OpenNV.AnimationCompare;

internal static partial class Catalog
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
    internal static CatalogManifest Build(FalloutPluginStack records, RuntimeLiveContentSource content,
        string outputDirectory, Action<string>? progress = null)
    {
        var directory = Path.GetFullPath(outputDirectory);
        foreach (var root in content.ContentRoots)
        {
            var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if ((directory + Path.DirectorySeparatorChar).StartsWith(full, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Catalog output must be outside owned content roots.");
        }
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new IOException("Catalog output must be a fresh directory; existing evidence is never overwritten.");
        Directory.CreateDirectory(directory);
        var owner = new Builder(records, content, directory, progress);
        return owner.Build();
    }

    private static string Canonical(string path) => path.Replace('\\', '/').ToLowerInvariant();
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));
    private static CatalogFailure Failure(string lane, string identity, Exception error, string? domain = null) =>
        new(lane, identity, error.GetType().Name, error.Message, domain ?? (lane switch
        {
            "actor-rig" => "actor-template-or-source-binding",
            "record" => "winning-record",
            "resource" => "resource-container",
            "weapon-presentation" => "weapon-source-presentation",
            "weapon-modifications" => "weapon-source-modifications",
            _ => lane,
        }));
    private static bool SourceError(Exception error) => error is not (OutOfMemoryException or StackOverflowException or OperationCanceledException);
    private static void Row<T>(TextWriter writer, T value) => writer.WriteLine(JsonSerializer.Serialize(value, Json));

    private sealed partial class Builder(FalloutPluginStack records, RuntimeLiveContentSource content,
        string directory, Action<string>? progress)
    {
        private readonly Dictionary<string, CatalogResource> _resources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<CatalogReference>> _references = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CatalogRig> _rigs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _rigForms = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<CatalogRigVariant>> _rigVariants = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CatalogReferenceSet> _referenceSets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CatalogRigSet> _rigSets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CatalogSourceOwnerSet> _sourceOwnerSets = new(StringComparer.Ordinal);
        private readonly Dictionary<IReadOnlyList<CatalogReference>, string> _referenceSetCache = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, CatalogReference[]> _rigReferences = new(StringComparer.Ordinal);
        private readonly List<CatalogFailure> _failures = [];
        private long _recordCount, _deletedCount, _caseCount;
        private bool _discoveryComplete = true;
        private readonly Dictionary<string, string> _files = new()
        {
            ["records"] = "catalog-records.jsonl",
            ["resources"] = "catalog-resources.jsonl",
            ["rigs"] = "catalog-rigs.jsonl",
            ["cases"] = "catalog-cases.jsonl",
            ["failures"] = "catalog-failures.jsonl",
            ["referenceSets"] = "catalog-reference-sets.jsonl",
            ["rigSets"] = "catalog-rig-sets.jsonl",
            ["sourceOwnerSets"] = "catalog-source-owner-sets.jsonl",
            ["weaponVariants"] = "catalog-weapon-variants.jsonl",
            ["failureGroups"] = "catalog-failure-groups.jsonl",
        };
        private StreamWriter Writer(string role) => new(Path.Combine(directory, _files[role]), append: false);
        private void State(string state) => File.WriteAllText(Path.Combine(directory, "catalog.json"),
            JsonSerializer.Serialize(Manifest(state), new JsonSerializerOptions(Json) { WriteIndented = true }));
        private CatalogManifest Manifest(string state) => new("opennv-animation-catalog/v2", directory,
            content.Game, content.SaveCompatibilityId, state, _recordCount, _deletedCount, _resources.Count,
            _rigs.Count, _caseCount, _failures.Count, _files, Plugins: records.Plugins.Select(plugin =>
                new CatalogPlugin(plugin.Plugin.Name, plugin.Plugin.Path, plugin.Sha256, plugin.Bytes)).ToArray(),
            SourceStackHash: OpenNvRigEngineControl.SourceStackHash(records.Plugins.Select(plugin => new OpenNvRigPlugin(plugin.Plugin.Name, plugin.Sha256)).ToArray()),
            CatalogContentHash: _discoveryComplete && _resources.Count != 0 && _resources.Values.All(resource => resource.Sha256 is not null)
                ? Hash(JsonSerializer.Serialize(new
                {
                    plugins = records.Plugins.Select(plugin => new { plugin.Plugin.Name, plugin.Sha256 }),
                    content.Settings,
                    resources = _resources.Values.OrderBy(resource => resource.Path, StringComparer.Ordinal)
                        .Select(resource => new { resource.Path, resource.Sha256 })
                }, Json)) : null);

        internal CatalogManifest Build()
        {
            State("incomplete");
            try
            {
                ReadRecords();
                progress?.Invoke("Reading every winning NIF/KF resource and controller declaration.");
                foreach (var path in DiscoverResources())
                    ReadResource(path);
                ReadActorRigs();
                using (var rows = Writer("rigs"))
                    foreach (var (id, rig) in _rigs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                        Row(rows, rig with { SourceForms = _rigForms[id].Order(StringComparer.Ordinal).ToArray(), Variants = _rigVariants[id] });
                WriteCases();
                using (var rows = Writer("referenceSets"))
                    foreach (var row in _referenceSets.Values.OrderBy(row => row.Id, StringComparer.Ordinal)) Row(rows, row);
                using (var rows = Writer("rigSets"))
                    foreach (var row in _rigSets.Values.OrderBy(row => row.Id, StringComparer.Ordinal)) Row(rows, row);
                using (var rows = Writer("sourceOwnerSets"))
                    foreach (var row in _sourceOwnerSets.Values.OrderBy(row => row.Id, StringComparer.Ordinal)) Row(rows, row);
                using (var rows = Writer("resources"))
                    foreach (var row in _resources.Values.OrderBy(row => row.Path, StringComparer.Ordinal)) Row(rows, row);
                using (var rows = Writer("failures")) foreach (var failure in _failures) Row(rows, failure);
                using (var rows = Writer("failureGroups"))
                    foreach (var group in _failures.GroupBy(failure => (failure.Lane, failure.Domain, failure.ErrorType)))
                        Row(rows, new CatalogFailureGroup(group.Key.Lane, group.Key.Domain, group.Key.ErrorType, group.LongCount(),
                            group.Select(failure => failure.Identity).Distinct().Take(8).ToArray(), group.Select(failure => failure.Reason).Distinct().Take(8).ToArray()));
                var status = !_discoveryComplete ? "incomplete-resource-denominator; discovery failures" :
                    _failures.Count == 0 ? "enumeration-complete; runtime coverage pending" : "enumeration-complete-with-explicit-failures";
                State(status);
                return Manifest(status);
            }
            catch (Exception error) when (SourceError(error))
            {
                _failures.Add(Failure("catalog-fatal", directory, error));
                using var rows = Writer("failures"); foreach (var failure in _failures) Row(rows, failure);
                State("incomplete; fatal catalog failure");
                throw;
            }
        }
    }
}
