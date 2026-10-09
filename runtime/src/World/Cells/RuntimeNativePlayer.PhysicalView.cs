using Godot;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private Camera3D? _physicalViewCamera;
    private uint _physicalViewMask;
    private void PublishPlayerPhysicalView()
    {
        if (_physicalViewCamera is null) { _physicalViewCamera = _camera; _physicalViewMask = _camera.CullMask; }
        if (_physicalViewCamera != _camera)
            throw new NotSupportedException("Changing the active camera during player physical motion requires its actual presentation handoff.");
        _thirdPerson!.SetPhysicalView(_camera, _xr is not null || !_thirdPersonMode);
    }
    private void RetirePlayerPhysicalView()
    {
        _thirdPerson?.RetirePhysicalView();
        if (_physicalViewCamera is { } camera && GodotObject.IsInstanceValid(camera)) camera.CullMask = _physicalViewMask;
        _physicalViewCamera = null;
    }
}
