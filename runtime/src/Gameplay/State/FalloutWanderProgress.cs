using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// One accepted movement request. Activity selection owns when to start the
// next request; rendering, combat interruption and save capture must not draw
// another random destination for this one.
internal sealed record FalloutWanderProgress(FalloutFormKey Cell, string NavigationSha256,
    float[] Destination, bool Arrived)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Cell.OwnerPlugin) || Cell.ObjectId == 0 ||
            NavigationSha256 is not { Length: 64 } || !NavigationSha256.All(Uri.IsHexDigit) ||
            Destination is not { Length: 3 } || Destination.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Saved wander movement request is invalid.");
    }
}
