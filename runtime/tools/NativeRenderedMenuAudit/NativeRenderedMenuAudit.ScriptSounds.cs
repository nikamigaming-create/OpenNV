using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private async Task ScriptSounds(string baseRoot, string mod, string root, string questId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        NativeOwnedScriptSoundPlayer? presenter = null;
        AudioEffectCapture? capture = null;
        var paused = GetTree().Paused;
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
            var hash = SHA256.HashData(quest.ReadData());
            var fields = quest.ReadSubrecords().ToArray();
            var sourceIndex = Array.FindIndex(fields, field => field.Signature == "SCTX" &&
                FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span)).Any(IsPlaySound));
            if (sourceIndex < 0) throw new InvalidDataException("Owned sound fixture requires a source PlaySound stage entry.");
            var begin = sourceIndex;
            while (begin > 0 && fields[begin].Signature != "QSDT") --begin;
            if (fields[begin].Signature != "QSDT") throw new InvalidDataException("Owned sound command has no stage-entry scope.");
            var end = begin + 1;
            while (end < fields.Length && fields[end].Signature is not ("QSDT" or "INDX" or "QOBJ")) ++end;
            var entry = fields[begin..end];
            var command = FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(fields[sourceIndex].Data.Span)).First(IsPlaySound);
            var executor = new FalloutReferenceScripts(records, world, new FalloutQuestState(records),
                new((_, _) => false, _ => throw new InvalidDataException("Sound fixture invented a presentation effect.")));
            presenter = new(world.Sounds, RuntimeLiveContentSource.Current!, world.Menus); AddChild(presenter);
            capture = new AudioEffectCapture { BufferLength = 2 }; AudioServer.AddBusEffect(0, capture);
            long samples = 0; var peak = 0f;
            void Drain()
            {
                var count = capture.GetFramesAvailable();
                if (count == 0) return;
                var frames = capture.GetBuffer(count);
                if (frames.Length != count) throw new InvalidDataException("Native sound audit lost an audio packet.");
                samples += frames.Length;
                foreach (var frame in frames) peak = Math.Max(peak, Math.Max(Math.Abs(frame.X), Math.Abs(frame.Y)));
            }
            executor.ExecuteStage(quest, entry, command);
            var request = world.Sounds.LastRequest ?? throw new InvalidDataException("Owned sound command did not publish a request.");
            var media = world.Sounds.LastMedia ?? throw new InvalidDataException("Owned sound command did not prepare its stream.");
            var deadline = Time.GetTicksMsec() + checked((ulong)((media.Duration / request.Selection.PitchScale + 3) * 1000));
            while (world.Sounds.ActiveVoices != 0 && Time.GetTicksMsec() < deadline)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); Drain();
                if (world.Sounds.LastError is not null) throw new InvalidOperationException(world.Sounds.LastError);
            }
            Drain();
            if (world.Sounds.ActiveVoices != 0 || peak <= 0.000001f || capture.GetDiscardedFrames() != 0)
                throw new InvalidOperationException("Owned sound did not complete with observed nonzero mixer samples and no sample loss.");

            world.Menus.Publish(false, [1013]); GetTree().Paused = true;
            executor.ExecuteStage(quest, entry, command); var queuedId = world.Sounds.LastRequest!.Id;
            var queued = presenter.GetChildren().OfType<AudioStreamPlayer>().Single(voice => voice.Name == $"ScriptSound_{queuedId}");
            executor.ExecuteStage(quest, entry, command + " 1"); var systemId = world.Sounds.LastRequest!.Id;
            var system = presenter.GetChildren().OfType<AudioStreamPlayer>().Single(voice => voice.Name == $"ScriptSound_{systemId}");
            var waitUntil = Time.GetTicksMsec() + 250;
            while (Time.GetTicksMsec() < waitUntil) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); Drain(); }
            if (queued.Playing || !system.Playing || system.GetPlaybackPosition() <= 0 || system.StreamPaused)
                throw new InvalidOperationException("Owned native sound menu/system routing failed.");
            GetTree().Paused = false; world.Menus.Publish(true);
            waitUntil = Time.GetTicksMsec() + 250;
            while (Time.GetTicksMsec() < waitUntil) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); Drain(); }
            if (!queued.Playing || queued.GetPlaybackPosition() <= 0)
                throw new InvalidOperationException("Queued owned sound did not start after returning to GameMode.");
            presenter.Free(); presenter = null;
            if (world.Sounds.ActiveVoices != 0 || !SHA256.HashData(quest.ReadData()).AsSpan().SequenceEqual(hash))
                throw new InvalidDataException("Sound retirement leaked a voice or changed source bytes.");
            GD.Print("OPENNV_NATIVE_SCRIPT_SOUND_PASS " + JsonSerializer.Serialize(new
            {
                quest = quest.FormKey,
                winner = quest.Plugin.Name,
                command,
                request,
                media,
                samples,
                peak,
                discarded = capture.GetDiscardedFrames(),
                completion = true,
                menuQueue = true,
                systemSound = true,
                retirement = true,
                sourceReadonly = true,
                recording = false,
                boundary = "isolated-owned-command-and-native-mixer;endpoint-and-campaign-parity-unverified"
            }));
        }
        finally
        {
            presenter?.Free(); GetTree().Paused = paused;
            if (capture is not null)
            {
                for (var index = AudioServer.GetBusEffectCount(0) - 1; index >= 0; --index)
                    if (AudioServer.GetBusEffect(0, index) == capture) AudioServer.RemoveBusEffect(0, index);
                capture.Dispose();
            }
            RuntimeLiveContentSource.Clear();
        }
        static bool IsPlaySound(string command) => command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0]
            .Equals("PlaySound", StringComparison.OrdinalIgnoreCase);
    }
}
