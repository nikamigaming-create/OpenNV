using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class NativeOwnedDialogueMenu
{
    private string _standaloneDialogStack = "", _standaloneDialogSha256 = "";
    private FalloutStandaloneInterfaceState? _standaloneInterface;
    private FalloutStandaloneInterfaceObject? _standaloneDialog;
    internal string StandaloneDialogStack => _standaloneDialogStack;
    internal string StandaloneDialogSha256 => _standaloneDialogSha256;
    private void CaptureStandaloneDialogInput()
    {
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Dialog has no selected source.");
        if (!source.TryRead("menus/dialog/dialog_menu.xml", null, out var bytes, out _))
            throw new InvalidDataException("Dialog construction omitted the actual source XML input.");
        _standaloneDialogStack = source.StackId; _standaloneDialogSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
    internal void BindStandaloneDialog(RuntimeNativeStandaloneInterface manager)
    {
        if (_standaloneDialog is not null) throw new InvalidOperationException("Dialog source factory cannot publish twice.");
        _standaloneInterface = manager.SourceState; _standaloneDialog = manager.ConstructDialog(this);
        PublishActualDialogTiles();
    }
    internal void CloseStandaloneDialog()
    {
        if (_standaloneDialog is { } dialog) _standaloneInterface!.CloseDialog(dialog);
    }
    private void RequireStandaloneDialogShow(FalloutFormKey? speaker) => RequireActualDialogShow(speaker);
    private void RetireStandaloneDialog()
    {
        if (_standaloneDialog is not { } dialog) return;
        try { _standaloneInterface!.RetireDialog(dialog); _standaloneDialog = null; }
        catch (Exception error) { _standaloneInterface!.RetainFailure(error); _failed(error); throw; }
    }
    private void ReleaseStandaloneDialogViewport()
    {
        var errors = new List<Exception>();
        try { RetireStandaloneDialog(); } catch (Exception error) { errors.Add(error); }
        try { _viewportLayout?.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Dialog source/native lifetime retirement failed.", errors);
    }
}
