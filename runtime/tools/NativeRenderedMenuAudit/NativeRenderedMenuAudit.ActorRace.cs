using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private async Task ActorRace(string baseRoot, string mod, string root, string referenceId, string raceId, string idleId, string[] dependencies)
    {
        SubViewport? view = null;
        RuntimeNativeNpc? actor = null;
        try
        {
            var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var reference = FalloutDialogueTopic.Find(records, "ACHR", referenceId);
            var instance = world.Get(reference.FormKey);
            var selection = world.InitializeActorTemplates(reference.FormKey, 1);
            var cell = FalloutCellSceneReader.Read(records, instance.Cell);
            var placed = cell.References.Single(item => item.FormKey == reference.FormKey);
            var requestedRace = FalloutDialogueTopic.Find(records, "RACE", raceId).FormKey;
            FalloutActorAppearanceState player = new(null, requestedRace, null, null);
            world.BindPlayerAppearance(() => player);
            var sourceHash = SHA256.HashData(records.GetEffective(instance.Base).ReadData());
            var armor = world.EquippedArmor(reference.FormKey, 1);
            var appearance = FalloutNpcAppearanceResolver.Resolve(records, placed.Base, placed.FormKey, armor, selection: selection);
            var units = OpenNV.Runtime.RuntimeConfiguration.Load().World.GameUnitsToMeters;
            Material Material(FalloutNpcAppearance body, FalloutNpcAppearancePart part,
                OpenNV.Runtime.Formats.Gamebryo.FalloutNifFile nif, OpenNV.Runtime.Formats.Gamebryo.FalloutNifGeometry geometry) =>
                NativeNpcMaterial.Resolve(body, part, nif, geometry, records, new Color(.4f, .4f, .4f));
            view = new SubViewport
            {
                Size = new(448, 448),
                OwnWorld3D = true,
                TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            AddChild(view);
            var environment = new WorldEnvironment
            {
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Color,
                    BackgroundColor = Colors.Transparent,
                    AmbientLightSource = Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = Colors.White,
                    AmbientLightEnergy = .7f,
                }
            };
            view.AddChild(environment);
            actor = RuntimeNativeNpc.Create(appearance, content, units, Material);
            actor.BindSourceBehavior(records, selection);
            view.AddChild(actor);
            actor.SetProcess(false); actor.SetPhysicsProcess(false);
            actor.PlayIdle(records, idleId); actor._Process(.125);
            if (actor.AnimationError is not null) throw new InvalidDataException(actor.AnimationError);
            var skeleton = actor.Skeleton;
            var phase = JsonSerializer.SerializeToElement(actor.AnimationState).GetProperty("sourceSeconds").GetSingle();
            var pose = Enumerable.Range(0, skeleton.Node.GetBoneCount()).Select(skeleton.Node.GetBonePose).ToArray();
            var actorId = actor.GetInstanceId();
            var height = actor.SourceHeight * units;
            var head = skeleton.Node.GlobalTransform * skeleton.Node.GetBoneGlobalPose(skeleton.BoneIndex("Bip01 Head"));
            var center = head.Origin + Vector3.Down * height * .18f;
            var camera = new Camera3D { Position = center + Vector3.Back * height, Fov = 35, Current = true };
            view.AddChild(camera); camera.LookAt(center);
            var original = await Pixels();
            if (!world.MatchRace(reference.FormKey, records.RuntimeFormKey(0x14)))
                throw new InvalidDataException("Owned actor/race fixture requires a changed race family.");
            var expected = world.ActorRace(reference.FormKey);
            if (!actor.SynchronizeAppearance(world, records, content, Material) || actor.Appearance.Race != expected ||
                !actor.Appearance.RuntimeFace || actor.GetInstanceId() != actorId || !ReferenceEquals(actor.Skeleton, skeleton))
                throw new InvalidDataException("Source race mutation did not refresh the existing native body and dynamic face.");
            var refreshedPhase = JsonSerializer.SerializeToElement(actor.AnimationState).GetProperty("sourceSeconds").GetSingle();
            var changedBones = Enumerable.Range(0, skeleton.Node.GetBoneCount()).Where(bone => pose[bone] != skeleton.Node.GetBonePose(bone)).ToArray();
            if (phase != refreshedPhase || changedBones.Length != 0)
                throw new InvalidDataException($"Race refresh reset the active source animation phase or bone pose: phase={phase:R}->{refreshedPhase:R} " +
                    $"processing={actor.IsProcessing()} bones={changedBones.Length} " + string.Join("; ", changedBones.Take(3).Select(bone =>
                        $"{skeleton.Node.GetBoneName(bone)} {pose[bone]} -> {skeleton.Node.GetBonePose(bone)}")));
            var changed = await Pixels();
            if (original.SequenceEqual(changed)) throw new InvalidDataException("Changed owned race produced identical body pixels.");
            var priorParts = actor.Parts.ToArray();
            // Matching an adult to an older target can resolve back to the
            // target's current race. Native invalidation still applies because
            // the two input races differ; only exact same-race input is a no-op.
            if (!world.MatchRace(reference.FormKey, records.RuntimeFormKey(0x14)) ||
                !actor.SynchronizeAppearance(world, records, content, Material) || actor.Appearance.Race != expected ||
                priorParts.SequenceEqual(actor.Parts) || !changed.SequenceEqual(await Pixels()))
                throw new InvalidDataException("A changed race-family request lost its native invalidation when the age tier resolved to the current race.");
            var parts = actor.Parts.ToArray(); var state = actor.AnimationState;
            player = player with { Race = expected };
            if (world.MatchRace(reference.FormKey, records.RuntimeFormKey(0x14)) ||
                actor.SynchronizeAppearance(world, records, content, Material) || !parts.SequenceEqual(actor.Parts) ||
                !JsonSerializer.Serialize(state).Equals(JsonSerializer.Serialize(actor.AnimationState), StringComparison.Ordinal))
                throw new InvalidDataException("Same-race command changed native body, face or animation state.");
            var saved = JsonSerializer.Deserialize<FalloutActorOverrides[]>(JsonSerializer.Serialize(world.CaptureActorOverrides()))!;
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(world.Capture()); cold.RestoreActorOverrides(saved);
            actor.Free(); actor = null;
            var coldAppearance = FalloutNpcAppearanceResolver.Resolve(records, placed.Base, placed.FormKey, armor,
                cold.ActorAppearanceOverride(reference.FormKey), cold.Get(reference.FormKey).Templates);
            actor = RuntimeNativeNpc.Create(coldAppearance, content, units, Material);
            actor.BindSourceBehavior(records, cold.Get(reference.FormKey).Templates);
            view.AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
            actor.PlayIdle(records, idleId); actor._Process(.125);
            if (!changed.SequenceEqual(await Pixels())) throw new InvalidDataException("Cold race body did not restore identical owned pixels.");
            // Restore the original base snapshot. Existing resident and warm
            // bodies must also follow removal of an override, not just additions.
            cold.RestoreActorOverrides([]);
            if (!actor.SynchronizeAppearance(cold, records, content, Material) || actor.Appearance.Race != appearance.Race)
                throw new InvalidDataException("Removing the race override did not restore source appearance.");
            if (!original.SequenceEqual(await Pixels())) throw new InvalidDataException("Source race restoration did not restore baseline pixels.");
            if (!SHA256.HashData(records.GetEffective(instance.Base).ReadData()).AsSpan().SequenceEqual(sourceHash))
                throw new InvalidDataException("Appearance mutation wrote into owned source bytes.");
            GD.Print($"OPENNV_NATIVE_ACTOR_RACE_PASS reference={reference.FormKey} race={expected} bodyIdentity=true animationPhase=true skeletonPoseOwner=true " +
                "dynamicFace=true changedPixels=true sameRaceStable=true coldPixels=true sourceRestored=true sourceReadonly=true " +
                "recording=false boundary=isolated-owned-actor-fixture playerTarget=unbound matchedTiming=unbound parity=unverified");

            async Task<byte[]> Pixels()
            {
                for (var frame = 0; frame < 3; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var pixels = view.GetTexture().GetImage();
                if (pixels.IsEmpty()) throw new InvalidDataException("Owned actor fixture has no native pixels.");
                var data = pixels.GetData();
                if (!data.Where((_, index) => index % 4 == 3).Any(value => value != 0))
                    throw new InvalidDataException("Owned actor fixture has no visible body pixels.");
                return data;
            }
        }
        finally { if (view is null) actor?.Free(); view?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
