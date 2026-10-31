using OpenNV.Runtime.Content;

namespace OpenNV.AnimationCompare;

internal static partial class Catalog
{
    private sealed partial class Builder
    {
        private static readonly string[] AnimationObservables = ["source-identity-and-hash", "controller-admission-and-phase",
            "complete-local-and-world-transform-cohort", "root-accumulation", "skin-deformation", "text-key-event-order",
            "animation-audio", "visibility-material-uv-morph", "collision-and-hinge-state", "final-pixels", "frame-time"];

        private void WriteCases()
        {
            progress?.Invoke("Writing all clip/controller cases, rig joins and weapon perspective/action coverage.");
            using var rows = Writer("cases");
            foreach (var resource in _resources.Values.OrderBy(resource => resource.Path, StringComparer.Ordinal).ToArray())
            {
                var owners = _modelOwners.GetValueOrDefault(resource.Path) ?? [];
                var owner = owners.OrderByDescending(candidate => References(candidate.FormKey.ToString()).Count > 0).FirstOrDefault();
                var ownerSet = owners.Count == 0 ? null : OwnerSet(owners);
                var ownerReferences = owners.SelectMany(candidate => References(candidate.FormKey.ToString()))
                    .DistinctBy(reference => reference.Form).OrderBy(reference => reference.RuntimeFormId).ToArray();
                if (resource.Sequences.Count == 0 && resource.Controllers.Count == 0 && resource.Failures.Count == 0)
                {
                    // Absence of explicit controllers does not establish absence
                    // of shader-driven wind, procedural motion or runtime owners.
                    if (resource.Kind == "external-animation")
                        Emit(rows, "animation-resource", null, null, resource, null, "missing-sequence", "unspecified",
                            "external-clip", ["KF has no decoded controller sequence."], []);
                    else
                        Emit(rows, "model-animation-classification", owner, null, resource, null, "procedural-animation-admission", "embedded",
                            "no-explicit-controller", ["No explicit NIF controller; shader/procedural/runtime animation classification remains unbound."], ownerReferences,
                            sourceOwnerSetId: ownerSet);
                    continue;
                }
                if (resource.Failures.Count != 0)
                    Emit(rows, "resource-decode", null, null, resource, null, "decode", "unspecified", "source-resource",
                        resource.Failures.Select(failure => failure.Reason).ToArray(), []);
                foreach (var sequence in resource.Sequences)
                {
                    // Always retain the resource-level denominator, even when no
                    // winning record or compatible scene rig is known to use it.
                    Emit(rows, "animation-resource", null, null, resource, sequence, sequence.Name, "unspecified", "external-or-embedded-clip",
                        ["Record/action/rig runtime admission is unbound at resource scope."], []);
                    if (owner is not null)
                        Emit(rows, "animated-object", owner, null, resource, sequence, sequence.Name, "embedded",
                            "shared-source-model-sequence; per-record state separate", [], ownerReferences, sourceOwnerSetId: ownerSet);
                }
                foreach (var controller in resource.Controllers)
                {
                    if (owners.Count == 0)
                        Emit(rows, "embedded-controller", null, null, resource, null, $"{controller.Type}#{controller.Block}", "embedded",
                            "time-controller", ["Owning record/runtime instance is unbound."], []);
                    if (owner is not null)
                        Emit(rows, "animated-object", owner, null, resource, null, $"{controller.Type}#{controller.Block}", "embedded",
                            "shared-source-time-controller; per-record state separate", controller.Time is null ? ["Controller time/target binding is unbound."] : [],
                            ownerReferences, sourceOwnerSetId: ownerSet);
                }
            }
            foreach (var (form, failure) in _actorFailures)
                Emit(rows, "actor-rig", _actorRecords[form], null, null, null, form, "third-person", "unresolved-actor",
                    [failure.Reason], References(form));
            foreach (var rig in _rigs.Values)
            {
                var references = RigReferences(rig);
                var sourceOwner = _rigForms[rig.Id].OrderByDescending(form => References(form).Count > 0)
                    .Select(form => _actorRecords.GetValueOrDefault(form)).FirstOrDefault(owner => owner is not null);
                Emit(rows, "skin-binding", sourceOwner, rig, ReadResource(rig.SkeletonPath), null,
                    "bind-rest-and-active-deformation-owner", rig.Family == "first-person" ? "first-person" : "third-person",
                    "all-skin-variants-retained; no skin-times-clip Cartesian product", [], references);
            }
            WriteSkeletalPoseCases(rows);
            foreach (var owner in _modelOwners.Values.SelectMany(owners => owners).DistinctBy(owner => owner.FormKey))
                Emit(rows, "source-object-state", owner, null, null, null, "all-authored-animation-triggers", "source-context",
                    "record-specific-conditions-and-state-owner", ["Script/activation/package state admission requires runtime evidence; shared model motion is a separate case."],
                    References(owner.FormKey.ToString()));
            WriteWeaponCases(rows);
        }

