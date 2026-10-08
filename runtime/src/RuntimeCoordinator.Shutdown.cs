namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void StopNativeOpenXr()
    {
        // Raw hand-query bodies must retire while the physics server is live.
        // Final process cleanup is later than an ordinary scene transition.
        if (IsInstanceValid(_nativePlayer)) _nativePlayer!.RetireXrAdapter();
        if (IsInstanceValid(_nativeXr)) _nativeXr!.ProcessMode = ProcessModeEnum.Disabled;
        GetViewport().UseXR = false;
        if (IsInstanceValid(_openXr) && _openXr!.IsInitialized()) _openXr.Uninitialize();
        _openXr = null;
    }
}
