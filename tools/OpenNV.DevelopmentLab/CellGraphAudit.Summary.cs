using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAudit
{
    private static object SummarizeComponent(FalloutPluginStack records, IReadOnlySet<FalloutFormKey> selected,
        IReadOnlyList<JsonElement> completed)
    {
        var expected = selected.SelectMany(cell => SourceReferenceIdentities(records, cell)).Order(StringComparer.Ordinal).ToArray();
        var references = completed.SelectMany(cell => cell.GetProperty("references").EnumerateArray()).ToArray();
        var bound = references.Select(reference => reference.GetProperty("identity").GetString()!).ToHashSet(StringComparer.Ordinal);
        var resources = completed.SelectMany(cell => cell.GetProperty("resources").EnumerateArray())
            .GroupBy(resource => resource.GetProperty("path").GetString()!, StringComparer.OrdinalIgnoreCase).ToArray();
        var nav = completed.SelectMany(cell => cell.GetProperty("navMeshes").EnumerateArray())
            .DistinctBy(mesh => mesh.GetProperty("form").GetString(), StringComparer.Ordinal).ToArray();
        var resourceFailures = resources.Where(group => group.Any(resource => resource.GetProperty("failures").GetArrayLength() != 0))
            .Select(group => new { path = group.Key, kind = group.First().GetProperty("kind").GetString(),
                hashes = group.Select(resource => resource.GetProperty("sha256").GetString()).Distinct().ToArray(),
                references = references.Where(reference => reference.GetProperty("model").GetString() is { } model &&
                    Canonical(model).Equals(group.Key, StringComparison.OrdinalIgnoreCase))
                    .Select(reference => reference.GetProperty("identity").GetString()).Order(StringComparer.Ordinal).ToArray(),
                issues = group.SelectMany(resource => resource.GetProperty("failures").EnumerateArray())
                    .DistinctBy(issue => issue.GetRawText(), StringComparer.Ordinal).Select(issue => issue.Clone()).ToArray() }).ToArray();
        var actors = references.Where(reference => reference.GetProperty("signature").GetString() is "NPC_" or "CREA").ToArray();
        var packages = new List<object>();
        var packageErrors = 0;
        foreach (var actor in actors)
        {
            var graph = actor.GetProperty("packages");
            if (graph.ValueKind == JsonValueKind.Null) continue;
            var templateErrors = graph.GetProperty("failures");
            packageErrors += templateErrors.GetArrayLength();
            if (templateErrors.GetArrayLength() != 0)
                packages.Add(new { actor = actor.GetProperty("identity").GetString(), owner = (string?)null,
                    package = (string?)null, priority = (int?)null, sourceProcedure = (byte?)null,
                    errors = templateErrors.Clone(), conditions = Array.Empty<JsonElement>() });
            foreach (var owner in graph.GetProperty("packageOwnerAlternatives").EnumerateArray())
                foreach (var package in owner.GetProperty("authoredPriorityAlternatives").EnumerateArray())
                {
                    var errors = package.GetProperty("errors"); packageErrors += errors.GetArrayLength();
                    packages.Add(new { actor = actor.GetProperty("identity").GetString(), owner = owner.GetProperty("owner").GetString(),
                        package = package.GetProperty("package").GetString(), priority = (int?)package.GetProperty("priority").GetInt32(),
                        sourceProcedure = package.GetProperty("sourceProcedure").ValueKind == JsonValueKind.Null ? null : (byte?)package.GetProperty("sourceProcedure").GetByte(),
                        errors = errors.Clone(), conditions = package.GetProperty("conditions").EnumerateArray().Select(condition => condition.Clone()).ToArray() });
                }
        }
        var bindings = packages.Select(package => JsonSerializer.SerializeToElement(package)).ToArray();
        var sourcePackages = bindings.Where(package => package.GetProperty("package").ValueKind != JsonValueKind.Null).ToArray();
        var roots = references.Select(reference => reference.GetProperty("enableDomain"))
            .Where(domain => domain.ValueKind != JsonValueKind.Null)
            .Select(domain => domain.GetProperty("root").GetString()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var resourceHashConsistency = resources.All(group => group.Select(resource => resource.GetProperty("sha256").GetString()).Distinct().Count() == 1);
        return new
        {
            denominator = new
            {
                selectedCells = selected.Count, completeCellReports = completed.Count, effectiveSourceReferences = expected.Length,
                readerBoundReferences = references.Length, unboundSourceReferences = expected.Count(identity => !bound.Contains(identity)),
                referencesWithFailures = references.Count(reference => reference.GetProperty("failures").GetArrayLength() != 0),
                referenceFailureEvents = references.Sum(reference => reference.GetProperty("failures").GetArrayLength()),
                uniqueResourcePaths = resources.Length,
                models = resources.Count(group => group.First().GetProperty("kind").GetString() == "model"),
                animations = resources.Count(group => group.First().GetProperty("kind").GetString() == "animation"),
                textures = resources.Count(group => group.First().GetProperty("kind").GetString() == "texture"),
                uniqueFailedResources = resourceFailures.Length, actors = actors.Length,
                packageBindings = sourcePackages.Length, uniquePackages = sourcePackages.Select(package => package.GetProperty("package").GetString()).Distinct().Count(),
                packageConditionBindings = sourcePackages.Sum(package => package.GetProperty("conditions").GetArrayLength()),
                packageErrorEvents = packageErrors, enableParentEdges = references.Count(reference => reference.GetProperty("enableParent").ValueKind != JsonValueKind.Null),
                distinctEnableRoots = roots.Length, navMeshes = nav.Length,
                navTriangles = nav.Sum(mesh => mesh.GetProperty("triangles").GetInt32()),
                navSupportWithoutPackedOrBoxHit = completed.Sum(cell => cell.GetProperty("denominator").GetProperty("navSupportWithoutPackedOrBoxHit").GetInt32())
            },
            invariants = new
            {
                sourceReferenceIdentityUnique = expected.Distinct(StringComparer.Ordinal).Count() == expected.Length,
                reportedReferenceIdentityUnique = bound.Count == references.Length,
                everyReportedReferenceIsInSelectedSource = bound.IsSubsetOf(expected), resourceHashConsistency,
                everySourceReferenceIsBoundOrExplicitlyUnbound = references.Length + expected.Count(identity => !bound.Contains(identity)) == expected.Length
            },
            unboundSourceReferenceIdentities = expected.Where(identity => !bound.Contains(identity)).ToArray(),
            enableRoots = roots, resourceFailures,
            packageFailures = bindings.Where(package => package.GetProperty("errors").GetArrayLength() != 0),
            authoredProcedureBindings = sourcePackages.GroupBy(package => package.GetProperty("sourceProcedure").GetRawText())
                .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new { procedure = group.Key,
                    packages = group.Select(package => package.GetProperty("package").GetString()).Distinct().Order(StringComparer.Ordinal),
                    actors = group.Select(package => package.GetProperty("actor").GetString()).Distinct().Order(StringComparer.Ordinal), bindings = group.Count() }),
            mathematicalScope = "Source packed-triangle and box projections only. A missing sample is not a floor-hole proof; filters, convex/sphere/capsule contacts and live Jolt publication remain unverified.",
            appearanceDomain = "Selected saved or diagnostic actor appearance and resource graphs; arbitrary script/outfit/leveled-template alternatives are not exhausted.",
            nativeSnapshots = completed.Where(cell => cell.GetProperty("nativeReview").ValueKind != JsonValueKind.Null)
                .Select(cell => new { sourceAuditCell = cell.GetProperty("cell").GetProperty("formKey").Clone(),
                    snapshot = cell.GetProperty("nativeReview").Clone(), cellAuditRuntimeBuild = cell.GetProperty("runtimeAuditBuild").Clone(),
                    currentBuildNativeExecutionVerified = false })
        };
    }
}
