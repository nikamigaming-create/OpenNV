using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseObjectAnimation()
    {
        const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var root = new Node3D();
        AddChild(root);
        var firstSample = 0f;
        var secondSample = 0f;
        RuntimeNifControllerPlayer Make(Action<float> sample)
        {
            var player = new RuntimeNifControllerPlayer { SourceController = 7, SourceSha256 = hash };
            player.Configure([
                new("Forward", 0, 2, 3, 5, [new(sample)])
                { TextKeys = [new(3, "forward start"), new(4, "loop start"), new(5, "forward end")] },
                new("Backward", 0, 1, 1, 2, [new(sample)])
                { TextKeys = [new(1, "backward start"), new(2, "backward end")] },
                new("Right", 2, 1, 0, 1, [new(sample)])
                { TextKeys = [new(0, "right start"), new(1, "right end")] },
            ]);
            root.AddChild(player);
            player.ProcessMode = ProcessModeEnum.Disabled;
            return player;
        }
        static void Require(bool value, string message)
        { if (!value) throw new InvalidDataException(message); }
        static void Reject(Action action)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException) { return; }
            throw new InvalidDataException("Invalid object animation state was admitted.");
        }
        try
        {
            RuntimeNifControllerPlayer Automatic()
            {
                var player = new RuntimeNifControllerPlayer { SourceController = 8, SourceSha256 = hash };
                player.Configure([new("Idle", 0, 1, 0, 3, [])
                    { TextKeys = [new(0, "Sound: OriginalLoop"), new(2, "Enum: StopSounds")] }]);
                root.AddChild(player); player.ProcessMode = ProcessModeEnum.Disabled;
                return player;
            }
            var automatic = Automatic(); var automaticCold = Automatic();
            var automaticKeys = new List<string>(); var automaticColdKeys = new List<string>();
            automatic.TextKeyHandler = key => { automaticKeys.Add(key.Text); return "audit-bound"; };
            automaticCold.TextKeyHandler = key => { automaticColdKeys.Add(key.Text); return "audit-bound"; };
            automatic._Process(.75);
            var automaticState = JsonSerializer.Deserialize<FalloutObjectAnimationSnapshot>(JsonSerializer.Serialize(automatic.CaptureObjectState()))!;
            automaticCold.RestoreObjectState(automaticState);
            Require(!automaticState.ScriptSelected && automaticCold.CaptureScriptState() is null,
                "Restored automatic animation invented a source command.");
            automatic._Process(3); automaticCold._Process(3);
            Require(automaticKeys.SequenceEqual(["Sound: OriginalLoop", "Enum: StopSounds", "Sound: OriginalLoop"]) &&
                automaticColdKeys.SequenceEqual(["Enum: StopSounds", "Sound: OriginalLoop"]) &&
                automatic.CaptureObjectState() == automaticCold.CaptureObjectState(),
                "Cold automatic animation replayed its consumed sound key or changed its next cycle.");
            var first = Make(time => firstSample = time);
            var second = Make(time => secondSample = time);
            Require(first.ActiveSequence is null && second.ActiveSequence is null, "Multiple loops invented an automatic group.");
            var keys = new List<string>();
            first.TextKeyHandler = key => { keys.Add(key.Text); return "audit-bound"; };
            first.RequestSourceSequence("forward", 1);
            first._Process(.25);
            Require(firstSample == 3.5 && secondSample == 0 && keys.SequenceEqual(["forward start"]),
                "Source start/frequency or instance isolation differs.");
            first.RequestSourceSequence("Backward", 0);
            var state = JsonSerializer.Deserialize<FalloutObjectAnimationSnapshot>(JsonSerializer.Serialize(first.CaptureScriptState()))!;
            second.RestoreScriptState(state);
            var coldKeys = new List<string>();
            second.TextKeyHandler = key => { coldKeys.Add(key.Text); return "audit-bound"; };
            first._Process(.875); second._Process(.875);
            Require(first.ActiveSequence == "Backward" && second.ActiveSequence == "Backward" && firstSample == 1.125 && secondSample == firstSample,
                "Queued transition lost its full cycle, remainder or cold state.");
            Require(keys.SequenceEqual(["forward start", "loop start", "forward end", "backward start"]) &&
                coldKeys.SequenceEqual(["loop start", "forward end", "backward start"]), "Boundary ordering or consumed-event restoration differs.");
            first.RequestSourceSequence("Forward", 2);
            first._Process(0);
            Require(firstSample == 4 && keys[^1] == "loop start", "Immediate loop ignored its authored start or replayed an intro.");
            var before = keys.Count; first._Process(0);
            Require(keys.Count == before, "A zero frame replayed a source event.");
            first.RequestSourceSequence("Right", 1); first._Process(2);
            Require(!first.Playing && firstSample == 1, "Clamp animation did not finish.");
            var finished = first.CaptureScriptState()!;
            second.RestoreScriptState(finished); second._Process(0);
            Require(!second.Playing && !second.IsProcessing() && coldKeys.Count == 3, "Cold finished animation restarted.");
            first.RequestSourceSequence("Forward", 1); first._Process(.25); first.RequestSourceSequence("Right", 0);
            first.TextKeyHandler = key =>
            {
                if (key.Text == "forward end") first.RequestSourceSequence("Backward", 1);
                return "audit-bound";
            };
            first._Process(1);
            Require(first.ActiveSequence == "Backward" && firstSample == 1, "Stale queued group replaced an immediate boundary callback.");
            first.RequestSourceSequence("Forward", 1); first._Process(.25); first.RequestSourceSequence("Right", 0);
            first.TextKeyHandler = key =>
            {
                if (key.Text == "forward end") first.RequestSourceSequence("Backward", 0);
                return "audit-bound";
            };
            first._Process(.75);
            Require(first.ActiveSequence == "Backward", "Boundary callback could not replace a queued group.");
            first.RequestSourceSequence("Forward", 1);
            first.TextKeyHandler = key =>
            {
                if (key.Text == "loop start") first.RequestSourceSequence("Right", 0);
                return "audit-bound";
            };
            first._Process(1.25);
            Require(first.ActiveSequence == "Right" && firstSample == .25,
                "A queued text-key request skipped its cycle boundary inside a long frame.");
            var unchanged = JsonSerializer.Serialize(first.CaptureScriptState());
            Reject(() => first.RequestSourceSequence("Absent", 1));
            Reject(() => first.RequestSourceSequence("Forward", 3));
            Reject(() => first.RequestSourceSequence("Right", 2));
            Reject(() => first.RestoreScriptState(finished with { Sha256 = new string('b', 64) }));
            Reject(() => first.RestoreScriptState(finished with { PendingSequence = "Absent" }));
            Require(JsonSerializer.Serialize(first.CaptureScriptState()) == unchanged, "Rejected request mutated the animation.");
            GD.Print("OPENNV_OBJECT_ANIMATION_CONTRACT_PASS modes=true alternatives=true authoredStart=true sourceFrequency=true boundaryOrder=true callbackGeneration=true instanceIsolation=true coldPending=true coldCompleted=true sourceDriftRefused=true");
        }
        finally { root.Free(); }
    }
}
