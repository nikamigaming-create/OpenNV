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
    private async Task StopSound(string baseRoot, string mod, string root, string questId, string[] dependencies)
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
            var source = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(source.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
            var hash = SHA256.HashData(quest.ReadData());
            var fields = quest.ReadSubrecords().ToArray();
            var sourceIndex = Array.FindIndex(fields, field => field.Signature == "SCTX" &&
                FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span)).Any(IsStop));
            if (sourceIndex < 0) throw new InvalidDataException("Owned stop fixture requires a source StopSound stage entry.");
            var begin = sourceIndex;
            while (begin > 0 && fields[begin].Signature != "QSDT") --begin;
            if (fields[begin].Signature != "QSDT") throw new InvalidDataException("Owned stop command has no stage-entry scope.");
            var end = begin + 1;
            while (end < fields.Length && fields[end].Signature is not ("QSDT" or "INDX" or "QOBJ")) ++end;
            var entry = fields[begin..end];
            var command = FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(fields[sourceIndex].Data.Span)).First(IsStop);
            var sound = FalloutSoundRecordReader.Read(FalloutSoundRecordReader.Find(records, command.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries)[1]));
            if (!sound.IsLooping || !sound.IsTwoDimensional)
                throw new InvalidDataException("Selected owned stop fixture requires the authored 2D loop.");
            var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Stop fixture invented a presentation effect.")));
            var configuration = RuntimeConfiguration.Load(); var units = configuration.World.GameUnitsToMeters;
            var dad = FalloutDialogueTopic.Find(records, "ACHR", "CG00DadREF");
            var doctor = FalloutDialogueTopic.Find(records, "ACHR", "CG00DoctorLiREF");
            var cell = FalloutCellSceneReader.Read(records, world.Get(dad.FormKey).Cell); world.LoadCell(cell);
            scene = new Node3D(); AddChild(scene);
            var actors = new Dictionary<FalloutFormKey, Node3D>();
            foreach (var reference in new[] { dad, doctor })
            {
                var placed = cell.References.Single(item => item.FormKey == reference.FormKey);
                var body = RuntimeNativeNpc.Create(records, source, placed, units,
                    (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, new Color(.4f, .4f, .4f)),
                    world.EquippedArmor(reference.FormKey, 1), world.InitializeActorTemplates(reference.FormKey, 1));
                body.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale),
                    GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * units);
                scene.AddChild(body); body.SetProcess(false); body.SetPhysicsProcess(false); actors.Add(reference.FormKey, body);
            }
            var listener = new Camera3D { Position = actors[dad.FormKey].Position, Current = true }; scene.AddChild(listener);
            var animation = new NativeOwnedAnimationSoundPlayer(records, source, actors[dad.FormKey], units, new(42));
            actors[dad.FormKey].AddChild(animation);
            presenter = new(world.Sounds, source, world.Menus); AddChild(presenter);
            capture = new AudioEffectCapture { BufferLength = 2 }; AudioServer.AddBusEffect(0, capture);
            long samples = 0; var peak = 0f;
            void Drain()
            {
                var count = capture.GetFramesAvailable();
                if (count == 0) return;
                var frames = capture.GetBuffer(count);
                if (frames.Length != count) throw new InvalidDataException("Stop sound audit lost a mixer packet.");
                samples += frames.Length;
                foreach (var frame in frames) peak = Math.Max(peak, Math.Max(Math.Abs(frame.X), Math.Abs(frame.Y)));
            }
            async Task Wait(ulong milliseconds)
            {
                var deadline = Time.GetTicksMsec() + milliseconds;
                while (Time.GetTicksMsec() < deadline) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); Drain(); }
            }
            var animationCompletions = 0;
            if (!animation.DispatchSound(sound.FormKey, completed: () => ++animationCompletions).StartsWith("source-sound-", StringComparison.Ordinal))
                throw new InvalidDataException("Owned animation loop did not start.");
            var menu = NativeOwnedSoundPlayback.CreateMenu(sound, records, source, new(43)); scene.AddChild(menu); menu.Play();
            world.Sounds.Play(quest.FormKey, sound.FormKey); var request = world.Sounds.LastRequest!;
            var script = presenter.GetChildren().OfType<AudioStreamPlayer>().Single();
            var native = animation.GetChildren().OfType<AudioStreamPlayer>().Single();
            if (records.SoundVoices.ActiveVoices != 3 || !menu.Playing || !native.Playing || !script.Playing ||
                script.Stream.GetInstanceId() == native.Stream.GetInstanceId() ||
                ((AudioStreamWav)script.Stream).LoopMode != AudioStreamWav.LoopModeEnum.Forward ||
                ((AudioStreamWav)native.Stream).LoopMode != AudioStreamWav.LoopModeEnum.Forward)
                throw new InvalidDataException("Source loop instances were missing or shared mutable streams.");
            await Wait(250);
            if (peak <= .000001f || capture.GetDiscardedFrames() != 0)
                throw new InvalidDataException("Owned loops did not produce nonzero, lossless mixer samples.");
            world.Menus.Publish(false, [1084]); GetTree().Paused = true; world.Sounds.Update(false);
            executor.ExecuteStage(quest, entry, command);
            if (menu.Playing || native.Playing || script.Playing || world.Sounds.IsActive(request.Id) ||
                records.SoundVoices.ActiveVoices != 0 || animationCompletions != 1)
                throw new InvalidDataException("Owned StopSound missed a native owner or lost its response completion.");
            GetTree().Paused = false; world.Menus.Publish(true);
            await Wait(250); peak = 0;
            await Wait(250);
            if (peak > .000001f) throw new InvalidDataException("Stopped source loops remained audible in the native mixer.");

            var spatialSound = FalloutSoundRecordReader.Read(FalloutSoundRecordReader.Find(records, "WPNRifleAssaultReloadIn"));
            if (spatialSound.IsTwoDimensional) throw new InvalidDataException("Owned reference-filter fixture requires a spatial SOUN.");
            var otherAnimation = new NativeOwnedAnimationSoundPlayer(records, source, actors[doctor.FormKey], units, new(44));
            actors[doctor.FormKey].AddChild(otherAnimation);
            foreach (var owner in new[] { animation, otherAnimation })
                if (!owner.DispatchSound(spatialSound.FormKey).StartsWith("source-sound-", StringComparison.Ordinal))
                    throw new InvalidDataException("Owned spatial reference voice did not start: " + JsonSerializer.Serialize(owner.State));
            var first = animation.GetChildren().OfType<AudioStreamPlayer3D>().Single();
            var second = otherAnimation.GetChildren().OfType<AudioStreamPlayer3D>().Single();
            records.SoundVoices.Stop(spatialSound.FormKey, dad.FormKey);
            if (first.Playing || !second.Playing || records.SoundVoices.ActiveVoices != 1)
                throw new InvalidDataException("Native reference filtering did not follow the actual source actor tree.");
            records.SoundVoices.Stop(spatialSound.FormKey);
            if (second.Playing || records.SoundVoices.ActiveVoices != 0)
                throw new InvalidDataException("Global stop missed another reference's spatial instance.");
            executor.ExecuteStage(quest, entry, command);
            if (animationCompletions != 1 || !SHA256.HashData(quest.ReadData()).AsSpan().SequenceEqual(hash))
                throw new InvalidDataException("Repeated stop replayed a completion or changed owned source bytes.");
            presenter.Free(); presenter = null; scene.Free(); scene = null;
            if (records.SoundVoices.ActiveVoices != 0) throw new InvalidDataException("Native sound retirement leaked registry entries.");
            GD.Print("OPENNV_NATIVE_STOP_SOUND_PASS " + JsonSerializer.Serialize(new
            {
                quest = quest.FormKey,
                winner = quest.Plugin.Name,
                command,
                sound = sound.FormKey,
                sourceLoopInstances = 3,
                script = true,
                animation = true,
                menu = true,
                independentLoopStreams = true,
                hardStop = true,
                pausedStop = true,
                silentAfterStop = true,
                completionOnce = true,
                actualReferenceFilter = true,
                spatialSound = spatialSound.FormKey,
                spatialPresentation = otherAnimation.State,
                samples,
                discarded = capture.GetDiscardedFrames(),
                sourceReadonly = true,
                retirement = true,
                recording = false,
                boundary = "isolated-owned-source-and-native-mixer;campaign-and-retail-timing-unverified"
            }));
        }
        finally
        {
            GetTree().Paused = paused;
            presenter?.Free(); scene?.Free();
            if (capture is not null)
            {
                for (var index = AudioServer.GetBusEffectCount(0) - 1; index >= 0; --index)
                    if (AudioServer.GetBusEffect(0, index) == capture) AudioServer.RemoveBusEffect(0, index);
                capture.Dispose();
            }
            RuntimeLiveContentSource.Clear();
        }
        static bool IsStop(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0]
            .Equals("StopSound", StringComparison.OrdinalIgnoreCase);
    }
}
