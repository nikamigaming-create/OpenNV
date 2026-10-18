namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void PrepareNativeOpenXrExit()
    {
        // Raw hand-query bodies must retire while the physics server is live.
        // Final process cleanup is later than an ordinary scene transition.
        if (IsInstanceValid(_nativePlayer)) _nativePlayer!.RetireXrAdapter();
        if (IsInstanceValid(_nativeXr)) _nativeXr!.ProcessMode = ProcessModeEnum.Disabled;
        // Both callers immediately quit the process. Keep the viewport/camera
        // stereo contract intact through the last queued draw; Godot owns final
        // OpenXR interface/driver cleanup after scene and render retirement.
    }
}
