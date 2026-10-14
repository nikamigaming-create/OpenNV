using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandaloneInterfaceState
{
    internal Guid Constructor => _constructor;
    private FalloutStandaloneMenuLifecycleSource? _menuLifecycleSource;
    private FalloutImageSpaceState? _menuLifecycleImages;
    private FalloutDirectInputState? _menuLifecycleInput;
    private FalloutPlayerThreadContextState? _menuLifecycleThread;
    private Action? _requireMenuLifecycle;
    private Func<FalloutPlayerControlHudFrame>? _readMenuFrame;
    private readonly Dictionary<Guid, FalloutStandaloneMenuFields> _ordinaryMenus = [];
    private readonly Dictionary<Guid, (FalloutUiComponentStore Ui, FalloutUiComponentBinding Root)> _ordinaryMenuRoots = [];
    private readonly List<FalloutStandaloneMenuFieldWrite> _ordinaryMenuWrites = [];
    private readonly HashSet<Guid> _ordinaryMenuInvocations = [];
    private readonly Dictionary<uint, (Guid Object, ulong Native)> _currentOrdinaryMenus = [];
    private string? _firstOrdinaryMenuFailure;

    internal FalloutStandaloneMenuLifecycleSource ReadBoundMenuLifecycleSource()
    {
        RequireOwnerThread(); RequireMenuLifecycle(); RequireMenuFieldIdle();
        return _menuLifecycleSource!;
    }
    private FalloutStandaloneMenuFieldInvocation? _ordinaryMenuInvocation;
    private long _ordinaryMenuReentry;

    internal void BindMenuLifecycles(FalloutStandaloneMenuLifecycleSource source, FalloutImageSpaceState images,
        FalloutDirectInputState input, FalloutPlayerThreadContextState thread,
        Action requireActualOwners, Func<FalloutPlayerControlHudFrame> readActualFrame)
    {
        RequireCurrent(native: false); source.Validate(); ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(requireActualOwners); ArgumentNullException.ThrowIfNull(readActualFrame);
        RequireMenuFieldIdle(); requireActualOwners();
        if (_menuLifecycleSource is not null || source.Interface != Source || input.Source != _stack ||
            !ReferenceEquals(input.ReadManagerOwner(), input) || thread.Source.Main != Source.Main ||
            images.SourceMainImageProcess != _process)
            throw new InvalidOperationException("Menu lifecycle changed its actual Interface/image/input/thread owners.");
        _menuLifecycleSource = source; _menuLifecycleImages = images; _menuLifecycleInput = input;
        _menuLifecycleThread = thread; _requireMenuLifecycle = requireActualOwners; _readMenuFrame = readActualFrame;
        images.BindSourceMenuStackEffect(source);
    }

    internal FalloutStandaloneInterfaceObject ConstructStartMenuFields(
        FalloutStandaloneInterfaceNativePublication publication, Func<bool> livingNative)
    {
        RequireCurrent(native: false); RequireMenuLifecycle(); RequireMenuFieldIdle();
        ArgumentNullException.ThrowIfNull(publication); ArgumentNullException.ThrowIfNull(livingNative);
        if (publication.Role != FalloutStandaloneInterfaceObjectRole.OtherMenu || publication.Factory == Guid.Empty ||
            publication.Process != _process || publication.Source != Source.Identity || publication.Stack != _stack ||
            publication.NativeInstance == 0 || publication.Resource != FalloutStandaloneMenuLifecycleSource.StartMenuPath ||
            !FalloutAdvancementRuntimeReceipt.Digest(publication.ResourceSha256) || string.IsNullOrWhiteSpace(publication.Owner) ||
            _currentOrdinaryMenus.ContainsKey(FalloutStandaloneMenuLifecycleSource.StartMenu) ||
            _objects.Any(row => row.Native.Factory == publication.Factory || row.Process == _process &&
                row.Native.NativeInstance == publication.NativeInstance && row.Retired is null) || !ReadNative(livingNative))
            throw new InvalidDataException("StartMenu field constructor lacks its actual current source/native factory.");
        var menu = new FalloutStandaloneInterfaceObject(Guid.NewGuid(), _process, publication.Role, Next(), null, null, publication);
        _objects.Add(menu); _native.Add(menu.Identity, livingNative);
        if (!_currentOrdinaryMenus.TryAdd(FalloutStandaloneMenuLifecycleSource.StartMenu, (menu.Identity, publication.NativeInstance)))
            throw new InvalidOperationException("A source menu class already owns a current native object.");
        _ordinaryMenus.Add(menu.Identity, new(menu.Identity, _process, _menuLifecycleSource!.Identity,
            FalloutStandaloneMenuLifecycleSource.StartMenu, 0, FalloutStandaloneMenuLifecycleSource.ConstructedLifecycle,
            FalloutStandaloneMenuLifecycleSource.ConstructorFlags, Next(), _sequence, null, null, null, null));
        return menu;
    }

    internal FalloutStandaloneMenuFieldWrite BindStartMenuRoot(Guid invocation,
        FalloutStandaloneInterfaceObject menu, FalloutUiComponentStore ui, FalloutUiComponentBinding root)
    {
        ArgumentNullException.ThrowIfNull(ui); ArgumentNullException.ThrowIfNull(root);
        return ExecuteMenuField(invocation, menu, FalloutStandaloneMenuFieldOperation.BindRoot, call =>
        {
            var row = MenuFields(menu.Identity);
            if (row.Root is not null) throw new InvalidOperationException("Current menu root cannot be rebound through constructor replay.");
            ui.RequireComponent(root);
            var sourceRoot = ui.CopyComponent(root);
            if (sourceRoot.Name.LocalName != "menu" || sourceRoot.Element("class")?.Value.Trim() != "entity_StartMenu")
                throw new InvalidDataException("Session menu changed its actual selected source class/root.");
            SetMenuFields(row with { Root = root }); _ordinaryMenuRoots.Add(menu.Identity, (ui, root));
            Step(call, FalloutStandaloneMenuFieldStep.Root);
            var property = sourceRoot.Element("stackingtype") ?? throw new InvalidDataException("Source menu omitted stackingtype.");
            var stacking = BitConverter.SingleToUInt32Bits(FalloutStandaloneDialogSource.Stacking(property));
            if (FalloutStandaloneDialogSource.Registers(stacking))
            {
                row = MenuFields(menu.Identity); SetMenuFields(row with { Identity = row.ClassCode });
                Step(call, FalloutStandaloneMenuFieldStep.Identity);
                var pushed = FalloutStandaloneMenuStack.Push(_slots, _mode, row.ClassCode);
                CommitStack(pushed); Step(call, FalloutStandaloneMenuFieldStep.Push);
                StoreMenuImage(call, pushed.ImageMenuByte);
            }
            // The returned-root callback stores its class code after the Menu
            // binder. The inherited constructor leaves this separate field
            // unwritten; a constant virtual getter cannot supply that store.
            SetMenuFields(MenuFields(menu.Identity) with { RegisteredCode = MenuFields(menu.Identity).ClassCode });
        });
    }

    internal FalloutStandaloneMenuFieldWrite ReplaceStartMenuTransition(Guid invocation,
        FalloutStandaloneInterfaceObject menu, bool closing = false)
    {
        return ExecuteMenuField(invocation, menu, FalloutStandaloneMenuFieldOperation.ReplaceTransition, call =>
        {
            var row = MenuFields(menu.Identity); var tree = MenuRoot(row);
            if (closing)
            {
                tree.Ui.StoreFloat(tree.Root, "source-property-6002", 1);
                SetMenuFields(row with { CloseReferenceBits = 0x3f800000 });
                row = MenuFields(menu.Identity);
                _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { CloseReferenceBits = 0x3f800000, Changed = Next() };
            }
            var duration = FalloutStandaloneMenuLifecycleSource.Duration(
                BitConverter.SingleToUInt32Bits(tree.Ui.ReadFloat(tree.Root, "fadein")),
                BitConverter.SingleToUInt32Bits(tree.Ui.ReadFloat(tree.Root, "fadeout")));
            Step(call, FalloutStandaloneMenuFieldStep.Transition);
            var allocation = _menuLifecycleThread!.EnterMenuAllocation(invocation, _constructor, () => call.Require(call.Step));
            _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { Allocation = allocation, Duration = duration, Changed = Next() };
            try
            {
                for (var node = _menuTransitions.First; node is not null; node = node.Next)
                    if (node.Value.Menu == menu.Identity) { _menuTransitions.Remove(node); Next(); break; }
                var requested = Next();
                _menuTransitions.AddLast(new FalloutStandaloneMenuTransition(Guid.NewGuid(), menu.Identity, requested, 0, duration, Next()));
                allocation = _menuLifecycleThread.ReturnMenuAllocation(allocation, () => call.Require(call.Step));
                _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { Allocation = allocation, Changed = Next() };
            }
            catch (Exception error)
            {
                if (!allocation.Returned) _menuLifecycleThread.RetainMenuAllocationFailure(allocation, error);
                throw;
            }
            // The source menu stores its own lifecycle after allocation has
            // restored the actual prior TLS kind. The Interface mode is separate.
            SetMenuFields(row with { Lifecycle = closing ? FalloutStandaloneMenuLifecycleSource.LeavingLifecycle : FalloutStandaloneMenuLifecycleSource.EnteringLifecycle });
        });
    }

    internal FalloutStandaloneMenuFieldWrite AdvanceStartMenuFields(Guid invocation, FalloutStandaloneInterfaceObject menu)
    {
        return ExecuteMenuField(invocation, menu, FalloutStandaloneMenuFieldOperation.AdvanceTransition, call =>
        {
            var row = MenuFields(menu.Identity); var tree = MenuRoot(row);
            Step(call, FalloutStandaloneMenuFieldStep.Transition);
            var allocation = _menuLifecycleThread!.EnterMenuAllocation(invocation, _constructor, () => call.Require(call.Step));
            _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { Allocation = allocation, Changed = Next() };
            try
            {
                var faults = _ordinaryMenuReentry; var frame = _readMenuFrame!(); call.Require(call.Step);
                if (_ordinaryMenuReentry != faults) throw new InvalidOperationException("Menu frame callback swallowed a source reentry refusal.");
                if (frame.Clock.Source != Source.Main.EngineSha256 || frame.Clock.Process != _process ||
                    frame.Clock.Epoch < 1 || frame.Clock.Sequence < 0 || (frame.ControlMode == 4) != (frame.DivisorBits is not null))
                    throw new InvalidDataException("Menu update borrowed another actual current source timer/control mode.");
                _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { Frame = frame, Changed = Next() };
                for (var node = _menuTransitions.First; node is not null;)
                {
                    var next = node.Next; var value = node.Value; RequireAnyMenuTransition(value.Menu);
                    var advanced = FalloutStandaloneMenuTransitions.Advance(value.ElapsedBits, value.DurationBits,
                        frame.Clock.ScaledSecondsBits, frame.ControlMode, frame.DivisorBits);
                    node.Value = value with { ElapsedBits = advanced.Elapsed, ProgressBits = advanced.Progress, Changed = Next() };
                    if (value.Menu == menu.Identity)
                    {
                        _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { Progress = advanced.Progress, Changed = Next() };
                        // The only-source-menu arm writes the image effect through
                        // its independently constructed byte. Other stack contexts
                        // retain their separate source menu arbitration boundary.
                        if (_slots[0] != row.ClassCode || _slots[1] != 0)
                            throw new NotSupportedException("source-menu-transition-current-non-top-menu-arbitration-unowned");
                        var leaving = row.Lifecycle == FalloutStandaloneMenuLifecycleSource.LeavingLifecycle;
                        if (row.Lifecycle is not (FalloutStandaloneMenuLifecycleSource.LeavingLifecycle or FalloutStandaloneMenuLifecycleSource.EnteringLifecycle))
                            throw new InvalidOperationException("Menu transition changed its actual current lifecycle field.");
                        var destroy = leaving && advanced.Remove && row.CloseReferenceBits == 0x3f800000;
                        if (!destroy)
                        {
                            var scalar = leaving ? BitConverter.SingleToUInt32Bits(1f - BitConverter.UInt32BitsToSingle(advanced.Progress)) : advanced.Progress;
                            var visible = leaving && advanced.Remove ? 0u : 0x3f800000u;
                            tree.Ui.StoreFloat(tree.Root, "visible", BitConverter.UInt32BitsToSingle(visible));
                            _active[checked((int)(row.ClassCode - FalloutStandaloneMenuLifecycleSource.FirstMenu))] = visible == 0 ? (byte)0 : (byte)1;
                            SetMenuFields(MenuFields(menu.Identity) with { Visible = visible });
                            _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { Visible = visible, Changed = Next() };
                            // With a sole StartMenu the entering arm selects its
                            // effect; the leaving arm's separate HUD/null predicate
                            // does not. The current enabled byte remains authoritative.
                            if (!leaving && _menuLifecycleImages!.ReadSourceMenuStackEnabled(call) == 1)
                            {
                                var image = _menuLifecycleImages.StoreSourceMenuStackProgress(call, scalar); RecordMenuImage(image);
                                if (advanced.Remove) RecordMenuImage(_menuLifecycleImages.StoreSourceMenuStackByte(call, 0));
                            }
                            if (advanced.Remove)
                                SetMenuFields(MenuFields(menu.Identity) with { Lifecycle = leaving ? FalloutStandaloneMenuLifecycleSource.ConstructedLifecycle : FalloutStandaloneMenuLifecycleSource.LiveLifecycle });
                        }
                    }
                    if (advanced.Remove) _menuTransitions.Remove(node);
                    node = next;
                }
                allocation = _menuLifecycleThread.ReturnMenuAllocation(allocation, () => call.Require(call.Step));
                _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { Allocation = allocation, Changed = Next() };
            }
            catch (Exception error)
            {
                if (!allocation.Returned) _menuLifecycleThread.RetainMenuAllocationFailure(allocation, error);
                throw;
            }
        });
    }

    private void RequireAnyMenuTransition(Guid menu)
    {
        if (_ordinaryMenus.TryGetValue(menu, out var row) && row.Process == _process && row.Retired is null)
        { _ = MenuRoot(row); return; }
        RequireHudTransitionMenu(menu);
    }

    internal FalloutStandaloneMenuFieldWrite StoreStartMenuVisible(Guid invocation,
        FalloutStandaloneInterfaceObject menu, uint suppliedBits)
    {
        return ExecuteMenuField(invocation, menu, FalloutStandaloneMenuFieldOperation.StoreVisible, call =>
        {
            var row = MenuFields(menu.Identity); var tree = MenuRoot(row);
            tree.Ui.StoreFloat(tree.Root, "visible", BitConverter.UInt32BitsToSingle(suppliedBits));
            _active[checked((int)(row.ClassCode - FalloutStandaloneMenuLifecycleSource.FirstMenu))] =
                FalloutStandaloneMenuLifecycleSource.Visible(suppliedBits) ? (byte)1 : (byte)0;
            SetMenuFields(row with { Visible = suppliedBits });
            _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { Visible = suppliedBits, Changed = Next() };
            Step(call, FalloutStandaloneMenuFieldStep.Visible);
        });
    }

    internal FalloutStandaloneMenuFieldWrite CloseStartMenuFields(Guid invocation,
        FalloutStandaloneInterfaceObject menu)
    {
        return ExecuteMenuField(invocation, menu, FalloutStandaloneMenuFieldOperation.ForceRemove, call =>
        {
            var row = MenuFields(menu.Identity);
            if (row.Closed is not null) throw new InvalidOperationException("A closed source menu cannot replay its close prefix.");
            if (row.RegisteredCode is not { } registered)
                throw new NotSupportedException("source-menu-destructor-current-registered-code-unowned");
            // The base destructor clears its active table entry before removing
            // the identity. Native QueueFree happens only after this C# prefix.
            _active[checked((int)(registered - FalloutStandaloneMenuLifecycleSource.FirstMenu))] = 0; Next();
            if (row.Identity != 0)
            {
                var removed = FalloutStandaloneMenuStack.Remove(_slots, _mode, row.Identity, true);
                CommitStack(removed); Step(call, FalloutStandaloneMenuFieldStep.Remove);
                StoreMenuImage(call, removed.ImageMenuByte);
            }
            if (_menuTransitions.Any(node => node.Menu == menu.Identity))
                throw new InvalidOperationException("Menu destructor cannot retire an entered source transition node.");
            if (FalloutStandaloneMenuStack.Top(_slots) == 0)
            {
                Step(call, FalloutStandaloneMenuFieldStep.InputClose);
                var inputClose = _menuLifecycleInput!.ApplySourceMenuClose(call);
                _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { InputClose = inputClose, Changed = Next() };
            }
            SetMenuFields(MenuFields(menu.Identity) with { Closed = Next(), Root = null });
            _ordinaryMenuRoots.Remove(menu.Identity);
            var index = _objects.FindIndex(item => item.Identity == menu.Identity);
            _objects[index] = _objects[index] with { Closed = Next() };
        });
    }

    internal FalloutStandaloneMenuFieldWrite ReturnClosedMenuMode(Guid invocation, FalloutStandaloneInterfaceObject menu)
    {
        return ExecuteMenuField(invocation, menu, FalloutStandaloneMenuFieldOperation.QuietReturn, call =>
        {
            var row = MenuFields(menu.Identity);
            if (row.Closed is null || _mode != FalloutStandaloneMenuLifecycleSource.ReturningMode ||
                FalloutStandaloneMenuStack.Top(_slots) != 0 || _menuTransitions.Any(transition =>
                    _ordinaryMenus.TryGetValue(transition.Menu, out var item) && item.Process == _process && item.Retired is null) ||
                _dialogMenus.Any(item => item.Process == _process && item.Retired is null) ||
                _ordinaryMenus.Values.Any(item => item.Process == _process && item.Closed is null))
                throw new NotSupportedException("source-menu-return-mode-four-current-menu-prefix-unreturned");
            // The genuine source Interface update has no remaining living
            // menu/root on this arm. Its input buffer child returns before mode1.
            Step(call, FalloutStandaloneMenuFieldStep.QuietReturn);
            var flush = _menuLifecycleInput!.FlushSourceMenuMouseBuffer(call);
            _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { MouseFlush = flush, Changed = Next() };
            _mode = FalloutStandaloneMenuLifecycleSource.QuietMode; Next();
            _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { AfterMode = _mode, Changed = Next() };
        }, allowRetiredMenu: true);
    }

    internal void RetireStartMenuFields(FalloutStandaloneInterfaceObject menu)
    {
        RequireOwnerThread(); RequireMenuFieldIdle(); var row = MenuFields(menu.Identity);
        if (row.Process != _process || row.Closed is null || row.Root is not null)
            throw new InvalidOperationException("Native menu retirement must follow its real source close prefix.");
        if (row.Retired is not null) return;
        SetMenuFields(row with { Retired = Next() });
        var index = _objects.FindIndex(item => item.Identity == menu.Identity);
        _objects[index] = _objects[index] with { Retired = Next() }; _native.Remove(menu.Identity);
        _currentOrdinaryMenus.Remove(row.ClassCode);
    }

    internal void RetireFailedStartMenuFields(FalloutStandaloneInterfaceObject menu)
    {
        RequireOwnerThread(); RequireMenuFieldIdle(); var row = MenuFields(menu.Identity);
        if (row.Process != _process) throw new InvalidOperationException("Menu cleanup changed its actual current process.");
        if (row.Retired is not null) return;
        SetMenuFields(row with { Retired = Next() });
        var index = _objects.FindIndex(item => item.Identity == menu.Identity);
        _objects[index] = _objects[index] with { Retired = Next() }; _native.Remove(menu.Identity);
        _ordinaryMenuRoots.Remove(menu.Identity); _currentOrdinaryMenus.Remove(row.ClassCode);
        // The original failed/pending queue and field values stay captured.
    }
    // A native census cannot publish a menu. This check permits only objects
    // whose actual native projection and source field/root constructor exist.
    internal bool HasCurrentNativeMenu(uint code, ulong nativeInstance)
    {
        // This read is also used by the native lifetime callback. It must
        // remain a pure census guard and cannot reenter source mutation.
        if (_thread is { } thread && thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Menu census changed its actual owner thread.");
        return _currentOrdinaryMenus.TryGetValue(code, out var current) && current.Native == nativeInstance &&
            _ordinaryMenus.TryGetValue(current.Object, out var row) && row.Process == _process && row.Retired is null;
    }

    internal void RequireMenuFieldChild(FalloutStandaloneMenuFieldInvocation call, FalloutStandaloneMenuFieldStep step)
    {
        RequireOwnerThread();
        if (!ReferenceEquals(call.Owner, this) || !ReferenceEquals(_ordinaryMenuInvocation, call) ||
            call.Process != _process || call.Step != step || _ordinaryMenuWrites.Count == 0 ||
            _ordinaryMenuWrites[^1].Invocation != call.Identity || _ordinaryMenuWrites[^1].Returned)
        {
            ++_ordinaryMenuReentry;
            throw new InvalidOperationException("Menu child lost its actual source invocation/thread/field step.");
        }
        _requireMenuLifecycle!();
    }

    private FalloutStandaloneMenuFieldWrite ExecuteMenuField(Guid identity, FalloutStandaloneInterfaceObject menu,
        FalloutStandaloneMenuFieldOperation operation, Action<FalloutStandaloneMenuFieldInvocation> action, bool allowRetiredMenu = false)
    {
        RequireCurrent(); RequireMenuLifecycle(); RequireMenuFieldIdle();
        if (_firstOrdinaryMenuFailure is { } failed) throw new NotSupportedException("Source menu retains a failed writer: " + failed);
        var row = MenuFields(menu.Identity);
        if (identity == Guid.Empty || !_ordinaryMenuInvocations.Add(identity) || row.Process != _process || !allowRetiredMenu && row.Retired is not null ||
            !_objects.Any(item => item.Identity == menu.Identity && item.Native == menu.Native && item.Process == _process &&
                (allowRetiredMenu || item.Retired is null)) ||
            !allowRetiredMenu && (!_native.TryGetValue(menu.Identity, out var living) || !ReadNative(living)))
            throw new InvalidOperationException("Menu field writer replayed or changed its actual current source/native lifetime.");
        var call = new FalloutStandaloneMenuFieldInvocation(this, identity, menu.Identity) { Step = FalloutStandaloneMenuFieldStep.Constructor };
        var entered = Next(); var faults = _ordinaryMenuReentry;
        _ordinaryMenuWrites.Add(new(identity, menu.Identity, _process, _menuLifecycleSource!.Identity, operation,
            entered, entered, call.Step, _slots.ToArray(), _slots.ToArray(), _mode, _mode, _context,
            null, null, null, false, false, null, BeforeFields: row, AfterFields: row));
        _ordinaryMenuInvocation = call;
        try
        {
            action(call); call.Require(call.Step);
            if (_ordinaryMenuReentry != faults || !allowRetiredMenu &&
                (!_native.TryGetValue(menu.Identity, out var returnedLiving) || !ReadNative(returnedLiving)))
                throw new InvalidOperationException("Menu child swallowed reentry or lost its actual native factory.");
            Step(call, FalloutStandaloneMenuFieldStep.Returned);
            _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _firstOrdinaryMenuFailure ??= error.Message;
            _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { Failure = error.Message, Changed = Next() };
            throw;
        }
        finally { _ordinaryMenuInvocation = null; }
        return _ordinaryMenuWrites[^1];
    }

    private void CommitStack(FalloutStandaloneMenuStackResult result)
    {
        result.Slots.ToArray().CopyTo(_slots, 0); Next(); _mode = result.Mode; Next();
        _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with
        { AfterSlots = _slots.ToArray(), AfterMode = _mode, Result = result.Result, ImageMenuByte = result.ImageMenuByte, Changed = Next() };
    }
    private void StoreMenuImage(FalloutStandaloneMenuFieldInvocation call, byte? value)
    {
        if (value is not { } supplied) return;
        var image = _menuLifecycleImages!.StoreSourceMenuStackByte(call, supplied); RecordMenuImage(image);
        call.Require(call.Step);
        _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { ImageReturned = true, Changed = Next() };
    }
    private void RecordMenuImage(FalloutSourceMenuImageWrite image)
    {
        var writes = (_ordinaryMenuWrites[^1].ImageWrites ?? []).Append(image).ToArray();
        _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { ImageWrites = writes, Changed = Next() };
    }
    internal bool IsStartMenuTransitionReturned(FalloutStandaloneInterfaceObject menu)
    {
        RequireOwnerThread(); return !HasMenuFieldTransition(menu.Identity);
    }
    private void Step(FalloutStandaloneMenuFieldInvocation call, FalloutStandaloneMenuFieldStep step)
    {
        call.Step = step; _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { Step = step, Changed = Next() };
    }
    private FalloutStandaloneMenuFields MenuFields(Guid menu) => _ordinaryMenus.TryGetValue(menu, out var value) ? value :
        throw new InvalidOperationException("Source menu has no actual inherited field constructor.");
    private void SetMenuFields(FalloutStandaloneMenuFields row)
    {
        var stored = row with { Changed = Next() }; _ordinaryMenus[row.Object] = stored;
        if (_ordinaryMenuInvocation is { } active && active.Object == row.Object)
            _ordinaryMenuWrites[^1] = _ordinaryMenuWrites[^1] with { AfterFields = stored, Changed = Next() };
    }
    private (FalloutUiComponentStore Ui, FalloutUiComponentBinding Root) MenuRoot(FalloutStandaloneMenuFields row)
    {
        if (row.Root is null || !_ordinaryMenuRoots.TryGetValue(row.Object, out var tree) || tree.Root != row.Root)
            throw new InvalidOperationException("Menu source field lost its actual current C# UI root.");
        tree.Ui.RequireComponent(tree.Root); return tree;
    }
    private void RequireMenuLifecycle()
    {
        if (_menuLifecycleSource is null || _requireMenuLifecycle is null || _menuLifecycleImages is null ||
            _menuLifecycleInput is null || _menuLifecycleThread is null || _readMenuFrame is null)
            throw new NotSupportedException("source-menu-current-lifecycle-input-image-thread-owner-unbound");
        _requireMenuLifecycle();
    }
    private void RequireMenuFieldIdle()
    {
        if (_ordinaryMenuInvocation is not null)
        {
            ++_ordinaryMenuReentry;
            throw new InvalidOperationException("Source menu field writer cannot reenter construction, capture or another writer.");
        }
    }

    internal bool HasMenuFieldTransition(Guid menu)
    {
        RequireOwnerThread();
        return _menuTransitions.Any(row => row.Menu == menu);
    }
}
