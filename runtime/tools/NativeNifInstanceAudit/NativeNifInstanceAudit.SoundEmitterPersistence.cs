using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseSoundEmitterPersistence()
    {
        GD.Print($"OPENNV_NATIVE_BONE_EMITTER_ASSEMBLY mvid={typeof(NativeNifInstanceAudit).Assembly.ManifestModule.ModuleVersionId}");
        using var nativeValidity = new Expression();
        Require(nativeValidity.Parse("is_instance_id_valid(id)", new[] { "id" }) == Error.Ok &&
            !BoneEmitterInstanceAlive(nativeValidity, 0), "Native retirement validity query was not admitted.");
        var directory = Path.Combine(Path.GetTempPath(), "opennv-bone-emitter-" + Guid.NewGuid().ToString("N"));
        var plugin = Path.Combine(directory, "BoneEmitter.esm");
        Directory.CreateDirectory(directory);
        var paused = GetTree().Paused;
        Node3D? actor = null, coldActor = null;
        FalloutPluginStack? records = null;
        FalloutReferenceWorld? world = null, cold = null;
        AudioStreamPlayback? warmPlayback = null, coldPlayback = null;
        var audioOwners = new Dictionary<string, ulong>(StringComparer.Ordinal);
        try
        {
            File.WriteAllBytes(plugin, BoneEmitterPlugin());
            records = FalloutPluginStack.Load(directory, ["BoneEmitter.esm"]);
            world = new(records);
            var reference = new FalloutFormKey("BoneEmitter.esm", 2);
            var state = world.Get(reference);
            actor = BoneEmitterActor(reference, "WarmSkeleton"); AddChild(actor);
            var skeleton = actor.GetChildren().OfType<Skeleton3D>().Single();
            var poses = BoneEmitterPoses(skeleton); var placement = actor.Transform;
            var randomBefore = state.SoundRandom.State;
            var owner = new NativeOwnedAnimationSoundPlayer(records, null!, actor, .01f, state.SoundRandom, state.AnimationSoundEvents);
            actor.AddChild(owner);
            var binding = owner.ResolveEmitter("Joint", true);
            Require(binding.Emitter is BoneAttachment3D && binding.FollowEmitter && state.SoundRandom.State == randomBefore,
                "Warm source bone binding changed RNG or lacked its actual native pose adapter.");
            var sound = FalloutSoundRecordReader.Read(records, new("BoneEmitter.esm", 1));
            var loop = FalloutSoundLoop.Read(sound);
            var wav = BoneEmitterWav(sound.LogicalPath);
            audioOwners.Add("warmWav", wav.GetInstanceId());
            BoneEmitterStreams(owner).Add(sound.LogicalPath, wav);
            var selected = FalloutAnimationSound.Select(sound, [sound.LogicalPath], state.SoundRandom, true);
            var generation = state.AnimationSoundEvents.Begin(records, selected, "Sound:EmitterLoop Joint", false, [sound.LogicalPath]);
            state.AnimationSoundEvents.BindMedia(generation, wav.GetMeta("opennv_owned_media_sha256").AsString());
            var pcm = new NativeOwnedPcmStream(wav, loop);
            var player = new AudioStreamPlayer { Stream = pcm.Stream };
            audioOwners.Add("warmStream", pcm.Stream.GetInstanceId());
            audioOwners.Add("warmPlayer", player.GetInstanceId());
            GetTree().Paused = true;
            BoneEmitterTrack(owner, player, binding.Emitter, loop, sound.FormKey, generation, pcm);
            BoneEmitterPlay(owner, player);
            warmPlayback = player.GetStreamPlayback() ?? throw new InvalidDataException("Warm source-bone voice has no native playback.");
            audioOwners.Add("warmPlayback", warmPlayback.GetInstanceId());
            AudioServer.Lock();
            try
            {
                player.ProcessMode = ProcessModeEnum.Always;
                Require(warmPlayback.MixAudio(1.137f, 1283).Length == 1283, "Actual native source-bone PCM did not start.");
            }
            finally { player.ProcessMode = ProcessModeEnum.Inherit; AudioServer.Unlock(); }
            var random = state.SoundRandom.State;
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            var active = saved.Single(value => value.Reference == reference).AnimationSoundEvents!.Events.Single();
            var playback = active.Playback!;
            var identity = playback.SourceBone ?? throw new InvalidDataException("Actual source bone was captured as an opaque node path.");
            Require(playback.Samples.Position > 0 && playback.Samples.Loops > 0 &&
                playback.Samples.Position != Math.Truncate(playback.Samples.Position) &&
                identity.Block == skeleton.GetBoneMeta(skeleton.FindBone("Joint"), "opennv_nif_block").AsInt32() &&
                identity.SkeletonSha256 == skeleton.GetMeta("opennv_nif_source_sha256").AsString(),
                "Warm PCM did not retain its fractional clock and actual parsed source skeleton identity.");
            cold = new(records);
            cold.Restore(saved.Select(value => value with { AnimationSoundEvents = null }).ToArray());
            coldActor = BoneEmitterActor(reference, "ColdSkeleton"); AddChild(coldActor);
            var coldSkeleton = coldActor.GetChildren().OfType<Skeleton3D>().Single();
            Require(!coldSkeleton.GetChildren().OfType<BoneAttachment3D>().Any(),
                "Fresh source skeleton already contained a sound adapter or replayed a sound key.");
            var coldState = cold.Get(reference);
            var coldEvents = coldState.AnimationSoundEvents;
            var coldOwner = new NativeOwnedAnimationSoundPlayer(records, null!, coldActor, .01f, coldState.SoundRandom, coldEvents);
            // Populate only first-party RAM media before restoring the real
            // reference ledger. No source request, key or RNG selection is run.
            var coldWav = BoneEmitterWav(sound.LogicalPath);
            audioOwners.Add("coldWav", coldWav.GetInstanceId());
            BoneEmitterStreams(coldOwner).Add(sound.LogicalPath, coldWav);
            coldEvents.Restore(saved.Single(value => value.Reference == reference).AnimationSoundEvents!, records);
            coldActor.AddChild(coldOwner); coldOwner.RequirePcmRestored();
            var coldPlayer = (AudioStreamPlayer)coldOwner.ActiveNativeVoices.Single();
            var coldPcm = BoneEmitterPcm(coldOwner, coldPlayer);
            audioOwners.Add("coldStream", coldPcm.Stream.GetInstanceId());
            audioOwners.Add("coldPlayer", coldPlayer.GetInstanceId());
            var coldBone = coldSkeleton.GetChildren().OfType<BoneAttachment3D>().Single();
            Require(coldBone.BoneIdx == coldSkeleton.FindBone("Joint") && !coldBone.OverridePose &&
                coldPcm.Capture() == playback.Samples && coldState.SoundRandom.State == random &&
                JsonSerializer.Serialize(coldEvents.Events.Single() with { Playback = null }) == JsonSerializer.Serialize(active with { Playback = null }),
                "Cold source bone replayed history/RNG, changed its source clock or rebound a different actual bone.");
            coldPlayback = coldPlayer.GetStreamPlayback() ?? throw new InvalidDataException("Cold source-bone voice has no native playback.");
            audioOwners.Add("coldPlayback", coldPlayback.GetInstanceId());
            AudioServer.Lock();
            try
            {
                var warmReferences = warmPlayback.GetReferenceCount(); var coldReferences = coldPlayback.GetReferenceCount();
                for (var index = 0; index < 8; index++)
                    Require(BoneEmitterInstanceAlive(nativeValidity, warmPlayback.GetInstanceId()) &&
                        BoneEmitterInstanceAlive(nativeValidity, coldPlayback.GetInstanceId()),
                        "Native validity query lost an actual bound playback.");
                Require(warmPlayback.GetReferenceCount() == warmReferences && coldPlayback.GetReferenceCount() == coldReferences,
                    "Native validity polling acquired or released an original playback reference.");
                player.ProcessMode = ProcessModeEnum.Always; coldPlayer.ProcessMode = ProcessModeEnum.Always;
                var expected = warmPlayback.MixAudio(1.137f, 257); var actual = coldPlayback.MixAudio(1.137f, 257);
                Require(expected.Length == 257 && expected.SequenceEqual(actual) && pcm.Capture() == coldPcm.Capture(),
                    "Native bone-emitter cold restoration changed the exact interpolated 257-sample suffix or fractional clock.");
            }
            finally
            {
                player.ProcessMode = ProcessModeEnum.Inherit; coldPlayer.ProcessMode = ProcessModeEnum.Inherit;
                AudioServer.Unlock();
            }
            var cache = new Dictionary<string, Node3D>(StringComparer.Ordinal);
            Require(RuntimeNativeNifSoundEmitters.RestoreBone(coldActor, reference, identity, cache) == coldBone &&
                coldSkeleton.GetChildren().OfType<BoneAttachment3D>().Count() == 1,
                "Repeated cold emitter binding duplicated the canonical native source bone.");
            foreach (var invalid in new[]
            {
                identity with { Reference = new("BoneEmitter.esm", 4) }, identity with { SkeletonPath = "meshes\\fixture\\other.nif" },
                identity with { SkeletonSha256 = new string('0', 64) }, identity with { Block = 9 }, identity with { Name = "OtherJoint" }
            }) BoneEmitterReject(() => RuntimeNativeNifSoundEmitters.RestoreBone(coldActor, reference, invalid, cache));
            var hash = coldSkeleton.GetMeta("opennv_nif_source_sha256");
            coldSkeleton.SetMeta("opennv_nif_source_sha256", new string('0', 64));
            try { BoneEmitterReject(() => RuntimeNativeNifSoundEmitters.RestoreBone(coldActor, reference, identity, cache)); }
            finally { coldSkeleton.SetMeta("opennv_nif_source_sha256", hash); }
            var catalog = coldSkeleton.GetMeta("opennv_nif_fixed_strings");
            coldSkeleton.RemoveMeta("opennv_nif_fixed_strings");
            try { BoneEmitterReject(() => RuntimeNativeNifSoundEmitters.RestoreBone(coldActor, reference, identity, cache)); }
            finally { coldSkeleton.SetMeta("opennv_nif_fixed_strings", catalog); }
            var duplicate = new BoneAttachment3D { Name = "DuplicateSourceSoundBone", BoneIdx = coldBone.BoneIdx };
            duplicate.SetMeta("opennv_nif_sound_bone_emitter", true); coldSkeleton.AddChild(duplicate);
            try { BoneEmitterReject(() => RuntimeNativeNifSoundEmitters.RestoreBone(coldActor, reference, identity, cache)); }
            finally { duplicate.Free(); }
            var originalBlock = coldBone.GetMeta("opennv_nif_block");
            coldBone.SetMeta("opennv_nif_block", identity.Block + 1);
            try { Require(!coldEvents.CanCapture, "Source bone adapter block drift was admitted in an active capture."); }
            finally { coldBone.SetMeta("opennv_nif_block", originalBlock); }
            coldBone.SetMeta("opennv_animation_object_form", "BoneEmitter.esm:000005");
            try
            {
                Require(!coldEvents.CanCapture, "Independent ANIO continuation was admitted as a primary bone voice.");
                BoneEmitterReject(() => RuntimeNativeNifSoundEmitters.RestoreBone(coldActor, reference, identity, cache));
            }
            finally { coldBone.RemoveMeta("opennv_animation_object_form"); }
            cold.SetEnabled(new("BoneEmitter.esm", 4), false);
            Require(coldEvents.CanCapture, "Queued parent disable replaced applied authoritative enable state.");
            cold.AdvanceEnableChanges(0, new(1, 1), _ => false);
            Require(coldState.Enabled && !cold.IsEnabled(reference) && !coldEvents.CanCapture &&
                coldPcm.Capture() == pcm.Capture() && coldEvents.Events.Single().End == FalloutAnimationSoundEnd.Active,
                "Disabled XESP owner invented loop completion or admitted an independent emitter continuation.");
            BoneEmitterReject(() => typeof(NativeOwnedAnimationSoundPlayer)
                .GetMethod("RestorePcmVoices", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(coldOwner, null));
            Require(actor.Transform == placement && BoneEmitterPoses(skeleton).SequenceEqual(poses) &&
                BoneEmitterPoses(coldSkeleton).SequenceEqual(poses), "Sound persistence changed source placement or bone poses.");
            GD.Print("OPENNV_NATIVE_BONE_EMITTER_PERSISTENCE_PASS actualSourceSkeleton=true recreatedWithoutKey=true namedPathChange=true exactSuffixSamples=257 fractionalClock=true oneAdapter=true sourceIdentityDriftRefused=true anioRefused=true authoritativeDisabledRefused=true rngUnchanged=true sourcePoseUnchanged=true firstPartyMedia=true componentOnly=true");
        }
        finally
        {
            GetTree().Paused = paused;
            try
            {
                if (IsInstanceValid(coldActor)) coldActor!.Free();
                if (IsInstanceValid(actor)) actor!.Free();
                if (coldPlayback is not null)
                    GD.Print($"OPENNV_NATIVE_BONE_EMITTER_PLAYBACK_RETIRING owner=cold id={audioOwners["coldPlayback"]} references={coldPlayback.GetReferenceCount()}");
                if (warmPlayback is not null)
                    GD.Print($"OPENNV_NATIVE_BONE_EMITTER_PLAYBACK_RETIRING owner=warm id={audioOwners["warmPlayback"]} references={warmPlayback.GetReferenceCount()}");
            }
            finally
            {
                // These are the fixture's original GetStreamPlayback bindings.
                // Keep them through real player retirement, then release them.
                coldPlayback?.Dispose(); warmPlayback?.Dispose();
            }
            cold?.Dispose(); world?.Dispose(); records?.Dispose();
            File.Delete(plugin); Directory.Delete(directory);
            // Mixer and main-thread retirement are asynchronous. Observe the
            // native boolean utility without recreating a managed binding.
            // Real wall time bounds failure even if the driver stops mixing.
            var retirement = Stopwatch.StartNew();
            string[] retained;
            while (true)
            {
                retained = audioOwners.Where(value => BoneEmitterInstanceAlive(nativeValidity, value.Value))
                    .Select(value => $"{value.Key}={value.Value}").ToArray();
                if (retained.Length == 0 || retirement.Elapsed.TotalSeconds >= 2) break;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Require(retained.Length == 0, "Native source-bone audio owners remained live after bounded source retirement: " +
                string.Join(", ", retained) + $"; driver={AudioServer.GetDriverName()} lastMixSeconds={AudioServer.GetTimeSinceLastMix():R} wallSeconds={retirement.Elapsed.TotalSeconds:R}");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GD.Print($"OPENNV_NATIVE_BONE_EMITTER_RETIREMENT_PASS audioOwners={audioOwners.Count} nativeIdsGone=true nonbindingValidity=true mixerAndMainRetired=true boundedWallSeconds=2");
    }

    private static bool BoneEmitterInstanceAlive(Expression query, ulong id)
    {
        // The native utility restores these exact bits as an unsigned ObjectID.
        using var inputs = new Godot.Collections.Array { unchecked((long)id) };
        using var result = query.Execute(inputs, showError: false);
        Require(!query.HasExecuteFailed() && result.VariantType == Variant.Type.Bool,
            "Native retirement validity query failed: " + query.GetErrorText());
        return result.AsBool();
    }

    private static Node3D BoneEmitterActor(FalloutFormKey reference, string name)
    {
        var actor = new Node3D
        {
            Name = name + "Actor",
            ProcessMode = ProcessModeEnum.Pausable,
            Transform = new(new Basis(Vector3.Up, .7f).Scaled(Vector3.One * 1.2f), new(7, 4, -3))
        };
        var skeleton = NativeNifMeshBuilder.BuildActorSkeleton(ActorSkinFixture("ActorRoot", false), 1);
        try
        {
            actor.SetMeta("opennv_reference_form_key", reference.ToString());
            skeleton.Node.Name = name; skeleton.Node.SetMeta("opennv_source_model", "meshes\\fixture\\skeleton.nif");
            actor.AddChild(skeleton.Node); skeleton.BindSoundSource(actor);
            skeleton.Node.SetBonePosePosition(skeleton.BoneIndex("ActorRoot"), new(2, 1, -4));
            skeleton.Node.SetBonePosePosition(skeleton.BoneIndex("Joint"), new(1, 3, 2));
            skeleton.Node.SetBonePoseRotation(skeleton.BoneIndex("Joint"), new(Vector3.Up, .3f));
            return actor;
        }
        catch { if (skeleton.Node.GetParent() is null) skeleton.Node.Free(); actor.Free(); throw; }
    }
    private static Transform3D[] BoneEmitterPoses(Skeleton3D skeleton) =>
        Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBonePose).ToArray();
    private static Dictionary<string, AudioStream> BoneEmitterStreams(NativeOwnedAnimationSoundPlayer owner) =>
        (Dictionary<string, AudioStream>)typeof(NativeOwnedAnimationSoundPlayer)
            .GetField("_streams", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static NativeOwnedPcmStream BoneEmitterPcm(NativeOwnedAnimationSoundPlayer owner, AudioStreamPlayer player)
    {
        var voice = ((IDictionary)typeof(NativeOwnedAnimationSoundPlayer).GetField("_voices", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(owner)!)[player]!;
        return (NativeOwnedPcmStream)voice.GetType().GetProperty("Pcm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(voice)!;
    }
    private static void BoneEmitterTrack(NativeOwnedAnimationSoundPlayer owner, AudioStreamPlayer player, Node3D emitter,
        FalloutSoundLoop loop, FalloutFormKey sound, long generation, NativeOwnedPcmStream pcm) =>
        typeof(NativeOwnedAnimationSoundPlayer).GetMethod("TrackVoice", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(owner, [player, emitter, loop, sound, null, generation, true, pcm]);
    private static void BoneEmitterPlay(NativeOwnedAnimationSoundPlayer owner, AudioStreamPlayer player) =>
        typeof(NativeOwnedAnimationSoundPlayer).GetMethod("PlayVoice", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, [player]);
    private static void BoneEmitterReject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        catch (TargetInvocationException error) when (error.InnerException is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Source bone semantic identity drift was admitted.");
    }
    private static AudioStreamWav BoneEmitterWav(string path)
    {
        var samples = Enumerable.Range(0, 1024).SelectMany(index => BitConverter.GetBytes(checked((short)(index * 29 - 14000)))).ToArray();
        var wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 32000, Data = samples };
        wav.SetMeta("opennv_owned_media_source", "first-party-bone-emitter-persistence-fixture");
        wav.SetMeta("opennv_owned_media_path", path); wav.SetMeta("opennv_owned_media_sha256", Convert.ToHexString(SHA256.HashData(samples)));
        return wav;
    }
    private static byte[] BoneEmitterPlugin()
    {
        static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
        static byte[] Field(string name, byte[] payload) => Bytes(writer =>
        { writer.Write(System.Text.Encoding.ASCII.GetBytes(name)); writer.Write(checked((ushort)payload.Length)); writer.Write(payload); });
        static byte[] Record(string name, uint id, params byte[][] fields) => Bytes(writer =>
        {
            var payload = Join(fields); writer.Write(System.Text.Encoding.ASCII.GetBytes(name)); writer.Write(payload.Length);
            writer.Write(0U); writer.Write(id); writer.Write(0UL); writer.Write(payload);
        });
        var data = new byte[36]; data[0] = 1; data[1] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)(FalloutSoundFlags.TwoDimensional | FalloutSoundFlags.Loop));
        for (var index = 0; index < 5; index++) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12 + index * 2), 100);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 23); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), 117);
        var references = Join(Record("ACHR", 2, Field("NAME", BitConverter.GetBytes(3U)), Field("DATA", new byte[24]),
            Field("XESP", [4, 0, 0, 0, 0, 0xa5, 0x5a, 0xff])),
            Record("REFR", 4, Field("NAME", BitConverter.GetBytes(5U)), Field("DATA", new byte[24])));
        var group = Bytes(writer =>
        { writer.Write("GRUP"u8); writer.Write(24 + references.Length); writer.Write(20U); writer.Write(6U); writer.Write(0UL); writer.Write(references); });
        return Join(Record("TES4", 0), Record("NPC_", 3), Record("STAT", 5),
            Record("SOUN", 1, Field("EDID", "EmitterLoop\0"u8.ToArray()), Field("FNAM", "fx\\bone.wav\0"u8.ToArray()), Field("SNDD", data)),
            Record("CELL", 20, Field("DATA", [1])), group);
    }
}
