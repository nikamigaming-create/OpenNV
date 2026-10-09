using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Tools;

public partial class NativePcmPlaybackAudit
{
    private async Task CheckSounPcmPause(AudioStreamWav samples)
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-pcm-pause-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var plugin = Path.Combine(directory, "PcmPause.esm");
        var previousPause = GetTree().Paused;
        var previousMode = ProcessMode;
        Node3D? actor = null, coldActor = null;
        FalloutPluginStack? records = null;
        try
        {
            ProcessMode = ProcessModeEnum.Always;
            File.WriteAllBytes(plugin, PauseSoundPlugin());
            records = FalloutPluginStack.Load(directory, ["PcmPause.esm"]);
            actor = new Node3D { Name = "PcmPauseActor", ProcessMode = ProcessModeEnum.Pausable };
            actor.SetMeta("opennv_reference_form_key", "PcmPause.esm:000002");
            AddChild(actor);
            var events = new FalloutAnimationSoundEvents(new("PcmPause.esm", 2));
            var owner = new NativeOwnedAnimationSoundPlayer(records, null!, actor, .01f, new(7), events);
            actor.AddChild(owner);
            var sound = FalloutSoundRecordReader.Read(records, new("PcmPause.esm", 1));
            var loop = FalloutSoundLoop.Read(sound);
            using var wav = PauseWav(samples.Data, samples.MixRate, samples.Stereo, sound.LogicalPath);
            var pcm = new NativeOwnedPcmStream(wav, loop);
            var generation = PauseSoundGeneration(records, events, sound, wav);
            var player = new AudioStreamPlayer { Stream = pcm.Stream };
            GetTree().Paused = true;
            PauseTrack(owner, player, actor, loop, sound.FormKey, generation, pcm);
            PausePlay(owner, player);
            var initial = PauseClock(pcm);
            if (!initial.Playing || initial.Position != 0 || initial.Loops != 0)
                throw new InvalidDataException("Paused-before-Play SOUN lost its original initial source clock.");
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            if (PauseClock(pcm) != initial)
                throw new InvalidDataException("SOUN PCM advanced after Play under an already paused world.");
            GetTree().Paused = false;
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            GetTree().Paused = true;
            var saved = events.Capture();
            var warmClock = PauseClock(pcm);
            if (warmClock.Loops <= 0)
                throw new InvalidDataException("The actual native mixer did not consume the source SOUN loop.");
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            if (PauseClock(pcm) != warmClock)
                throw new InvalidDataException("SOUN pause fading changed its retained source clock.");

            coldActor = new Node3D { Name = "PcmPauseCold", ProcessMode = ProcessModeEnum.Pausable };
            coldActor.SetMeta("opennv_reference_form_key", "PcmPause.esm:000002");
            AddChild(coldActor);
            var coldEvents = new FalloutAnimationSoundEvents(events.Reference);
            var coldOwner = new NativeOwnedAnimationSoundPlayer(records, null!, coldActor, .01f, new(7), coldEvents);
            // First-party fixture media is injected into the ordinary memory
            // cache. The real RestorePcmVoices/TrackVoice/PlayVoice route follows.
            var streams = (Dictionary<string, AudioStream>)typeof(NativeOwnedAnimationSoundPlayer)
                .GetField("_streams", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coldOwner)!;
            streams.Add(sound.LogicalPath, PauseWav(samples.Data, samples.MixRate, samples.Stereo, sound.LogicalPath));
            coldEvents.Restore(saved, records);
            coldActor.AddChild(coldOwner); coldOwner.RequirePcmRestored();
            var coldPlayer = (AudioStreamPlayer)coldOwner.ActiveNativeVoices.Single();
            var coldPcm = PausePcm(coldOwner, coldPlayer);
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            if (PauseClock(coldPcm) != warmClock || coldEvents.Capture().Events.Single().Playback!.Samples != warmClock)
                throw new InvalidDataException("Cold SOUN restoration advanced its original clock behind the paused world.");
            using (var warmPlayback = player.GetStreamPlayback())
            using (var coldPlayback = coldPlayer.GetStreamPlayback())
            {
                AudioServer.Lock();
                try
                {
                    player.ProcessMode = ProcessModeEnum.Always; coldPlayer.ProcessMode = ProcessModeEnum.Always;
                    var expected = warmPlayback.MixAudio(1.137f, 257);
                    var actual = coldPlayback.MixAudio(1.137f, 257);
                    if (expected.Length != 257 || !expected.SequenceEqual(actual) || pcm.Capture() != coldPcm.Capture() ||
                        pcm.Capture() == warmClock)
                        throw new InvalidDataException("Native SOUN eligibility override changed or blocked its restored sample suffix.");
                }
                finally
                {
                    player.ProcessMode = ProcessModeEnum.Inherit; coldPlayer.ProcessMode = ProcessModeEnum.Inherit;
                    AudioServer.Unlock();
                }
            }
            var modePaused = PauseClock(coldPcm);
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            if (PauseClock(coldPcm) != modePaused)
                throw new InvalidDataException("Restoring a native voice process mode failed to suspend its PCM clock.");

            GetTree().Paused = false;
            var finite = FalloutSoundRecordReader.Read(records, new("PcmPause.esm", 3));
            using var finiteWav = PauseWav(Enumerable.Range(0, 128).SelectMany(_ => samples.Data).ToArray(),
                samples.MixRate, samples.Stereo, finite.LogicalPath);
            using var finitePcm = new NativeOwnedPcmStream(finiteWav, new(FalloutSoundLoopMode.None, 0, 0));
            var finiteGeneration = PauseSoundGeneration(records, events, finite, finiteWav);
            var finitePlayer = new AudioStreamPlayer { Stream = finitePcm.Stream, ProcessMode = ProcessModeEnum.Pausable };
            PauseTrack(owner, finitePlayer, actor, new(FalloutSoundLoopMode.None, 0, 0), finite.FormKey, finiteGeneration, finitePcm);
            PausePlay(owner, finitePlayer);
            await ToSignal(GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
            var voice = ((IDictionary)typeof(NativeOwnedAnimationSoundPlayer)
                .GetField("_voices", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!)[finitePlayer]!;
            if (typeof(NativeOwnedAnimationSoundPlayer).GetMethod("ReadFiniteVoice", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(owner, [finitePlayer, voice]) is not FalloutFiniteSoundVoice)
                throw new InvalidDataException("Finite save-drain fixture has no proven original native playback.");
            GetTree().Paused = true;
            var finitePaused = PauseClock(finitePcm);
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            if (PauseClock(finitePcm) != finitePaused)
                throw new InvalidDataException("Independent finite PCM attachment ignored its actual native node pause.");
            using var drain = (NativeOwnedFiniteSoundSaveDrain)typeof(NativeOwnedAnimationSoundPlayer)
                .GetMethod("PrepareFiniteSaveDrain", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, [finitePlayer, voice])!;
            drain.Activate();
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
            if (!GetTree().Paused || !drain.ObserveFinished() ||
                events.Events.Single(entry => entry.Generation == finiteGeneration).End != FalloutAnimationSoundEnd.NativeFinished)
                throw new InvalidDataException("Actual finite save drain was blocked by PCM suspension or lost native Finished.");
            NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(actor);
            NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(coldActor);
            if (owner.ActiveNativeVoices.Count != 0 || coldOwner.ActiveNativeVoices.Count != 0 ||
                events.Capture().Events.Single(entry => entry.Generation == generation).End != FalloutAnimationSoundEnd.SourceUnloaded ||
                coldEvents.Capture().Events.Single().End != FalloutAnimationSoundEnd.SourceUnloaded)
                throw new InvalidDataException("Committed source unload retained a native loop or lost its saved terminal state.");
        }
        finally
        {
            GetTree().Paused = previousPause;
            if (IsInstanceValid(coldActor)) coldActor!.Free();
            if (IsInstanceValid(actor)) actor!.Free();
            records?.Dispose();
            File.Delete(plugin); Directory.Delete(directory);
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ProcessMode = previousMode;
        }
    }

    private static FalloutPcmPlaybackSnapshot PauseClock(NativeOwnedPcmStream pcm)
    {
        AudioServer.Lock();
        try { return pcm.Capture(); }
        finally { AudioServer.Unlock(); }
    }
    private static AudioStreamWav PauseWav(byte[] samples, int rate, bool stereo, string path)
    {
        var wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = stereo, Data = samples };
        wav.SetMeta("opennv_owned_media_source", "first-party-pcm-pause-fixture");
        wav.SetMeta("opennv_owned_media_path", path);
        wav.SetMeta("opennv_owned_media_sha256", Convert.ToHexString(SHA256.HashData(samples)));
        return wav;
    }
    private static long PauseSoundGeneration(FalloutPluginStack records, FalloutAnimationSoundEvents events,
        FalloutSoundRecord sound, AudioStreamWav wav)
    {
        var selected = FalloutAnimationSound.Select(sound, [sound.LogicalPath], new(7), ownsLoopStop: true);
        var generation = events.Begin(records, selected, "Sound:" + sound.FormKey, false, [sound.LogicalPath]);
        events.BindMedia(generation, wav.GetMeta("opennv_owned_media_sha256").AsString());
        return generation;
    }
    private static void PauseTrack(NativeOwnedAnimationSoundPlayer owner, AudioStreamPlayer node, Node3D actor,
        FalloutSoundLoop loop, FalloutFormKey sound, long generation, NativeOwnedPcmStream pcm) =>
        typeof(NativeOwnedAnimationSoundPlayer).GetMethod("TrackVoice", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(owner, [node, actor, loop, sound, null, generation, true, pcm]);
    private static void PausePlay(NativeOwnedAnimationSoundPlayer owner, AudioStreamPlayer node) =>
        typeof(NativeOwnedAnimationSoundPlayer).GetMethod("PlayVoice", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, [node]);
    private static NativeOwnedPcmStream PausePcm(NativeOwnedAnimationSoundPlayer owner, AudioStreamPlayer node)
    {
        var voice = ((IDictionary)typeof(NativeOwnedAnimationSoundPlayer)
            .GetField("_voices", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!)[node]!;
        return (NativeOwnedPcmStream)voice.GetType().GetProperty("Pcm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(voice)!;
    }
    private static byte[] PauseSoundPlugin()
    {
        static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
        static byte[] Field(string signature, byte[] payload)
        {
            var bytes = new byte[6 + payload.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)payload.Length));
            payload.CopyTo(bytes, 6); return bytes;
        }
        static byte[] Record(string signature, uint id, params byte[][] fields)
        {
            var payload = Join(fields); var bytes = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)payload.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); payload.CopyTo(bytes, 24); return bytes;
        }
        static byte[] Sound(uint id, bool loop)
        {
            var data = new byte[36]; data[0] = 1; data[1] = 2;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)(FalloutSoundFlags.TwoDimensional | (loop ? FalloutSoundFlags.Loop : 0)));
            for (var index = 0; index < 5; index++) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12 + index * 2), 100);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22), 100);
            if (loop)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 23);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), 117);
            }
            return Record("SOUN", id, Field("EDID", Encoding.ASCII.GetBytes((loop ? "PcmPauseLoop" : "PcmPauseFinite") + "\0")),
                Field("OBND", new byte[12]), Field("FNAM", Encoding.ASCII.GetBytes(loop ? "fx\\pause.wav\0" : "fx\\finite.wav\0")), Field("SNDD", data));
        }
        return Join(Record("TES4", 0), Sound(1, true), Record("REFR", 2), Sound(3, false));
    }
}
