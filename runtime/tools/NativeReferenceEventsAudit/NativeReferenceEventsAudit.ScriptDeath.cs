using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private async Task ExerciseOwnedScriptDeath(string dataRoot, string savePath)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var save = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(savePath))!;
        var level = save.Vitals!.Level;
        var globals = FalloutGlobalState.Read(records);
        using var world = new FalloutReferenceWorld(records);
        world.RestoreEncounterZones(save.EncounterZones); world.Restore(save.References!);
        world.RestoreActorOverrides(save.ActorOverrides);
        var selected = save.References!.Where(state => state.ScriptError is
            "OnLoad: Reached native script command Kill (0 arguments) has no owner.").ToArray();
        Require(selected.Length > 0, "Owned checkpoint has no selected script death failures.");
        var scenes = selected.Select(state => state.Placement?.Cell ?? state.Cell).Distinct()
            .Select(cell => world.ComposeResidency(FalloutCellSceneReader.Read(records, cell))).ToArray();
        foreach (var cell in scenes) world.LoadCell(cell);
        var selectedKeys = selected.Select(state => state.Reference).ToHashSet();
        var references = scenes.SelectMany(cell => cell.References).DistinctBy(reference => reference.FormKey).ToArray();
        var bases = scenes.SelectMany(cell => cell.BaseObjects).DistinctBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value);
        var root = new Node3D(); AddChild(root);
        var creatures = new List<RuntimeNativeCreature>();
        RuntimeNativeCreature Creature(FalloutReferenceWorld owner, FalloutPlacedReference reference, Node3D parent)
        {
            owner.InitializeActorTemplates(reference.FormKey, level, globals);
            var state = owner.Get(reference.FormKey);
            var actor = RuntimeNativeCreature.Create(records, content, reference, state, .0142875f);
            actor.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * .0142875f);
            RuntimeNativeActorContacts.Configure(actor, actor.Skeleton, 2);
            actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath, owner, state, records, content, 2, 3);
            parent.AddChild(actor);
            actor.SetPhysicsProcess(false);
            creatures.Add(actor);
            return actor;
        }
        async Task Frames()
        {
            for (var index = 0; index < 3; index++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        try
        {
            foreach (var reference in references.Where(reference => selectedKeys.Contains(reference.FormKey))) _ = Creature(world, reference, root);
            var reported = new List<string>();
            var events = new RuntimeNativeReferenceEvents { ReportDivergence = reported.Add };
            var cell = scenes[0] with { References = references.Where(reference => selectedKeys.Contains(reference.FormKey)).ToArray(), BaseObjects = bases };
            events.Configure(records, world, new(records), cell, root,
                new((_, _) => false, _ => throw new InvalidDataException("Owned script death invented a host effect."), PlayerLevel: () => level),
                _ => Transform3D.Identity, .0142875f, 1);
            root.AddChild(events);
            events._Process(0); events.SetProcess(false);
            foreach (var state in selected)
            {
                var actual = world.Get(state.Reference);
                var local = FalloutScriptLocals.ReadDeclarations(actual.Script!.Record)["bDead"].Index;
                Require(actual.ScriptError is null && actual.Destroyed && actual.Read(local) == 1 && actual.Injury is
                { Dead: true, Killer: null, DeathEventPending: false }, "Source OnLoad did not recover without inventing corpse death: " + actual.ScriptError);
            }
            Require(reported.Count == 0, "Recovered source OnLoad reached another failure.");
            var live = references.Where(reference => bases[reference.Base].Signature == "CREA" && !selectedKeys.Contains(reference.FormKey))
                .First(reference =>
                {
                    world.InitializeActorTemplates(reference.FormKey, level, globals);
                    return world.IsEnabled(reference.FormKey) && !world.IsDead(reference.FormKey) &&
                        !world.HealthSource(reference.FormKey).Essential && !FalloutActorHealthSource.StartsDead(records, reference.Base, world.Get(reference.FormKey).Templates);
                });
            var living = Creature(world, live, root);
            Require(world.KillActor(live.FormKey, null, level, globals), "Living source creature did not enter scripted death.");
            await Frames();
            Require(living.Combat!.Error is null && living.Combat.CorpseAimPoints.Any() &&
                world.Get(live.FormKey).Capture().Ragdoll is { Bodies.Count: > 0 } &&
                living.FindChildren("*", "Area3D", true, false).OfType<Area3D>().All(area => area.CollisionLayer == 0),
                "Authoritative script death did not activate source ragdoll and retire living contacts: " + living.Combat.Error);
            var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            living.Free(); creatures.Remove(living);
            using var cold = new FalloutReferenceWorld(records);
            cold.RestoreEncounterZones(world.CaptureEncounterZones()); cold.Restore(snapshots); cold.RestoreActorOverrides(world.CaptureActorOverrides());
            foreach (var sourceCell in scenes) cold.LoadCell(sourceCell);
            var continued = Creature(cold, live, root);
            await Frames();
            Require(continued.Combat!.Error is null && continued.Combat.CorpseAimPoints.Any() &&
                cold.Get(live.FormKey).Injury is { Dead: true, Killer: null, DeathEventPending: true } &&
                !cold.KillActor(live.FormKey, records.RuntimeFormKey(0x14), level, globals), "Cold script corpse lost its state or repeated death.");
            foreach (var state in selected)
                Require(cold.Get(state.Reference) is { ScriptError: null, Destroyed: true }, "Cold source OnLoad lost its completed script state.");
            GD.Print($"OPENNV_OWNED_SCRIPT_DEATH_PASS recovered={selected.Length} sourceOnLoad=true authoredCorpseNoRepeat=true livingRagdoll=true coldRagdoll=true unknownKiller=true fixture=true parity=unverified");
        }
        finally { root.Free(); }
    }
}
