using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private uint _selfCameraSourceMask;

    private void ConfigureSelfCameraVisibility()
    {
        _selfCameraSourceMask = _camera.CullMask;
        PublishSelfCameraVisibility();
    }

    private void PublishSelfCameraVisibility() => _camera.CullMask = FalloutNpcAppearanceSelfView.CameraMask(
        _selfCameraSourceMask, _sourceCamera is not null || _furniturePhase != 0 || !_thirdPersonMode);
}
