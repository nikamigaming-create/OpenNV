using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private async Task OwnedEndedSounds(string game, string mod, string root, string checkpoint,
        string actorId, string soundId, string[] dependencies, bool finiteRetirement = false, bool actorRetirement = false)
    {
        var original = File.ReadAllBytes(checkpoint);
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(original) ?? throw new InvalidDataException("Sound fixture checkpoint is absent.");
            if (saved.SaveCompatibilityId != content.SaveCompatibilityId || saved.References is null ||
                saved.Schema is not (FalloutNativeCampaignSave.ExpectedSchema or FalloutNativeCampaignSave.NativeSoundHistorySchema or FalloutNativeCampaignSave.TerminalResultsSchema))
                throw new InvalidDataException("Sound fixture requires its genuine complete matching current or preceding checkpoint.");
            static FalloutFormKey Key(string value)
            {
                var parts = value.Split(':');
                return parts.Length == 2 ? new(parts[0], Convert.ToUInt32(parts[1], 16)) : throw new ArgumentException("Expected plugin:hex-object-id.");
            }
            var caller = Key(actorId); var sound = Key(soundId);
            var quests = new FalloutQuestState(records); quests.Restore(saved.Quests!);
            var globals = FalloutGlobalState.Read(records); globals.Restore(saved.Globals!);
            var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records), FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe")));
            clock.Restore(saved.GameTime!);
            using var world = new FalloutReferenceWorld(records);
            world.RestoreEncounterZones(saved.EncounterZones); world.Restore(saved.References);
            world.RestoreActorOverrides(saved.ActorOverrides); world.RestoreFactionRelations(saved.FactionRelations);
            var cell = FalloutCellSceneReader.Read(records, world.Placement(caller).Cell); world.LoadCell(cell);
            var reference = cell.References.Single(value => value.FormKey == caller);
            if (!world.IsEnabled(caller) || records.GetEffective(reference.Base).Signature != "NPC_" || world.Get(caller).Injury?.Dead == true)
                throw new InvalidDataException("Sound fixture needs the checkpoint's actual enabled living source NPC.");
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            Transform3D Placement(FalloutReferenceWorld owner, FalloutPlacedReference placed)
            {
                var source = owner.Placement(placed.FormKey);
                return new(GamebryoCoordinate.ConvertReferenceEuler(new(source.RotationRadians[0], source.RotationRadians[1], source.RotationRadians[2]), placed.Scale),
                    GamebryoCoordinate.ConvertVector(new(source.Position[0], source.Position[1], source.Position[2])) * units);
            }
            RuntimeNativeNpc Assemble(FalloutReferenceWorld owner)
            {
                var templates = owner.InitializeActorTemplates(caller, saved.Vitals?.Level ?? 1, globals);
                var npc = RuntimeNativeNpc.Create(records, content, reference, units, (_, _, _, _) => new StandardMaterial3D(),
                    owner.EquippedArmor(caller, saved.Vitals?.Level ?? 1, globals), templates, owner.ActorAppearanceOverride(caller));
                try
                {
                    npc.Transform = Placement(owner, reference);
                    npc.Combat = RuntimeNativeActorCombat.Attach(npc, npc.Skeleton, npc.Appearance.SkeletonPath, owner, owner.Get(caller), records, content, 1, 1);
                    npc.ConfigureAi(records, quests, cell, placed => Placement(owner, placed), clock: clock, globals: globals, world: owner);
                    fixture.AddChild(npc); npc.SetProcess(false); npc.SetPhysicsProcess(false); npc.Combat!.SetPhysicsProcess(false);
                    return npc;
                }
                catch { npc.Free(); throw; }
            }
            var warm = Assemble(world);
            using var warmLifetime = new PackageFixtureLifetime(warm);
            var camera = new Camera3D { Position = warm.Position + Vector3.Up, Current = true }; fixture.AddChild(camera);
            var events = world.Get(caller).AnimationSoundEvents;
            var sounds = new NativeOwnedAnimationSoundPlayer(records, content, warm, units, world.Get(caller).SoundRandom, events);
            warm.AddChild(sounds);
            if (actorRetirement)
            {
                await OwnedActorFiniteRetirement(records, content, world, warm, sounds, events, cell, caller, sound, original,
                    saved.Vitals?.Level ?? 1, globals, Assemble);
                return;
            }
            if (finiteRetirement)
            {
                await OwnedFiniteSoundRetirement(records, content, world, warm, sounds, events, cell, caller, sound, original);
                return;
            }
            for (var ordinal = 0; ordinal < 3; ordinal++)
            {
                sounds.DispatchSound(sound);
                var active = events.Events.Last();
                if (!active.Played || active.End != FalloutAnimationSoundEnd.Active || sounds.CanCaptureSilent)
                    throw new InvalidDataException("Owned source sound did not establish its actual unfinished generation.");
                var timeout = Stopwatch.StartNew();
                while (events.Events.Last().End == FalloutAnimationSoundEnd.Active && timeout.Elapsed.TotalSeconds < 10)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (events.Events.Last().End != FalloutAnimationSoundEnd.NativeFinished)
                    throw new InvalidDataException("Owned sound failed to deliver native Finished; zero voices are insufficient.");
            }
            if (!sounds.CanCaptureSilent || sounds.Unbound.Count == 0 || events.Events.Any(entry => entry.PartialLanes.Count == 0))
                throw new InvalidDataException("Ended partial sound lost its history or remained a capture blocker.");
            // Exercise the real receiver's source predicate failure in this
            // disposable component. This is no campaign hit or combat proof.
            var part = world.BodyParts(caller).Parts.First().Type;
            var hit = world.DamageActor(caller, caller, part, .25f, 1, saved.Vitals?.Level ?? 1, globals);
            typeof(RuntimeNativeActorCombat).GetMethod("RequestHitReaction", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(warm.Combat, [hit, (int)part]);
            if (world.Get(caller).HitReaction is not null || world.Get(caller).HitReactionFaults.Faults.Count == 0 ||
                world.Get(caller).CurrentHitReactionError is null)
                throw new InvalidDataException("Owned source did not retain its actual stopped predicate-read fault.");
            var before = world.Capture();
            fixture.RemoveChild(warm); world.UnloadCell(cell.Cell.FormKey);
            if (JsonSerializer.Serialize(world.Capture()) != JsonSerializer.Serialize(before))
                throw new InvalidDataException("Child-first retirement changed finished sounds or stopped source history.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(before))!); cold.LoadCell(cell);
            var resumed = Assemble(cold);
            using var resumedLifetime = new PackageFixtureLifetime(resumed);
            var coldEvents = cold.Get(caller).AnimationSoundEvents;
            var coldSounds = new NativeOwnedAnimationSoundPlayer(records, content, resumed, units, cold.Get(caller).SoundRandom, coldEvents);
            resumed.AddChild(coldSounds);
            if (!coldSounds.CanCaptureSilent || coldSounds.Unbound.Count != sounds.Unbound.Count ||
                resumed.FindChildren("*", "", true, false).Any(node => node is AudioStreamPlayer or AudioStreamPlayer3D) ||
                JsonSerializer.Serialize(cold.Capture()) != JsonSerializer.Serialize(before))
                throw new InvalidDataException("Cold source history replayed a voice or changed fault, random, pose or audio history.");
            coldSounds.DispatchSound(sound);
            var interrupted = coldSounds.ActiveNativeVoices.Single();
            interrupted.GetParent().RemoveChild(interrupted); interrupted.Free();
            if (coldEvents.Events.Last().End != FalloutAnimationSoundEnd.Cancelled || coldSounds.CanCaptureSilent)
                throw new InvalidDataException("Emitter teardown was mistaken for genuine native completion.");
            var refused = false;
            try { cold.Capture(); } catch (NotSupportedException) { refused = true; }
            if (!refused) throw new InvalidDataException("Cancelled source voice entered a complete checkpoint.");
            GD.Print("OPENNV_OWNED_ENDED_SOUND_PASS " + JsonSerializer.Serialize(new
            {
                runtimeBuild = typeof(RuntimeNativeActorCombat).Assembly.ManifestModule.ModuleVersionId,
                reference = caller.ToString(),
                sound = sound.ToString(),
                sourceSha256 = Convert.ToHexString(SHA256.HashData(records.GetEffective(sound).ReadData())),
                receipts = before.Single(value => value.Reference == caller).AnimationSoundEvents,
                hitFaults = before.Single(value => value.Reference == caller).HitReactionFaults,
                nativeFinished = 3,
                partialRetained = true,
                retirement = true,
                coldNoReplay = true,
                cancelledRefused = true,
                checkpointSha256 = Convert.ToHexString(SHA256.HashData(original)),
                boundary = "owned-source-event-and-stopped-read-component;campaign-input-output-audio-and-parity-unverified"
            }));
        }
        finally
        {
            fixture.Free();
            if (!original.AsSpan().SequenceEqual(File.ReadAllBytes(checkpoint))) throw new InvalidDataException("Sound fixture changed its genuine checkpoint input.");
        }
    }
}
