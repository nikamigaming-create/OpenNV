using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private async Task SoundPaths(string baseRoot, string mod, string root, string questId, string[] dependencies)
    {
        Node3D? scene = null;
        NativeOwnedScriptSoundPlayer? presenter = null;
        AudioEffectCapture? capture = null;
        var paused = GetTree().Paused;
        try
        {
            var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
            var questHash = SHA256.HashData(quest.ReadData());
            var fields = quest.ReadSubrecords().ToArray();
            var changes = fields.Select((field, index) => (Field: field, Index: index))
                .Where(row => row.Field.Signature == "SCTX")
                .SelectMany(row => FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(row.Field.Data.Span))
                    .Where(line => FalloutGameModeProgram.Tokens(line)[0].Equals("SetSoundSourceFile", StringComparison.OrdinalIgnoreCase))
                    .Select(line => (row.Index, Command: line))).ToArray();
            if (changes.Length < 2) throw new InvalidDataException("Owned sound path fixture requires source change and reset commands.");
            var soundRecord = FalloutSoundRecordReader.Find(records, FalloutGameModeProgram.Tokens(changes[0].Command)[1]);
            var soundHash = SHA256.HashData(soundRecord.ReadData());
            var original = FalloutSoundRecordReader.Read(records, soundRecord.FormKey);
            var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Sound path fixture invented a presentation effect.")));
            void Execute((int Index, string Command) change)
            {
                var begin = change.Index;
                while (begin > 0 && fields[begin].Signature != "QSDT") --begin;
                if (fields[begin].Signature != "QSDT") throw new InvalidDataException("Sound path command has no stage entry.");
                var end = begin + 1;
                while (end < fields.Length && fields[end].Signature is not ("QSDT" or "INDX" or "QOBJ")) ++end;
                executor.ExecuteStage(quest, fields[begin..end], change.Command);
            }

            // Use the actual source actor and placement for the spatial adapter.
            var dad = FalloutDialogueTopic.Find(records, "ACHR", "CG01DadREF");
            var cell = FalloutCellSceneReader.Read(records, world.Get(dad.FormKey).Cell); world.LoadCell(cell);
            var placed = cell.References.Single(reference => reference.FormKey == dad.FormKey);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            var body = RuntimeNativeNpc.Create(records, content, placed, units,
                (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, new Color(.4f, .4f, .4f)),
                world.EquippedArmor(dad.FormKey, 1), world.InitializeActorTemplates(dad.FormKey, 1));
            body.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale),
                GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * units);
            scene = new Node3D(); AddChild(scene); scene.AddChild(body); body.SetProcess(false); body.SetPhysicsProcess(false);
            scene.AddChild(new Camera3D { Position = body.Position, Current = true });
            var animation = new NativeOwnedAnimationSoundPlayer(records, content, body, units, new(53)); body.AddChild(animation);
            presenter = new(world.Sounds, content, world.Menus); AddChild(presenter);
            capture = new AudioEffectCapture { BufferLength = 2 }; AudioServer.AddBusEffect(0, capture);

            // Prime both descriptor caches and retain a prepared, queued old voice.
            world.Menus.Publish(false, [1013]);
            world.Sounds.Play(quest.FormKey, soundRecord.FormKey);
            var prior = world.Sounds.LastRequest!; var priorMedia = world.Sounds.LastMedia!;
            var queued = presenter.GetChildren().OfType<AudioStreamPlayer>().Single();
            if (!animation.DispatchSound(soundRecord.FormKey).StartsWith("source-sound-", StringComparison.Ordinal))
                throw new InvalidDataException("Owned original animation sound did not start: " + JsonSerializer.Serialize(animation.State));
            var priorAnimation = JsonSerializer.SerializeToElement(animation.LastEvent);
            Execute(changes[0]);
            var changed = FalloutSoundRecordReader.Read(records, soundRecord.FormKey);
            if (changed.LogicalPath == original.LogicalPath || !world.Sounds.IsActive(prior.Id) || queued.Playing ||
                prior.Source.LogicalPath != original.LogicalPath || world.Sounds.LastMedia != priorMedia ||
                FalloutSoundRecordReader.Read(soundRecord).LogicalPath != original.LogicalPath)
                throw new InvalidDataException("Sound path change lost prepared media or changed owned bytes.");
            Execute(changes[0]);
            if (records.SoundPaths.Revision(soundRecord.FormKey) != 1) throw new InvalidDataException("Repeated path change invalidated unchanged sound state.");
            world.Sounds.Play(quest.FormKey, soundRecord.FormKey);
            var current = world.Sounds.LastRequest!; var currentMedia = world.Sounds.LastMedia!;
            if (!animation.DispatchSound(soundRecord.FormKey).StartsWith("source-sound-", StringComparison.Ordinal))
                throw new InvalidDataException("Owned changed animation sound did not start: " + JsonSerializer.Serialize(animation.State));
            var changedAnimation = JsonSerializer.SerializeToElement(animation.LastEvent);
            var animationPath = changedAnimation.GetProperty("selected").GetProperty("Path").GetString()!;
            if (current.Source.LogicalPath != changed.LogicalPath || !currentMedia.Path.StartsWith(changed.LogicalPath + "\\", StringComparison.OrdinalIgnoreCase) ||
                !animationPath.StartsWith(changed.LogicalPath + "\\", StringComparison.OrdinalIgnoreCase) ||
                animation.Sources.Single().LogicalPath != changed.LogicalPath || prior.Source.LogicalPath != original.LogicalPath)
                throw new InvalidDataException("Script or animation descriptor cache retained an obsolete source path.");
            records.SoundVoices.Stop(soundRecord.FormKey);
            world.Menus.Publish(true); GetTree().Paused = false;
            long samples = 0; var peak = 0f;
            var deadline = Time.GetTicksMsec() + checked((ulong)(Math.Max(priorMedia.Duration / prior.Selection.PitchScale,
                currentMedia.Duration / current.Selection.PitchScale) * 1000 + 3000));
            while (world.Sounds.ActiveVoices != 0 && Time.GetTicksMsec() < deadline)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var count = capture.GetFramesAvailable();
                if (count == 0) continue;
                var frames = capture.GetBuffer(count); samples += frames.Length;
                foreach (var frame in frames) peak = Math.Max(peak, Math.Max(Math.Abs(frame.X), Math.Abs(frame.Y)));
            }
            if (world.Sounds.ActiveVoices != 0 || peak <= .000001f || capture.GetDiscardedFrames() != 0 || world.Sounds.LastError is not null)
                throw new InvalidDataException("Owned changed sound did not complete with lossless, nonzero native mixer output.");
            Execute(changes[^1]);
            world.Sounds.Play(quest.FormKey, soundRecord.FormKey);
            if (world.Sounds.LastRequest!.Source.LogicalPath != original.LogicalPath || records.SoundPaths.Revision(soundRecord.FormKey) != 2 ||
                !SHA256.HashData(quest.ReadData()).AsSpan().SequenceEqual(questHash) ||
                !SHA256.HashData(soundRecord.ReadData()).AsSpan().SequenceEqual(soundHash))
                throw new InvalidDataException("Source reset did not refresh playback or source bytes changed.");
            records.SoundVoices.Stop(soundRecord.FormKey);
            presenter.Free(); presenter = null; scene.Free(); scene = null;
            if (records.SoundVoices.ActiveVoices != 0) throw new InvalidDataException("Sound path fixture leaked native voice registrations.");
            var discarded = capture.GetDiscardedFrames();
            if (discarded != 0) throw new InvalidDataException("Sound path fixture discarded native mixer samples.");
            // End the mixer observation before synchronous source-graph reload.
            for (var index = AudioServer.GetBusEffectCount(0) - 1; index >= 0; --index)
                if (AudioServer.GetBusEffect(0, index) == capture) AudioServer.RemoveBusEffect(0, index);
            using var fresh = FalloutPluginStack.Load(content.PluginSources);
            if (fresh.SoundPaths.Read(soundRecord.FormKey).Revision != 0 ||
                FalloutSoundRecordReader.Read(fresh, soundRecord.FormKey).LogicalPath != original.LogicalPath)
                throw new InvalidDataException("New source graph inherited prior path overrides.");
            GD.Print("OPENNV_NATIVE_SOUND_PATHS_PASS " + JsonSerializer.Serialize(new
            {
                quest = quest.FormKey,
                winner = quest.Plugin.Name,
                sound = soundRecord.FormKey,
                commands = changes.Select(change => change.Command),
                original = original.LogicalPath,
                changed = changed.LogicalPath,
                priorMedia,
                currentMedia,
                priorAnimation,
                changedAnimation,
                samples,
                peak,
                discarded,
                scriptCacheRefresh = true,
                animationCacheRefresh = true,
                preparedMediaRetained = true,
                sourceReset = true,
                graphScope = true,
                sourceReadonly = true,
                retirement = true,
                recording = false,
                boundary = "isolated-owned-source-and-native-mixer;ordinary-childhood-and-retail-parity-unverified"
            }));
        }
        finally
        {
            GetTree().Paused = paused; presenter?.Free(); scene?.Free();
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