        private IReadOnlyList<CatalogReference> References(string form) => _references.GetValueOrDefault(form) ?? [];

        private CatalogReference[] RigReferences(CatalogRig rig)
        {
            if (!_rigReferences.TryGetValue(rig.Id, out var references))
                _rigReferences.Add(rig.Id, references = _rigForms[rig.Id].SelectMany(References).DistinctBy(reference => reference.Form)
                    .OrderBy(reference => reference.RuntimeFormId).ToArray());
            return references;
        }

        private string ReferenceSet(IReadOnlyList<CatalogReference> references)
        {
            if (_referenceSetCache.TryGetValue(references, out var existing)) return existing;
            var id = "references:" + Hash(System.Text.Json.JsonSerializer.Serialize(references, Json));
            _referenceSets.TryAdd(id, new(id, references));
            _referenceSetCache.Add(references, id);
            return id;
        }

        private void Emit(TextWriter rows, string family, FalloutPluginRecord? owner, CatalogRig? rig, CatalogResource? resource,
            CatalogSequence? sequence, string action, string perspective, string role, IReadOnlyList<string> blockers,
            IReadOnlyList<CatalogReference> references, byte? weaponMods = null, CatalogResource? attachment = null,
            IReadOnlyList<string>? equivalentRigIds = null, IReadOnlyList<string>? relatedRigIds = null,
            string? actionBindingHash = null, string? sourceOwnerSetId = null, bool includeRigFailures = true,
            string? rigHashOverride = null, IReadOnlyList<string>? requiredObservables = null,
            IReadOnlyList<string>? executionBlockers = null)
        {
            var allBlockers = blockers.Concat(resource?.Failures.Select(failure => failure.Reason) ?? [])
                .Concat(includeRigFailures ? rig?.Failures.Select(failure => failure.Reason) ?? [] : []).Distinct(StringComparer.Ordinal).ToList();
            if (references.Count == 0) allBlockers.Add("No authored animated reference is joined; controlled dynamic creation is not admitted by the catalog.");
            var form = owner?.FormKey.ToString();
            var id = "case:" + Hash(string.Join('\n', family, form ?? "", rig?.Id ?? "", resource?.Path ?? "",
                sequence?.Block.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", action, perspective, role));
            var bindings = new List<CatalogBindingResource>();
            // Body/skin resources live once in the rig row; only the additional
            // equipped model belongs to this action's attachment set.
            if (attachment is not null) bindings.Add(new("equipped-weapon", attachment.Path, attachment.SourceIdentity, attachment.Sha256));
            Row(rows, new CatalogCase(id, family, form, owner is null ? null : records.RuntimeFormId(owner.FormKey),
                rig?.Id, rigHashOverride ?? rig?.BindingSha256, resource?.Path ?? "", resource?.Sha256 ?? (form is null ? null : _recordHashes.GetValueOrDefault(form)), sequence?.Block, action,
                perspective, role, allBlockers.Count == 0 ? "source-bound" : "unbound", allBlockers,
                requiredObservables ?? AnimationObservables, [], EntryReferenceCandidates: [], WeaponMods: weaponMods, BindingResources: bindings,
                SourceRecordHash: form is null ? null : _recordHashes.GetValueOrDefault(form), ReferenceSetId: ReferenceSet(references),
                EquivalentRigIds: [], RelatedRigIds: [], ActionBindingHash: actionBindingHash,
                EquivalentRigSetId: RigSet(equivalentRigIds, family == "skeletal-pose" ? "same-skeletal-pose-binding" : "same-weapon-action-binding"),
                RelatedRigSetId: RigSet(relatedRigIds, "unbound-admission-domain; not equivalence"), SourceOwnerSetId: sourceOwnerSetId,
                ExecutionBlockers: executionBlockers));
            _caseCount++;
        }
    }
}
