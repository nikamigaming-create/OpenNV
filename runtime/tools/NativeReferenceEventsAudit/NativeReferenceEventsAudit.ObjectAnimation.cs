using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void ExerciseOwnedObjectAnimation(string dataRoot, string savePath)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var save = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(savePath))!;
        world.RestoreEncounterZones(save.EncounterZones); world.Restore(save.References!);
        world.RestoreActorOverrides(save.ActorOverrides);
        var selected = save.References!.Where(state => state.ScriptError?.Contains("command playGroup (2 arguments) has no owner.",
            StringComparison.OrdinalIgnoreCase) == true).ToArray();
        Require(selected.Length > 0, "Owned checkpoint contains no selected legacy animation failures.");
        var scenes = selected.Select(state => state.Placement?.Cell ?? state.Cell).Distinct()
            .Select(cell => world.ComposeResidency(FalloutCellSceneReader.Read(records, cell))).ToArray();
        foreach (var cell in scenes) world.LoadCell(cell);
        var selectedKeys = selected.Select(state => state.Reference).ToHashSet();
        var references = scenes.SelectMany(cell => cell.References).Where(reference => selectedKeys.Contains(reference.FormKey))
            .DistinctBy(reference => reference.FormKey).ToArray();
        var bases = scenes.SelectMany(cell => cell.BaseObjects).DistinctBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value);
        var prototypes = new Dictionary<string, RuntimeNativeNifPrototype>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<Node3D>();
        var inventory = new FalloutPlayerInventory();
        var quests = new FalloutQuestState(records);
        var grantCalls = 0;
        void Effect(FalloutReferenceScriptEffect effect)
        {
            Require(effect.Kind == FalloutReferenceEffectKind.AddItem && effect.Target == records.RuntimeFormKey(0x14) && effect.Argument is not null,
                "Owned animation scripts reached another effect owner.");
            inventory.Add(records, effect.Argument!.Value, effect.Value, 1, true);
            grantCalls++;
        }
        Node3D NewRoot() { var root = new Node3D(); AddChild(root); roots.Add(root); return root; }
        RuntimeNativeReferencePresentation Presentation(FalloutReferenceWorld owner, Node3D root)
        {
            var result = new RuntimeNativeReferencePresentation(owner, references, reference =>
            {
                var model = bases[reference.Base].ModelPath ?? throw new InvalidDataException("Selected object has no source model.");
                if (!prototypes.TryGetValue(model, out var prototype))
                {
                    Require(content.TryRead(model, null, out var bytes, out _), "Owned object model is absent.");
                    prototypes.Add(model, prototype = new(bytes, .0142875f));
                }
                var node = prototype.InstantiatePlaced(Transform3D.Identity);
                root.AddChild(node);
                return node;
            });
            root.AddChild(result);
            foreach (var reference in references) _ = result.Resolve(reference.FormKey);
            return result;
        }
        try
        {
            var root = NewRoot();
            var presentation = Presentation(world, root);
            var host = new FalloutReferenceScriptHost((_, _) => false, Effect,
                PlayGroup: presentation.PlayGroup, IsAnimPlaying: presentation.IsAnimPlaying);
            var scripts = new FalloutReferenceScripts(records, world, quests, host);
            foreach (var state in selected)
            {
                var result = scripts.Dispatch(state.Reference, "OnLoad");
                Require(result is { Error: null, Blocks: 1 } && result.RecoveredError == state.ScriptError,
                    $"Legacy source animation failure did not recover for {state.Reference}: {result.Error}");
            }
            Require(grantCalls == 0, "OnLoad recovery repeated an inventory grant.");
            var representatives = selected.GroupBy(state => state.Script).Select(group =>
                group.First(state => world.CanActivate(state.Reference))).ToArray();
            Require(representatives.Length == 6, "Selected owned checkpoint lost a plant script family.");
            var cell = scenes[0] with { References = references, BaseObjects = bases };
            var divergence = new List<string>();
            var events = new RuntimeNativeReferenceEvents { ReportDivergence = divergence.Add };
            events.Configure(records, world, quests, cell, root, host, _ => Transform3D.Identity, .0142875f, 1);
            root.AddChild(events);
            events._Process(0);
            foreach (var state in representatives)
            {
                var node = presentation.Nodes[state.Reference];
                Require(events.TryActivate(node), "Native source object could not receive activation.");
                events._Process(0);
                var instance = world.Get(state.Reference);
                Require(instance.ScriptError is null && instance.Destroyed && instance.Read(1) == 1,
                    $"Harvest did not execute the full authored result: {instance.ScriptError}");
                Require(presentation.IsAnimPlaying(state.Reference, null) && presentation.IsAnimPlaying(state.Reference, "Forward") &&
                    presentation.IsAnimPlaying(state.Reference, "Backward"), "IsAnimPlaying compared a clip name instead of its object sequence type.");
                Require(!events.TryActivate(node), "Destroyed object admitted a second native harvest.");
                var inventoryAfter = JsonSerializer.Serialize(inventory.Capture());
                scripts.Activate(state.Reference, records.RuntimeFormKey(0x14));
                Require(JsonSerializer.Serialize(inventory.Capture()) == inventoryAfter, "Repeated source activation granted a second reward.");
            }
            Require(grantCalls == representatives.Length && divergence.Count == 0, "Native harvest duplicated a grant or hid a script failure.");
            events.SetProcess(false);
            foreach (var controller in NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(root)) controller._Process(.01);
            var states = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records);
            cold.RestoreEncounterZones(world.CaptureEncounterZones()); cold.Restore(states); cold.RestoreActorOverrides(world.CaptureActorOverrides());
            foreach (var scene in scenes) cold.LoadCell(scene);
            var coldRoot = NewRoot();
            var coldPresentation = Presentation(cold, coldRoot);
            foreach (var reference in references)
            {
                var expected = states.Single(state => state.Reference == reference.FormKey);
                Require(JsonSerializer.Serialize(expected.ObjectAnimations) == JsonSerializer.Serialize(cold.Get(reference.FormKey).Capture().ObjectAnimations),
                    "Cold source object clock or selection changed.");
            }
            var beforeColdActivation = JsonSerializer.Serialize(inventory.Capture());
            var coldScripts = new FalloutReferenceScripts(records, cold, quests,
                host with { PlayGroup = coldPresentation.PlayGroup, IsAnimPlaying = coldPresentation.IsAnimPlaying });
            foreach (var state in representatives) coldScripts.Activate(state.Reference, records.RuntimeFormKey(0x14));
            Require(JsonSerializer.Serialize(inventory.Capture()) == beforeColdActivation, "Cold harvest repeated an inventory grant.");
            var retained = JsonSerializer.Serialize(world.Capture());
            presentation.SetResidency([], _ => null, _ => true);
            Require(presentation.WarmNodeCount == references.Length && JsonSerializer.Serialize(world.Capture()) == retained,
                "Warm eviction discarded object animation state.");
            presentation.SetResidency(references, _ => throw new InvalidDataException("Warm source model was rebuilt."));
            Require(JsonSerializer.Serialize(world.Capture()) == retained, "Warm reentry changed object animation state.");
            const string windowModel = "meshes/architecture/primm/windowem01.nif";
            Require(content.TryRead(windowModel, null, out var windowBytes, out _), "Selected authored window model is absent.");
            var window = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(windowBytes), .0142875f);
            root.AddChild(window.Root);
            var windowController = NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(window.Root).Single();
            Require(windowController.ActiveSequence is null && windowController.SequenceNames.Count == 3, "Authored window invented a loop selection.");
            windowController.RequestSourceSequence("Left", 1); windowController._Process(.1);
            Require(windowController.Playing, "Selected source window loop did not advance.");
            foreach (var controller in NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(root)) controller.SetProcess(false);
            var replacementRoot = NewRoot();
            _ = Presentation(world, replacementRoot);
            var beforeRetirement = JsonSerializer.Serialize(world.Capture());
            root.Free(); roots.Remove(root);
            Require(JsonSerializer.Serialize(world.Capture()) == beforeRetirement,
                "Retiring the prior cell presentation removed its replacement's object animation owner.");
            GD.Print($"OPENNV_OWNED_OBJECT_ANIMATION_PASS recovered={selected.Length} scriptFamilies={representatives.Length} sourceModels={prototypes.Count + 1} grants={grantCalls} duplicateGrants=0 nativeActivation=true coldState=true warmState=true sequenceTypeQuery=true sourceWindow=true fixture=true parity=unverified");
        }
        finally
        {
            foreach (var root in roots) root.Free();
            foreach (var prototype in prototypes.Values) prototype.Scene.Root.Free();
        }
    }
}
