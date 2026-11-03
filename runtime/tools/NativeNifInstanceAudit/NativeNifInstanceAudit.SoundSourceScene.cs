using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeNifInstanceAudit
{
    private void ExerciseSoundSourceScenes(FalloutPluginStack records, RuntimeLiveContentSource content, FalloutFormKey reference)
    {
        Node3D? warm = null, cold = null;
        NativeOwnedAnimationSoundPlayer? warmOwner = null, coldOwner = null;
        var sound = new FalloutFormKey(reference.OwnerPlugin, 1);
        var finite = new FalloutFormKey(reference.OwnerPlugin, 7);
        var path = FalloutSoundRecordReader.Read(records, sound).LogicalPath;
        var paused = GetTree().Paused;
        using var enableOwner = new FalloutReferenceWorld(records);
        try
        {
            GetTree().Paused = true;
            static Node3D History(FalloutFormKey identity, string name)
            {
                var root = new Node3D { Name = name };
                root.SetMeta("opennv_reference_form_key", identity.ToString());
                return root;
            }
            warm = History(reference, "SourceSpaceHistory"); AddChild(warm);
            var view = new SubViewport { Name = "ActualSourceViewport", OwnWorld3D = true };
            warm.AddChild(view);
            var actor = BoneEmitterActor(reference, "SourceSpaceFirst"); view.AddChild(actor);
            var alternate = BoneEmitterActor(reference, "SourceSpaceReference"); warm.AddChild(alternate);
            alternate.Position = new(91, 17, -31);
            var sourceCamera = new Camera3D { Transform = new(new Basis(Vector3.Right, .2f), new(2, 1, -3)) };
            view.AddChild(sourceCamera);
            var worldEye = new Transform3D(new Basis(Vector3.Up, .9f), new(23, 4, -17));
            var sourcePoseReady = false;
            var first = new NativeOwnedSoundSourceScene("fixture-first", actor, warm,
                () => worldEye * sourceCamera.GlobalTransform.AffineInverse(),
                () => { if (!sourcePoseReady) throw new NotSupportedException("Actual fixture source pose failed."); });
            var other = new NativeOwnedSoundSourceScene("fixture-reference", alternate, warm,
                RequirePublishedPose: () => throw new NotSupportedException("Unused opposite POV pose failed."));
            var current = first;
            NativeOwnedSoundSourceScene Resolve(string? id) => id switch
            {
                null => current,
                "fixture-first" => first,
                "fixture-reference" => other,
                _ => throw new NotSupportedException("Fixture source scene is unknown.")
            };
            var events = new FalloutAnimationSoundEvents(reference, () => enableOwner.IsEnabled(reference));
            var random = new FalloutSoundRandomState(7331);
            warmOwner = new(records, content, warm, .01f, random, events,
                sourceScenes: Resolve, sourcePoseBoundary: true);
            BoneEmitterStreams(warmOwner).Add(path, PlacedSoundMedia(content, path));
            warm.AddChild(warmOwner);
            var emitter = warmOwner.ResolveEmitter("Joint", true).Emitter;
            Require(!warm.HasMeta("opennv_nif_fixed_strings") && emitter.GetViewport() != warm.GetViewport(),
                "Source-space fixture accidentally provided a proxy NIF root or a shared viewport.");
            warmOwner.DispatchSound(sound, emitter);
            Require(warmOwner.ActiveNativeVoices.Count == 0 && !warmOwner.CanCaptureSilent && events.Events.Count == 0,
                "Deferred source sound played, drew RNG or admitted capture before actual pose publication.");
            BoneEmitterReject(warmOwner.PublishSourcePose);
            Require(events.Events.Count == 0 && random.State == 7331 && warmOwner.ActiveNativeVoices.Count == 0 &&
                !warmOwner.CanCaptureSilent, "Failed actual source pose admitted RNG, generation, playback or capture.");
            sourcePoseReady = true;
            warmOwner.PublishSourcePose();
            var voice = (AudioStreamPlayer3D)warmOwner.ActiveNativeVoices.Single();
            Require(voice.GetViewport() == warm.GetViewport() && voice.GetParent() == warmOwner &&
                voice.GlobalPosition.DistanceTo(first.Position(emitter)) < .00001f,
                "First-person source sound remained in the isolated audio viewport or used unprojected local coordinates.");
            current = other; worldEye = new(worldEye.Basis, worldEye.Origin + new Vector3(3, -1, 4));
            warmOwner._Process(0);
            Require(voice.GlobalPosition.DistanceTo(first.Position(emitter)) < .00001f &&
                voice.GlobalPosition.DistanceTo(alternate.GlobalPosition) > 1,
                "POV switch rebound a retained voice to a different source scene.");
            var snapshot = JsonSerializer.Deserialize<FalloutAnimationSoundEventsSnapshot>(JsonSerializer.Serialize(events.Capture()))!;
            var playback = snapshot.Events.Single().Playback!;
            Require(playback.SourceScene == "fixture-first" && playback.SourceBone is { Name: "Joint" },
                "PCM continuation lost the original source scene or actual source bone.");
            var randomAfter = random.State;
            var sourceCameraTransform = sourceCamera.Transform;
            NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(warm);
            var beforeRetiredKey = events.Events.Count;
            Require(warmOwner.DispatchSound(finite, emitter) == "source-sound-awaiting-pose-publication" &&
                warmOwner.ActiveNativeVoices.Count == 0 && events.Events.Count == beforeRetiredKey,
                "Explicit original-scene bone was not actually queued while opposite POV was current.");
            actor.Free();
            BoneEmitterReject(warmOwner.PublishSourcePose);
            Require(events.Events.Count == beforeRetiredKey && random.State == randomAfter && !warmOwner.CanCaptureSilent,
                "Queued retired source bone drew RNG/published a generation or bypassed controlled source-owner refusal.");
            warm.Free(); warm = null; warmOwner = null;

            cold = History(reference, "ColdSourceSpaceHistory"); AddChild(cold);
            var coldView = new SubViewport { OwnWorld3D = true }; cold.AddChild(coldView);
            var coldActor = BoneEmitterActor(reference, "ColdSourceSpaceFirst"); coldView.AddChild(coldActor);
            var coldOther = BoneEmitterActor(reference, "ColdSourceSpaceReference"); cold.AddChild(coldOther);
            coldOther.Position = new(91, 17, -31);
            var coldSourceCamera = new Camera3D { Transform = sourceCameraTransform }; coldView.AddChild(coldSourceCamera);
            var coldPoseReady = true;
            var coldFirst = new NativeOwnedSoundSourceScene("fixture-first", coldActor, cold,
                () => worldEye * coldSourceCamera.GlobalTransform.AffineInverse(),
                () => { if (!coldPoseReady) throw new NotSupportedException("Cold source pose failed after voice admission."); });
            var coldReference = new NativeOwnedSoundSourceScene("fixture-reference", coldOther, cold);
            var coldCurrent = coldReference;
            NativeOwnedSoundSourceScene ColdResolve(string? id) => id switch
            {
                null => coldCurrent,
                "fixture-first" => coldFirst,
                "fixture-reference" => coldReference,
                _ => throw new NotSupportedException("Cold fixture source scene is unknown.")
            };
            // Constructor sees no active generations, as in the existing source-
            // bone fixture; the real saved ledger is then restored before Ready.
            var coldEvents = new FalloutAnimationSoundEvents(reference, () => enableOwner.IsEnabled(reference));
            var coldRandom = new FalloutSoundRandomState(randomAfter);
            coldOwner = new(records, content, cold, .01f, coldRandom, coldEvents,
                sourceScenes: ColdResolve, sourcePoseBoundary: true);
            BoneEmitterStreams(coldOwner).Add(path, PlacedSoundMedia(content, path));
            coldEvents.Restore(snapshot, records);
            cold.AddChild(coldOwner);
            Require(coldOwner.ActiveNativeVoices.Count == 0 && !coldOwner.CanCaptureSilent,
                "Cold player audio restored before its actual source-scene publication.");
            coldOwner.PublishSourcePose();
            var restored = (AudioStreamPlayer3D)coldOwner.ActiveNativeVoices.Single();
            Require(restored.GetViewport() == cold.GetViewport() && coldRandom.State == randomAfter &&
                coldEvents.Events.Count == snapshot.Events.Count &&
                coldEvents.Capture().Events.Single().Playback is { SourceScene: "fixture-first", SourceBone.Name: "Joint" },
                "Cold POV switch replayed a source key, redrew RNG or lost actual emitter identity.");
            NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(cold);
            coldCurrent = coldFirst;
            var finiteEmitter = coldOwner.ResolveEmitter("Joint").Emitter;
            coldOwner.DispatchSound(finite, finiteEmitter); coldOwner.PublishSourcePose();
            var finiteVoice = (AudioStreamPlayer3D)coldOwner.ActiveNativeVoices.Single();
            Require(finiteEmitter.GetViewport() != cold.GetViewport() && finiteVoice.GetViewport() == cold.GetViewport() &&
                finiteVoice.GetParent() is NativeOwnedFiniteSoundHost && coldOwner.HasEmitterVoices(finiteEmitter) &&
                coldOwner.HasEmitterVoices(coldActor) && !coldOwner.CanRetireEmitter(finiteEmitter),
                "Finite source voice did not enter the actual world-audio graph lifetime.");
            coldPoseReady = false;
            ((NativeOwnedFiniteSoundHost)finiteVoice.GetParent())._Process(0);
            Require(coldOwner.ActiveNativeVoices.Count == 1,
                "Already hosted finite tail acquired a new failed-pose admission dependency.");
            records.SoundVoices.Stop(finite, reference);
            Require(coldOwner.ActiveNativeVoices.Count == 0 && !coldOwner.HasEmitterVoices(finiteEmitter),
                "Source finite stop did not retire its real voice.");
            var worldEmitter = new Node3D { Name = "ActualWorldEffect", Position = new(13, 2, -7) };
            cold.AddChild(worldEmitter);
            string? worldAdmission = null;
            Require(coldOwner.DispatchWorldSound(finite, worldEmitter, admitted: actual => worldAdmission = actual) == "source-sound-playing" &&
                worldAdmission == "source-sound-playing" && coldOwner.ActiveNativeVoices.Count == 1,
                "Published world sound unnecessarily depended on the failed actor-palette pose.");
            records.SoundVoices.Stop(finite, reference);
            var beforeWorldQueue = coldEvents.Events.Count;
            worldAdmission = null;
            coldOwner.DispatchSound(sound, finiteEmitter);
            Require(coldOwner.DispatchWorldSound(finite, worldEmitter, admitted: actual => worldAdmission = actual) == "source-sound-awaiting-pose-publication" &&
                worldAdmission is null && !coldOwner.CanRetireEmitter(worldEmitter) && coldEvents.Events.Count == beforeWorldQueue,
                "World sound reordered earlier actor keys, claimed deferred playback, or released its pending emitter.");
            BoneEmitterReject(coldOwner.PublishSourcePose);
            Require(coldEvents.Events.Count == beforeWorldQueue && worldAdmission is null && !coldOwner.CanRetireEmitter(worldEmitter),
                "Failed actor publication advanced a later world generation or released its actual emitter.");
            coldPoseReady = true;
            coldOwner.PublishSourcePose();
            Require(worldAdmission == "source-sound-playing" && coldEvents.Events.Count == beforeWorldQueue + 2 &&
                coldEvents.Events[beforeWorldQueue].Sound == sound && coldEvents.Events[beforeWorldQueue + 1].Sound == finite,
                "Deferred world admission lost actual playback disposition or source event order.");
            NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(cold);
            records.SoundVoices.Stop(finite, reference);
            Require(coldOwner.CanRetireEmitter(worldEmitter), "Naturally admitted/stopped world sound retained its retired queue lease.");
            worldEmitter.Free();
            GD.Print("OPENNV_SOUND_SOURCE_SCENE_PASS isolatedSource=true realBone=true worldAudio=true poseBoundary=true coldSceneIdentity=true povSwitchNoRebind=true finiteHost=true");
        }
        finally
        {
            records.SoundVoices.Stop(finite, reference);
            if (warm is not null) NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(warm);
            if (cold is not null) NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(cold);
            cold?.Free(); warm?.Free(); GetTree().Paused = paused;
        }
    }
}
