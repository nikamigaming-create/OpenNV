using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class NativeOwnedDialogueMenu
{
    private RuntimeNativeStandaloneInterface? _standaloneDialogManager;
    private FalloutStandaloneDialogDocument? _standaloneDialogDocument;
    private FalloutStandaloneDialogDeclaration? _standaloneDialogDeclaration;
    private XElement ReadActualDialogMenu(RuntimeNativeStandaloneInterface? manager)
    {
        if (manager is null)
            return FalloutMenuXml.Expand(FalloutMenuXml.Read(FalloutStandaloneDialogSource.Path)).Elements("menu").Single();
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Dialog omitted its real selected source.");
        _standaloneDialogManager = manager;
        _standaloneDialogDocument = FalloutStandaloneDialogSource.Read(source);
        _standaloneDialogDeclaration = FalloutStandaloneDialogSource.Bind(manager.SourceState.Source, source.StackId, _standaloneDialogDocument);
        return _standaloneDialogDocument.Root;
    }
    private void InitializeActualDialogTraits(XElement root)
    {
        if (_standaloneDialogManager is not null)
        {
            // The original XML's values precede the opener's real writes.
            // In particular, constructor creation does not show the topics.
            _ = _tiles.Number(root, "_DialogVisible");
            _ = _tiles.Number(root, "_ShowSubtitles");
            _tiles.Bind(root, "lifecycle", 4);
            _tiles.Bind(root, "stackingtype", BitConverter.UInt32BitsToSingle(_standaloneDialogDeclaration!.StackingBits));
            Visible = false;
        }
        else
        {
            _tiles.Bind(root, "_DialogVisible", 1); _tiles.Bind(root, "_ShowSubtitles", 1);
        }
    }
    private void PublishActualDialogTiles()
    {
        if (_standaloneInterface is not { } state || _standaloneDialog is not { } dialog) return;
        var document = _standaloneDialogDocument ?? throw new InvalidOperationException("Standalone Dialog omitted actual source expansion.");
        var declaration = _standaloneDialogDeclaration ?? throw new InvalidOperationException("Standalone Dialog omitted its class/control declaration.");
        state.PublishDialogTiles(dialog, declaration, () => IsInstanceValid(this) && IsInsideTree() && !IsQueuedForDeletion() &&
            ReferenceEquals(_tiles.Root, document.Root) && _standaloneDialogStack == declaration.Stack);
    }
    private void RequireActualDialogShow(FalloutFormKey? speaker)
    {
        if (_standaloneInterface is null) return;
        var state = _standaloneInterface;
        try
        {
            (_standaloneDialogManager ?? throw new InvalidOperationException("Original Dialog lost the real native manager."))
                .OpenDialog(this, _standaloneDialog ?? throw new InvalidOperationException("Original Dialog lost its actual factory."),
                    speaker ?? throw new InvalidOperationException("Dialog opener omitted the actual placed speaking actor."));
        }
        catch (Exception error) { state.RetainFailure(error); throw; }
    }
    internal uint? ActualStandaloneDialogMenu => _standaloneInterface is { } state && _standaloneDialog is { } dialog &&
        state.ReadDialogState(dialog) is { Step: FalloutStandaloneDialogStep.ShowReturned, Retired: null }
            ? FalloutStandaloneDialogSource.MenuId : null;
}
