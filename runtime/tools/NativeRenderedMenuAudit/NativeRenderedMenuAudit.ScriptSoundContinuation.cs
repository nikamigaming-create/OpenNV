using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private async Task ScriptSoundContinuation(string baseRoot, string mod, string root, string questId,
        string checkpoint, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        NativeOwnedScriptSoundPlayer? presenter = null;
        RuntimeNativeNifPrototype? prototype = null;
        Node3D? scene = null;
        AudioEffectCapture? capture = null;
        var savedHash = SHA256.HashData(File.ReadAllBytes(checkpoint));
        try
        {
            var source = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(source.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var state = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(checkpoint)) ??
                throw new InvalidDataException("Sound continuation checkpoint is absent.");
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
            var script = FalloutScriptLocals.AttachedScript(records, quest) ?? throw new InvalidDataException("Quest has no SCPT.");
            var before = state.Scripts!.Instances.Single(item => item.Quest == quest.FormKey);
            var error = before.Error ?? throw new InvalidDataException("Selected checkpoint has no retained command failure.");
            var program = FalloutGameModeProgram.Read(script.ReadSubrecords().Single(field => field.Signature == "SCTX").Data.Span);
            var bindings = new FalloutScriptBindings(records, quest, script, script.ReadSubrecords());
            var tail = program.MissingCommandContinuation(error, token => bindings.TryForm(token) is not null) ??
                throw new InvalidDataException("Original missing command has no fixed unique source site.");
            var sourceLine = FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(script.ReadSubrecords()
                .Single(field => field.Signature == "SCTX").Data.Span)).Single(line =>
                    line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].EndsWith(".PlaySound3D", StringComparison.OrdinalIgnoreCase));
            var tokens = sourceLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var emitterKey = bindings.Reference(tokens[0][..tokens[0].LastIndexOf('.')]);
            var descriptor = FalloutSoundRecordReader.Read(records, bindings.Form(tokens[1]).FormKey);
            var cell = FalloutCellSceneReader.Read(records, world.Get(emitterKey).Cell); world.LoadCell(cell);
            var placed = cell.References.Single(item => item.FormKey == emitterKey);
            var model = cell.BaseObjects[placed.Base].ModelPath ?? throw new InvalidDataException("Sound reference has no source model.");
            if (!source.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            prototype = new(bytes, units);
            scene = new Node3D(); AddChild(scene);
            var emitter = prototype.InstantiatePlaced(new(
                GamebryoCoordinate.ConvertReferenceEuler(new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale),
                GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * units));
            scene.AddChild(emitter);
            var listener = new Camera3D { Position = emitter.GlobalPosition, Current = true }; scene.AddChild(listener);
            var quests = new FalloutQuestState(records); quests.Restore(state.Quests!);
            var claimed = records.EffectiveRecords("QUST").Where(record => record.FormKey != quest.FormKey)
                .Select(record => record.FormKey).ToHashSet();
            var scripts = new FalloutQuestScripts(records, quests, claimed, new FalloutPlayerInventory(), references: world);
            scripts.Restore(state.Scripts);
            var interpreter = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new InvalidDataException("Sound continuation audit reached an unrelated native effect.")));
            scripts.Host = new((_, _) => throw new InvalidDataException("Sound continuation audit reached an unrelated stage change."),
                _ => throw new InvalidDataException("Sound continuation audit reached an unrelated actor query."),
                ExecuteProgram: interpreter.ExecuteProgram);
            presenter = new(world.Sounds, source, world.Menus, key => key == emitterKey ? emitter :
                throw new InvalidDataException("Original continuation requested a different emitter."), units);
            AddChild(presenter);
            capture = new AudioEffectCapture { BufferLength = 2 }; AudioServer.AddBusEffect(0, capture);
            var priorStage = quests.Stage(quest.FormKey);
            scripts.Advance(0);
            var resumed = scripts.Capture().Instances.Single(item => item.Quest == quest.FormKey);
            var request = world.Sounds.LastRequest ?? throw new InvalidDataException("Original sound continuation published no request.");
            var media = world.Sounds.LastMedia ?? throw new InvalidDataException("Original sound continuation loaded no media.");
            if (resumed.Error is not null || resumed.Executions != before.Executions + 1 ||
                resumed.Clock!.Invocations != before.Clock!.Invocations + 1 || resumed.Continuations!.Count != (before.Continuations?.Count ?? 0) + 1 ||
                quests.Stage(quest.FormKey) != priorStage || request.Reference != emitterKey || request.Source.FormKey != descriptor.FormKey || media.Stereo)
                throw new InvalidDataException("Original stopped invocation changed its prefix, clock, source identity or stage: " +
                    JsonSerializer.Serialize(new { before, resumed, priorStage, stage = quests.Stage(quest.FormKey), request, media }));
            var voice = emitter.GetChildren().OfType<AudioStreamPlayer3D>().Single();
            listener.GlobalPosition = emitter.GlobalPosition + Vector3.Right * descriptor.MinimumDistanceGameUnits * units;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var nearDb = voice.VolumeDb;
            var expectedNearDb = request.Selection.GainDb + descriptor.AttenuationDbAtDistanceGameUnits(descriptor.MinimumDistanceGameUnits);
            if (Math.Abs(nearDb - expectedNearDb) > .001) throw new InvalidDataException("Positioned script sound lost source attenuation.");
            var start = voice.GlobalPosition;
            emitter.Position += Vector3.Right * units;
            if (Math.Abs(voice.GlobalPosition.DistanceTo(start) - units) > .0001) throw new InvalidDataException("Script sound did not follow its actual reference root.");
            long samples = 0; var peak = 0f;
            var deadline = Time.GetTicksMsec() + checked((ulong)((media.Duration / request.Selection.PitchScale + 3) * 1000));
            while (world.Sounds.ActiveVoices != 0 && Time.GetTicksMsec() < deadline)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var count = capture.GetFramesAvailable();
                foreach (var frame in capture.GetBuffer(count))
                { ++samples; peak = Math.Max(peak, Math.Max(Math.Abs(frame.X), Math.Abs(frame.Y))); }
            }
            if (world.Sounds.ActiveVoices != 0 || peak <= .000001f || capture.GetDiscardedFrames() != 0)
                throw new InvalidDataException("Positioned source sound failed completion or lost native mixer output.");
            GD.Print("OPENNV_NATIVE_SCRIPT_SOUND_CONTINUATION_PASS " + JsonSerializer.Serialize(new
            {
                quest = quest.FormKey,
                script = script.FormKey,
                sourceHash = FalloutScriptSourceIdentity.Hash(script),
                request,
                media,
                samples,
                peak,
                nearDb,
                expectedNearDb,
                originalTail = tail.StartStatement,
                historicalFault = resumed.Continuations!.Last(),
                consumedPrefixRetained = true,
                clockOnce = true,
                sourcePlacement = true,
                rootFollowing = true,
                sourceAttenuation = true,
                completion = true,
                recording = false,
                boundary = "isolated-owned-stopped-invocation-and-native-mixer;campaign-and-retail-parity-unverified"
            }));
        }
        finally
        {
            presenter?.Free(); scene?.Free(); prototype?.Scene.Root.Free();
            if (capture is not null)
            {
                for (var index = AudioServer.GetBusEffectCount(0) - 1; index >= 0; --index)
                    if (AudioServer.GetBusEffect(0, index) == capture) AudioServer.RemoveBusEffect(0, index);
                capture.Dispose();
            }
            RuntimeLiveContentSource.Clear();
            if (!savedHash.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(checkpoint))))
                throw new InvalidDataException("Sound continuation audit changed its private checkpoint.");
        }
    }
}
