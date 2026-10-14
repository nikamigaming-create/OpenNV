using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandaloneInterfaceState
{
    private readonly List<FalloutStandaloneInterfaceColdHandoff> _ordinaryMenuColdConstructors = [];
    private readonly Dictionary<Guid, FalloutOriginalInputSnapshot> _ordinaryMenuInputs = [];
    private bool HasCurrentSourcePauseMenu => _mode == 3 && _slots[0] == FalloutStandaloneMenuLifecycleSource.StartMenu &&
        _slots.Skip(1).All(value => value == 0) && _ordinaryMenus.Values.Any(row => row.Process == _process &&
            row.ClassCode == _slots[0] && row.Root is not null && row.Closed is null && row.Retired is null &&
            row.CloseReferenceBits is null && row.Visible is { } visible && FalloutStandaloneMenuLifecycleSource.Visible(visible));
    private string? OrdinaryMenuSaveBlocker => _ordinaryMenuInvocation is not null ? "source-menu-field-child-entered" :
        _firstOrdinaryMenuFailure;

    private object MenuFieldSummary => new
    {
        source = _menuLifecycleSource?.Identity,
        process = _process,
        sequence = _sequence,
        menuCount = _ordinaryMenus.Count,
        writeCount = _ordinaryMenuWrites.Count,
        coldConstructorCount = _ordinaryMenuColdConstructors.Count,
        lastWrite = _ordinaryMenuWrites.Count == 0 ? null : _ordinaryMenuWrites[^1],
        lastMenu = _ordinaryMenuWrites.Count == 0 ? null : _ordinaryMenus[_ordinaryMenuWrites[^1].Object],
        historyIncluded = false
    };

    private FalloutStandaloneMenuFieldsSnapshot CaptureMenuFields()
    {
        if (_menuLifecycleInput is { } input) _ordinaryMenuInputs[_process] = input.CaptureSourceInput();
        return new((_menuLifecycleSource ?? FalloutStandaloneMenuLifecycleSource.Reviewed(Source)).Identity, _process, _sequence,
            _ordinaryMenus.Values.OrderBy(row => row.Constructed).ToArray(), _ordinaryMenuWrites.ToArray(), _ordinaryMenuColdConstructors.ToArray(),
            _ordinaryMenuInputs.Select(row => new FalloutStandaloneMenuInputLifetime(row.Key, row.Value)).ToArray());
    }

    private void RestoreMenuFields(FalloutStandaloneMenuFieldsSnapshot? saved)
    {
        if (saved is null) return;
        _firstOrdinaryMenuFailure = saved.Writes.FirstOrDefault(row => row.Failure is not null)?.Failure;
        _ordinaryMenuColdConstructors.AddRange(saved.ColdConstructors);
        foreach (var input in saved.Inputs) _ordinaryMenuInputs.Add(input.Process, input.State);
        foreach (var row in saved.Menus) _ordinaryMenus.Add(row.Object, row);
        foreach (var write in saved.Writes)
        {
            _ordinaryMenuInvocations.Add(write.Invocation);
            _ordinaryMenuWrites.Add(write with { BeforeSlots = write.BeforeSlots.ToArray(), AfterSlots = write.AfterSlots.ToArray(), ImageWrites = write.ImageWrites?.ToArray() });
        }
        // Historical roots/native objects remain values from their old
        // process. The fresh Interface constructor owns an empty current queue.
        for (var node = _menuTransitions.First; node is not null;)
        {
            var next = node.Next;
            if (_ordinaryMenus.ContainsKey(node.Value.Menu)) _menuTransitions.Remove(node);
            node = next;
        }
    }

    private static void ValidateMenuFields(FalloutStandaloneInterfaceSnapshot saved)
    {
        var source = FalloutStandaloneMenuLifecycleSource.Reviewed(saved.Source);
        var data = saved.MenuFields ?? throw new InvalidDataException("Interface omitted the general menu-field constructor/history.");
        if (data.Source != source.Identity || data.Process != saved.CapturedProcess || data.Sequence != saved.Sequence ||
            data.Menus is null || data.Writes is null || data.ColdConstructors is null || data.Inputs is null)
            throw new InvalidDataException("Menu fields changed their actual source/process capture.");
        var inputProcesses = new HashSet<Guid>(); var inputFactories = new HashSet<Guid>();
        var inputSource = FalloutOriginalInputSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(saved.Source.Main));
        foreach (var input in data.Inputs)
        {
            if (input.Process == Guid.Empty || !inputProcesses.Add(input.Process) || !inputFactories.Add(input.State.Factory))
                throw new InvalidDataException("Menu input history reused an actual manager/process lifetime.");
            FalloutOriginalInputValidation.ValidateSnapshot(inputSource, saved.Stack, input.State);
        }
        var objects = saved.Objects.ToDictionary(row => row.Identity);
        var menus = new Dictionary<Guid, FalloutStandaloneMenuFields>(); long constructed = 0;
        foreach (var row in data.Menus)
        {
            if (row.Object == Guid.Empty || !menus.TryAdd(row.Object, row) || row.Process == Guid.Empty || row.Source != source.Identity ||
                row.ClassCode != FalloutStandaloneMenuLifecycleSource.StartMenu || row.Identity != 0 && row.Identity != row.ClassCode ||
                row.Flags != FalloutStandaloneMenuLifecycleSource.ConstructorFlags || row.Lifecycle is not (1 or 2 or 4 or 8) ||
                row.Constructed <= constructed || row.Changed < row.Constructed || row.Changed > saved.Sequence ||
                !objects.TryGetValue(row.Object, out var actual) || actual.Role != FalloutStandaloneInterfaceObjectRole.OtherMenu ||
                actual.Process != row.Process || row.Constructed <= actual.Constructed ||
                row.RegisteredCode is { } registered && registered != row.ClassCode ||
                row.Root is { } root && (root.Factory == Guid.Empty || root.MenuInstance == Guid.Empty || root.Epoch < 0 || string.IsNullOrWhiteSpace(root.Path)) ||
                row.CloseReferenceBits is not null and not 0x3f800000 ||
                row.Closed is { } closed && (closed <= row.Constructed || closed > row.Changed || row.Root is not null) ||
                row.Retired is { } retired && (row.Closed is { } closedAt && retired < closedAt || retired > row.Changed || actual.Retired is null))
                throw new InvalidDataException("Menu fields lost their inherited constructor, actual class/root or close/retire prefix.");
            constructed = row.Constructed;
        }
        var currentFields = data.Menus.ToDictionary(row => row.Object, row => row with
        { Identity = 0, Lifecycle = 4, Root = null, Visible = null, Closed = null, Retired = null, RegisteredCode = null, CloseReferenceBits = null, Changed = row.Constructed });
        var ids = new HashSet<Guid>(); long previous = 0;
        foreach (var write in data.Writes)
        {
            if (write.Invocation == Guid.Empty || !ids.Add(write.Invocation) || !menus.TryGetValue(write.Object, out var row) ||
                write.Process != row.Process || write.Source != source.Identity || write.Entered <= previous ||
                write.Entered <= row.Constructed || write.Changed < write.Entered || write.Changed > saved.Sequence ||
                write.BeforeSlots is not { Count: 10 } || write.AfterSlots is not { Count: 10 } ||
                !Enum.IsDefined(write.Operation) || !Enum.IsDefined(write.Step) || write.Context != 0 ||
                write.Returned != (write.Step == FalloutStandaloneMenuFieldStep.Returned) ||
                write.Returned && write.Failure is not null || !write.Returned && string.IsNullOrWhiteSpace(write.Failure) ||
                write.ImageMenuByte is null && write.ImageReturned || write.ImageMenuByte is > 1 ||
                write.Returned && write.ImageMenuByte is not null && !write.ImageReturned)
                throw new InvalidDataException("Menu writer omitted its real caller order, source fields or failed prefix.");
            if (write.BeforeFields is not { } beforeFields || write.AfterFields is not { } afterFields ||
                !SameFields(beforeFields, currentFields[row.Object]) || beforeFields.Object != row.Object || afterFields.Object != row.Object ||
                afterFields.Process != row.Process || afterFields.Source != source.Identity || afterFields.ClassCode != row.ClassCode ||
                afterFields.Flags != row.Flags || afterFields.Constructed != row.Constructed || afterFields.Changed > write.Changed ||
                afterFields.Changed < beforeFields.Changed)
                throw new InvalidDataException("Menu field receipt changed its actual constructor/current field lineage.");
            currentFields[row.Object] = afterFields;
            if (write.Allocation is { } scope)
            {
                if (scope.Invocation != write.Invocation || scope.Process == Guid.Empty || scope.OwnerFactory == Guid.Empty ||
                    scope.EnteredKind != 13 || scope.Initialized != 1 || scope.Returned && scope.RestoredKind != scope.BeforeKind ||
                    write.Returned && !scope.Returned || write.Operation == FalloutStandaloneMenuFieldOperation.ReplaceTransition && write.Duration is null)
                    throw new InvalidDataException("Menu transition lost its genuine source kind13 allocation/return.");
            }
            if (write.InputClose is { } input && (input.Invocation != write.Invocation || input.InterfaceProcess != write.Process ||
                input.Menu != write.Object || input.InputFactory == Guid.Empty || input.Stack != saved.Stack ||
                input.Source != FalloutOriginalInputSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(saved.Source.Main)).Identity ||
                write.Returned && !input.Returned || input.Observations is null || input.Count is < 0 or > 2))
                throw new InvalidDataException("Menu close changed its actual input-manager query receipt.");
            if (write.MouseFlush is { } flush && (flush.Invocation != write.Invocation || flush.InterfaceProcess != write.Process ||
                flush.Menu != write.Object || flush.Stack != saved.Stack || flush.InputFactory == Guid.Empty || !flush.Returned ||
                flush.SourceElementExtent != 20 || flush.Present != (flush.NativeDeviceFactory is not null)))
                throw new InvalidDataException("Menu quiet return lost its genuine nullable native mouse-buffer child.");
            if (write.Returned && write.Operation == FalloutStandaloneMenuFieldOperation.QuietReturn && write.MouseFlush is null ||
                write.Returned && write.Operation is FalloutStandaloneMenuFieldOperation.ForceRemove or FalloutStandaloneMenuFieldOperation.NormalRemove &&
                FalloutStandaloneMenuStack.Top(write.AfterSlots) == 0 && write.InputClose is null)
                throw new InvalidDataException("Returned menu operation omitted its reached input child.");
            if (write.ImageWrites is { } images)
                foreach (var image in images)
                    if (image.Invocation != write.Invocation || image.Menu != write.Object || image.Process != write.Process ||
                        image.Factory == Guid.Empty || image.Interface == Guid.Empty || !Enum.IsDefined(image.Field) ||
                        image.Field == FalloutSourceMenuImageField.Enabled && (image.Enabled is null or > 1 || image.Progress is not null) ||
                        image.Field == FalloutSourceMenuImageField.Progress && (image.Enabled is not null || image.Progress is null))
                        throw new InvalidDataException("Menu child changed its independently owned actual image write.");
            if (write.ImageReturned && write.ImageMenuByte is { } imageByte &&
                !(write.ImageWrites ?? []).Any(image => image.Field == FalloutSourceMenuImageField.Enabled && image.Enabled == imageByte))
                throw new InvalidDataException("Menu declared an image-byte return without the actual reached field write.");
            if (write.Operation == FalloutStandaloneMenuFieldOperation.ReplaceTransition && write.Returned && write.Allocation is null ||
                write.Operation == FalloutStandaloneMenuFieldOperation.AdvanceTransition && write.Returned && (write.Allocation is null || write.Frame is null))
                throw new InvalidDataException("Returned menu transition omitted its actual thread/current timer child.");
            previous = write.Changed;
        }
        foreach (var row in data.Menus)
            if (!SameFields(row, currentFields[row.Object]))
                throw new InvalidDataException("Current menu field value has no matching actual source writer.");
        previous = 0;
        var processes = new HashSet<Guid>();
        foreach (var cold in data.ColdConstructors)
        {
            if (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess == Guid.Empty || cold.PreviousProcess == cold.CurrentProcess ||
                cold.PreviousConstructor == Guid.Empty || cold.CurrentConstructor == Guid.Empty || cold.PreviousConstructor == cold.CurrentConstructor ||
                cold.Changed <= previous || cold.Changed > saved.Sequence || !processes.Add(cold.CurrentProcess))
                throw new InvalidDataException("Menu cold history replayed or reused its actual fresh Interface constructor.");
            previous = cold.Changed;
        }
        if (data.ColdConstructors.LastOrDefault() != saved.ColdHandoff)
            throw new InvalidDataException("Menu cold boundary differs from its actual Interface constructor handoff.");
    }

    private static bool SameFields(FalloutStandaloneMenuFields first, FalloutStandaloneMenuFields second) =>
        first.Object == second.Object && first.Process == second.Process && first.Source == second.Source &&
        first.ClassCode == second.ClassCode && first.Identity == second.Identity && first.Lifecycle == second.Lifecycle &&
        first.Flags == second.Flags && first.Constructed == second.Constructed && first.Root == second.Root &&
        first.Visible == second.Visible && first.RegisteredCode == second.RegisteredCode && first.CloseReferenceBits == second.CloseReferenceBits;

    // Legacy Dialog and general Menu writers mutate the same ten fields. A
    // single chronological value walk also handles the fresh cold constructor.
    private static void ValidateCombinedMenuStack(FalloutStandaloneInterfaceSnapshot saved)
    {
        var slots = new uint[10]; var active = new byte[60]; uint mode = 1;
        var ordinary = saved.MenuFields!.Writes.ToDictionary(row => row.Changed);
        var dialogRows = saved.Menus.Dialogs.ToDictionary(row => row.Object);
        var dialogs = new Dictionary<long, FalloutStandaloneMenuStackWrite>();
        var pushedDialogs = new HashSet<Guid>(); long previousDialogWrite = 0;
        foreach (var write in saved.Menus.StackWrites)
        {
            if (!dialogRows.TryGetValue(write.Dialog, out var row) || !pushedDialogs.Add(write.Dialog) ||
                write.Changed <= previousDialogWrite || write.Changed <= row.Constructed || write.Changed > row.Changed ||
                write.Changed > saved.Sequence || write.Before is not { Count: 10 } || write.After is not { Count: 10 } ||
                write.Slot is < -1 or > 9 || row.Root is null || row.MenuIdentity != FalloutStandaloneDialogSource.MenuId ||
                !FalloutStandaloneDialogSource.Registers(row.Declaration.StackingBits) ||
                write.Returned && write.Failure is not null ||
                !write.Returned && (!write.RequiresSceneByte || string.IsNullOrWhiteSpace(write.Failure) ||
                    write.Failure != row.Failure || saved.Failure is null ||
                    row.Step is not (FalloutStandaloneDialogStep.SceneByteEntered or FalloutStandaloneDialogStep.Retired)) ||
                write.Returned && write.RequiresSceneByte && row.Step == FalloutStandaloneDialogStep.SceneByteEntered ||
                write.Slot == -1 && (!write.Returned || write.RequiresSceneByte))
                throw new InvalidDataException("Dialog stack operation lost its actual constructor, scene child or retained failed return.");
            if (!dialogs.TryAdd(write.Changed, write))
                throw new InvalidDataException("Dialog stack operations reused a source mutation boundary.");
            previousDialogWrite = write.Changed;
        }
        var ordinaryRows = saved.MenuFields.Menus.ToDictionary(row => row.Object);
        var cold = saved.MenuFields.ColdConstructors.ToDictionary(row => row.Changed);
        var order = ordinary.Keys.Concat(dialogs.Keys).Concat(cold.Keys).Order().ToArray();
        if (order.Distinct().Count() != order.Length) throw new InvalidDataException("Menu source writes share an impossible mutation boundary.");
        foreach (var changed in order)
        {
            if (cold.ContainsKey(changed)) { Array.Clear(slots); Array.Clear(active); mode = 1; continue; }
            if (dialogs.TryGetValue(changed, out var dialog))
            {
                var row = dialogRows.TryGetValue(dialog.Dialog, out var found) ? found :
                    throw new InvalidDataException("Dialog stack writer has no source menu.");
                if (!dialog.Before.SequenceEqual(slots) || dialog.BeforeMode != mode)
                    throw new InvalidDataException("Dialog stack writer lost the shared current source fields.");
                var pushed = FalloutStandaloneMenuStack.Push(slots, mode, row.MenuIdentity);
                if (!dialog.After.SequenceEqual(pushed.Slots) || dialog.AfterMode != pushed.Mode || dialog.Slot != pushed.Result ||
                    dialog.RequiresSceneByte != (pushed.ImageMenuByte is not null))
                    throw new InvalidDataException("Dialog push changed its source first-empty slot or dependent image arm.");
                slots = pushed.Slots.ToArray(); mode = pushed.Mode; continue;
            }
            var write = ordinary[changed];
            if (!write.BeforeSlots.SequenceEqual(slots) || write.BeforeMode != mode)
                throw new InvalidDataException("General menu writer lost the same current shared Interface words.");
            var menu = ordinaryRows[write.Object];
            var expected = write.Operation switch
            {
                FalloutStandaloneMenuFieldOperation.BindRoot when write.Result is not null => FalloutStandaloneMenuStack.Push(slots, mode, menu.ClassCode),
                FalloutStandaloneMenuFieldOperation.ForceRemove when write.Result is not null => FalloutStandaloneMenuStack.Remove(slots, mode, menu.Identity, true),
                FalloutStandaloneMenuFieldOperation.NormalRemove when write.Result is not null => FalloutStandaloneMenuStack.Remove(slots, mode, menu.Identity, false),
                _ => new FalloutStandaloneMenuStackResult(slots, mode, 0, null, false)
            };
            var expectedMode = write.Operation == FalloutStandaloneMenuFieldOperation.QuietReturn && write.Returned ? 1u : expected.Mode;
            if (!write.AfterSlots.SequenceEqual(expected.Slots) || write.AfterMode != expectedMode ||
                write.Result is not null && write.Result != expected.Result || write.ImageMenuByte != expected.ImageMenuByte)
                throw new InvalidDataException("Menu writer changed its source stack/mode result or dependent image byte.");
            slots = expected.Slots.ToArray(); mode = expectedMode;
            if (write.Operation is FalloutStandaloneMenuFieldOperation.StoreVisible or FalloutStandaloneMenuFieldOperation.AdvanceTransition && write.Visible is { } visible)
                active[checked((int)menu.ClassCode - 1001)] = FalloutStandaloneMenuLifecycleSource.Visible(visible) ? (byte)1 : (byte)0;
            if (write.Operation is FalloutStandaloneMenuFieldOperation.ForceRemove or FalloutStandaloneMenuFieldOperation.NormalRemove &&
                write.Step is FalloutStandaloneMenuFieldStep.Remove or FalloutStandaloneMenuFieldStep.InputClose or FalloutStandaloneMenuFieldStep.Returned)
                active[checked((int)menu.ClassCode - 1001)] = 0;
        }
        if (!saved.MenuSlots.SequenceEqual(slots) || saved.Mode != mode || !saved.ActiveMenus.SequenceEqual(active))
            throw new InvalidDataException("Interface fields have no matching complete source writer history.");
    }

    internal static void RequireSaving(FalloutStandaloneInterfaceSnapshot saved)
    {
        Validate(saved);
        var current = saved.MenuFields!.Menus.Where(row => row.Process == saved.CapturedProcess && row.Retired is null).ToArray();
        if (saved.Retired || saved.Enabled != 1 || saved.OwnManager is not { Retired: null } || saved.Dialog is { Retired: null } ||
            saved.DialogByte != 0 || saved.Boundary is not null || saved.Failure is not null || saved.ActorDistances?.Failure is not null ||
            saved.HudControl is not { Available: true } || saved.HudControl.Call?.Failure is not null ||
            saved.MenuFields.Writes.Any(row => row.Failure is not null) || current.Length > 1 ||
            current.Length == 0 && (saved.Mode != 1 || saved.MenuSlots.Any(value => value != 0) || saved.Menus.Transitions.Count != 0) ||
            current.Length == 1 && (saved.Mode != 3 || saved.MenuSlots[0] != current[0].ClassCode ||
                saved.MenuSlots.Skip(1).Any(value => value != 0) || current[0].Root is null || current[0].Closed is not null || current[0].CloseReferenceBits is not null ||
                current[0].Visible is not { } visible || !FalloutStandaloneMenuLifecycleSource.Visible(visible)) ||
            saved.Menus.Dialogs.Any(row => row.Retired is null))
            throw new NotSupportedException("Save requires a returned source Interface/menu prefix and actual current native lifetime.");
    }
}
