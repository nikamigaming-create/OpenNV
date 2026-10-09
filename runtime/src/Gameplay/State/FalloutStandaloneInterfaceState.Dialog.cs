using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandaloneInterfaceState
{
    private readonly List<FalloutStandaloneDialogState> _dialogMenus = [];
    private readonly List<FalloutStandaloneMenuStackWrite> _menuWrites = [];
    private readonly List<FalloutStandaloneMenuTransition> _menuTransitions = [];
    private readonly Dictionary<Guid, Func<bool>> _menuRoots = [];
    private Guid? _selectedMenu, _selectedTile;
    private FalloutStandaloneDialogInvocation? _dialogInvocation;
    private bool _dialogChild;

    internal void PublishDialogTiles(FalloutStandaloneInterfaceObject dialog,
        FalloutStandaloneDialogDeclaration declaration, Func<bool> livingExactRoot)
    {
        RequireCurrent(); RequireDialog(dialog); ArgumentNullException.ThrowIfNull(livingExactRoot);
        FalloutStandaloneDialogSource.Validate(Source, _stack, declaration);
        if (_dialogMenus.Any(row => row.Object == dialog.Identity) || declaration.Resources[0].Sha256 != dialog.Native.ResourceSha256 ||
            !ReadNative(livingExactRoot))
            throw new InvalidOperationException("Dialog tiles require the actual fresh returned native root and original XML closure.");
        var constructed = Next();
        var tiles = new List<FalloutStandaloneDialogTile>();
        var controls = new Guid?[4];
        foreach (var callback in declaration.Controls)
        {
            var tile = new FalloutStandaloneDialogTile(Guid.NewGuid(), callback.Ordinal, callback.Id, callback.Path, callback.Kind);
            tiles.Add(tile);
            if (tile.Id <= 3) controls[checked((int)tile.Id)] = tile.Identity;
        }
        _dialogMenus.Add(new(dialog.Identity, _process, declaration, constructed, null, 0, 4, 0x100,
            controls, tiles.ToArray(), null, FalloutStandaloneDialogStep.Constructed, Next(), null, null, null, null));
        _menuRoots.Add(dialog.Identity, livingExactRoot);
    }

    internal FalloutStandaloneDialogState ReadDialogState(FalloutStandaloneInterfaceObject dialog)
    { RequireOwnerThread(); RequireDialog(dialog); return DialogState(dialog.Identity); }
    private FalloutStandaloneDialogState DialogState(Guid identity) => _dialogMenus.SingleOrDefault(row => row.Object == identity) ??
        throw new InvalidOperationException("Dialog omitted its original menu constructor/returned tile owner.");
    private void SetDialog(FalloutStandaloneDialogState row)
    {
        var index = _dialogMenus.FindIndex(item => item.Object == row.Object);
        if (index < 0) throw new InvalidOperationException("Dialog lost its actual current constructor prefix.");
        _dialogMenus[index] = row with { Changed = Next() };
    }
    internal void OpenDialog(FalloutStandaloneInterfaceObject dialog, FalloutFormKey speaker, IFalloutStandaloneDialogChildren children)
    {
        RequireCurrent(); RequireDialog(dialog);
        if (speaker.ObjectId == 0 || string.IsNullOrWhiteSpace(speaker.OwnerPlugin) || children is null ||
            children.Source != Source.Identity || string.IsNullOrWhiteSpace(children.Owner))
            throw new InvalidDataException("Dialog opener requires the actual speaker and same source child owner.");
        var row = DialogState(dialog.Identity);
        if (row.Step == FalloutStandaloneDialogStep.ShowReturned)
        {
            if (row.Speaker != speaker) throw new InvalidOperationException("Existing Dialog root cannot change its speaker through a second opener.");
            RequireDialogRoot(row); return;
        }
        if (_dialogInvocation is not null || row.Step != FalloutStandaloneDialogStep.Constructed || row.Retired is not null)
            throw new InvalidOperationException("An entered/failed Dialog source prefix cannot be replayed implicitly.");
        var invocation = new FalloutStandaloneDialogInvocation(this, dialog.Identity); _dialogInvocation = invocation;
        try
        {
            RequireDialogRoot(row);
            row = row with { Root = Guid.NewGuid(), Step = FalloutStandaloneDialogStep.RootStored }; SetDialog(row);
            if (FalloutStandaloneDialogSource.Registers(row.Declaration.StackingBits))
            {
                row = row with { MenuIdentity = FalloutStandaloneDialogSource.MenuId, Step = FalloutStandaloneDialogStep.MenuIdentityStored }; SetDialog(row);
                PushDialog(invocation, children);
                row = DialogState(dialog.Identity);
                // The original selected object and tile fields are distinct
                // from the own Pip-Boy manager and alternate selector.
                if (_selectedMenu != dialog.Identity)
                {
                    if (_selectedTile is { } tile)
                        CallDialogChild(invocation, FalloutStandaloneDialogStep.SelectionCleared,
                            () => children.StoreTileSelectionLocus(invocation, tile, 0));
                    _selectedTile = null; Next(); _selectedMenu = null; Next();
                    row = row with { Step = FalloutStandaloneDialogStep.SelectionCleared }; SetDialog(row);
                }
            }
            row = DialogState(dialog.Identity);
            row = row with { Step = FalloutStandaloneDialogStep.ControlsRequired }; SetDialog(row);
            if (row.Controls.Any(control => control is null))
                throw new InvalidDataException("Original Dialog opener returned null: one of four source tile controls is missing.");
            row = row with { Speaker = speaker, Step = FalloutStandaloneDialogStep.SpeakerStored }; SetDialog(row);
            CallDialogChild(invocation, FalloutStandaloneDialogStep.ActorPreparationEntered,
                () => children.PrepareActors(invocation, speaker));
            // This child has additional original response/actor/camera/effect
            // operations. Returning it alone never certifies the menu tail.
            EnterDialogBoundary(invocation, "original-response-camera-effect-and-Dialog-Show-tail");
        }
        catch (Exception error)
        {
            row = DialogState(dialog.Identity);
            SetDialog(row with
            {
                Failure = error.GetType().Name + ": " +
                (string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message)
            });
            RetainFailure(error); throw;
        }
        finally { _dialogChild = false; _dialogInvocation = null; }
    }
    private void RequireDialogRoot(FalloutStandaloneDialogState row)
    {
        if (!_menuRoots.TryGetValue(row.Object, out var root) || !ReadNative(root))
            throw new InvalidOperationException("Dialog source control tree lost its exact current native publication.");
    }
    private void PushDialog(FalloutStandaloneDialogInvocation invocation, IFalloutStandaloneDialogChildren children)
    {
        var row = DialogState(invocation.Dialog);
        var before = _slots.ToArray(); var mode = _mode;
        var slot = Array.FindIndex(_slots, value => value == 0);
        if (slot < 0)
        {
            _menuWrites.Add(new(row.Object, Next(), before, _slots.ToArray(), mode, _mode, -1, true, false, null)); return;
        }
        _slots[slot] = row.MenuIdentity; Next();
        if (slot == 0) { _mode = 3; Next(); }
        var sceneByte = slot == 0 && row.MenuIdentity != 1001 || slot == 1 && _slots[0] == 1001;
        var write = new FalloutStandaloneMenuStackWrite(row.Object, Next(), before, _slots.ToArray(), mode, _mode,
            slot, !sceneByte, sceneByte, null);
        _menuWrites.Add(write);
        SetDialog(row with { Step = FalloutStandaloneDialogStep.PushCommitted });
        if (!sceneByte) return;
        try
        {
            CallDialogChild(invocation, FalloutStandaloneDialogStep.SceneByteEntered,
                () => children.StoreMenuSceneByte(invocation, 1));
            _menuWrites[^1] = write with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _menuWrites[^1] = write with
            {
                Failure = error.GetType().Name + ": " +
                (string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message),
                Changed = Next()
            };
            throw;
        }
    }
    private void CallDialogChild(FalloutStandaloneDialogInvocation invocation, FalloutStandaloneDialogStep step, Action call)
    {
        if (_dialogChild) throw new InvalidOperationException("Original Dialog child cannot recursively dispatch another child.");
        SetDialog(DialogState(invocation.Dialog) with { Step = step }); _dialogChild = true;
        try
        {
            call(); RequireDialogChild(invocation, step);
            if (_failure is not null || _boundary is not null || DialogState(invocation.Dialog) is { Boundary: not null } or { Failure: not null })
                throw new InvalidOperationException(_failure ?? _boundary ?? "Dialog child retained an unreturned source prefix.");
            RequireDialogRoot(DialogState(invocation.Dialog));
        }
        finally { _dialogChild = false; }
    }
    internal void RequireDialogChild(FalloutStandaloneDialogInvocation invocation, FalloutStandaloneDialogStep step)
    {
        RequireOwnerThread();
        if (_dialogInvocation != invocation || invocation.Owner != this || invocation.Process != _process || !_dialogChild ||
            DialogState(invocation.Dialog).Step != step)
            throw new InvalidOperationException("Dialog child changed its actual source invocation/thread/step.");
    }
    internal void EnterDialogBoundary(FalloutStandaloneDialogInvocation invocation, string owner)
    {
        RequireOwnerThread(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (_dialogInvocation != invocation || invocation.Owner != this || invocation.Process != _process)
            throw new InvalidOperationException("Unknown Dialog arm lost its actual entered source invocation.");
        var boundary = "source-FO3-Dialog-child-unowned:" + owner;
        SetDialog(DialogState(invocation.Dialog) with { Boundary = boundary });
        EnterUnownedConsumer(owner);
    }
    private void RetireDialogTiles(Guid identity)
    {
        var index = _dialogMenus.FindIndex(row => row.Object == identity);
        if (index < 0 || _dialogMenus[index].Retired is not null) return;
        var row = _dialogMenus[index];
        // A native object retirement does not manufacture an original stack
        // pop or mode4->mode1 return. Any entered source failure stays retained.
        SetDialog(row with { Retired = Next(), Step = FalloutStandaloneDialogStep.Retired });
        _menuRoots.Remove(identity);
    }
}
