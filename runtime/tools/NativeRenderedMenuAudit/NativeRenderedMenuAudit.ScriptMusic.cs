using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

public partial class NativeRenderedMenuAudit
{
    private async Task ScriptMusic(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        NativeOwnedMusicPlayer? music = null;
        AudioEffectCapture? capture = null;
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            var script = records.GetEffective(new("FalloutNV.esm", 0x11e65e));
            var sourceHash = SHA256.HashData(script.ReadData());
            var fields = script.ReadSubrecords().ToArray();
            var command = FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(fields.Single(field => field.Signature == "SCTX").Data.Span))
                .Single(line => line.StartsWith("PlayMusic ", StringComparison.OrdinalIgnoreCase));
            var bindings = new FalloutScriptBindings(records, script, script, fields);
            var form = bindings.Form(command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1]).FormKey;
            music = new(records) { Name = "OwnedMusicAudit" }; AddChild(music);
            capture = new AudioEffectCapture { BufferLength = 2 }; AudioServer.AddBusEffect(0, capture);
            long samples = 0; var peak = 0f;
            void Drain()
            {
                var count = capture.GetFramesAvailable();
                if (count == 0) return;
                var frames = capture.GetBuffer(count);
                if (frames.Length != count) throw new InvalidDataException("Native music audit lost an audio packet.");
                samples += frames.Length;
                foreach (var frame in frames) peak = Math.Max(peak, Math.Max(Math.Abs(frame.X), Math.Abs(frame.Y)));
            }
            async Task FramesUntil(Func<bool> completed, ulong deadline)
            {
                while (!completed() && Time.GetTicksMsec() < deadline)
                { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); Drain(); }
            }
            music.Play(form);
            await FramesUntil(() => music.Capture()!.PositionSeconds > .25, Time.GetTicksMsec() + 3000);
            var saved = JsonSerializer.Deserialize<FalloutMusicPlaybackSnapshot>(JsonSerializer.Serialize(music.Capture()))!;
            if (!saved.Playing || saved.PositionSeconds <= 0 || !music.Playing)
                throw new InvalidOperationException("Owned music did not begin actual native playback.");
            var voice = music.GetChildren().OfType<AudioStreamPlayer>().Single();
            var declaration = FalloutMusicType.Read(records.GetEffective(form));
            if (!voice.IsInGroup("opennv_music") || voice.VolumeDb != declaration.GainDb ||
                voice.Stream is not AudioStreamMP3 { Loop: false })
                throw new InvalidDataException("Owned music lost its movie routing, attenuation or nonloop declaration.");
            music.Free(); music = new(records) { Name = "ColdOwnedMusicAudit" };
            music.Restore(saved); AddChild(music);
            await FramesUntil(() => music.Capture()!.PositionSeconds > saved.PositionSeconds + .1, Time.GetTicksMsec() + 3000);
            if (!music.Playing || music.Capture()!.PositionSeconds <= saved.PositionSeconds)
                throw new InvalidOperationException("Music cold restore did not resume its actual playback cursor.");
            try
            {
                music.Restore(saved with { MediaSha256 = new string('0', 64) });
                throw new InvalidOperationException("Changed music media was admitted.");
            }
            catch (InvalidDataException) { }
            if (!music.Playing || music.SourceForm != form) throw new InvalidOperationException("Invalid music restore replaced active playback.");
            voice = music.GetChildren().OfType<AudioStreamPlayer>().Single(value => !value.IsQueuedForDeletion());
            voice.StreamPaused = true;
            var pauseUntil = Time.GetTicksMsec() + 200;
            await FramesUntil(() => Time.GetTicksMsec() >= pauseUntil, pauseUntil);
            var paused = music.Capture()!;
            if (!voice.StreamPaused || !paused.Playing || paused.PositionSeconds >= paused.DurationSeconds)
                throw new InvalidOperationException("Movie-style music pause was captured as a completed stinger.");
            voice.StreamPaused = false;
            // Hold main-thread Finished delivery while the actual mixer finishes.
            // Capture must already observe completion instead of saving cursor zero.
            voice.SetProcessInternal(false);
            await FramesUntil(() => !music.Playing, Time.GetTicksMsec() + checked((ulong)((saved.DurationSeconds + 3) * 1000)));
            var ended = music.Capture()!;
            if (ended.Playing || ended.PositionSeconds != ended.DurationSeconds || peak <= .000001f || capture.GetDiscardedFrames() != 0)
                throw new InvalidOperationException("Owned music failed completion or produced no lossless nonzero mixer samples.");
            voice.SetProcessInternal(true);
            await FramesUntil(() => !music.GetChildren().OfType<AudioStreamPlayer>().Any(value => !value.IsQueuedForDeletion()),
                Time.GetTicksMsec() + 1000);
            if (music.GetChildren().OfType<AudioStreamPlayer>().Any(value => !value.IsQueuedForDeletion()))
                throw new InvalidOperationException("Music Finished retained its native playback voice.");
            music.Restore(ended);
            if (music.Playing || music.Capture()!.Playing) throw new InvalidOperationException("Completed music restarted on restore.");
            music.Play(new("FalloutNV.esm", 0x171b4a));
            if (music.GetChildren().OfType<AudioStreamPlayer>().Where(value => !value.IsQueuedForDeletion())
                    .Single().Stream is not AudioStreamMP3 { Loop: true })
                throw new InvalidOperationException("Positive ANAM did not enable native looping.");
            music.Play(new("FalloutNV.esm", 0x8a6d4));
            if (music.Playing || music.Capture() is not { Path: null, Playing: false } ||
                !sourceHash.SequenceEqual(SHA256.HashData(script.ReadData())))
                throw new InvalidOperationException("Silent music failed to stop playback or source bytes changed.");
            GD.Print($"OPENNV_NATIVE_SCRIPT_MUSIC_PASS script={script.FormKey} music={form} samples={samples} peak={peak:R} " +
                "native=true gain=true movieGroup=true pausedCapture=true beforeFinishedCapture=true activeCold=true endedCold=true failedRestoreAtomic=true loop=true stop=true sourceReadonly=true campaign=unverified parity=unverified");
        }
        finally
        {
            music?.Free();
            if (capture is not null)
            {
                for (var index = AudioServer.GetBusEffectCount(0) - 1; index >= 0; --index)
                    if (AudioServer.GetBusEffect(0, index) == capture) AudioServer.RemoveBusEffect(0, index);
                capture.Dispose();
            }
            RuntimeLiveContentSource.Clear();
        }
    }
}
