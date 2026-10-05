using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeDefaultActivationAudit
{
    private async Task ExerciseOwned(string game, string mod, string root, string checkpoint, string selector, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var checkpointBytes = File.ReadAllBytes(checkpoint);
            var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
            var openingCell = FalloutCellSceneReader.Read(records, new("FalloutNV.esm", 0x103df9));
            var restored = FalloutNativeCampaignSave.Read(checkpoint, content.SaveCompatibilityId, records,
                FalloutNativeVigorResolver.Resolve(records, openingCell), FalloutNativeTagSkillResolver.Resolve(records, controls),
                FalloutOpeningInventoryGrantResolver.Resolve(records, controls, "VCG01"),
                FalloutNativeTraitFarewellResolver.Resolve(records, controls, openingCell));
            var saved = restored.State;
            if (saved.References is null || saved.Quests is null || saved.Globals is null || saved.Vitals is null)
                throw new InvalidDataException("Owned default-loot proof requires a recognized complete source checkpoint.");
            var fields = selector.Split(':');
            if (fields.Length != 2) throw new ArgumentException("Owned loot requires plugin:hex-object-id.");
            var identity = new FalloutFormKey(fields[0], Convert.ToUInt32(fields[1], 16));
            var record = records.GetEffective(identity);
            if (record.Signature != "ACHR") throw new InvalidDataException("Owned loot requires an actual winning ACHR.");
            using var world = new FalloutReferenceWorld(records);
            world.RestoreEncounterZones(saved.EncounterZones); world.Restore(saved.References);
            world.RestoreActorOverrides(saved.ActorOverrides); world.RestoreFactionRelations(saved.FactionRelations);
            var quests = new FalloutQuestState(records); quests.Restore(saved.Quests);
            var globals = FalloutGlobalState.Read(records); globals.Restore(saved.Globals);
            var level = saved.Vitals.Level;
            var sourceCell = world.ComposeResidency(FalloutCellSceneReader.Read(records, world.Placement(identity).Cell));
            var reference = sourceCell.References.Single(value => value.FormKey == identity);
            var cell = sourceCell with { References = [reference] }; world.LoadCell(cell);
            var state = world.Get(identity);
            if (records.GetEffective(state.Base).Signature != "NPC_" || state.Script is not { } script)
                throw new InvalidDataException("Owned loot requires a real source NPC with an attached script.");
            var source = script.Record.ReadSubrecords().Single(row => row.Signature == "SCTX");
            var blocks = FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(source.Data.Span));
            Require(!blocks.Any(block => block.Event.Equals("OnActivate", StringComparison.OrdinalIgnoreCase)) &&
                blocks.Any(block => block.Event.Equals("OnDeath", StringComparison.OrdinalIgnoreCase)),
                "Selected source does not declare the required independent-default/OnDeath contract.");
            var ownedRecords = new[] { record, records.GetEffective(state.Base), script.Record };
            var sourceHashes = ownedRecords.Select(owner => SHA256.HashData(owner.ReadData())).ToArray();
            Require(world.CanActivate(identity) && !world.IsDead(identity) && state.ScriptError is null,
                "Selected checkpoint does not retain an enabled living actor ready for this component death.");
            _ = world.InitializeActorTemplates(identity, level, globals);
            Require(world.KillActor(identity, records.RuntimeFormKey(0x14), level, globals), "Owned component death did not settle.");
            var delay = FalloutGameSettingFloats.Read(records, "fDyingTimer");
            Require(world.AdvanceDeathEvent(identity, delay, delay, false), "Owned source death event was not consumed.");
            var sourceScripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                effect => throw new NotSupportedException("Unbound owned component death effect: " + effect.Kind),
                PlayerLevel: () => level, Globals: globals));
            var fault = sourceScripts.Dispatch(identity, "OnDeath");
            Require(fault.Error is not null && fault.Error.StartsWith("OnDeath:", StringComparison.Ordinal) &&
                state.ScriptError == fault.Error && state.Injury is { Dead: true, DeathEventPending: false },
                "Owned source did not retain a real consumed failing death invocation.");
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            RuntimeNativeNpc Assemble(FalloutReferenceWorld owner)
            {
                var actor = RuntimeNativeNpc.Create(records, content, reference, units, (_, _, _, _) => new StandardMaterial3D(),
                    owner.EquippedArmor(identity, level, globals), owner.InitializeActorTemplates(identity, level, globals), owner.ActorAppearanceOverride(identity));
                try
                {
                    var pose = owner.Placement(identity);
                    actor.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(pose.RotationRadians[0], pose.RotationRadians[1],
                        pose.RotationRadians[2]), reference.Scale), GamebryoCoordinate.ConvertVector(new(pose.Position[0], pose.Position[1], pose.Position[2])) * units);
                    RuntimeNativeActorContacts.Configure(actor, actor.Skeleton, 2);
                    actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                        owner, owner.Get(identity), records, content, 2, 3);
                    fixture.AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
                    actor.Combat.SetProcess(false); actor.Combat.SetPhysicsProcess(false);
                    return actor;
                }
                catch { actor.Free(); throw; }
            }
            async Task<RigidBody3D> FreezeBodies(RuntimeNativeNpc actor)
            {
                await Deferred();
                Require(actor.Combat!.Error is null && actor.Combat.Dead, "Actual source corpse has no prepared native owner: " + actor.Combat.Error);
                var rig = actor.GetChildren().OfType<RuntimeNativeActorRagdoll>().Single();
                rig.SetProcess(false); rig.SetPhysicsProcess(false);
                var bodies = rig.GetChildren().OfType<RigidBody3D>().ToArray();
                Require(rig.Active && bodies.Length > 0 && bodies.All(body => body.GetChildren().OfType<CollisionShape3D>()
                    .Any(shape => shape.Shape is not null)), "Owned loot aim has no actual source ragdoll collider.");
                foreach (var body in bodies) body.Freeze = true;
                return bodies[0];
            }
            var player = new FalloutPlayerInventory(); player.Restore(restored.Inventory, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
            world.BindPlayerInventory(player);
            var reports = new List<string>(); var completed = new List<bool>(); var interactionCount = 0;
            NativeOwnedContainerMenu? menu = null;
            RuntimeNativeReferenceEvents Bind(FalloutReferenceWorld owner, RuntimeNativeNpc actor, FalloutPlayerInventory playerInventory)
            {
                var events = new RuntimeNativeReferenceEvents { ReportDivergence = reports.Add };
                events.Configure(records, owner, quests, cell, fixture, new((_, _) => false, effect =>
                {
                    if (effect.Kind != FalloutReferenceEffectKind.DefaultActivate)
                        throw new NotSupportedException("Owned default-loot emitted an unrelated effect: " + effect.Kind);
                    events.DefaultActivate(effect.Target ?? effect.Source, effect.Argument);
                }, PlayerLevel: () => level, Globals: globals), _ => actor.Transform, units, 1);
                events.Interact = (placed, node, signature, activator) =>
                {
                    Require(placed.FormKey == identity && node == actor && signature == "NPC_" && owner.IsDead(identity) &&
                        owner.CanActivate(identity) && activator == records.RuntimeFormKey(0x14),
                        "Native default callback lost its real enabled corpse, model or player identity.");
                    if (menu is not null) return;
                    var inventory = owner.Inventory(identity, level, globals).Contents;
                    var full = records.GetEffective(placed.Base).ReadSubrecords().Single(row => row.Signature == "FULL");
                    menu = new(records, playerInventory, inventory, saved.PlayerName, FalloutDialogueTopic.Text(full.Data.Span), () => { }, () => { });
                    fixture.AddChild(menu);
                    Require(menu.Error is null, "Original container XML/font owner refused: " + menu.Error);
                    interactionCount++;
                };
                events.ObservePlayerActivationFinished = (_, success) => completed.Add(success);
                fixture.AddChild(events); events.SetProcess(false);
                return events;
            }
            var actor = Assemble(world);
            try
            {
                var collider = await FreezeBodies(actor);
                var inventory = world.Inventory(identity, level, globals).Contents;
                var before = JsonSerializer.Serialize(state.Capture());
                var questBefore = JsonSerializer.Serialize(quests.Capture());
                var events = Bind(world, actor, player);
                Require(events.TryActivate(collider) && !events.TryActivate(collider), "Owned default input was refused or queued twice.");
                Pump(events); Pump(events);
                Require(menu is not null && interactionCount == 1 && completed.SequenceEqual([true]) &&
                    JsonSerializer.Serialize(state.Capture()) == before && JsonSerializer.Serialize(quests.Capture()) == questBefore,
                    "Opening real corpse inventory lost its stopped invocation, source prefix, items or completion.");
                var loot = inventory.Items.Where(item => FalloutInventoryAccess.CanTransfer(records, records.GetEffective(item.FormKey), false)).ToArray();
                Require(loot.Length > 0, "Selected original corpse has no source loot to test.");
                var counts = loot.ToDictionary(item => item.FormKey, item => player.Item(item.FormKey)?.Count ?? 0);
                menu!.GetChildren().OfType<NativeBitmapMenuButton>().Single(button => button.Text == FalloutGameSettingStrings.Read(records, "sTakeAll"))
                    .EmitSignal(BaseButton.SignalName.Pressed);
                Require(loot.All(item => inventory.Item(item.FormKey) is null && player.Item(item.FormKey)?.Count == counts[item.FormKey] + item.Count) &&
                    state.ScriptError == fault.Error && JsonSerializer.Serialize(state.Capture() with { Inventory = null }) ==
                    JsonSerializer.Serialize(JsonSerializer.Deserialize<FalloutReferenceSnapshot>(before)! with { Inventory = null }),
                    "Original TakeAll did not use shared inventory or changed unrelated corpse state.");
                var suffix = JsonSerializer.Serialize(state.Capture());
                menu!.GetChildren().OfType<NativeBitmapMenuButton>().Single(button => button.Text == FalloutGameSettingStrings.Read(records, "sTakeAll"))
                    .EmitSignal(BaseButton.SignalName.Pressed);
                Require(JsonSerializer.Serialize(state.Capture()) == suffix, "Repeated TakeAll regenerated the source inventory.");
                menu!.Free(); menu = null; events.Free(); actor.Free();
                var retired = state.Capture();
                Require(JsonSerializer.Serialize(retired) == suffix, "Normal corpse retirement lost the fault or consumed inventory.");
                using var cold = new FalloutReferenceWorld(records);
                cold.RestoreEncounterZones(world.CaptureEncounterZones()); cold.Restore(world.Capture());
                cold.RestoreActorOverrides(world.CaptureActorOverrides()); cold.RestoreFactionRelations(world.CaptureFactionRelations()); cold.LoadCell(cell);
                var playerState = player.Capture(); var coldPlayer = new FalloutPlayerInventory();
                coldPlayer.Restore(playerState.Inventory, playerState.EquippedRuntimeFormIds.ToArray(), playerState.InventoryRandomState); cold.BindPlayerInventory(coldPlayer);
                var coldActor = Assemble(cold);
                try
                {
                    var coldCollider = await FreezeBodies(coldActor); var coldEvents = Bind(cold, coldActor, coldPlayer);
                    Require(coldEvents.TryActivate(coldCollider), "Cold actual corpse default activation was refused."); Pump(coldEvents);
                    Require(menu is not null && interactionCount == 2 && completed.SequenceEqual([true, true]) &&
                        cold.Get(identity).ScriptError == fault.Error &&
                        JsonSerializer.Serialize(cold.Get(identity).Capture().Variables) == JsonSerializer.Serialize(retired.Variables) &&
                        JsonSerializer.Serialize(cold.Get(identity).Capture().Inventory) == JsonSerializer.Serialize(retired.Inventory) &&
                        loot.All(item => coldPlayer.Item(item.FormKey)?.Count == counts[item.FormKey] + item.Count) &&
                        JsonSerializer.Serialize(quests.Capture()) == questBefore,
                        "Cold corpse lost the exact fault, prefix, real inventory or default callback.");
                    Require(ownedRecords.Zip(sourceHashes).All(pair => pair.Second.SequenceEqual(SHA256.HashData(pair.First.ReadData()))) &&
                        checkpointBytes.SequenceEqual(File.ReadAllBytes(checkpoint)), "Owned record or checkpoint source bytes changed.");
                    GD.Print($"OPENNV_OWNED_DEFAULT_LOOT_PASS reference={identity} script={script.Record.FormKey} " +
                        $"scriptSha256={Convert.ToHexString(sourceHashes[2])} sourceOnActivate=false componentDeath=true consumedDeathFault={JsonSerializer.Serialize(fault.Error)} " +
                        $"actualNativeBodies=true queuedOnce=true sourceContainerXml=true transferredKinds={loot.Length} retainedFaultAndPrefix=true " +
                        "coldInventoryAndDefault=true sourceAndCheckpointUnchanged=true campaignAndPixelsUnverified=true");
                    menu!.Free(); menu = null; coldEvents.Free();
                }
                finally { coldActor.Free(); }
            }
            finally { if (GodotObject.IsInstanceValid(actor)) actor.Free(); }
        }
        finally { fixture.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
