using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandaloneInterfaceState
{
    internal FalloutStandaloneInterfaceSnapshot Capture()
    {
        RequireOwnerThread();
        if (_enabled != 0 && (_manager is null || !_native.TryGetValue(_manager.Identity, out var living) || !ReadNative(living)))
            throw new NotSupportedException("Current interface cannot capture a restored/dead native manager as a living factory return.");
        if (_objects.Any(item => item.Process == _process && item.Retired is null &&
            (!_native.TryGetValue(item.Identity, out var itemLiving) || !ReadNative(itemLiving))))
            throw new NotSupportedException("Current interface object omitted an actual native retirement callback.");
        var snapshot = new FalloutStandaloneInterfaceSnapshot(Schema, Source, _stack, _process, _constructor, _sequence,
            _enabled, _mode, _context, _slots.ToArray(), _active.ToArray(), _dialogByte, _console, _consoleCounter,
            _manager, _managerFields, _dialog, _objects.ToArray(), _alternate, _cold, _boundary, _failure, _retired, CaptureDialogMenus());
        Validate(snapshot); return snapshot;
    }
    internal static void Validate(FalloutStandaloneInterfaceSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Source is null || string.IsNullOrWhiteSpace(saved.Stack) ||
            saved.CapturedProcess == Guid.Empty || saved.Constructor == Guid.Empty || saved.Sequence < 0 || saved.Enabled > 1 || saved.DialogByte > 1 ||
            saved.MenuSlots is not { Count: 10 } || saved.ActiveMenus is not { Count: 60 } || saved.Objects is null ||
            saved.Objects.Any(item => item is null || item.Native is null) ||
            saved.Objects.Select(item => item.Identity).Distinct().Count() != saved.Objects.Count ||
            saved.Objects.Select(item => item.Native.Factory).Distinct().Count() != saved.Objects.Count ||
            !Enum.IsDefined(saved.Console) || saved.Console == FalloutConsolePresence.Present != (saved.ConsoleCounter is not null) ||
            saved.Boundary is not null && string.IsNullOrWhiteSpace(saved.Boundary) ||
            saved.Failure is not null && string.IsNullOrWhiteSpace(saved.Failure))
            throw new InvalidDataException("Standalone interface omitted a complete source constructor/current object lifetime.");
        saved.Source.Validate();
        // Original stack writes preserve entered failures independently of
        // quiet construction. Active bytes/context/console have separate writers.
        if (saved.Context != 0 ||
            saved.Alternate is not null || saved.Console != FalloutConsolePresence.Absent || saved.ConsoleCounter is not null)
            throw new NotSupportedException("Current interface retains an unowned original menu/context/console writer.");
        foreach (var item in saved.Objects)
        {
            var native = item.Native;
            if (item.Identity == Guid.Empty || item.Process == Guid.Empty ||
                item.Role is not (FalloutStandaloneInterfaceObjectRole.PipBoyManager or FalloutStandaloneInterfaceObjectRole.DialogMenu) || item.Role != native.Role ||
                item.Constructed < 1 || item.Constructed > saved.Sequence || native.Factory == Guid.Empty || native.Process != item.Process ||
                native.Source != saved.Source.Identity || native.Stack != saved.Stack || native.NativeInstance == 0 ||
                !FalloutAdvancementRuntimeReceipt.Digest(native.ResourceSha256) || string.IsNullOrWhiteSpace(native.Owner) ||
                native.Resource != (item.Role == FalloutStandaloneInterfaceObjectRole.DialogMenu ? "menus/dialog/dialog_menu.xml" : "menus/main/hud_main_menu.xml") ||
                item.Closed is { } closed && (closed <= item.Constructed || closed > saved.Sequence) ||
                item.Retired is { } retired && (retired <= item.Constructed || retired > saved.Sequence || item.Closed is { } end && end > retired) ||
                item.Role == FalloutStandaloneInterfaceObjectRole.PipBoyManager && item.Closed is not null)
                throw new InvalidDataException("Standalone interface retained a false factory/class/resource or closure prefix.");
        }
        ValidateDialogMenus(saved);
        RequireObject(saved.OwnManager, FalloutStandaloneInterfaceObjectRole.PipBoyManager);
        RequireObject(saved.Dialog, FalloutStandaloneInterfaceObjectRole.DialogMenu);
        if ((saved.OwnManager is null) != (saved.ManagerFields is null) || saved.Enabled != 0 && saved.OwnManager is not { Retired: null } ||
            saved.DialogByte != 0 && saved.Dialog is not { Closed: null, Retired: null } ||
            saved.Dialog is { Closed: null, Retired: null } && saved.DialogByte != 1 || saved.Retired && saved.Enabled != 0)
            throw new InvalidDataException("Interface flags do not belong to their actual current manager/dialog lifetime.");
        if (saved.ManagerFields is { } fields && (fields.Selection != -1 || fields.FirstFlag != 0 || fields.SecondFlag != 0 ||
            fields.ThirdFlag != 0 || fields.Word != 0 || fields.FinalFlag != 0 || fields.BaseFlag != 1 || fields.FinalWord != 0 ||
            fields.References is not { Count: 13 } || fields.References.Any(value => value is not null) ||
            fields.Words is not { Count: 9 } || fields.Words.Any(value => value != 0)))
            throw new NotSupportedException("Pip-Boy manager fields changed without the original constructor/writer owner.");
        if (saved.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != saved.CapturedProcess ||
            cold.PreviousProcess == cold.CurrentProcess || cold.PreviousConstructor == Guid.Empty || cold.CurrentConstructor != saved.Constructor ||
            cold.PreviousConstructor == cold.CurrentConstructor || cold.Changed < 1 || cold.Changed > saved.Sequence))
            throw new InvalidDataException("Interface cold handoff restored a process-local native/constructor identity.");
        void RequireObject(FalloutStandaloneInterfaceObject? item, FalloutStandaloneInterfaceObjectRole role)
        {
            if (item is not null && (item.Process != saved.CapturedProcess || item.Role != role || !saved.Objects.Contains(item)))
                throw new InvalidDataException("Current interface pointer refers to foreign or omitted object history.");
        }
    }
    internal static void RequirePlayable(FalloutStandaloneInterfaceSnapshot saved)
    {
        Validate(saved);
        if (saved.Retired || saved.Enabled != 1 || saved.OwnManager is not { Retired: null } || saved.Dialog is { Retired: null } ||
            saved.DialogByte != 0 || saved.Boundary is not null || saved.Failure is not null || saved.Mode != 1 ||
            saved.MenuSlots.Any(value => value != 0) || saved.Menus.Transitions.Count != 0 ||
            saved.Menus.Dialogs.Any(row => row.Retired is null))
            throw new NotSupportedException("Playable interface capture requires its genuine returned manager and completed native menu lifetimes.");
    }
    private void Restore(FalloutStandaloneInterfaceSnapshot saved)
    {
        RequirePlayable(saved);
        if (saved.Source != Source || saved.Stack != _stack || saved.CapturedProcess == _process || saved.Constructor == _constructor)
            throw new InvalidDataException("Cold interface changed the selected source or reused an old process/constructor.");
        _sequence = saved.Sequence; _objects.AddRange(saved.Objects); RestoreDialogMenus(saved.Menus);
        // All old native identities remain historical. The logical constructor
        // values agree, but no old native pointer/lease becomes a current one.
        _cold = new(saved.CapturedProcess, _process, saved.Constructor, _constructor, Next());
        _enabled = saved.Enabled; _mode = saved.Mode; _context = saved.Context;
    }
}
