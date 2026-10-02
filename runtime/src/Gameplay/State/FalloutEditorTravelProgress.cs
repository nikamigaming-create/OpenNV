using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutEditorTravelProgress(FalloutFormKey Cell, float[] Location, bool Complete)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Cell.OwnerPlugin) || Cell.ObjectId == 0 ||
            Location is not { Length: 3 } || Location.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Saved editor travel destination is invalid.");
    }
}
