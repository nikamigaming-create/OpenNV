using OpenNV.Runtime.Content;

namespace OpenNV.AnimationCompare;

internal static partial class Catalog
{
    private sealed partial class Builder
    {
        private void WriteWeaponCases(TextWriter rows)
        {
            using var variantRows = Writer("weaponVariants");
            foreach (var weapon in records.EffectiveRecords("WEAP"))
            {
                var masks = new List<byte> { 0 };
                try
                {
                    var available = FalloutWeaponModifications.Read(records, weapon.FormKey).Aggregate(0, (mask, mod) => mask | (1 << mod.Slot));
                    for (byte mask = 1; mask < 8; mask++) if ((mask & available) == mask) masks.Add(mask);
                }
                catch (Exception error) when (SourceError(error))
                {
                    _failures.Add(Failure("weapon-modifications", weapon.FormKey.ToString(), error));
                    Emit(rows, "weapon", weapon, null, null, null, "modifications", "both", "source-modification-graph", [error.Message], References(weapon.FormKey.ToString()));
                }
                foreach (var mask in masks)
                    foreach (var first in new[] { true, false })
                    {
                        var perspective = first ? "first-person" : "third-person";
                        try
                        {
                            // Runtime effects and source geometry are independent
                            // catalog lanes. Missing gameplay-effect support must
                            // not erase an authored model or animation variant.
                            var presentation = FalloutWeaponPresentation.Read(records, weapon.FormKey, first, weaponMods: 0);
                            presentation = presentation with { Model = ReadModifiedModel(weapon, presentation.Model, mask, first) };
                            var model = presentation.Model?.ModelPath is { } path ? ReadResource(path) : null;
                            var executionBlockers = new List<string>();
                            var modifications = new List<CatalogWeaponModifier>();
                            foreach (var modification in FalloutWeaponModifications.Read(records, weapon.FormKey).Where(mod => (mask & (1 << mod.Slot)) != 0))
                            {
                                string admission = "existing runtime effect owner";
                                try { FalloutWeaponModifications.RequireRuntimeEffect(modification); }
                                catch (NotSupportedException error) { admission = "unbound"; executionBlockers.Add(error.Message); }
                                modifications.Add(new(modification.Slot, modification.Item.ToString(), modification.Effect,
                                    modification.Value, modification.Secondary, admission));
                            }
                            Row(variantRows, new CatalogWeaponVariant(weapon.FormKey.ToString(), records.RuntimeFormId(weapon.FormKey), mask,
                                perspective, _recordHashes.GetValueOrDefault(weapon.FormKey.ToString()), model?.Path, model?.Sha256,
                                presentation.AnimationGroup, presentation.AttackAnimation, presentation.ReloadAnimation, modifications, executionBlockers));
                            var rigs = first ? _rigs.Values.Where(rig => rig.Id == _firstPersonRig).ToArray()
                                : _rigs.Values.Where(rig => rig.Family == "humanoid").ToArray();
                            var actions = new List<(string Role, string Group)>
                            {
                                ("equip", presentation.AnimationGroup + "equip"), ("unequip", presentation.AnimationGroup + "unequip"),
                                ("aim", presentation.AimGroup(first, false)), ("iron-sights", presentation.AimGroup(first, true)),
                            };
                            try { actions.Add(("reload", presentation.AnimationGroup + presentation.ReloadGroup)); }
                            catch (Exception error) when (SourceError(error))
                            { Emit(rows, "weapon", weapon, null, model, null, "reload", perspective, $"mods:{mask}", [error.Message], References(weapon.FormKey.ToString()), mask, model); }
                            try { actions.Add(("attack", presentation.AnimationGroup + presentation.AttackGroup)); }
                            catch (Exception error) when (SourceError(error))
                            { Emit(rows, "weapon", weapon, null, model, null, "attack", perspective, $"mods:{mask}", [error.Message], References(weapon.FormKey.ToString()), mask, model); }
                            if (rigs.Length == 0)
                                Emit(rows, "weapon", weapon, null, model, null, "all-actions", perspective, $"mods:{mask}", ["No source humanoid presentation rig is resolved."], [], mask, model);
                            foreach (var (role, group) in actions)
                                WriteWeaponAction(rows, weapon, mask, model, rigs, perspective, role, group, executionBlockers);
                        }
                        catch (Exception error) when (SourceError(error))
                        {
                            _failures.Add(Failure("weapon-presentation", weapon.FormKey.ToString() + ":" + perspective, error));
                            Emit(rows, "weapon", weapon, null, null, null, "all-actions", perspective, $"mods:{mask}", [error.Message], References(weapon.FormKey.ToString()), mask);
                        }
                    }
            }
        }

        private sealed record WeaponBinding(CatalogRig Rig, CatalogResource Clip, CatalogSequence Sequence,
            List<string> RigIds, IReadOnlyList<string> Blockers);

