using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Tools;

public partial class NativePcmPlaybackAudit : Node
{
    public override async void _Ready()
    {
        try
        {
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
            GD.Print("OPENNV_NATIVE_PCM_PLAYBACK_PASS fractionalCold=true envelope=true mixer=true pause=true");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
