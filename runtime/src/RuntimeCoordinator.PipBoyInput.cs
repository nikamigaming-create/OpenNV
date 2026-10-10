using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutPipBoyInputBlocker NativePipBoySourceInputAccess()
    {
        var player = _nativePlayer;
        var driver = _nativeOpeningStageDriver;
        if (player is null || !GodotObject.IsInstanceValid(player) || !player.IsInsideTree() || driver is null)
            return FalloutPipBoyInputBlocker.PlayerUnavailable;
        return player.SourcePipBoyInputAccess(
            _nativeDoorLoading || _nativeSessionTransitioning || _retiringNativeSession, driver.PipBoy.Available);
    }

    private FalloutPipBoyInputBlocker NativeWristPipBoyInputAccess()
    {
        var access = NativePipBoySourceInputAccess();
        if (access != FalloutPipBoyInputBlocker.None) return access;
        if (_nativePlayer!.ModalInput) return FalloutPipBoyInputBlocker.ModalInput;
        if (_nativeXr?.PipBoyInputFailure is not null) return FalloutPipBoyInputBlocker.InputFailure;
        var view = _nativePipBoy;
        if (view is null || !GodotObject.IsInstanceValid(view))
            return FalloutPipBoyInputBlocker.MenuUnavailable;
        if (view.SourceInputFailure is not null) return FalloutPipBoyInputBlocker.MenuFailure;
        return view.SourceInputSurfacePublished ? FalloutPipBoyInputBlocker.None : FalloutPipBoyInputBlocker.MenuUnavailable;
    }
}
