using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPipBoyInputBlocker
{
    None,
    Unbound,
    PlayerUnavailable,
    SourceControl,
    ModalInput,
    Furniture,
    Loading,
    Equipment,
    MenuUnavailable,
    MenuFailure,
    InputFailure,
}

// Input permission consumes the committed control state. It neither changes
// that state nor establishes a device, rendered frame or completed menu action.
internal static class FalloutPipBoyInputPolicy
{
    internal static FalloutPipBoyInputBlocker Read(FalloutPlayerControlState controls,
        bool furnitureActive, bool loading, bool equipped)
    {
        if (!controls.PipBoy) return FalloutPipBoyInputBlocker.SourceControl;
        if (furnitureActive) return FalloutPipBoyInputBlocker.Furniture;
        if (loading) return FalloutPipBoyInputBlocker.Loading;
        if (!equipped) return FalloutPipBoyInputBlocker.Equipment;
        return FalloutPipBoyInputBlocker.None;
    }
}
