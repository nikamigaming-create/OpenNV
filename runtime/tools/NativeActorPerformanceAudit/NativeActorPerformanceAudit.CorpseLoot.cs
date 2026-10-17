using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private async Task SavedEquippedCorpseLoot(string game, string mod, string root, string checkpoint,
        string actorId, string[] dependencies)
    {
        var fixture = new Node3D(); AddChild(fixture);
        var bytes = File.ReadAllBytes(checkpoint);
        try
        {
            var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
            var opening = FalloutCellSceneReader.Read(records, new("FalloutNV.esm", 0x103df9));
            var restored = FalloutNativeCampaignSave.Read(checkpoint, content.SaveCompatibilityId, records,
                FalloutNativeVigorResolver.Resolve(records, opening), FalloutNativeTagSkillResolver.Resolve(records, controls),
                FalloutOpeningInventoryGrantResolver.Resolve(records, controls, "VCG01"),
                FalloutNativeTraitFarewellResolver.Resolve(records, controls, opening));
            var saved = restored.State;
            var fields = actorId.Split(':');
            if (fields.Length != 2) throw new ArgumentException("Equipped loot requires plugin:hex-reference.");
            var reference = new FalloutFormKey(fields[0], Convert.ToUInt32(fields[1], 16));
            var originalReferences = saved.References ?? throw new InvalidDataException("Equipped loot checkpoint has no complete reference world.");
            var original = originalReferences.Single(snapshot => snapshot.Reference == reference);
            if (records.GetEffective(reference).Signature != "ACHR" || original.Injury?.Dead != true ||
                original.CorpseEquipment is not { Attachments.Count: > 0, HandlingWeapon: not null } ||
                original.Ragdoll is not { Bodies.Count: > 0 } || original.Ragdoll.Bodies.Any(body => !body.Sleeping))
                throw new InvalidDataException("Equipped loot requires a genuine complete, already-dead, settled equipped corpse checkpoint.");
            var globals = FalloutGlobalState.Read(records); globals.Restore(saved.Globals!);
            var quests = new FalloutQuestState(records); quests.Restore(saved.Quests!);
            var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records),
                FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe"))); clock.Restore(saved.GameTime!);
            var config = RuntimeConfiguration.Load(); var units = config.World.GameUnitsToMeters;
            var level = saved.Vitals!.Level;
            FalloutReferenceWorld Restore(IReadOnlyList<FalloutReferenceSnapshot> snapshots)
            {
                var world = new FalloutReferenceWorld(records);
                world.RestoreEncounterZones(saved.EncounterZones); world.Restore(snapshots);
                world.RestoreActorOverrides(saved.ActorOverrides); world.RestoreFactionRelations(saved.FactionRelations);
                return world;
            }
            using var world = Restore(originalReferences);
            var sourceCell = FalloutCellSceneReader.Read(records, world.Placement(reference).Cell);
            var placed = sourceCell.References.Single(value => value.FormKey == reference);
            var cell = sourceCell with { References = [placed] }; world.LoadCell(cell);
            Transform3D Placement(FalloutReferenceWorld owner, FalloutPlacedReference source)
            {
                var pose = owner.Placement(source.FormKey);
                return new(GamebryoCoordinate.ConvertReferenceEuler(new(pose.RotationRadians[0], pose.RotationRadians[1],
                    pose.RotationRadians[2]), source.Scale), GamebryoCoordinate.ConvertVector(new(pose.Position[0], pose.Position[1], pose.Position[2])) * units);
            }
            RuntimeNativeNpc Assemble(FalloutReferenceWorld owner, bool sourceLiving = false)
            {
                var actor = RuntimeNativeNpc.Create(records, content, placed, units, (_, _, _, _) => new StandardMaterial3D(),
                    owner.EquippedArmor(reference, level, globals), owner.InitializeActorTemplates(reference, level, globals),
                    owner.ActorAppearanceOverride(reference));
                try
                {
                    actor.Transform = Placement(owner, placed);
                    RuntimeNativeActorContacts.Configure(actor, actor.Skeleton, 2);
                    var context = new NativeActorCombatContext(() => null, () => saved.Vitals,
                        (_, _) => throw new InvalidDataException("Loot fixture cannot apply gameplay damage."),
                        (_, _) => throw new InvalidDataException("Loot fixture cannot plan combat movement."), _ => true,
                        () => level, globals, config.Player.StepHeightMeters, config.Simulation.GravityMetersPerSecondSquared);
                    actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                        owner, owner.Get(reference), records, content, 2, 3, context);
                    if (!sourceLiving) actor.ConfigureAi(records, quests, cell, source => Placement(owner, source), clock: clock, globals: globals, world: owner);
                    fixture.AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
                    actor.Combat.SetProcess(false); actor.Combat.SetPhysicsProcess(false);
                    return actor;
                }
                catch { actor.Free(); throw; }
            }
            async Task<RigidBody3D[]> Bodies(RuntimeNativeNpc actor)
            {
                var deferred = new TaskCompletionSource(); Callable.From(() => deferred.SetResult()).CallDeferred();
                await deferred.Task;
                RequireLoot(actor.Combat!.Error is null && actor.Combat.Dead, "Deferred original corpse owner failed: " + actor.Combat.Error);
                var rig = actor.GetChildren().OfType<RuntimeNativeActorRagdoll>().Single();
                var bodies = rig.GetChildren().OfType<RigidBody3D>().ToArray();
                RequireLoot(rig.Settled && bodies.Length == original.Ragdoll.Bodies.Count, "Original native physical corpse is not actually settled.");
                rig.SetProcess(false); rig.SetPhysicsProcess(false);
                // Preserve this admitted snapshot during the UI component.
                // This does not create or claim source death/collapse gameplay.
                foreach (var body in bodies) body.Freeze = true;
                return bodies;
            }
            var actor = Assemble(world); using var lifetime = new PackageFixtureLifetime(actor);
            var bodies = await Bodies(actor);
            var state = world.Get(reference);
            var inventory = world.Inventory(reference, level, globals).Contents;
            var player = new FalloutPlayerInventory(restored.State.InventoryRandomState);
            player.Restore(restored.Inventory, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
            world.BindPlayerInventory(player);
            var before = state.Capture(); var history = LootHistory(before);
            var attachments = new[] { CorpseField("_enemyObject").GetValue(actor.Combat), CorpseField("_packageWeapon").GetValue(actor.Combat) }
                .OfType<NativeActorWeaponAttachment>().ToArray();
            var graph = JsonSerializer.Serialize(before.CorpseEquipment!.Attachments);
            var skeletonChildren = actor.Skeleton.Node.GetChildren().Select(node => node.GetInstanceId()).ToArray();
            var counts = JsonSerializer.Serialize(new { Source = inventory.Capture(), Target = player.Capture() });
            var revisions = (inventory.Revision, player.Revision);
            NativeOwnedContainerMenu? menu = null; var changes = 0; var activations = 0;
            var events = new RuntimeNativeReferenceEvents { ReportDivergence = message => GD.Print("OPENNV_CORPSE_LOOT_SOURCE_BOUNDARY " + message) };
            events.Configure(records, world, quests, cell, fixture, new((_, _) => false, effect =>
            {
                if (effect.Kind != FalloutReferenceEffectKind.DefaultActivate)
                    throw new NotSupportedException("Loot fixture has no host effect for " + effect.Kind);
                events.DefaultActivate(effect.Target ?? effect.Source, effect.Argument);
            }, PlayerLevel: () => level, Globals: globals), source => Placement(world, source), units, 1);
            events.Interact = (target, node, signature, activator) =>
            {
                RequireLoot(target.FormKey == reference && node == actor && signature == "NPC_" &&
                    activator == records.RuntimeFormKey(0x14) && world.IsDead(reference), "Ordinary default loot lost its actual corpse/collider identity.");
                if (menu is not null) return;
                menu = new(records, player, inventory, saved.PlayerName,
                    FalloutDialogueTopic.Text(records.GetEffective(original.Base).ReadSubrecords().Single(field => field.Signature == "FULL").Data.Span),
                    () => { }, () => changes++);
                fixture.AddChild(menu); activations++;
            };
            fixture.AddChild(events); events.SetProcess(false);
            RequireLoot(events.TryActivate(bodies[0]), "Original corpse collider refused the ordinary default activation.");
            for (var phase = 0; phase < 2; phase++) { events.SetProcess(true); events._Process(0); events.SetProcess(false); }
            RequireLoot(menu is not null && menu.Error is null && activations == 1, "Original container XML/default activation failed: " + menu?.Error);
            void TakeAll() => menu!.GetChildren().OfType<NativeBitmapMenuButton>()
                .Single(button => button.Text == FalloutGameSettingStrings.Read(records, "sTakeAll")).EmitSignal(BaseButton.SignalName.Pressed);
            void Unchanged()
            {
                RequireLoot(counts == JsonSerializer.Serialize(new { Source = inventory.Capture(), Target = player.Capture() }) &&
                    revisions == (inventory.Revision, player.Revision) && graph == JsonSerializer.Serialize(state.Capture().CorpseEquipment!.Attachments) &&
                    skeletonChildren.SequenceEqual(actor.Skeleton.Node.GetChildren().Select(node => node.GetInstanceId())) &&
                    attachments.All(attachment => GodotObject.IsInstanceValid(attachment.Attachment) && attachment.Attachment.IsInsideTree()),
                    "Refused UI transfer changed actual item/condition/revision data or removed/reordered native equipment.");
            }
            CorpseField("_pendingHitscanImpacts").SetValue(actor.Combat, 1);
            try
            {
                TakeAll();
                RequireLoot(menu!.TransferError is not null && menu.GetNode<Label>("ContainerTransferRefusal").Visible &&
                    changes == 0 && attachments.All(attachment => attachment.Attachment.IsInsideTree()) &&
                    counts == JsonSerializer.Serialize(new { Source = inventory.Capture(), Target = player.Capture() }),
                    "Pending native attack continuation was waived or hidden by ordinary Take All.");
            }
            finally { CorpseField("_pendingHitscanImpacts").SetValue(actor.Combat, 0); }
            Unchanged();
            var sleeping = bodies[0].Sleeping;
            bodies[0].Sleeping = false;
            try
            {
                TakeAll();
                RequireLoot(menu!.TransferError is not null && changes == 0 &&
                    counts == JsonSerializer.Serialize(new { Source = inventory.Capture(), Target = player.Capture() }) &&
                    attachments.All(attachment => attachment.Attachment.IsInsideTree()), "Awake native corpse admitted an equipment transfer.");
            }
            finally { bodies[0].Sleeping = sleeping; }
            var visual = CorpseField("_corpseVisualBoundary").GetValue(actor.Combat);
            CorpseField("_corpseVisualBoundary").SetValue(actor.Combat, "Diagnostic unowned equipment visual continuation.");
            try
            {
                TakeAll();
                RequireLoot(menu!.TransferError is not null && changes == 0 &&
                    counts == JsonSerializer.Serialize(new { Source = inventory.Capture(), Target = player.Capture() }) &&
                    attachments.All(attachment => attachment.Attachment.IsInsideTree()), "Opaque native equipment admitted or disappeared before transfer.");
            }
            finally { CorpseField("_corpseVisualBoundary").SetValue(actor.Combat, visual); }
            Unchanged();
            var finiteAudio = false;
            var selectedWeapon = FalloutWeaponPresentation.Read(records, before.CorpseEquipment.HandlingWeapon!.Weapon, false);
            var finiteSound = selectedWeapon.Sounds.Values.Select(key => FalloutSoundRecordReader.Read(records, key))
                .FirstOrDefault(sound => (sound.Flags & FalloutSoundFlags.Loop) == 0);
            if (finiteSound is not null)
            {
                var sounds = new NativeOwnedAnimationSoundPlayer(records, content, actor, units, state.SoundRandom, state.AnimationSoundEvents);
                actor.AddChild(sounds); CorpseField("_enemySounds").SetValue(actor.Combat, sounds);
                sounds.DispatchSound(finiteSound.FormKey);
                var generation = state.AnimationSoundEvents.Events.Last().Generation;
                RequireLoot(sounds.ActiveNativeVoices.Any() && sounds.CanAwaitFiniteCompletion,
                    "Original equipment sound has no actual finite native binding.");
                TakeAll();
                RequireLoot(menu!.TransferError is not null && changes == 0 &&
                    counts == JsonSerializer.Serialize(new { Source = inventory.Capture(), Target = player.Capture() }) &&
                    attachments.All(attachment => attachment.Attachment.IsInsideTree()), "An active actual finite voice was waived by corpse loot.");
                var wait = Stopwatch.StartNew();
                while (!sounds.CanCaptureSilent && wait.Elapsed.TotalSeconds < 10)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                RequireLoot(sounds.CanCaptureSilent && state.AnimationSoundEvents.Events.Single(entry => entry.Generation == generation)
                    .End == FalloutAnimationSoundEnd.NativeFinished, "Actual source equipment voice did not genuinely finish.");
                finiteAudio = true; history = LootHistory(state.Capture());
                Unchanged();
            }
            player.PrepareChange = (_, _) => new(() =>
            {
                RequireLoot(inventory.Item(before.CorpseEquipment.HandlingWeapon!.Weapon) is null &&
                    attachments.All(attachment => GodotObject.IsInstanceValid(attachment.Attachment) && attachment.Attachment.GetParent() is null),
                    "Equipment retired irreversibly or before accepted inventory publication.");
                throw new InvalidDataException("Selected target-owner commit failure.");
            }, () => { });
            try
            {
                TakeAll();
                RequireLoot(menu!.TransferError is not null && changes == 0, "Post-publication owner failure was falsely reported as successful loot.");
                Unchanged();
            }
            finally { player.PrepareChange = null; }
            var loot = inventory.Items.Where(item => FalloutInventoryAccess.CanTransfer(records, records.GetEffective(item.FormKey), false)).ToArray();
            var targetBefore = player.Capture();
            TakeAll();
            var after = state.Capture();
            RequireLoot(menu!.Error is null && changes == 1 &&
                after.CorpseEquipment is { Attachments.Count: 0, HandlingWeapon: null, WeaponHandling: null } &&
                after.Engagement?.WeaponHandling is null && attachments.All(attachment => !GodotObject.IsInstanceValid(attachment.Attachment)) &&
                loot.All(item => inventory.Item(item.FormKey) is null &&
                    player.Item(item.FormKey)!.Count == (targetBefore.Inventory.Items.SingleOrDefault(value => value.FormKey == item.FormKey)?.Count ?? 0) + item.Count) &&
                LootHistory(after) == history, "Ordinary Take All did not retire only transferred current equipment or changed original source/corpse history.");
            var playerAfter = JsonSerializer.Serialize(player.Capture());
            TakeAll();
            RequireLoot(playerAfter == JsonSerializer.Serialize(player.Capture()) && LootHistory(state.Capture()) == history,
                "Repeated ordinary Take All manufactured inventory or replayed a source owner.");
            menu.Free(); menu = null; events.Free();
            fixture.RemoveChild(actor); world.UnloadCell(cell.Cell.FormKey);
            var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = Restore(snapshots); cold.LoadCell(cell);
            var resumed = Assemble(cold); using var coldLifetime = new PackageFixtureLifetime(resumed);
            _ = await Bodies(resumed);
            var actual = cold.Get(reference).Capture();
            RequireLoot(actual.CorpseEquipment is { Attachments.Count: 0, HandlingWeapon: null, WeaponHandling: null } &&
                CorpseField("_enemyObject").GetValue(resumed.Combat) is null && CorpseField("_packageWeapon").GetValue(resumed.Combat) is null &&
                CorpseField("_enemyWeaponHandling").GetValue(resumed.Combat) is null &&
                JsonSerializer.Serialize(actual.Inventory) == JsonSerializer.Serialize(after.Inventory) &&
                LootHistory(actual with { Ragdoll = after.Ragdoll }) == history, "Cold native binding recreated transferred weapon equipment or lost the retained corpse fault.");
            await CorpseLootRemoveItemFixture(records, Restore, owner => Assemble(owner), Bodies, originalReferences, reference, original, player);
            CorpseLootLivingRefusal(records, owner => Assemble(owner, sourceLiving: true), cell, reference, player);
            RequireLoot(bytes.SequenceEqual(File.ReadAllBytes(checkpoint)), "Equipped loot component modified the genuine checkpoint.");
            GD.Print($"OPENNV_NATIVE_EQUIPPED_CORPSE_LOOT_PASS actor={reference} bodies={bodies.Length} attachments={attachments.Length} " +
                $"ordinaryDefaultAndUi=true noEarlyRetirement=true pendingAttackRefused=true movingOpaqueRefused=true finiteNativeAudio={finiteAudio} " +
                "postCommitRollback=true nodeOrderRestored=true " +
                "itemConditionOwnershipConservation=true retainedSourceFault=true nativeCurrentColdAndRetirement=true sourceRemoveItem=true " +
                "livingPresentedRefused=true recording=false fixture=owned-component campaignDropPhysicsAndRetail=unverified");
        }
        finally { if (GodotObject.IsInstanceValid(fixture)) fixture.Free(); }
    }

    private static async Task CorpseLootRemoveItemFixture(FalloutPluginStack records,
        Func<IReadOnlyList<FalloutReferenceSnapshot>, FalloutReferenceWorld> restore,
        Func<FalloutReferenceWorld, RuntimeNativeNpc> assemble, Func<RuntimeNativeNpc, Task<RigidBody3D[]>> bodies,
        IReadOnlyList<FalloutReferenceSnapshot> snapshots, FalloutFormKey reference, FalloutReferenceSnapshot original,
        FalloutPlayerInventory player)
    {
        using var world = restore(snapshots);
        var cell = FalloutCellSceneReader.Read(records, world.Placement(reference).Cell) with
        { References = [] };
        var placed = FalloutCellSceneReader.Read(records, cell.Cell.FormKey).References.Single(value => value.FormKey == reference);
        cell = cell with { References = [placed] }; world.LoadCell(cell);
        var actor = assemble(world); using var lifetime = new PackageFixtureLifetime(actor);
        _ = await bodies(actor);
        var state = world.Get(reference); var before = state.Capture();
        var selected = original.CorpseEquipment!.HandlingWeapon!.Weapon;
        var count = state.Inventory!.Contents.Item(selected)!.Count;
        var commands = new FalloutInventoryCommands(records, world, player, () => state.Templates?.Level ?? 1,
            prepareActorChange: _ => actor.Combat!.PrepareInventoryChange());
        commands.Execute(new(FalloutInventoryCommandKind.Remove, reference, selected, Count: count));
        var actual = state.Capture();
        RequireLoot(actual.CorpseEquipment!.Attachments.All(attachment => attachment.Source.Weapon != selected) &&
            actual.CorpseEquipment.HandlingWeapon is null && actual.Engagement?.WeaponHandling is null &&
            state.Inventory.Contents.Item(selected) is null &&
            before.Inventory!.Contents.Inventory.Items.Where(item => item.FormKey != selected).All(item =>
                JsonSerializer.Serialize(state.Inventory.Contents.Item(item.FormKey)) == JsonSerializer.Serialize(item)) &&
            LootHistory(actual) == LootHistory(before), "Actual source RemoveItem retired unrelated native owners or changed ammo/condition/source faults.");
        actor.GetParent().RemoveChild(actor); world.UnloadCell(cell.Cell.FormKey);
        using var cold = restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
        cold.LoadCell(cell);
        var resumed = assemble(cold); using var resumedLifetime = new PackageFixtureLifetime(resumed);
        _ = await bodies(resumed);
        RequireLoot(cold.Get(reference).Inventory!.Contents.Item(selected) is null &&
            cold.Get(reference).Capture().CorpseEquipment!.Attachments.All(attachment => attachment.Source.Weapon != selected) &&
            CorpseField("_enemyWeaponHandling").GetValue(resumed.Combat) is null,
            "Cold source RemoveItem recreated its removed actual equipment or handling.");
    }

    private static void CorpseLootLivingRefusal(FalloutPluginStack records, Func<FalloutReferenceWorld, RuntimeNativeNpc> assemble,
        FalloutCellScene cell, FalloutFormKey reference, FalloutPlayerInventory player)
    {
        // Fresh source defaults are an isolated negative, not a resurrection of
        // the saved corpse or a modification of campaign health/inventory.
        using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
        var actor = assemble(world); using var lifetime = new PackageFixtureLifetime(actor);
        RequireLoot(!actor.Combat!.Dead && world.Health(reference).Current > 0, "Selected original source is not a living negative fixture.");
        var state = world.Get(reference);
        state.Engagement = new(records.RuntimeFormKey(0x14));
        typeof(RuntimeNativeActorCombat).GetMethod("PrepareCombatPresentation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(actor.Combat, null);
        var attachment = (NativeActorWeaponAttachment)CorpseField("_enemyObject").GetValue(actor.Combat)!;
        var inventory = world.Inventory(reference, state.Templates?.Level ?? 1).Contents;
        var before = JsonSerializer.Serialize(new { Source = inventory.Capture(), Target = player.Capture() });
        var refused = false;
        try { inventory.TransferTo(player, attachment.Weapon.Form, inventory.Item(attachment.Weapon.Form)!.Count); }
        catch (NotSupportedException) { refused = true; }
        RequireLoot(refused && attachment.Attachment.IsInsideTree() &&
            before == JsonSerializer.Serialize(new { Source = inventory.Capture(), Target = player.Capture() }),
            "Living presented equipment was removed or transferred without its living animation retirement owner.");
    }

    private static string LootHistory(FalloutReferenceSnapshot snapshot) => JsonSerializer.Serialize(snapshot with
    { Inventory = null, CorpseEquipment = null, Engagement = snapshot.Engagement is { } history ? history with { WeaponHandling = null } : null });

    private static void RequireLoot(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
