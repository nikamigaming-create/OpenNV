using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeCouchTriggerAudit : Node3D
{
    public override async void _Ready()
    {
        Node3D? root = null;
        RuntimeNativeNifPrototype? prototype = null;
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 1) throw new ArgumentException("Couch trigger audit needs one owned root.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var configuration = RuntimeConfiguration.Load(); var units = configuration.World.GameUnitsToMeters;
            var cell = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", "GSDocMitchellHouse").FormKey);
            world.LoadCell(cell);
            var quests = new FalloutQuestState(records);
            var quest = FalloutDialogueTopic.Find(records, "QUST", "VCG01").FormKey;
            quests.EnterStage(quest, 80); quests.ApplyObjective(quest, 40, true, true);
            root = new Node3D(); AddChild(root);
            Transform3D Placement(FalloutPlacedReference reference) => new(
                new Basis(Vector3.Up, -reference.RotationRadians[2]),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
            var furniture = cell.References.Single(reference => reference.EditorId == "DocMitchellCouchREF");
            var model = cell.BaseObjects[furniture.Base].ModelPath!;
            if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException("Source furniture is absent.", model);
            prototype = new(bytes, units);
            var couch = prototype.InstantiatePlaced(Placement(furniture));
            couch.SetMeta("opennv_reference_form_key", furniture.FormKey.ToString()); root.AddChild(couch);
            var actorSource = cell.References.Single(reference => reference.EditorId == "DocMitchellREF");
            var actor = RuntimeNativeNpc.Create(records, content, actorSource, units, (_, _, _, _) => new StandardMaterial3D());
            root.AddChild(actor); actor.Transform = Placement(actorSource);
            actor.ConfigureContactShapes(configuration.Player.CollisionLayer);
            actor.ConfigureAi(records, quests, cell, Placement);
            var creation = FalloutNativeRaceSexResolver.Resolve(records);
            var playerAppearance = FalloutNpcAppearanceResolver.Resolve(records, creation.Player, equippedArmor: [],
                appearanceState: FalloutNativeCharacterCreation.ActorState(records, creation.Player, creation.Initial));
            var player = new RuntimeNativePlayer(); root.AddChild(player); player.Configure(configuration, Placement(furniture));
            player.SetPhysicsProcess(false); player.SetProcessUnhandledInput(false);
            player.CreateFurnitureBody = () => RuntimeNativeNpc.Create(playerAppearance with { Reference = records.RuntimeFormKey(0x14) },
                content, units, (_, _, _, _) => new StandardMaterial3D());
            var effects = new List<FalloutReferenceScriptEffect>();
            var events = new RuntimeNativeReferenceEvents { Player = player };
            events.Configure(records, world, quests, cell, root, new(events.IsCurrentFurniture, effect =>
            {
                if (effect.Kind == FalloutReferenceEffectKind.DefaultActivate) events.DefaultActivate(effect.Target!.Value);
                else effects.Add(effect);
            }), Placement, units, configuration.Player.CollisionLayer);
            root.AddChild(events);
            events.SetProcess(false);
            void AdvanceEvents(double seconds) { events.SetProcess(true); events._Process(seconds); events.SetProcess(false); }
            var collider = couch.FindChildren("*", "", true, false).OfType<StaticBody3D>().First();
            if (!events.TryActivate(collider)) throw new InvalidOperationException("Source couch activation was not admitted.");
            AdvanceEvents(0);
            var approach = player.FurnitureApproach;
            player.GlobalTransform = new(approach.Basis, approach.Origin + approach.Basis.Z * units * 32);
            for (var frame = 0; frame < 900 && !effects.Any(effect => effect.Kind == FalloutReferenceEffectKind.Conversation); frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                player._PhysicsProcess(1.0 / 60);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                AdvanceEvents(1.0 / 60);
                var state = JsonSerializer.SerializeToElement(player.FurnitureState);
                if (state.GetProperty("error").ValueKind != JsonValueKind.Null) throw new InvalidOperationException(state.ToString());
            }
            var chair = world.Get(FalloutDialogueTopic.Find(records, "REFR", "VCG01DocMitchellChairTriggerREF").FormKey);
            var couchTrigger = world.Get(FalloutDialogueTopic.Find(records, "REFR", "VCG01DocMitchellCouchTriggerREF").FormKey);
            var conversation = effects.SingleOrDefault(effect => effect.Kind == FalloutReferenceEffectKind.Conversation);
            if (conversation?.Target != actorSource.FormKey || !quests.Objective(quest, 40).Completed ||
                chair.Read(chair.Script!.Locals["bDocInChair"]) != 1 || chair.ScriptError is not null || couchTrigger.ScriptError is not null)
                throw new InvalidOperationException(JsonSerializer.Serialize(new { conversation, chair = chair.Capture(), couch = couchTrigger.Capture(), actor = actor.AnimationState, events = events.State }));
            GD.Print($"OPENNV_NATIVE_COUCH_TRIGGER_AUDIT_PASS sourceFurniture=true posedActorContacts=true playerContacts=true sourceTimers=true objective=true conversationRequest=true otherScriptFailures={world.ResidentInstances.Count(instance => instance.ScriptError is not null)} ordinary-route-camera-audio-pixels=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally { root?.Free(); prototype?.Scene.Root.Free(); }
    }
}
