using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.AnimationCompare;

internal static partial class Catalog
{
    private sealed partial class Builder
    {
        private readonly Dictionary<string, CatalogFailure> _actorFailures = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FalloutPluginRecord> _actorRecords = new(StringComparer.OrdinalIgnoreCase);
        private string? _firstPersonRig;

        private void ReadActorRigs()
        {
            progress?.Invoke("Resolving every winning CREA/NPC template, skeleton, body and declared-animation graph.");
            foreach (var actor in records.EffectiveRecords("CREA").Concat(records.EffectiveRecords("NPC_")))
            {
                var form = actor.FormKey.ToString();
                _actorRecords.Add(form, actor);
                try
                {
                    string skeleton, family; string[] models, declarations, directories;
                    var failures = new List<CatalogFailure>();
                    if (actor.Signature == "CREA")
                    {
                        var appearance = FalloutCreatureAppearanceResolver.Resolve(records, actor.FormKey);
                        skeleton = appearance.SkeletonPath; family = "creature";
                        models = appearance.Models.ToArray(); declarations = appearance.AnimationFiles.ToArray();
                        directories = [Canonical(skeleton[..skeleton.LastIndexOf('/')])];
                    }
                    else
                    {
                        var appearance = FalloutNpcAppearanceResolver.Resolve(records, actor.FormKey);
                        skeleton = appearance.SkeletonPath; family = "humanoid";
                        models = appearance.Models.Concat(appearance.Armor.SelectMany(armor => armor.Addons.Prepend(armor.Model)))
                            .Where(part => part.ModelPath is not null).Select(part => part.ModelPath!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        declarations = [];
                        directories = [Canonical(skeleton[..skeleton.LastIndexOf('/')])];
                        foreach (var blocker in appearance.Blockers) failures.Add(new("actor-appearance", form, "SourceBinding", blocker, "appearance-binding"));
                    }
                    AddRig(family, skeleton, models, directories, declarations, form, failures);
                }
                catch (Exception error) when (SourceError(error))
                {
                    var failure = Failure("actor-rig", form, error); _actorFailures.Add(form, failure); _failures.Add(failure);
                    // Traits/inventory/appearance selection can fail independently
                    // of the winning model-template group. Retain a qualified
                    // skeleton domain without claiming a complete appearance.
                    try
                    {
                        var modelOwner = FalloutActorTemplateOwner.Resolve(records, actor, 64);
                        var skeleton = FalloutNpcAppearanceResolver.PathField(modelOwner, "MODL", "meshes", required: true)!;
                        var baseDirectory = Canonical(skeleton[..skeleton.LastIndexOf('/')]);
                        var declarations = actor.Signature == "CREA" ? DeclaredCreatureAnimations(modelOwner, baseDirectory) : [];
                        AddRig(actor.Signature == "CREA" ? "creature" : "humanoid", skeleton, [], [baseDirectory], declarations, form,
                            [failure, new("actor-appearance", form, "IncompleteAppearance", "Only the independent model-template skeleton was resolved; body/skin graph remains incomplete.", "appearance-binding")]);
                    }
                    catch (Exception modelError) when (SourceError(modelError))
                    { _failures.Add(Failure("actor-skeleton-model-owner", form, modelError)); }
                }
            }
            // This is the existing source engine's first-person namespace, not
            // a synthesized actor or a replacement for a placed reference.
            const string firstPerson = "meshes/characters/_1stperson/skeleton.nif";
            _firstPersonRig = AddRig("first-person", firstPerson, [], ["meshes/characters/_1stperson"], [],
                "engine:first-person-presentation", []);
            var playerBase = records.RuntimeFormKey(FalloutEngineForms.PlayerBase).ToString();
            _rigForms[_firstPersonRig].Add(playerBase);
            _rigVariants[_firstPersonRig].Add(new(playerBase, [firstPerson]));
            var playerReference = records.RuntimeFormKey(FalloutEngineForms.PlayerReference);
            // PlayerREFR is engine-instantiated and need not have an ACHR row.
            // Its location remains unknown here; the runner must use/restores a
            // real checkpoint, never invent an authored cell or spawn position.
            _references["engine:first-person-presentation"] = [new(playerReference.ToString(), FalloutEngineForms.PlayerReference,
                null, null, false, "engine player reference; checkpoint location required", playerBase)];
        }

        private string AddRig(string family, string skeleton, IReadOnlyList<string> models,
            IReadOnlyList<string> directories, IReadOnlyList<string> declarations, string form, List<CatalogFailure> failures)
        {
            skeleton = Canonical(skeleton);
            var paths = models.Prepend(skeleton).Select(Canonical).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (_actorRecords.TryGetValue(form, out var actor))
                foreach (var path in paths)
                {
                    if (!_modelOwners.TryGetValue(path, out var owners)) _modelOwners.Add(path, owners = []);
                    if (!owners.Any(owner => owner.FormKey == actor.FormKey)) owners.Add(actor);
                }
            var resources = paths.Select(ReadResource).ToArray();
            foreach (var resource in resources) failures.AddRange(resource.Failures);
            foreach (var animation in declarations) ReadResource(animation);
            var fingerprint = failures.Count == 0 && resources.All(resource => resource.AnimationBindingSha256 is not null)
                ? Hash(JsonSerializer.Serialize(new
                {
                    family,
                    bindings = resources.Select(resource => resource.AnimationBindingSha256),
                    directories,
                    declarations = declarations.Select(Canonical)
                }, Json)) : null;
            // Failed/unknown graphs never collapse into one allegedly equivalent rig.
            var id = "rig:" + (fingerprint ?? Hash(form + "\n" + string.Join('\n', paths)));
            if (!_rigs.ContainsKey(id))
            {
                _rigs.Add(id, new(id, family, skeleton, paths, [], directories, declarations.Select(Canonical).ToArray(), fingerprint, failures));
                _rigForms.Add(id, []);
                _rigVariants.Add(id, []);
            }
            _rigForms[id].Add(form);
            _rigVariants[id].Add(new(form, paths));
            _failures.AddRange(failures.Where(failure => failure.Lane == "actor-appearance"));
            return id;
        }

        private static string[] DeclaredCreatureAnimations(FalloutPluginRecord model, string directory)
        {
            var field = model.ReadSubrecords().SingleOrDefault(field => field.Signature == "KFFZ").Data;
            if (field.IsEmpty) return [];
            if (field.Span[^1] != 0) throw new InvalidDataException("CREA KFFZ lacks its source string terminator.");
            return System.Text.Encoding.Latin1.GetString(field.Span).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(Canonical).Select(path => path.StartsWith("meshes/", StringComparison.Ordinal) ? path :
                    directory + (path.Contains('/') ? "/" : "/specialanims/") + path).ToArray();
        }
    }
}
