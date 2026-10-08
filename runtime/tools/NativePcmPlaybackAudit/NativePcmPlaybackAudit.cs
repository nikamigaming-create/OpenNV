using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Tools;

public partial class NativePcmPlaybackAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            CheckUnboundNativeStreamLifetime();
            var bytes = new byte[320];
            for (var index = 0; index < bytes.Length / 2; index++)
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2, 2), (short)(index * 131 - 10000));
            using var wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 32000, Data = bytes };
            var loop = new FalloutSoundLoop(FalloutSoundLoopMode.EnvelopeFast, 23, 117);
            var source = new NativeOwnedPcmStream(wav, loop);
            using var playback = source.Stream.InstantiatePlayback();
            playback.Start();
            var prefix = playback.MixAudio(1.137f, 93);
            var saved = source.Capture();
            var suffix = playback.MixAudio(1.137f, 211);
            var restored = new NativeOwnedPcmStream(wav, loop, saved);
            using var cold = restored.Stream.InstantiatePlayback();
            cold.Start();
            var actual = cold.MixAudio(1.137f, 211);
            if (prefix.Length != 93 || suffix.Length != 211 || !suffix.SequenceEqual(actual) ||
                source.Capture() != restored.Capture() || saved.Position == Math.Truncate(saved.Position))
                throw new InvalidDataException("Native PCM cold continuation changed the fractional sample suffix.");
            restored.ReleaseEnvelope();
            _ = cold.MixAudio(1, 256);
            if (cold.IsPlaying()) throw new InvalidDataException("Original PCM release tail did not finish.");
            var waiting = new NativeOwnedPcmStream(wav, loop);
            using var waitingPlayback = waiting.Stream.InstantiatePlayback();
            waiting.ReleaseEnvelope();
            var pendingStart = waiting.Capture();
            if (!pendingStart.StartPending || pendingStart.Playing || !pendingStart.Releasing)
                throw new InvalidDataException("A source release before native Start lost its pending clock.");
            var waitingCold = new NativeOwnedPcmStream(wav, loop, pendingStart);
            using var waitingColdPlayback = waitingCold.Stream.InstantiatePlayback();
            waitingPlayback.Start(); waitingColdPlayback.Start();
            if (!waitingPlayback.MixAudio(1, 64).SequenceEqual(waitingColdPlayback.MixAudio(1, 64)) ||
                waiting.Capture() != waitingCold.Capture() || waitingColdPlayback.IsPlaying())
                throw new InvalidDataException("Cold native Start restarted an already released source envelope.");
            GetTree().Paused = true;
            var loading = new NativeOwnedPcmStream(wav, loop, saved);
            var loadingPlayer = new AudioStreamPlayer3D { Stream = loading.Stream, AreaMask = 0 };
            AddChild(loadingPlayer); loadingPlayer.Play();
            await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
            if (loading.Capture() != saved)
                throw new InvalidDataException("Cold spatial audio advanced while the world was loading paused.");
            GetTree().Paused = false;
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            GetTree().Paused = true;
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            var loadingPaused = loading.Capture();
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            if (loading.Capture() != loadingPaused)
                throw new InvalidDataException("Cold spatial audio advanced behind the paused session menu.");
            loadingPlayer.Stop(); loadingPlayer.Free(); loading.Stream.Dispose();
            GetTree().Paused = false;
            var live = new NativeOwnedPcmStream(wav, loop);
            var player = new AudioStreamPlayer { Stream = live.Stream };
            AddChild(player); player.Play();
            for (var index = 0; index < 30; index++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (live.Capture().Loops == 0) throw new InvalidDataException("Godot's real mixer did not consume the PCM loop.");
            player.StreamPaused = true;
            // AudioServer retires its pause fade on the mixer thread. A few
            // headless process frames can run before even one audio buffer.
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            var paused = live.Capture();
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
            if (paused != live.Capture()) throw new InvalidDataException("Paused PCM playback advanced.");
            player.Stop();
            for (var index = 0; index < 10; index++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            player.Free();
            source.Stream.Dispose(); restored.Stream.Dispose(); live.Stream.Dispose();
            waiting.Stream.Dispose(); waitingCold.Stream.Dispose();
            await CheckSounPcmPause(wav);
            await CheckSounMixerFailure(wav);
            GD.Print("OPENNV_NATIVE_PCM_PLAYBACK_PASS fractionalCold=true envelope=true mixer=true pause=true sourceMixerFaultRetained=true unboundConstructionRetired=true sourceSoundPause=true finiteDrainOverride=true");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task CheckSounMixerFailure(AudioStreamWav wav)
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-pcm-fault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var plugin = Path.Combine(directory, "PcmFault.esm");
        var priorPause = GetTree().Paused;
        try
        {
            byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
            byte[] Record(string signature, uint id, params byte[][] fields)
            {
                var payload = Join(fields); var bytes = new byte[24 + payload.Length];
                Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)payload.Length));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); payload.CopyTo(bytes, 24); return bytes;
            }
            byte[] Field(string signature, byte[] payload)
            {
                var bytes = new byte[6 + payload.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)payload.Length));
                payload.CopyTo(bytes, 6); return bytes;
            }
            var data = new byte[36]; data[0] = 1; data[1] = 2;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)(FalloutSoundFlags.TwoDimensional | FalloutSoundFlags.EnvelopeFast));
            for (var index = 0; index < 5; index++) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12 + index * 2), 100);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22), 100);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 23);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), 117);
            File.WriteAllBytes(plugin, Join(Record("TES4", 0), Record("SOUN", 1,
                Field("EDID", Encoding.ASCII.GetBytes("PcmFault\0")), Field("OBND", new byte[12]),
                Field("FNAM", Encoding.ASCII.GetBytes("fx\\fault.wav\0")), Field("SNDD", data)), Record("REFR", 2)));
            using var records = FalloutPluginStack.Load(directory, ["PcmFault.esm"]);
            var sound = FalloutSoundRecordReader.Read(records, new("PcmFault.esm", 1));
            var loop = FalloutSoundLoop.Read(sound);
            var mediaHash = Convert.ToHexString(SHA256.HashData(wav.Data));
            wav.SetMeta("opennv_owned_media_source", "synthetic-native-pcm");
            wav.SetMeta("opennv_owned_media_path", sound.LogicalPath); wav.SetMeta("opennv_owned_media_sha256", mediaHash);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var track = typeof(NativeOwnedAnimationSoundPlayer).GetMethod("TrackVoice", flags)!;
            var play = typeof(NativeOwnedAnimationSoundPlayer).GetMethod("PlayVoice", flags)!;
            var finish = typeof(NativeOwnedAnimationSoundPlayer).GetMethod("FinishVoice", flags)!;
            GetTree().Paused = true;
            foreach (var end in new[] { FalloutAnimationSoundEnd.NativeFinished, FalloutAnimationSoundEnd.SourceStopped })
            {
                var actor = new Node3D(); AddChild(actor);
                try
                {
                    using var pcm = new NativeOwnedPcmStream(wav, loop);
                    var events = new FalloutAnimationSoundEvents(new("PcmFault.esm", 2));
                    // This component injects already-decoded first-party samples;
                    // no owned resource lookup or campaign actor is fabricated.
                    var owner = new NativeOwnedAnimationSoundPlayer(records, null!, actor, .01f, new(7), events);
                    actor.AddChild(owner);
                    var selected = FalloutAnimationSound.Select(sound, [sound.LogicalPath], new(7), ownsLoopStop: true);
                    var generation = events.Begin(records, selected, "Sound:PcmFault", false, [sound.LogicalPath]);
                    events.BindMedia(generation, mediaHash);
                    var node = new AudioStreamPlayer { Stream = pcm.Stream }; var callbacks = 0;
                    track.Invoke(owner, [node, actor, loop, sound.FormKey, (Action)(() => callbacks++), generation, true, pcm]);
                    play.Invoke(owner, [node]);
                    using (var playback = node.GetStreamPlayback())
                    {
                        AudioServer.Lock();
                        try { _ = playback.MixAudio(float.NaN, 16); }
                        finally { AudioServer.Unlock(); }
                    }
                    finish.Invoke(owner, [node, end]); finish.Invoke(owner, [node, end]);
                    var failed = events.Capture(); FalloutAnimationSoundEvents.ValidateSource(failed, records, events.Reference);
                    if (callbacks != 0 || owner.ActiveNativeVoices.Count != 0 || records.SoundVoices.ActiveVoices != 0 ||
                        failed.Events.Single().End != FalloutAnimationSoundEnd.Faulted ||
                        failed.Faults is not { Count: 1 } || !failed.Events.Single().Error!.Contains("PCM mixer failed", StringComparison.Ordinal) ||
                        !owner.Unbound.Any(lane => lane.Contains("PCM mixer failed", StringComparison.Ordinal)))
                        throw new InvalidDataException("A failed native SOUN mixer became a successful or repeated source completion, or retained its voice.");
                }
                finally { actor.Free(); }
            }
        }
        finally
        {
            GetTree().Paused = priorPause;
            File.Delete(plugin); Directory.Delete(directory);
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
