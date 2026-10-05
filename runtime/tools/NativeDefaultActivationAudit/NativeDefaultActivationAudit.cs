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
            else if (OS.GetCmdlineUserArgs() is ["--owned-corpse-ray-skin", var rayGame, var rayMod, var rayRoot, var rayCheckpoint, var rayReference, .. var rayDependencies])
                await ExerciseOwned(rayGame, rayMod, rayRoot, rayCheckpoint, rayReference, rayDependencies, presentation: true);
            else if (OS.GetCmdlineUserArgs() is ["--owned-corpse-ray-skin-renderer", var renderGame, var renderMod, var renderRoot, var renderCheckpoint, var renderReference, .. var renderDependencies])
                await ExerciseOwned(renderGame, renderMod, renderRoot, renderCheckpoint, renderReference, renderDependencies, presentation: true, requireRenderer: true);
            else if (OS.GetCmdlineUserArgs() is ["--owned-corpse-collapse", var floorGame, var floorMod, var floorRoot, var floorCheckpoint, var floorReference, .. var floorDependencies])
                await ExerciseOwned(floorGame, floorMod, floorRoot, floorCheckpoint, floorReference, floorDependencies, presentation: true, naturalCollapse: true);
            else if (OS.GetCmdlineUserArgs() is ["--owned-corpse-collapse-renderer", var paletteGame, var paletteMod, var paletteRoot, var paletteCheckpoint, var paletteReference, .. var paletteDependencies])
                await ExerciseOwned(paletteGame, paletteMod, paletteRoot, paletteCheckpoint, paletteReference, paletteDependencies, presentation: true, requireRenderer: true, naturalCollapse: true);
            else if (OS.GetCmdlineUserArgs().Length != 0)
                throw new ArgumentException("Expected --owned-default-loot/--owned-corpse-ray-skin/--owned-corpse-ray-skin-renderer/--owned-corpse-collapse/--owned-corpse-collapse-renderer game mod mod-root checkpoint plugin:reference [dependencies...].");
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
