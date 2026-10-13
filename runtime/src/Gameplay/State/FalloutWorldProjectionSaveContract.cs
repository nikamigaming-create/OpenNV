using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutWorldProjectionSaveContract
{
    internal static void ValidateShape(FalloutReferenceWorldProjectionSnapshot? projection, FalloutActorProcessRuntimeSnapshot? runtime)
    {
        if (runtime?.StandaloneMain is not { } main)
        {
            if (projection is not null) throw new InvalidDataException("Saved World projection belongs to another actual Main family.");
            return;
        }
        if (projection is null) throw new InvalidDataException("Complete standalone save omitted its actual current World projection.");
        ValidatePlayer(projection, main.Scene.ReferenceOrientation, main.Source, main.Scene.Stack, runtime.CapturedProcess);
    }

    internal static void RequireComplete(FalloutReferenceWorldProjectionSnapshot projection)
    {
        ArgumentNullException.ThrowIfNull(projection); projection.Validate();
        if (projection.ConstructionFailure is not null || projection.UnpublishedConstructor is not null ||
            projection.Publication.Current is not { ConstructorReturned: true, CurrentFovBits: not null, Failure: null, Retired: false } current ||
            current.Call is { Returned: false } || !float.IsFinite(BitConverter.UInt32BitsToSingle(current.CurrentFovBits.Value)))
            throw new NotSupportedException("Complete campaign save retains a failed, absent or unreturned actual World projection child.");
    }

    internal static void ValidatePlayer(FalloutReferenceWorldProjectionSnapshot projection,
        FalloutPlayerReferenceOrientationSnapshot? reference, FalloutMainScriptCallerSource main, string stack, Guid process)
    {
        RequireComplete(projection);
        if (reference is null) throw new InvalidDataException("Complete standalone save omitted its actual Player reference orientation.");
        FalloutPlayerReferenceOrientationOwner.Validate(reference);
        var current = projection.Publication.Current!;
        if (reference.Retired || reference.Failure is not null || !reference.PlayerFovReturned || reference.Call is { Returned: false })
            throw new NotSupportedException("Complete campaign save retains an unreturned Player reference/FOV writer.");
        if (reference.Source != current.Source || reference.Source.Controls.Main != main || reference.Stack != stack ||
            current.Stack != stack || reference.CapturedProcess != process || reference.ProjectionWorld != projection.Publication.CapturedWorld ||
            reference.ProjectionRootFactory != projection.Publication.CurrentRootFactory || reference.PlayerFovSetting != current.ConstructorSetting)
            throw new InvalidDataException("Saved Player reference fields lost their exact actual World/root/process/source association.");
    }
}