        private void WriteWeaponAction(TextWriter rows, FalloutPluginRecord weapon, byte mask, CatalogResource? model,
            IReadOnlyList<CatalogRig> rigs, string perspective, string role, string group, IReadOnlyList<string> executionBlockers)
        {
            if (model is null)
            {
                Emit(rows, "weapon", weapon, null, null, null, group, perspective, $"{role};mods:{mask}",
                    ["No inventory-model attachment is declared; an embedded actor weapon requires its own source binding owner."],
                    References(weapon.FormKey.ToString()), mask);
                return;
            }
            var bindings = new Dictionary<string, WeaponBinding>(StringComparer.Ordinal);
            var rejected = new List<string>();
            var missing = new List<string>();
            var incomplete = new List<string>();
            foreach (var rig in rigs)
            {
                if (rig.Failures.Count != 0) { incomplete.Add(rig.Id); continue; }
                var skeleton = ReadResource(rig.SkeletonPath);
                if (!skeleton.Nodes.Any(node => node.Name == "Weapon")) { rejected.Add(rig.Id); continue; }
                var paths = rig.AnimationDirectories.SelectMany(directoryPath => content.ActorAnimations.Variants(directoryPath, group))
                    .Select(Canonical).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (paths.Length == 0) { missing.Add(rig.Id); continue; }
                foreach (var path in paths)
                {
                    var clip = ReadResource(path);
                    if (clip.Sequences.Count == 0)
                    {
                        Emit(rows, "weapon", weapon, rig, clip, null, group, perspective, $"{role};mods:{mask}",
                            ["No decoded sequence."], RigReferences(rig), mask, model);
                        continue;
                    }
                    foreach (var sequence in clip.Sequences)
                    {
                        var blockers = rig.Failures.Concat(model.Failures).Concat(clip.Failures).Select(failure => failure.Reason).ToArray();
                        var targetRoles = sequence.Channels.Select(channel => WeaponTargetRole(rig, model, sequence, channel)).ToArray();
                        // Skin-only variants remain exercised by rig-animation cases.
                        // A weapon case aliases them only if the actual selected
                        // sequence and every target/attachment source binding agree.
                        var binding = Hash(System.Text.Json.JsonSerializer.Serialize(new
                        {
                            clip.Path,
                            clip.Sha256,
                            sequence.Block,
                            skeleton = skeleton.AnimationBindingSha256,
                            weapon = model.AnimationBindingSha256,
                            targetRoles,
                            unresolved = blockers.Length == 0 ? null : rig.Id
                        }, Json));
                        if (bindings.TryGetValue(binding, out var same)) same.RigIds.Add(rig.Id);
                        else bindings.Add(binding, new(rig, clip, sequence, [rig.Id], blockers));
                    }
                }
            }
            foreach (var (bindingHash, binding) in bindings)
            {
                var aliases = binding.RigIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var references = aliases.SelectMany(id => RigReferences(_rigs[id])).DistinctBy(reference => reference.Form)
                    .OrderBy(reference => reference.RuntimeFormId).ToArray();
                Emit(rows, "weapon", weapon, binding.Rig, binding.Clip, binding.Sequence, group, perspective,
                    $"{role};mods:{mask}", binding.Blockers, references, mask, model, aliases, actionBindingHash: bindingHash,
                    executionBlockers: executionBlockers);
            }
            if (missing.Count != 0)
                Emit(rows, "weapon-binding-admission", weapon, null, model, null, group, perspective, $"{role};mods:{mask}",
                    ["No requested source group candidate in these rigs; native fallback/selection remains unbound."], [], mask, model, relatedRigIds: missing);
            if (rejected.Count != 0)
                Emit(rows, "weapon-binding-admission", weapon, null, model, null, group, perspective, $"no-attachment;{role};mods:{mask}",
                    ["These source skeletons have no Weapon attachment; no gun/action Cartesian cases were fabricated."], [], mask, model, relatedRigIds: rejected);
            if (incomplete.Count != 0)
                Emit(rows, "weapon-binding-admission", weapon, null, model, null, group, perspective, $"incomplete-rig;{role};mods:{mask}",
                    ["Rig graph has explicit source/decode failures; its action-binding domain remains unresolved."], [], mask, model, relatedRigIds: incomplete);
        }

        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _resourceTargetRoles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _bodyTargetRoles = new(StringComparer.Ordinal);

        private IReadOnlyDictionary<string, string> ResourceTargetRoles(CatalogResource resource)
        {
            if (!_resourceTargetRoles.TryGetValue(resource.Path, out var roles))
                _resourceTargetRoles.Add(resource.Path, roles = resource.Nodes.GroupBy(node => node.Name, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => resource.AnimationBindingSha256 + ":" + string.Join(',', group.Select(node => node.Block)), StringComparer.Ordinal));
            return roles;
        }

        private string WeaponTargetRole(CatalogRig rig, CatalogResource model, CatalogSequence sequence, CatalogChannel channel)
        {
            if (channel.NodeName == sequence.TargetName && channel.ControllerType == "NiTransformController") return "accumulation-root";
            var skeleton = ReadResource(rig.SkeletonPath);
            if (ResourceTargetRoles(skeleton).TryGetValue(channel.NodeName, out var bone)) return "skeleton:" + bone;
            if (ResourceTargetRoles(model).TryGetValue(channel.NodeName, out var weapon)) return "weapon:" + weapon;
            if (!_bodyTargetRoles.TryGetValue(rig.Id, out var body))
                _bodyTargetRoles.Add(rig.Id, body = rig.ModelPaths.Where(path => path != rig.SkeletonPath).Select(ReadResource)
                    .SelectMany(resource => ResourceTargetRoles(resource)).GroupBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => string.Join(',', group.Select(pair => pair.Value)), StringComparer.Ordinal));
            return body.TryGetValue(channel.NodeName, out var target) ? "body:" + target : "absent-from-source-palettes:" + channel.NodeName;
        }
    }
}
