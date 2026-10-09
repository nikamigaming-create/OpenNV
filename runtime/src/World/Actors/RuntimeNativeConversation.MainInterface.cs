using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeConversation
{
    private RuntimeNativeMainInterface? _sourceMainNativeInterface;
    internal void BindSourceMainInterface(RuntimeNativeMainInterface manager)
    {
        if (_sourceMainNativeInterface is not null || _standaloneNativeInterface is not null || _menu is not null)
            throw new InvalidOperationException("Source Main interface must bind before the actual selected dialog constructor.");
        _sourceMainNativeInterface = manager;
    }
    private void PublishSourceMainDialog()
    {
        if (_sourceMainNativeInterface is { } manager)
            (_menu ?? throw new InvalidOperationException("Actual Dialog constructor returned no native menu.")).BindSourceMainDialog(manager);
    }
    private void CloseSourceMainDialog() => _menu?.CloseSourceMainDialog();
}
