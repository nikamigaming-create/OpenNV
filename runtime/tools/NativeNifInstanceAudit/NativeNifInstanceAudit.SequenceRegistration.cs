using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseRepeatedManagedSequences()
    {
        var nif = FalloutNifFile.Read(EmptyManagedSequenceFixture("duplicate"));
        var prototype = new RuntimeNativeNifPrototype(nif, .02f);
        Node3D? first = null; Node3D? second = null;
        try
        {
            first = prototype.Instantiate(); second = prototype.Instantiate();
            first.ProcessMode = second.ProcessMode = ProcessModeEnum.Disabled;
            AddChild(first); AddChild(second);
            RuntimeNifControllerPlayer Player(Node3D node) => node.FindChildren("*", "", true, false)
                .OfType<RuntimeNifControllerPlayer>().Single();
            var warm = Player(first); var cold = Player(second); var original = Player(prototype.Scene.Root);
            var source = JsonSerializer.SerializeToElement(warm.Observation).GetProperty("sequences");
            RequireEmptySequence(source.GetArrayLength() == 2 &&
                source.EnumerateArray().Select(sequence => sequence.GetProperty("SourceSequence").GetInt32()).SequenceEqual([8, 9]) &&
                source.EnumerateArray().Select(sequence => sequence.GetProperty("selectedByName").GetBoolean()).SequenceEqual([false, true]) &&
                warm.SequenceNames.SequenceEqual(["Loop"]) && warm.ActiveSequence is null &&
                warm.SequenceRange("Loop") == (10f, 11f),
                "Source registration discarded a repeated sequence or selected its earlier name binding.");
            var events = new List<FalloutNifTextKeyEvent>(); var resumedEvents = new List<FalloutNifTextKeyEvent>();
            warm.TextKeyHandler = key => { events.Add(key); return "audit-source-key"; };
            cold.TextKeyHandler = key => { resumedEvents.Add(key); return "audit-source-key"; };
            warm.RequestSourceSequence("Loop", 1); warm._Process(.4);
            RequireEmptySequence(warm.ActiveSourceSequence == 9 && warm.SourceTimeSeconds == 10.4 &&
                events.Select(key => key.Text).SequenceEqual(["start"]) && cold.TextKeyCount == 0 && original.TextKeyCount == 0,
                "Name selection used a superseded block, clock or another instance.");
            var state = JsonSerializer.Deserialize<FalloutObjectAnimationSnapshot>(JsonSerializer.Serialize(warm.CaptureScriptState()))!;
            cold.RestoreScriptState(state); warm._Process(2); cold._Process(2);
            RequireEmptySequence(warm.ActiveSourceSequence == 9 && cold.ActiveSourceSequence == 9 &&
                warm.SourceTimeSeconds == 11 && cold.SourceTimeSeconds == 11 && !warm.Playing && !cold.Playing &&
                resumedEvents.SequenceEqual(events.Skip(1)) && resumedEvents.Select(key => key.Text).SequenceEqual(["end"]) &&
                original.ActiveSequence is null && prototype.Scene.Surfaces == 1,
                $"Repeated source-name cold restoration diverged: warmBlock={warm.ActiveSourceSequence} coldBlock={cold.ActiveSourceSequence} " +
                $"warmTime={warm.SourceTimeSeconds} coldTime={cold.SourceTimeSeconds} warmPlaying={warm.Playing} coldPlaying={cold.Playing} " +
                $"warmKeys={JsonSerializer.Serialize(events)} coldKeys={JsonSerializer.Serialize(resumedEvents)} original={original.ActiveSequence} surfaces={prototype.Scene.Surfaces}.");
            var invalid = new RuntimeNifControllerPlayer();
            try
            {
                RejectEmptySequence(() => invalid.Configure([
                    new("Loop", 0, 1, 0, 1, []), new("loop", 0, 1, 0, 2, [])]));
            }
            finally { invalid.Free(); }
            RequireEmptySequence(!GodotObject.IsInstanceValid(invalid),
                "The refused detached controller retained a native node after retirement.");
            GD.Print("OPENNV_MANAGED_SEQUENCE_REGISTRATION_PASS fullNif=true retainedBlocks=true lastExactName=true clockFromSelectedBlock=true coldConsumedKeys=true instanceIsolation=true caseCollisionRefused=true refusedControllerFreed=true pixels=unverified");
        }
        finally { first?.Free(); second?.Free(); prototype.Scene.Root.Free(); }
    }
}
