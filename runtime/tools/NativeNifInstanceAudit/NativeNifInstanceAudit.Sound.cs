using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseSoundEvents(string root, string model)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
        var source = FalloutNifFile.Read(bytes);
        var prototype = new RuntimeNativeNifPrototype(bytes, .0142875f);
        var first = prototype.InstantiatePlaced(Transform3D.Identity);
        var second = prototype.InstantiatePlaced(new(Basis.Identity, new Vector3(10, 20, 30)));
        try
        {
            AddChild(first); AddChild(second);
            first.ProcessMode = ProcessModeEnum.Disabled; second.ProcessMode = ProcessModeEnum.Disabled;
            var firstSounds = new NativeOwnedAnimationSoundPlayer(records, content, first, .0142875f, new(11));
            var secondSounds = new NativeOwnedAnimationSoundPlayer(records, content, second, .0142875f, new(11));
            first.AddChild(firstSounds); second.AddChild(secondSounds);
            RuntimeNifControllerPlayer Bind(Node3D owner, NativeOwnedAnimationSoundPlayer sounds)
            {
                var controller = owner.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single(value => value.HasTextKeys);
                controller.TextKeyHandler = sounds.Dispatch;
                return controller;
            }
            var firstClock = Bind(first, firstSounds); var secondClock = Bind(second, secondSounds);
            var sequence = source.Blocks.Where(block => block.TypeName == "NiControllerSequence")
                .Select(block => source.ReadControllerSequence(block.Index)).Single(value => value.CycleType == 0);
            var keys = ((FalloutNifTextKeyExtraData)source.ReadObject(sequence.TextKeys)).Keys;
            var start = keys.Single(key => key.Value.StartsWith("Sound:", StringComparison.OrdinalIgnoreCase));
            var stop = keys.Single(key => key.Value.StartsWith("Enum: StopSounds ", StringComparison.OrdinalIgnoreCase));
            firstClock._Process((start.Time - sequence.StartTime) / sequence.Frequency + .001);
            secondClock._Process((start.Time - sequence.StartTime) / sequence.Frequency + .001);
            AudioStreamPlayer3D Voice(Node3D owner) => owner.FindChildren("*", "", true, false).OfType<AudioStreamPlayer3D>().Single();
            var firstVoice = Voice(first); var secondVoice = Voice(second);
            var firstStream = (AudioStreamWav)firstVoice.Stream; var secondStream = (AudioStreamWav)secondVoice.Stream;
            if (firstStream.GetInstanceId() == secondStream.GetInstanceId() || firstStream.LoopMode != AudioStreamWav.LoopModeEnum.Forward ||
                secondStream.LoopMode != AudioStreamWav.LoopModeEnum.Forward || firstVoice.GetParent() == firstSounds)
                throw new InvalidDataException("Named source emitter or independent sustain loop was not bound.");
            var before = firstVoice.GlobalPosition;
            var emitter = (Node3D)firstVoice.GetParent(); emitter.Position += Vector3.One;
            if (firstVoice.GlobalPosition == before || secondVoice.GlobalPosition == firstVoice.GlobalPosition)
                throw new InvalidDataException("Voice did not follow its own animated source node.");
            firstClock._Process((stop.Time - start.Time) / sequence.Frequency);
            if (firstStream.LoopMode != AudioStreamWav.LoopModeEnum.Disabled ||
                !firstVoice.GetMeta("opennv_sound_envelope_releasing", false).AsBool() ||
                secondStream.LoopMode != AudioStreamWav.LoopModeEnum.Forward)
                throw new InvalidDataException("Source StopSounds failed to release only the addressed voice.");
            if (firstSounds.Dispatch(new FalloutNifTextKeyEvent(0, 0, 0, "Sound: MissingSound missing-node")) != "unbound-source-sound" ||
                first.FindChildren("*", "", true, false).OfType<AudioStreamPlayer3D>().Count() != 1)
                throw new InvalidDataException("An absent source emitter created an invented audio voice.");
            GD.Print($"OPENNV_OWNED_NIF_SOUND_PASS model={model} namedEmitter=true followsSourceNode=true loopIsolation=true authoredRelease=true missingEmitterRejected=true outputSamples=unverified");
        }
        finally { first.Free(); second.Free(); prototype.Scene.Root.Free(); }
    }
}
