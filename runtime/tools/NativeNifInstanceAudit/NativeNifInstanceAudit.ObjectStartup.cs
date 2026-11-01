using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseObjectDefaultStartup()
    {
        RuntimeNifControllerPlayer Player() => new() { SourceController = 1, SourceSha256 = new string('0', 64) };
        foreach (var name in new[] { "Idle", "SpecialIdle" })
        {
            var sample = -1f;
            var starts = 0;
            var player = Player();
            var owner = new Node3D(); owner.AddChild(player);
            try
            {
                player.TextKeyHandler = key => { if (key.SourceSeconds == 0) starts++; return "handled"; };
                player.Configure([new(name, 0, 1, 0, 3, [new(time => sample = time)])
                { TextKeys = [new(0, "start"), new(3, "end")] }], autoStartManagedLoops: false);
                player.ConfigureObjectDefaultStartup();
                RequireEmptySequence(starts == 0, "Default selection dispatched text keys before the first animation advance.");
                player._Process(.5);
                RequireEmptySequence(player.ActiveSequence == name && player.Playing && sample == .5f && starts == 1,
                    "Canonical object default did not advance its actual source channel.");
                player.RestoreObjectState(player.CaptureObjectState()! with { ElapsedSeconds = 1.25, StartPending = false });
                RequireEmptySequence(sample == 1.25f, "Valid default continuation lost its saved source phase.");
                player._Process(.25);
                RequireEmptySequence(sample == 1.5f && starts == 1, "Default continuation replayed consumed start text keys.");
            }
            finally { owner.Free(); }
        }
        var lowercase = Player(); var ambiguous = Player(); var requested = Player(); var caller = Player();
        var direct = new RuntimeNifControllerPlayer();
        try
        {
            lowercase.Configure([new("idle", 0, 1, 0, 3, [])], autoStartManagedLoops: false);
            lowercase.ConfigureObjectDefaultStartup();
            lowercase.RestoreObjectState(new(1, new string('0', 64), "idle", 1, false, null, false));
            RequireEmptySequence(lowercase.ActiveSequence is null, "Lowercase idle became an object default.");
            lowercase.RequestSourceSequence("idle", 1);
            RequireEmptySequence(lowercase.ActiveSequence == "idle", "Default policy blocked an explicit lowercase group.");

            ambiguous.Configure([new("Idle", 0, 1, 0, 3, []), new("SpecialIdle", 0, 1, 0, 3, [])],
                autoStartManagedLoops: false);
            ambiguous.ConfigureObjectDefaultStartup();
            RequireEmptySequence(ambiguous.ActiveSequence is null && ambiguous.ObjectStartupUnbound is not null,
                "Ambiguous canonical defaults guessed a priority or hid their missing owner.");
            ambiguous.RequestSourceSequence("SpecialIdle", 1);
            RequireEmptySequence(ambiguous.ActiveSequence == "SpecialIdle" && ambiguous.ObjectStartupUnbound is null,
                "Explicit source selection did not supersede default ambiguity.");

            requested.Configure([new("Idle", 0, 1, 0, 3, []), new("Other", 0, 1, 0, 3, [])],
                autoStartManagedLoops: false);
            requested.RequestSourceSequence("Other", 1);
            requested.ConfigureObjectDefaultStartup();
            RequireEmptySequence(requested.ActiveSequence == "Other", "Default startup overwrote existing source intent.");

            caller.Configure([new("Other", 0, 1, 0, 3, [])]);
            caller.PlaySourceSequence("Other");
            caller._Process(.25);
            caller.ConfigureObjectDefaultStartup();
            caller._Process(.25);
            RequireEmptySequence(caller.ActiveSequence == "Other" && caller.SourceTimeSeconds == .5,
                "Placed object defaults stole or restarted an independent nested model's playback owner.");

            var morphTime = -1f;
            direct.Configure([new("DirectMorph", 2, 1, -2, 22.833334f, [new(time => morphTime = time)])
            { DirectClock = new(-1, 0x4c, 1, 0, -2, 22.833334f, -1, 0), ApplicationTime = () => 100 }]);
            direct.PlaySourceSequence("DirectMorph");
            direct.ConfigureObjectDefaultStartup();
            direct._Process(.5);
            RequireEmptySequence(morphTime == 22.833334f && direct.SourceTimeSeconds == 22.833334f,
                "Object defaults restarted, looped or stole an independent clamped APP_TIME morph.");
            GD.Print("OPENNV_OBJECT_DEFAULT_STARTUP_PASS canonicalCase=true liveChannels=true savedPhase=true consumedStartKeys=true explicitFirst=true callerOwnerRetained=true ambiguousUnbound=true directMorphClamp=true pixels=unverified");
        }
        finally { lowercase.Free(); ambiguous.Free(); requested.Free(); caller.Free(); direct.Free(); }
    }
}
