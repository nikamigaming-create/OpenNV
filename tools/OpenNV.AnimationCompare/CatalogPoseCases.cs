using System.Text.Json;

namespace OpenNV.AnimationCompare;

internal static partial class Catalog
{
    private sealed partial class Builder
    {
        private static readonly string[] PoseObservables = ["source-skeleton-and-clip-identity", "controller-admission-and-phase",
            "complete-local-and-world-bone-cohort", "root-accumulation", "text-key-event-order", "frame-time"];

        private void WriteSkeletalPoseCases(TextWriter rows)
        {
            var domains = _rigs.Values.GroupBy(rig => Hash(JsonSerializer.Serialize(new
            {
                rig.Family,
                skeleton = ReadResource(rig.SkeletonPath).AnimationBindingSha256 ?? "unresolved:" + rig.SkeletonPath,
                rig.AnimationDirectories,
                rig.DeclaredAnimations,
            }, Json)), StringComparer.Ordinal);
            foreach (var domain in domains)
            {
                var rigs = domain.OrderByDescending(item => RigReferences(item).Length > 0).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
                var rig = rigs[0];
                var skeleton = ReadResource(rig.SkeletonPath);
                var rigIds = rigs.Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
                var references = rigs.SelectMany(RigReferences).DistinctBy(reference => reference.Form)
                    .OrderBy(reference => reference.RuntimeFormId).ToArray();
                var owners = rigs.SelectMany(item => _rigForms[item.Id]).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(form => _actorRecords.GetValueOrDefault(form)).Where(candidate => candidate is not null).Select(candidate => candidate!).ToArray();
                var owner = owners.OrderByDescending(candidate => References(candidate.FormKey.ToString()).Count > 0).FirstOrDefault();
                var ownerSet = OwnerSet(owners);
                if (skeleton.AnimationBindingSha256 is null)
                {
                    Emit(rows, "skeletal-pose-admission", owner, rig, skeleton, null, "all-source-animations",
                        rig.Family == "first-person" ? "first-person" : "third-person", "unresolved-skeleton-domain",
                        ["Skeleton source graph is not completely decoded; no pose equivalence is claimed."], references,
                        relatedRigIds: rigIds, sourceOwnerSetId: ownerSet, includeRigFailures: false, requiredObservables: PoseObservables);
                    continue;
                }
                var skeletonNames = skeleton.Nodes.Select(node => node.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var clip in _resources.Values.Where(resource => resource.Kind == "external-animation" &&
                    (rig.DeclaredAnimations.Contains(resource.Path, StringComparer.OrdinalIgnoreCase) ||
                     rig.AnimationDirectories.Any(prefix => resource.Path.StartsWith(prefix.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)))).ToArray())
                    foreach (var sequence in clip.Sequences)
                    {
                        var transformBindings = sequence.Channels.Where(channel => channel.ControllerType == "NiTransformController" &&
                            channel.PropertyType.Length == 0 && channel.Variable1.Length == 0 && channel.Variable2.Length == 0 &&
                            (skeletonNames.Contains(channel.NodeName) || channel.NodeName == sequence.TargetName)).ToArray();
                        if (transformBindings.Length == 0) continue; // Complete controller/resource case remains in the denominator.
                        var bindingHash = Hash(JsonSerializer.Serialize(new
                        {
                            skeleton.AnimationBindingSha256,
                            clip.Path,
                            clip.Sha256,
                            sequence.Block,
                            sequence.TargetName,
                            transformBindings
                        }, Json));
                        Emit(rows, "skeletal-pose", owner, rig, clip, sequence, sequence.Name,
                            rig.Family == "first-person" ? "first-person" : "third-person",
                            "skeletal-transform-domain; skin/non-skeletal/state lanes separate; runtime selection pending", [], references,
                            equivalentRigIds: rigIds, actionBindingHash: bindingHash, sourceOwnerSetId: ownerSet,
                            includeRigFailures: false, rigHashOverride: skeleton.AnimationBindingSha256, requiredObservables: PoseObservables);
                    }
            }
        }
    }
}
