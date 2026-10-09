using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeConversation
{
    private RuntimeNativeStandaloneInterface? _standaloneNativeInterface;
    internal void BindStandaloneInterface(RuntimeNativeStandaloneInterface manager)
    {
        if (_standaloneNativeInterface is not null || _menu is not null)
            throw new InvalidOperationException("Conversation source interface must bind before its real dialog factory.");
        _standaloneNativeInterface = manager;
    }
    private void PublishStandaloneDialog()
    {
        if (_standaloneNativeInterface is { } manager)
            (_menu ?? throw new InvalidOperationException("Actual dialog factory returned no native menu.")).BindStandaloneDialog(manager);
    }
    private void CloseStandaloneDialog() => _menu?.CloseStandaloneDialog();
}
