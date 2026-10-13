using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseNodeEmitterPersistence()
    {
        using var nativeValidity = new Expression();
        Require(nativeValidity.Parse("is_instance_id_valid(id)", ["id"]) == Error.Ok &&
            !BoneEmitterInstanceAlive(nativeValidity, 0), "Native source-node retirement query was not admitted.");
        var directory = Directory.CreateTempSubdirectory("opennv-node-emitter-");
        var plugin = Path.Combine(directory.FullName, "BoneEmitter.esm");
        var paused = GetTree().Paused;
        Node3D? warmRoot = null, coldRoot = null;
        AudioStreamPlayback? warmPlayback = null, coldPlayback = null;
        FalloutPluginStack? records = null;
        FalloutReferenceWorld? world = null, cold = null;
        var audioOwners = new Dictionary<string, ulong>(StringComparer.Ordinal);
        try
        {
            File.WriteAllBytes(plugin, BoneEmitterPlugin());
            records = FalloutPluginStack.Load(directory.FullName, ["BoneEmitter.esm"]);
            world = new(records);
            var reference = new FalloutFormKey("BoneEmitter.esm", 4);
            var source = FalloutNifFile.Read(ActorSkinFixture("EmitterRoot", false));
            warmRoot = NodeEmitterModel(reference, source, "WarmReference"); AddChild(warmRoot);
            var warmEmitter = NodeEmitterNode(warmRoot, 1);
            var instance = world.Get(reference);
            var owner = new NativeOwnedAnimationSoundPlayer(records, null!, warmRoot, .01f,
                instance.SoundRandom, instance.AnimationSoundEvents, new(warmRoot, reference, source));
            warmRoot.AddChild(owner);
            var sound = FalloutSoundRecordReader.Read(records, new("BoneEmitter.esm", 1));
            var wav = BoneEmitterWav(sound.LogicalPath); BoneEmitterStreams(owner).Add(sound.LogicalPath, wav);
            audioOwners.Add("warmWav", wav.GetInstanceId());
            var selected = FalloutAnimationSound.Select(sound, [sound.LogicalPath], instance.SoundRandom, true);
            var generation = instance.AnimationSoundEvents.Begin(records, selected, "Sound:EmitterLoop Joint", false, [sound.LogicalPath]);
            instance.AnimationSoundEvents.BindMedia(generation, wav.GetMeta("opennv_owned_media_sha256").AsString());
            var pcm = new NativeOwnedPcmStream(wav, FalloutSoundLoop.Read(sound));
            var player = new AudioStreamPlayer { Stream = pcm.Stream };
            audioOwners.Add("warmStream", pcm.Stream.GetInstanceId());
            audioOwners.Add("warmPlayer", player.GetInstanceId());
            GetTree().Paused = true;
            BoneEmitterTrack(owner, player, warmEmitter, FalloutSoundLoop.Read(sound), sound.FormKey, generation, pcm);
            BoneEmitterPlay(owner, player);
            warmPlayback = player.GetStreamPlayback() ?? throw new InvalidDataException("Source-node PCM has no actual native playback.");
            audioOwners.Add("warmPlayback", warmPlayback.GetInstanceId());
            AudioServer.Lock();
            try
            {
                player.ProcessMode = ProcessModeEnum.Always;
                Require(warmPlayback.MixAudio(1.137f, 1283).Length == 1283, "Source-node PCM did not start.");
            }
            finally { player.ProcessMode = ProcessModeEnum.Inherit; AudioServer.Unlock(); }
            var random = instance.SoundRandom.State;
            var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            var active = snapshots.Single(value => value.Reference == reference).AnimationSoundEvents!.Events.Single();
            var playback = active.Playback!;
            var identity = playback.SourceNode ?? throw new InvalidDataException("Parsed source node became an opaque Godot path.");
            Require(playback.SourceBone is null && identity.Block == 1 && identity.Name == "Joint" &&
                identity.ModelSha256 == source.Sha256 && RuntimeNativeNifSoundEmitters.HasAnonymousPath(playback.EmitterPath),
                "Actual anonymous source-node capture lost its model/block identity.");

            cold = new(records); cold.Restore(snapshots.Select(value => value with { AnimationSoundEvents = null }).ToArray());
            coldRoot = NodeEmitterModel(reference, FalloutNifFile.Read(ActorSkinFixture("EmitterRoot", false)), "ColdReference"); AddChild(coldRoot);
            var coldInstance = cold.Get(reference);
            var coldOwner = new NativeOwnedAnimationSoundPlayer(records, null!, coldRoot, .01f,
                coldInstance.SoundRandom, coldInstance.AnimationSoundEvents, new(coldRoot, reference, source));
            var coldWav = BoneEmitterWav(sound.LogicalPath); BoneEmitterStreams(coldOwner).Add(sound.LogicalPath, coldWav);
            audioOwners.Add("coldWav", coldWav.GetInstanceId());
            coldInstance.AnimationSoundEvents.Restore(snapshots.Single(value => value.Reference == reference).AnimationSoundEvents!, records);
            coldRoot.AddChild(coldOwner); coldOwner.RequirePcmRestored();
            var coldPlayer = (AudioStreamPlayer)coldOwner.ActiveNativeVoices.Single();
            var coldPcm = BoneEmitterPcm(coldOwner, coldPlayer);
            audioOwners.Add("coldStream", coldPcm.Stream.GetInstanceId());
            audioOwners.Add("coldPlayer", coldPlayer.GetInstanceId());
            coldPlayback = coldPlayer.GetStreamPlayback() ?? throw new InvalidDataException("Cold source-node voice has no actual playback.");
            audioOwners.Add("coldPlayback", coldPlayback.GetInstanceId());
            var coldCaptured = coldInstance.AnimationSoundEvents.Capture().Events.Single().Playback!;
            Require(coldCaptured.SourceNode == identity && coldCaptured.EmitterPath != playback.EmitterPath &&
                coldPcm.Capture() == playback.Samples && coldInstance.SoundRandom.State == random,
                "Cold source-node binding replayed selection/RNG or depended on an old anonymous name.");
            AudioServer.Lock();
            try
            {
                player.ProcessMode = ProcessModeEnum.Always; coldPlayer.ProcessMode = ProcessModeEnum.Always;
                Require(warmPlayback.MixAudio(1.137f, 257).SequenceEqual(coldPlayback.MixAudio(1.137f, 257)) &&
                    pcm.Capture() == coldPcm.Capture(), "Cold source-node PCM changed the exact fractional suffix.");
            }
            finally
            {
                player.ProcessMode = ProcessModeEnum.Inherit; coldPlayer.ProcessMode = ProcessModeEnum.Inherit;
                AudioServer.Unlock();
            }
            var binding = new RuntimeNativeNifSoundEmitterBinding(coldRoot, reference, source);
            var coldEmitter = NodeEmitterNode(coldRoot, 1);
            Require(binding.Restore(identity) == coldEmitter && binding.Restore(binding.Capture(coldRoot)) == coldRoot,
                "Source root/node restoration did not reuse the actual current instance.");
            foreach (var invalid in new[]
            {
                identity with { Reference = new("BoneEmitter.esm", 2) }, identity with { ModelPath = "meshes\\fixture\\other.nif" },
                identity with { ModelSha256 = new string('0', 64) }, identity with { Block = 2 }, identity with { Name = "OtherJoint" }
            }) BoneEmitterReject(() => binding.Restore(invalid));
            var sourceHash = coldRoot.GetMeta("opennv_nif_source_sha256");
            coldRoot.SetMeta("opennv_nif_source_sha256", new string('0', 64));
            try { Require(!coldOwner.CanCaptureSilent, "Changed source model admitted its old active voice."); }
            finally { coldRoot.SetMeta("opennv_nif_source_sha256", sourceHash); }
            var sourceName = coldEmitter.GetMeta("opennv_nif_source_name");
            coldEmitter.SetMeta("opennv_nif_source_name", "ChangedJoint");
            try { BoneEmitterReject(() => binding.Restore(identity)); Require(!coldOwner.CanCaptureSilent, "Changed node name admitted its old source block."); }
            finally { coldEmitter.SetMeta("opennv_nif_source_name", sourceName); }
            coldEmitter.SetMeta("opennv_animation_object_form", "BoneEmitter.esm:000005");
            try { BoneEmitterReject(() => binding.Restore(identity)); Require(!coldOwner.CanCaptureSilent, "ANIO node borrowed static continuation."); }
            finally { coldEmitter.RemoveMeta("opennv_animation_object_form"); }
            var duplicate = new Node3D(); duplicate.SetMeta("opennv_nif_block", 1); duplicate.SetMeta("opennv_nif_source_name", "Joint");
            coldRoot.AddChild(duplicate);
            try { BoneEmitterReject(() => new RuntimeNativeNifSoundEmitterBinding(coldRoot, reference, source)); }
            finally { duplicate.Free(); }
            cold.SetEnabled(reference, false); cold.AdvanceEnableChanges(0, new(1, 1), _ => false);
            Require(!coldOwner.CanCaptureSilent && coldPcm.Capture() == pcm.Capture(),
                "Disabled reference admitted static continuation or changed its live samples.");
            var retainedBlock = coldEmitter.GetMeta("opennv_nif_block"); coldEmitter.SetMeta("opennv_nif_block", 9);
            try { BoneEmitterReject(() => binding.Restore(identity)); }
            finally { coldEmitter.SetMeta("opennv_nif_block", retainedBlock); }
            coldEmitter.Free(); BoneEmitterReject(() => binding.Restore(identity));
            GD.Print("OPENNV_NATIVE_NODE_EMITTER_PERSISTENCE_PASS parsedSource=true indexedBlocks=true anonymousNameIndependent=true " +
                "coldActualPlayback=true exactSuffixSamples=257 fractionalClock=true sourceDriftRefused=true retiredRefused=true " +
                "duplicateRefused=true anioRefused=true authoritativeDisabledRefused=true rngUnchanged=true firstPartyMedia=true componentOnly=true");
        }
        finally
        {
            GetTree().Paused = paused;
            try { if (IsInstanceValid(coldRoot)) coldRoot!.Free(); if (IsInstanceValid(warmRoot)) warmRoot!.Free(); }
            finally { coldPlayback?.Dispose(); warmPlayback?.Dispose(); }
            cold?.Dispose(); world?.Dispose(); records?.Dispose();
            File.Delete(plugin); Directory.Delete(directory.FullName);
            await RequireAudioRetirement(nativeValidity, audioOwners, "source-node");
        }
        GD.Print($"OPENNV_NATIVE_NODE_EMITTER_RETIREMENT_PASS audioOwners={audioOwners.Count} nativeIdsGone=true nonbindingValidity=true mixerAndMainRetired=true boundedWallSeconds=2");
    }

    private static Node3D NodeEmitterModel(FalloutFormKey reference, FalloutNifFile source, string name)
    {
        var root = RuntimeNativeNifMeshBuilder.Build(source, .01f).Root;
        try
        {
            root.Name = name; root.SetMeta("opennv_reference_form_key", reference.ToString());
            root.SetMeta("opennv_source_model", "meshes\\fixture\\object.nif");
            var model = root.GetChild<Node3D>(0); root.RemoveChild(model);
            root.AddChild(new Node3D { Name = model.Name }); root.AddChild(model, forceReadableName: false);
            Require(RuntimeNativeNifSoundEmitters.HasAnonymousPath(root.GetPathTo(NodeEmitterNode(root, 1)).ToString()),
                "Source-node fixture did not create a real anonymous adapter name.");
            return root;
        }
        catch { root.Free(); throw; }
    }

    private static Node3D NodeEmitterNode(Node3D root, int block) =>
        OpenNV.Runtime.SceneGraph.NodeTraversal.Descendants<Node3D>(root)
            .Single(node => node.HasMeta("opennv_nif_source_name") && node.GetMeta("opennv_nif_block", -1).AsInt32() == block);
}
