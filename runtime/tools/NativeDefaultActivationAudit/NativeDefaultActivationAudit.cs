using Godot;

public partial class NativeDefaultActivationAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            GD.Print("OPENNV_DEFAULT_ACTIVATION_OWNER mvid=" + typeof(OpenNV.Runtime.World.Cells.RuntimeNativeReferenceEvents).Assembly.ManifestModule.ModuleVersionId);
            ExerciseSynthetic();
            if (OS.GetCmdlineUserArgs() is ["--owned-default-loot", var game, var mod, var root, var checkpoint, var reference, .. var dependencies])
                await ExerciseOwned(game, mod, root, checkpoint, reference, dependencies);
            else if (OS.GetCmdlineUserArgs().Length != 0)
                throw new ArgumentException("Expected --owned-default-loot game mod mod-root checkpoint plugin:reference [dependencies...].");
            GD.Print("OPENNV_NATIVE_DEFAULT_ACTIVATION_PASS componentProof=true campaignSaveAndOrdinaryLootUnverified=true");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Pump(OpenNV.Runtime.World.Cells.RuntimeNativeReferenceEvents events)
    {
        events.SetProcess(true); events._Process(0); events.SetProcess(false);
    }
    private static Task Deferred()
    {
        var completion = new TaskCompletionSource();
        Callable.From(() => completion.SetResult()).CallDeferred();
        return completion.Task;
    }
}
