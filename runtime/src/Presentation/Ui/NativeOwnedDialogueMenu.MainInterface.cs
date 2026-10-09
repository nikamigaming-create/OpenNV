using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class NativeOwnedDialogueMenu
{
    private FalloutMainInterfaceState? _sourceMainInterface;
    private FalloutStandaloneInterfaceObject? _sourceMainDialog;
    internal void BindSourceMainDialog(RuntimeNativeMainInterface manager)
    {
        if (_sourceMainDialog is not null || _standaloneDialog is not null)
            throw new InvalidOperationException("Dialog cannot invent a second source class/native lifetime.");
        _sourceMainInterface = manager.SourceState; _sourceMainDialog = manager.ConstructDialog(this);
    }
    internal void CloseSourceMainDialog()
    {
        if (_sourceMainDialog is { } dialog) _sourceMainInterface!.CloseDialog(dialog);
    }
    private void RetireSourceMainDialog()
    {
        if (_sourceMainDialog is not { } dialog) return;
        try { _sourceMainInterface!.RetireDialog(dialog); _sourceMainDialog = null; }
        catch (Exception error) { _sourceMainInterface!.RetainFailure(error); _failed(error); throw; }
    }
}
