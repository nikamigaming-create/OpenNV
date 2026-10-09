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
    private async Task OwnedAttackVariants(string game, string mod, string root, string checkpoint, string actorId, string[] dependencies,
        bool routeLifecycle = false)
    {
        var fixture = new Node3D(); AddChild(fixture);
        var savedBytes = File.ReadAllBytes(checkpoint);
        try
        {
            var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
            var opening = FalloutCellSceneReader.Read(records, new("FalloutNV.esm", 0x103df9));
            var saved = FalloutNativeCampaignSave.Read(checkpoint, content.SaveCompatibilityId, records).State;
            var identity = actorId.Split(':');
            if (identity.Length != 2) throw new ArgumentException("Attack fixture requires plugin:hex-object-id.");
            var caller = new FalloutFormKey(identity[0], Convert.ToUInt32(identity[1], 16));
            if (records.GetEffective(caller).Signature != "ACHR") throw new InvalidDataException("Attack fixture is not a winning ACHR.");
            var globals = FalloutGlobalState.Read(records); globals.Restore(saved.Globals!);
            using var world = new FalloutReferenceWorld(records);
            world.RestoreEncounterZones(saved.EncounterZones); world.Restore(saved.References!);
            world.RestoreActorOverrides(saved.ActorOverrides); world.RestoreFactionRelations(saved.FactionRelations);
            var state = world.Get(caller);
            if (records.GetEffective(state.Base).Signature != "NPC_" || state.Injury?.Dead == true)
                throw new InvalidDataException("Attack fixture needs its actual living source NPC.");
            var originalEngagement = state.Engagement;
            if (originalEngagement is not null)
                throw new InvalidDataException("Independent attack assembly fixture needs an unengaged source checkpoint actor.");
            var cell = FalloutCellSceneReader.Read(records, world.Placement(caller).Cell); world.LoadCell(cell);
            var placed = cell.References.Single(reference => reference.FormKey == caller);
            var config = RuntimeConfiguration.Load();
            var vitals = saved.Vitals ?? throw new InvalidDataException("Attack checkpoint has no player vitals.");
            var level = vitals.Level;
            var units = config.World.GameUnitsToMeters;
            var context = new NativeActorCombatContext(() => null, () => vitals,
                (_, _) => throw new InvalidDataException("Attack assembly proof attempted gameplay damage."),
                (_, _) => throw new InvalidDataException("Attack assembly proof attempted route planning."),
                _ => true, () => level, globals, config.Player.StepHeightMeters, config.Simulation.GravityMetersPerSecondSquared);
            RuntimeNativeNpc Assemble(FalloutReferenceWorld owner)
            {
                var npc = RuntimeNativeNpc.Create(records, content, placed, units, (_, _, _, _) => new StandardMaterial3D(),
                    owner.EquippedArmor(caller, level, globals), owner.InitializeActorTemplates(caller, level, globals),
                    owner.ActorAppearanceOverride(caller));
                try
                {
                    var pose = owner.Placement(caller);
                    npc.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(pose.RotationRadians[0], pose.RotationRadians[1],
                        pose.RotationRadians[2]), placed.Scale), GamebryoCoordinate.ConvertVector(new(pose.Position[0], pose.Position[1], pose.Position[2])) * units);
                    npc.Combat = RuntimeNativeActorCombat.Attach(npc, npc.Skeleton, npc.Appearance.SkeletonPath,
                        owner, owner.Get(caller), records, content, 2, 3, context);
                    fixture.AddChild(npc); npc.SetProcess(false); npc.SetPhysicsProcess(false);
                    npc.Combat.SetProcess(false); npc.Combat.SetPhysicsProcess(false);
                    return npc;
                }
                catch { npc.Free(); throw; }
            }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var prepare = typeof(RuntimeNativeActorCombat).GetMethod("PrepareCombatPresentation", flags)!;
            var publish = typeof(RuntimeNativeActorCombat).GetMethod("PublishCombatPose", flags)!;
            var end = typeof(RuntimeNativeActorCombat).GetMethod("EndEngagement", flags)!;
            var provoke = typeof(RuntimeNativeActorCombat).GetMethod("Provoke", flags)!;
            var pathsField = typeof(RuntimeNativeActorCombat).GetField("_attackPaths", flags)!;
            var clipsField = typeof(RuntimeNativeActorCombat).GetField("_combatClips", flags)!;
            state.AttackRandom.Restore(17);
            state.Engagement = new(records.RuntimeFormKey(0x14), AttackRandomState: 17);
            var actor = Assemble(world); using var lifetime = new PackageFixtureLifetime(actor);
            prepare.Invoke(actor.Combat, null);
            if (state.Engagement!.AttackRandomState != 17 || state.Engagement.Animation is not null)
                throw new InvalidDataException("Native attack preparation selected a KF or consumed a variant draw.");
            var paths = (IReadOnlyList<string>)pathsField.GetValue(actor.Combat)!;
            var clips = (Dictionary<string, NativeActorCombatAnimation>)clipsField.GetValue(actor.Combat)!;
            if (routeLifecycle)
            {
                await OwnedRouteLifecycle(fixture, actor, world, records, content, saved, cell, globals, checkpoint, savedBytes);
                return;
            }
            // Restore the legal checkpoint history before applying an isolated
            // combat component clock. A source AI fault cannot admit a moving
            // attack/pursuit as a whole campaign save.
            var fixtureReferences = saved.References!.Where(reference => reference.Reference != caller)
                .Append(state.Capture() with { Engagement = originalEngagement }).ToArray();
            if (paths.Count < 2) throw new InvalidDataException("Selected source actor has no multiple-variant proof domain.");
            var chosen = FalloutAttackAnimationSelection.Bind(state.Engagement!.Transition("attack"), paths, source => clips[source].Hash);
            if (chosen.AttackRandomState == 17) throw new InvalidDataException("Owned action start did not consume its variant draw.");
            var selectedClip = clips[chosen.Animation!];
            var mid = chosen with { Seconds = selectedClip.Duration / 2, StartPending = false };
            var restoredChoice = JsonSerializer.Deserialize<FalloutActorEngagement>(JsonSerializer.Serialize(mid))!;
            // Collection fields are reconstructed by JSON; compare their exact
            // persistent values rather than the record's collection identities.
            static bool SameAttackState(FalloutActorEngagement first, FalloutActorEngagement second) =>
                JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
            if (!SameAttackState(FalloutAttackAnimationSelection.Bind(restoredChoice, paths, source => clips[source].Hash), mid) ||
                !SameAttackState(FalloutAttackAnimationSelection.Bind(mid.Transition("attack"), paths, source => clips[source].Hash),
                    FalloutAttackAnimationSelection.Bind(restoredChoice.Transition("attack"), paths, source => clips[source].Hash)))
                throw new InvalidDataException("Owned selected action lost its clock, draw state or next cold selection.");
            var prefix = selectedClip.Events.Crossed(0, mid.Seconds, true).ToArray();
            var remainder = selectedClip.Events.Crossed(restoredChoice.Seconds, selectedClip.Duration, restoredChoice.StartPending).ToArray();
            if (!prefix.Concat(remainder).SequenceEqual(selectedClip.Events.Crossed(0, selectedClip.Duration, true)))
                throw new InvalidDataException("Owned cold action replayed or omitted a source text key.");
            var reports = new List<object>();
            for (var index = 0; index < paths.Count; index++)
            {
                var path = paths[index]; var clip = clips[path];
                if (FalloutAttackAnimationSelection.Select(paths, () => (uint)index) != path || clip.Events.Discharges == 0)
                    throw new InvalidDataException("Native source alternative lost explicit indexing or authored discharge.");
                var active = new FalloutActorEngagement(records.RuntimeFormKey(0x14), "attack", .3, false,
                    path, clip.Hash, AttackRandomState: 17);
                var retained = JsonSerializer.Deserialize<FalloutActorEngagement>(JsonSerializer.Serialize(active))!;
                if (!SameAttackState(FalloutAttackAnimationSelection.Bind(retained, paths, source => clips[source].Hash), active))
                    throw new InvalidDataException("Owned cold attack redrew or changed its selected KF/hash/clock.");
                var expected = new List<Transform3D[]>();
                foreach (var seconds in new[] { 0d, clip.Duration / 2, clip.Duration })
                {
                    publish.Invoke(actor.Combat, [clip, clip.Time(seconds), seconds]);
                    var poses = BonePoses(actor);
                    if (poses.Any(pose => !pose.Origin.IsFinite() || !pose.Basis.X.IsFinite() || !pose.Basis.Y.IsFinite() || !pose.Basis.Z.IsFinite()))
                        throw new InvalidDataException("Owned native attack produced a nonfinite skeletal pose.");
                    expected.Add(poses);
                }
                using var cold = new FalloutReferenceWorld(records);
                cold.RestoreEncounterZones(saved.EncounterZones); cold.Restore(fixtureReferences);
                cold.RestoreActorOverrides(saved.ActorOverrides); cold.RestoreFactionRelations(saved.FactionRelations); cold.LoadCell(cell);
                cold.Get(caller).Engagement = retained;
                var resumed = Assemble(cold); using var resumedLifetime = new PackageFixtureLifetime(resumed);
                prepare.Invoke(resumed.Combat, null);
                if (!SameAttackState(cold.Get(caller).Engagement!, retained)) throw new InvalidDataException("Cold native preparation changed retained variant state.");
                var coldClips = (Dictionary<string, NativeActorCombatAnimation>)clipsField.GetValue(resumed.Combat)!;
                var sample = 0;
                foreach (var seconds in new[] { 0d, clip.Duration / 2, clip.Duration })
                {
                    publish.Invoke(resumed.Combat, [coldClips[path], coldClips[path].Time(seconds), seconds]);
                    if (!BonePoses(resumed).Zip(expected[sample++]).All(pair => pair.First.IsEqualApprox(pair.Second)))
                        throw new InvalidDataException("Owned warm/cold attack poses differ for the same source clock.");
                }
                if (!content.TryRead(path, null, out var sourceBytes, out var sourceIdentity) ||
                    Convert.ToHexString(SHA256.HashData(sourceBytes)) != clip.Hash || coldClips[path].Hash != clip.Hash)
                    throw new InvalidDataException("Owned attack source changed across native preparation.");
                reports.Add(new
                {
                    path,
                    clip.Hash,
                    sourceIdentity,
                    clip.Duration,
                    clip.Events.Discharges,
                    sequence = clip.Animation.Sequence.Name,
                    channels = clip.Animation.Sequence.ControlledBlocks.Length,
                    cold = true,
                    poses = 3
                });
            }
            var scriptError = state.ScriptError;
            var stoppedFrame = JsonSerializer.Serialize(state.ScriptStoppedFrame);
            var repeatedSelections = new List<object>();
            for (var engagement = 0; engagement < 8; engagement++)
            {
                var beforeRandom = state.AttackRandom.State;
                end.Invoke(actor.Combat, null);
                if (state.Engagement is not null || state.AttackRandom.State != beforeRandom)
                    throw new InvalidDataException("Ordinary engagement retirement discarded the reference's attack stream.");
                provoke.Invoke(actor.Combat, [records.RuntimeFormKey(0x14)]);
                if (state.Engagement is not { Action: "pursue", StartPending: true })
                    throw new InvalidDataException("Ordinary attack provocation did not create a fresh engagement.");
                prepare.Invoke(actor.Combat, null);
                if (state.AttackRandom.State != beforeRandom || state.Engagement.AttackRandomState != beforeRandom ||
                    state.Engagement.Animation is not null || !ReferenceEquals(clips, clipsField.GetValue(actor.Combat)))
                    throw new InvalidDataException("Cached native preparation failed to bind the new engagement without drawing or rebuilding.");
                var attack = FalloutAttackAnimationSelection.Bind(state.Engagement.Transition("attack"), paths, path => clips[path].Hash);
                state.AttackRandom.Restore(attack.AttackRandomState!.Value);
                state.Engagement = attack with { Seconds = clips[attack.Animation!].Duration / 2, StartPending = false };
                var beforePrepare = JsonSerializer.Serialize(state.Engagement);
                prepare.Invoke(actor.Combat, null);
                if (JsonSerializer.Serialize(state.Engagement) != beforePrepare ||
                    state.AttackRandom.State != attack.AttackRandomState || state.ScriptError != scriptError ||
                    JsonSerializer.Serialize(state.ScriptStoppedFrame) != stoppedFrame)
                    throw new InvalidDataException("Cached preparation changed the chosen KF/clock, stream or stopped source invocation.");
                repeatedSelections.Add(new { engagement, beforeRandom, attack.Animation, attack.AnimationHash, attack.AttackRandomState });
            }
            if (!savedBytes.SequenceEqual(File.ReadAllBytes(checkpoint))) throw new InvalidDataException("Attack audit changed its checkpoint input.");
            GD.Print("OPENNV_NATIVE_OWNED_ATTACK_VARIANTS " + JsonSerializer.Serialize(new
            {
                content.SaveCompatibilityId,
                runtimeBuild = typeof(RuntimeNativeActorCombat).Assembly.ManifestModule.ModuleVersionId,
                actor = caller.ToString(),
                checkpointSha256 = Convert.ToHexString(SHA256.HashData(savedBytes)),
                variants = reports,
                chosen,
                restoredClock = mid.Seconds,
                sourceKeyPrefix = prefix.Length,
                sourceKeyRemainder = remainder.Length,
                preparationNoDraw = true,
                cold = true,
                sourceUnchanged = true,
                repeatedSelections,
                cachedPreparationNoDraw = true,
                stoppedScriptUnchanged = true,
                campaign = false,
                framesRecorded = false,
                boundary = "independent-native-attack-component;whole-mixed-fault-save-refused;ordinary-attack-damage-and-cold-action-campaign-unverified;retail-global-RNG-phase-and-list-order-unmatched"
            }));
            GD.Print("OPENNV_NATIVE_OWNED_ATTACK_VARIANTS_PASS nativePreparation=true allVariants=true preparationNoDraw=true coldKfHashClock=true nativePoses=true repeatedEngagements=8 cachedPreparationNoDraw=true stoppedScriptUnchanged=true sourceUnchanged=true campaign=false framesRecorded=false");
        }
        finally { fixture.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
