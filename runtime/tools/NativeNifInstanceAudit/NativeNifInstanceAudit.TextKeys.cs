using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseTextKeys(string root, string model)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
        var source = FalloutNifFile.Read(bytes);
        var prototype = new RuntimeNativeNifPrototype(bytes, .0142875f);
        var first = prototype.InstantiatePlaced(Transform3D.Identity);
        var second = prototype.InstantiatePlaced(Transform3D.Identity);
        try
        {
            AddChild(first); AddChild(second);
            first.ProcessMode = ProcessModeEnum.Disabled; second.ProcessMode = ProcessModeEnum.Disabled;
            var controllers = first.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>()
                .Where(controller => controller.HasTextKeys).ToArray();
            if (controllers.Length == 0) throw new InvalidDataException("Source has no controller text-key owner.");
            var events = 0;
            foreach (var controller in controllers)
                foreach (var name in controller.SequenceNames)
                {
                    var sequence = source.Blocks.Where(block => block.TypeName == "NiControllerSequence")
                        .Select(block => source.ReadControllerSequence(block.Index)).Single(sequence => sequence.Name == name);
                    var keys = ((FalloutNifTextKeyExtraData)source.ReadObject(sequence.TextKeys)).Keys
                        .Where(key => key.Time >= sequence.StartTime && key.Time <= sequence.StopTime).OrderBy(key => key.Time).ToArray();
                    var actual = new List<FalloutNifTextKeyEvent>();
                    controller.TextKeyHandler = key => { actual.Add(key); return "audit-observed-source-event"; };
                    controller.PlaySourceSequence(name);
                    var duration = (sequence.StopTime - sequence.StartTime) / sequence.Frequency;
                    controller._Process(duration);
                    var expected = keys.Select(key => key.Value).ToList();
                    if (sequence.CycleType == 0) expected.AddRange(keys.Where(key => key.Time == sequence.StartTime).Select(key => key.Value));
                    if (!actual.Select(key => key.Text).SequenceEqual(expected))
                        throw new InvalidDataException("Runtime source events disagree with the owned sequence's first cycle.");
                    var before = actual.Count;
                    controller._Process(0);
                    controller.SeekSourceTime(sequence.StartTime + (sequence.StopTime - sequence.StartTime) / 2);
                    controller._Process(0);
                    if (actual.Count != before) throw new InvalidDataException("A zero advance or seek replayed source sounds.");
                    events += actual.Count;
                }
            if (second.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Any(value => value.TextKeyCount != 0) ||
                prototype.Scene.Root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Any(value => value.TextKeyCount != 0))
                throw new InvalidDataException("Advancing one controller published another instance's source events.");
            GD.Print($"OPENNV_OWNED_TEXT_KEYS_PASS model={model} controllers={controllers.Length} events={events} sourceOrder=true independent=true seekNoReplay=true audioOutput=unverified");
        }
        finally { first.Free(); second.Free(); prototype.Scene.Root.Free(); }
    }
}
