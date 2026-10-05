using Godot;

public partial class NativeAuthoredRagdollAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            ExerciseSynthetic();
            var args = OS.GetCmdlineUserArgs();
            if (args.Length > 0)
            {
                if (args.Length < 5 || args[0] != "--owned-authored-corpses")
                    throw new ArgumentException("Expected --owned-authored-corpses game mod mod-root plugin:reference[,reference...] [dependencies...].");
                await ExerciseOwned(args[1], args[2], args[3], args[4].Split(','), args.Skip(5).ToArray());
            }
            GD.Print("OPENNV_NATIVE_AUTHORED_RAGDOLL_PASS sourceFirstChild=true componentProof=true gameplayAndPixelsUnverified=true");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Malformed authored pose or incompatible accumulation root was admitted.");
    }

    private static Task Deferred()
    {
        var completion = new TaskCompletionSource();
        Callable.From(() => completion.SetResult()).CallDeferred();
        return completion.Task;
    }
}
