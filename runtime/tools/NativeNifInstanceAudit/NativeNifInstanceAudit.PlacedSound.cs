using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeNifInstanceAudit
{
    private async Task ExercisePlacedSoundPersistence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-placed-sound-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var file = Path.Combine(directory, "BoneEmitter.esm");
        RuntimeLiveContentSource? content = null;
        var detectionMaster = Path.Combine(directory, "FalloutNV.esm");
        var archiveIni = Path.Combine(directory, "fixture.ini");
        var bootstrapArchive = Path.Combine(directory, "Fixture.bsa");
        var mediaDirectory = Path.Combine(directory, "sound", "fx"); var mediaFile = Path.Combine(mediaDirectory, "bone.wav");
        FalloutPluginStack? records = null; FalloutReferenceWorld? world = null; FalloutReferenceWorld? cold = null;
        NativeOwnedPlacedSound? warmRoot = null; NativeOwnedPlacedSound? coldRoot = null;
        Node3D? warmContainer = null; NativeOwnedPlacedSound? unownedRoot = null; AudioStreamWav? unownedWav = null;
        AudioStreamWav? warmWav = null; AudioStreamWav? coldWav = null;
        try
        {
            File.WriteAllBytes(file, BoneEmitterPlugin(positioned: true));
            WritePlacedSoundBootstrapArchive(bootstrapArchive);
            File.WriteAllText(archiveIni, "[Archive]\nsArchiveList=Fixture.bsa\n");
            // Only installation detection uses this first-party empty TES4;
            // the selected source graph is the actual BoneEmitter fixture.
            File.WriteAllBytes(detectionMaster, BoneEmitterPlugin()[..24]);
            Directory.CreateDirectory(mediaDirectory);
            using (var generated = BoneEmitterWav("sound\\fx\\bone.wav"))
            using (var payload = new MemoryStream())
            {
                using (var writer = new BinaryWriter(payload, System.Text.Encoding.ASCII, leaveOpen: true))
                {
                    var data = generated.Data;
                    writer.Write("RIFF"u8); writer.Write(checked(36 + data.Length)); writer.Write("WAVE"u8);
                    writer.Write("fmt "u8); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1);
                    writer.Write(32000); writer.Write(64000); writer.Write((ushort)2); writer.Write((ushort)16);
                    writer.Write("data"u8); writer.Write(data.Length); writer.Write(data);
                }
                File.WriteAllBytes(mediaFile, payload.ToArray());
            }
            content = RuntimeLiveContentSource.Open(directory, RuntimeLiveContentSource.FalloutNewVegasGame,
                activePlugins: ["BoneEmitter.esm"], archiveIniPath: archiveIni, includeImplicitPlugins: false);
            Require(content.ArchivePaths.Count == 1 && content.ArchivePaths[0] == bootstrapArchive &&
                content.TryRead("fixtures/bootstrap.bin", null, out var bootstrap, out var bootstrapIdentity) &&
                bootstrap.AsSpan().SequenceEqual("OpenNV fixture bootstrap"u8) &&
                bootstrapIdentity.StartsWith(bootstrapArchive + "::", StringComparison.OrdinalIgnoreCase),
                "Placed sound fixture did not own its isolated first-party archive bootstrap.");
            records = FalloutPluginStack.Load(content.PluginSources);
            var reference = new FalloutFormKey("BoneEmitter.esm", 6); var cell = new FalloutFormKey("BoneEmitter.esm", 20);
            var placed = FalloutCellSceneReader.Read(records, cell).References.Single(value => value.FormKey == reference);
            ExerciseSoundSourceScenes(records, content, reference);
            world = new(records); var state = world.Get(reference); var errors = new List<string>();
            var path = FalloutSoundRecordReader.Read(records, placed.Base).LogicalPath;
            warmRoot = new(records, content, world, placed, .01f, errors.Add) { Position = new(3, 2, -4) };
            warmWav = PlacedSoundMedia(content, path); BoneEmitterStreams(warmRoot.PlaybackOwner).Add(path, warmWav);
            warmContainer = new Node3D(); AddChild(warmContainer); warmContainer.AddChild(warmRoot); warmRoot._Process(0); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(errors.Count == 0 && state.AnimationSoundEvents.Events.Count == 1 &&
                state.AnimationSoundEvents.Events[0].End == FalloutAnimationSoundEnd.Active && warmRoot.Position == new Vector3(3, 2, -4) &&
                warmRoot.PlaybackOwner.ActiveNativeVoices.Single() is AudioStreamPlayer3D actualVoice &&
                actualVoice.GlobalPosition == warmRoot.GlobalPosition,
                "Authored model-less SOUN did not own its actual source voice/position.");
            warmRoot._Process(0); Require(state.AnimationSoundEvents.Events.Count == 1, "Placed SOUN replayed its startup each frame.");
            var saved = world.Capture();
            OpenNV.Runtime.RuntimeCoordinator.RetireNativeActorBindings(warmContainer);
            OpenNV.Runtime.RuntimeCoordinator.RetireNativeActorBindings(warmContainer);
            warmRoot._Process(0);
            Require(state.AnimationSoundEvents.Events.Count == 1,
                "Explicit retired placed source restarted before its queued tree deletion.");
            warmContainer.Free(); warmContainer = null; warmRoot = null;
            Require(state.AnimationSoundEvents.Events[0].End == FalloutAnimationSoundEnd.SourceUnloaded && state.AnimationSoundEvents.CanCapture,
                "Placed SOUN source retirement became a lost/unowned cancellation.");
            cold = new(records); cold.Restore(saved); var restored = cold.Get(reference);
            coldRoot = new(records, content, cold, placed, .01f, errors.Add) { Position = new(3, 2, -4) };
            coldWav = PlacedSoundMedia(content, path); BoneEmitterStreams(coldRoot.PlaybackOwner).Add(path, coldWav);
            AddChild(coldRoot); coldRoot._Process(0); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(errors.Count == 0 && restored.AnimationSoundEvents.Events.Count == 1 && restored.AnimationSoundEvents.CanCapture,
                "Cold placed SOUN replayed startup or failed its actual saved PCM generation.");
            cold.SetEnabled(reference, false); cold.AdvanceEnableChanges(0, new(1, 1), _ => false);
            GamebryoReferenceEnableRuntime.Apply(coldRoot, false);
            Require(restored.AnimationSoundEvents.Events[0].End == FalloutAnimationSoundEnd.SourceStopped && restored.AnimationSoundEvents.CanCapture,
                "Authoritative SOUN Disable did not settle exactly its source loop.");
            cold.SetEnabled(reference, true); cold.AdvanceEnableChanges(0, new(1, 1), _ => false);
            GamebryoReferenceEnableRuntime.Apply(coldRoot, true); coldRoot._Process(0);
            Require(restored.AnimationSoundEvents.Events.Count == 2 && restored.AnimationSoundEvents.Events[1].End == FalloutAnimationSoundEnd.Active,
                "Placed SOUN Enable failed to create a fresh source handle epoch.");
            coldRoot.PlaybackOwner.ActiveNativeVoices.Single().Free();
            Require(!restored.AnimationSoundEvents.CanCapture,
                "Unexpected spatial voice loss was masked as an expected source unload.");
            unownedRoot = new(records, content, world, placed, .01f, errors.Add);
            unownedWav = PlacedSoundMedia(content, path); BoneEmitterStreams(unownedRoot.PlaybackOwner).Add(path, unownedWav);
            AddChild(unownedRoot); unownedRoot._Process(0); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(state.AnimationSoundEvents.CanCapture, "Fresh unowned-loss witness never acquired a real source voice.");
            unownedRoot.Free(); unownedRoot = null;
            Require(!state.AnimationSoundEvents.CanCapture,
                "Raw root loss without its source-retirement notification fabricated an unload receipt.");
            GD.Print("OPENNV_PLACED_SOUND_PERSISTENCE_PASS sourceReference=true sourcePosition=true oneStartup=true coldNoReplay=true outerRootSourceUnload=true retiredFence=true disableEnableEpoch=true unexpectedVoiceLossRefused=true unexpectedRootLossRefused=true componentOnly=true");
        }
        finally
        {
            unownedRoot?.Free(); coldRoot?.Free();
            if (warmContainer is not null) warmContainer.Free(); else warmRoot?.Free();
            unownedWav?.Dispose(); coldWav?.Dispose(); warmWav?.Dispose();
            cold?.Dispose(); world?.Dispose(); records?.Dispose(); content?.Dispose();
            File.Delete(mediaFile);
            if (Directory.Exists(mediaDirectory)) Directory.Delete(mediaDirectory);
            var soundDirectory = Path.GetDirectoryName(mediaDirectory)!;
            if (Directory.Exists(soundDirectory)) Directory.Delete(soundDirectory);
            File.Delete(detectionMaster); File.Delete(archiveIni); File.Delete(bootstrapArchive); File.Delete(file); Directory.Delete(directory);
        }
    }
    private static void WritePlacedSoundBootstrapArchive(string path)
    {
        // First-party container bootstrap only; no media request resolves here.
        var folder = System.Text.Encoding.ASCII.GetBytes("fixtures\0");
        var name = System.Text.Encoding.ASCII.GetBytes("bootstrap.bin\0");
        byte[] payload = "OpenNV fixture bootstrap"u8.ToArray();
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(0x00415342u); writer.Write(104u); writer.Write(36u); writer.Write(3u);
        writer.Write(1u); writer.Write(1u); writer.Write((uint)folder.Length); writer.Write((uint)name.Length); writer.Write(0u);
        writer.Write(0ul); writer.Write(1u); writer.Write((uint)(52 + name.Length));
        writer.Write((byte)folder.Length); writer.Write(folder);
        writer.Write(0ul); writer.Write((uint)payload.Length); writer.Write((uint)(52 + 1 + folder.Length + 16 + name.Length));
        writer.Write(name); writer.Write(payload);
    }

    private static AudioStreamWav PlacedSoundMedia(RuntimeLiveContentSource content, string path)
    {
        if (!content.TryRead(path, null, out var bytes, out var identity))
            throw new InvalidDataException("First-party placed sound source media is absent.");
        NativeOwnedMediaFormat.ValidateWav(bytes);
        var wav = AudioStreamWav.LoadFromBuffer(bytes, new Godot.Collections.Dictionary()) ??
            throw new InvalidDataException("First-party placed sound WAV was rejected.");
        wav.SetMeta("opennv_owned_media_source", identity); wav.SetMeta("opennv_owned_media_path", path);
        wav.SetMeta("opennv_owned_media_sha256", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        return wav;
    }

}
