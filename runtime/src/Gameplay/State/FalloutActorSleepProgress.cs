using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// A selected source bed is travel/search intent, not physical occupancy.
// FurnitureContinuation owns reservation, pose and entry/loop/exit clocks.
internal sealed record FalloutActorSleepProgress(FalloutFormKey Cell, FalloutFormKey Bed, bool Furniture)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Cell.OwnerPlugin) || Cell.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            string.IsNullOrWhiteSpace(Bed.OwnerPlugin) || Bed.ObjectId is 0 or > FalloutFormKey.ObjectIdMask)
            throw new InvalidDataException("Sleep continuation has no source cell or bed intent.");
    }
}
