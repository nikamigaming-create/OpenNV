namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    internal void RetireXrAdapter()
    {
        if (_xr is null) return;
        ProcessMode = ProcessModeEnum.Disabled;
        ReleaseXrContacts();
    }
}
