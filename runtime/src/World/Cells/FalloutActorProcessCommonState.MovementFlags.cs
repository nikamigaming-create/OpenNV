using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessMovementFlagFields(uint Flags, long Stores)
{
    internal static FalloutProcessMovementFlagFields Constructed => new(0, 0);
}
internal sealed record FalloutProcessMovementFlagUpdate(FalloutProcessSceneQuery Before, long Changed,
    uint Flags, uint? AfterFlags, int Stores, bool Returned);

internal sealed partial class FalloutActorProcessCommonState
{
    internal FalloutProcessMovementFlagUpdate ReadAndClearPlayerMovementFlags(FalloutFormKey actor, long epoch)
    {
        var before = ReadSceneQueries(actor, epoch);
        if (!before.Actor.EnginePlayer)
            throw new InvalidOperationException("Player movement flag consumption requires the actual engine Player.");
        if (before.Level != FalloutDetectionProcessLevel.High)
            return new(before, before.Changed, 0, null, 0, true);
        var current = Require(actor);
        var flags = current.SceneFields?.MovementFlags ??
            throw new InvalidDataException("High process movement flags have no actual constructor.");
        // The source reads this independent word, then clears it before any
        // flag-selected child. A later failed child must retain the clear.
        var changed = Next();
        _current[actor] = current with
        {
            SceneFields = current.SceneFields! with
            { MovementFlags = new(0, checked(flags.Stores + 1)) },
            Changed = changed
        };
        return new(before, changed, flags.Flags, 0, 1, true);
    }

    private static void ValidateMovementFlagFields(FalloutProcessCommonEntry entry)
    {
        var flags = entry.SceneFields?.MovementFlags;
        if ((entry.SceneFields is not null && entry.Level == FalloutDetectionProcessLevel.High) != (flags is not null) ||
            flags is not null && (flags.Stores < 0 || flags.Flags != 0))
            throw new InvalidDataException("Process movement flags lost their constructor or admitted an unowned producer.");
    }
}
