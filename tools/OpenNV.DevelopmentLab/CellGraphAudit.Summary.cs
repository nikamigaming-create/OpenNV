using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAudit
{
    // Keep neutral identities/counts and failed declarations, not every full
    // CELL JsonElement or decoded asset. Detailed reports are streamed to disk.
    private sealed class ComponentSummary
    {
        private readonly IReadOnlySet<FalloutFormKey> _selected;
        private readonly string[] _expected;
        private readonly HashSet<string> _expectedSet;
        internal readonly HashSet<string> Bound = new(StringComparer.Ordinal);
        internal readonly HashSet<string> CompletedCells = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Kind, HashSet<string?> Hashes, Dictionary<string, JsonElement> Issues)> _resources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _modelReferences = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _nav = new(StringComparer.Ordinal);
        private readonly HashSet<string> _enableRoots = new(StringComparer.Ordinal);
        private readonly List<object> _packageFailures = [];
        private readonly HashSet<string> _packages = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (HashSet<string> Packages, HashSet<string> Actors, int Count)> _procedures = new(StringComparer.Ordinal);
        private readonly List<object> _snapshots = [];
        private int _references, _referencesWithFailures, _referenceFailures, _actors, _packageBindings, _packageConditions, _packageErrors, _enableEdges, _navMissingSupport;
        internal bool InspectedReportsPassed { get; private set; } = true;
        internal int PackageErrorEvents => _packageErrors;
        internal bool InvariantsPassed => _expectedSet.Count == _expected.Length && Bound.Count == _references &&
            Bound.IsSubsetOf(_expectedSet) && _resources.All(pair => pair.Value.Hashes.Count == 1) &&
            _references + _expected.Count(identity => !Bound.Contains(identity)) == _expected.Length;

        internal ComponentSummary(FalloutPluginStack records, IReadOnlySet<FalloutFormKey> selected)
        {
            _selected = selected;
            _expected = selected.SelectMany(cell => SourceReferenceIdentities(records, cell)).Order(StringComparer.Ordinal).ToArray();
            _expectedSet = _expected.ToHashSet(StringComparer.Ordinal);
        }

        internal void Observe(JsonElement cell)
        {
            CompletedCells.Add(JsonSerializer.Deserialize<FalloutFormKey>(cell.GetProperty("cell").GetProperty("formKey"), Json).ToString());
            InspectedReportsPassed &= cell.GetProperty("failures").GetArrayLength() == 0 && cell.GetProperty("navFailures").GetArrayLength() == 0;
            _navMissingSupport += cell.GetProperty("denominator").GetProperty("navSupportWithoutPackedOrBoxHit").GetInt32();
            foreach (var mesh in cell.GetProperty("navMeshes").EnumerateArray()) _nav.TryAdd(mesh.GetProperty("form").GetString()!, mesh.GetProperty("triangles").GetInt32());
            foreach (var reference in cell.GetProperty("references").EnumerateArray())
            {
                var identity = reference.GetProperty("identity").GetString()!; _references++; Bound.Add(identity);
                var failures = reference.GetProperty("failures").GetArrayLength(); _referenceFailures += failures;
                if (failures != 0) { _referencesWithFailures++; InspectedReportsPassed = false; }
                if (reference.GetProperty("model").GetString() is { } model)
                {
                    var path = Canonical(model);
                    if (!_modelReferences.TryGetValue(path, out var users)) _modelReferences.Add(path, users = new(StringComparer.Ordinal));
                    users.Add(identity);
                }
                if (reference.GetProperty("enableParent").ValueKind != JsonValueKind.Null) _enableEdges++;
                var domain = reference.GetProperty("enableDomain");
                if (domain.ValueKind != JsonValueKind.Null && domain.GetProperty("root").GetString() is { } root) _enableRoots.Add(root);
                if (reference.GetProperty("signature").GetString() is not ("NPC_" or "CREA")) continue;
                _actors++; var graph = reference.GetProperty("packages"); if (graph.ValueKind == JsonValueKind.Null) continue;
                var templateErrors = graph.GetProperty("failures"); _packageErrors += templateErrors.GetArrayLength();
                if (templateErrors.GetArrayLength() != 0) _packageFailures.Add(new { actor = identity, owner = (string?)null, package = (string?)null,
                    priority = (int?)null, sourceProcedure = (byte?)null, errors = templateErrors.Clone(), conditions = Array.Empty<JsonElement>() });
                foreach (var owner in graph.GetProperty("packageOwnerAlternatives").EnumerateArray())
                    foreach (var package in owner.GetProperty("authoredPriorityAlternatives").EnumerateArray())
                    {
                        var errors = package.GetProperty("errors"); _packageErrors += errors.GetArrayLength();
                        var key = package.GetProperty("package").GetString();
                        if (errors.GetArrayLength() != 0) _packageFailures.Add(new { actor = identity, owner = owner.GetProperty("owner").GetString(), package = key,
                            priority = package.GetProperty("priority").GetInt32(), sourceProcedure = package.GetProperty("sourceProcedure").Clone(),
                            errors = errors.Clone(), conditions = package.GetProperty("conditions").Clone() });
                        if (key is null) continue;
                        _packageBindings++; _packages.Add(key); _packageConditions += package.GetProperty("conditions").GetArrayLength();
                        var procedure = package.GetProperty("sourceProcedure").GetRawText();
                        if (!_procedures.TryGetValue(procedure, out var bindings)) bindings = (new(StringComparer.Ordinal), new(StringComparer.Ordinal), 0);
                        bindings.Packages.Add(key); bindings.Actors.Add(identity); bindings.Count++; _procedures[procedure] = bindings;
                    }
            }
            foreach (var resource in cell.GetProperty("resources").EnumerateArray())
            {
                var path = resource.GetProperty("path").GetString()!;
                if (!_resources.TryGetValue(path, out var row)) row = (resource.GetProperty("kind").GetString()!, [], new(StringComparer.Ordinal));
                row.Hashes.Add(resource.GetProperty("sha256").GetString());
                foreach (var issue in resource.GetProperty("failures").EnumerateArray()) row.Issues.TryAdd(issue.GetRawText(), issue.Clone());
                if (row.Issues.Count != 0) InspectedReportsPassed = false;
                _resources[path] = row;
            }
            if (cell.GetProperty("nativeReview").ValueKind != JsonValueKind.Null)
                _snapshots.Add(new { sourceAuditCell = cell.GetProperty("cell").GetProperty("formKey").Clone(), snapshot = cell.GetProperty("nativeReview").Clone(),
                    cellAuditRuntimeBuild = cell.GetProperty("runtimeAuditBuild").Clone(), currentBuildNativeExecutionVerified = false });
        }

        internal bool ResourceHashesMatch(IReadOnlyDictionary<string, ResourceRow> resources) => _resources.All(pair =>
            pair.Value.Hashes.Count == 1 && resources.TryGetValue(pair.Key, out var row) && pair.Value.Hashes.Contains(row.Sha256));

        internal object Build() => new
        {
            denominator = new { selectedCells = _selected.Count, completeCellReports = CompletedCells.Count, effectiveSourceReferences = _expected.Length,
                readerBoundReferences = _references, unboundSourceReferences = _expected.Count(identity => !Bound.Contains(identity)),
                referencesWithFailures = _referencesWithFailures, referenceFailureEvents = _referenceFailures, uniqueResourcePaths = _resources.Count,
                models = _resources.Count(pair => pair.Value.Kind == "model"), animations = _resources.Count(pair => pair.Value.Kind == "animation"),
                textures = _resources.Count(pair => pair.Value.Kind == "texture"), uniqueFailedResources = _resources.Count(pair => pair.Value.Issues.Count != 0),
                actors = _actors, packageBindings = _packageBindings, uniquePackages = _packages.Count, packageConditionBindings = _packageConditions,
                packageErrorEvents = _packageErrors, enableParentEdges = _enableEdges, distinctEnableRoots = _enableRoots.Count,
                navMeshes = _nav.Count, navTriangles = _nav.Values.Sum(), navSupportWithoutPackedOrBoxHit = _navMissingSupport },
            invariants = new { sourceReferenceIdentityUnique = _expectedSet.Count == _expected.Length,
                reportedReferenceIdentityUnique = Bound.Count == _references, everyReportedReferenceIsInSelectedSource = Bound.IsSubsetOf(_expectedSet),
                resourceHashConsistency = _resources.All(pair => pair.Value.Hashes.Count == 1),
                everySourceReferenceIsBoundOrExplicitlyUnbound = _references + _expected.Count(identity => !Bound.Contains(identity)) == _expected.Length },
            unboundSourceReferenceIdentities = _expected.Where(identity => !Bound.Contains(identity)).ToArray(),
            enableRoots = _enableRoots.Order(StringComparer.Ordinal),
            resourceFailures = _resources.Where(pair => pair.Value.Issues.Count != 0).Select(pair => new { path = pair.Key, kind = pair.Value.Kind,
                hashes = pair.Value.Hashes, references = _modelReferences.GetValueOrDefault(pair.Key) ?? [], issues = pair.Value.Issues.Values }),
            packageFailures = _packageFailures,
            authoredProcedureBindings = _procedures.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { procedure = pair.Key,
                packages = pair.Value.Packages.Order(StringComparer.Ordinal), actors = pair.Value.Actors.Order(StringComparer.Ordinal), bindings = pair.Value.Count }),
            mathematicalScope = "Source packed-triangle and box projections only; native/filter contacts, other hulls and ordinary traversal remain unverified.",
            appearanceDomain = "Every authored alternative declaration is retained separately; selected appearances, state combinations and script-created resources keep independent owners.",
            nativeSnapshots = _snapshots
        };
    }
}
