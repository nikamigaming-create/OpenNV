using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Authoritative fields for the independently selected source class. Godot
// publishes actual object lifetimes; neither tree pause nor GameMode writes a
// field here. Unknown source children retain their entered prefix.
internal sealed partial class FalloutStandaloneInterfaceState
{
    internal const string Schema = "opennv-standalone-interface/v2";
    internal FalloutStandaloneInterfaceSource Source { get; }
    private readonly string _stack;
    private readonly Guid _process, _constructor = Guid.NewGuid();
    private readonly uint[] _slots = new uint[10];
    private readonly byte[] _active = new byte[60];
    private readonly List<FalloutStandaloneInterfaceObject> _objects = [];
    private readonly Dictionary<Guid, Func<bool>> _native = [];
    private FalloutStandaloneInterfaceObject? _manager, _dialog;
    private FalloutStandalonePipBoyManagerFields? _managerFields;
    private FalloutStandaloneInterfaceColdHandoff? _cold;
    private Guid? _alternate;
    private long _sequence;
    private int? _thread;
    private byte _enabled, _dialogByte;
    private uint _mode = 1, _context;
    private FalloutConsolePresence _console = FalloutConsolePresence.Absent;
    private sbyte? _consoleCounter = null;
    private string? _boundary, _failure;
    private bool _retired, _readingNative, _nativeReadRefused;
    internal Guid Process => _process;
    internal bool Retired => _retired;
    internal string? SaveBlocker => _retired ? "source-interface-retired" : _failure ?? _boundary ??
        DialogSaveBlocker ?? (_manager is null || _enabled == 0 ? "source-interface-native-manager-factory-not-returned" : null);
    internal object State => new
    {
        source = Source.Identity,
        _process,
        constructor = _constructor,
        _sequence,
        enabled = _enabled,
        mode = _mode,
        context = _context,
        dialog = _dialogByte,
        console = _console,
        ownManager = _manager,
        alternate = _alternate,
        menuSlots = _slots.ToArray(),
        activeMenus = _active.ToArray(),
        managerFields = _managerFields,
        menus = CaptureDialogMenus(),
        cold = _cold,
        boundary = _boundary,
        failure = _failure,
        retired = _retired
    };

    internal FalloutStandaloneInterfaceState(FalloutStandaloneInterfaceSource source, string stack, Guid process,
        FalloutStandaloneInterfaceSnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty) throw new ArgumentException("Interface requires the actual current Main process.", nameof(process));
        Source = source; _stack = stack; _process = process;
        if (restore is not null) Restore(restore);
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private void RequireCurrent(bool native = true)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_readingNative)
        {
            _nativeReadRefused = true;
            throw new InvalidOperationException("A native lifetime read cannot reenter source interface ownership.");
        }
        if (_thread is { } thread && thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Interface publication/query/retirement changed its actual presentation thread.");
        if (_failure is not null) throw new InvalidOperationException(_failure);
        if (_boundary is not null) throw new NotSupportedException(_boundary);
        if (native && _enabled != 0)
        {
            if (_manager is null || !_native.TryGetValue(_manager.Identity, out var living) || !ReadNative(living))
                throw new InvalidOperationException("The source own-menu manager has no current living native publication.");
        }
        if (native && _dialog is { Retired: null } dialog &&
            (!_native.TryGetValue(dialog.Identity, out var dialogLiving) || !ReadNative(dialogLiving)))
            throw new InvalidOperationException("Source dialog retains a missing native lifetime/retirement callback.");
    }
    internal FalloutStandaloneInterfaceObject PublishManager(FalloutStandaloneInterfaceNativePublication publication,
        Func<bool> livingNative)
    {
        RequireCurrent(native: false); ValidatePublication(publication, FalloutStandaloneInterfaceObjectRole.PipBoyManager);
        ArgumentNullException.ThrowIfNull(livingNative);
        if (_manager is not null || _native.Count != 0 || !ReadNative(livingNative))
            throw new InvalidOperationException("Source manager must be the returned fresh living factory object.");
        _thread = Environment.CurrentManagedThreadId;
        _managerFields = FalloutStandalonePipBoyManagerFields.Constructed;
        var manager = new FalloutStandaloneInterfaceObject(Guid.NewGuid(), _process, publication.Role, Next(), null, null, publication);
        _objects.Add(manager); _native.Add(manager.Identity, livingNative);
        _manager = manager; // The source stores the actual returned manager first.
        _enabled = 1; Next();
        return manager;
    }
    internal FalloutStandaloneInterfaceObject ConstructDialog(FalloutStandaloneInterfaceNativePublication publication,
        Func<bool> livingNative)
    {
        RequireCurrent(); ValidatePublication(publication, FalloutStandaloneInterfaceObjectRole.DialogMenu);
        ArgumentNullException.ThrowIfNull(livingNative);
        if (_dialog is { Retired: null } || !ReadNative(livingNative))
            throw new InvalidOperationException("Dialog constructor cannot replace another still-owned native menu.");
        var dialog = new FalloutStandaloneInterfaceObject(Guid.NewGuid(), _process, publication.Role, Next(), null, null, publication);
        _objects.Add(dialog); _native.Add(dialog.Identity, livingNative); _dialog = dialog;
        _dialogByte = 1; Next();
        return dialog;
    }
    internal void CloseDialog(FalloutStandaloneInterfaceObject dialog)
    {
        RequireOwnerThread(); RequireDialog(dialog);
        if (_dialog!.Closed is not null) return;
        _dialogByte = 0; var closed = _dialog with { Closed = Next() }; Replace(closed); _dialog = closed;
    }
    internal void RetireDialog(FalloutStandaloneInterfaceObject dialog)
    {
        RequireOwnerThread(); RequireDialog(dialog);
        if (_dialog!.Retired is not null) return;
        RetireDialogTiles(dialog.Identity);
        _dialogByte = 0; var retired = _dialog with { Retired = Next() }; Replace(retired); _dialog = retired;
        _native.Remove(retired.Identity);
    }
    private void RequireDialog(FalloutStandaloneInterfaceObject dialog)
    {
        if (_dialog is null || dialog.Identity != _dialog.Identity || dialog.Process != _process || dialog.Native != _dialog.Native ||
            dialog.Role != _dialog.Role || dialog.Constructed != _dialog.Constructed)
            throw new InvalidOperationException("Dialog closure does not own the actual current source menu.");
    }
    private void Replace(FalloutStandaloneInterfaceObject item)
    {
        var index = _objects.FindIndex(value => value.Identity == item.Identity);
        if (index < 0) throw new InvalidOperationException("Source object lost its factory history.");
        _objects[index] = item;
    }
    private void RequireOwnerThread()
    {
        if (_readingNative)
        {
            _nativeReadRefused = true;
            throw new InvalidOperationException("A native lifetime reader cannot close, retire or mutate its source owner.");
        }
        if (_thread is { } thread && thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Source interface ownership must retire on its actual presentation thread.");
    }
    private bool ReadNative(Func<bool> read)
    {
        if (_readingNative)
        {
            _nativeReadRefused = true;
            throw new InvalidOperationException("Native interface lifetime observation cannot recurse.");
        }
        _readingNative = true; _nativeReadRefused = false;
        var entered = _sequence;
        try
        {
            var living = read();
            if (_nativeReadRefused || entered != _sequence)
                throw new InvalidOperationException("Native lifetime callback swallowed a source mutation/reentry refusal.");
            return living;
        }
        catch (Exception error) { RetainFailure(error); throw; }
        finally { _readingNative = false; }
    }
    internal int Read(FalloutStandaloneInterfaceQuery query)
    {
        RequireCurrent();
        return query switch
        {
            FalloutStandaloneInterfaceQuery.MenuGate => FalloutStandaloneInterfaceSource.MenuGate(_enabled, _mode) ? 1 : 0,
            FalloutStandaloneInterfaceQuery.GuiModeTwo => FalloutStandaloneInterfaceSource.GuiModeTwo(_context) ? 1 : 0,
            FalloutStandaloneInterfaceQuery.FirstPredicate => _enabled != 0 && _dialogByte != 0 ? 1 : 0,
            FalloutStandaloneInterfaceQuery.FinalPredicate => ReadConsoleOpen() ? 1 : 0,
            FalloutStandaloneInterfaceQuery.ForeignMenu => ReadForeignMenu() ? 1 : 0,
            FalloutStandaloneInterfaceQuery.ContextKind => FalloutStandaloneInterfaceSource.ContextKind(_mode),
            _ => throw new ArgumentOutOfRangeException(nameof(query))
        };
    }
    private bool ReadConsoleOpen()
    {
        if (_enabled == 0 || _console == FalloutConsolePresence.Absent) return false;
        if (_console != FalloutConsolePresence.Present || _consoleCounter is not { } counter)
            throw new NotSupportedException("source-FO3-existing-console-factory-or-counter-unowned");
        return FalloutStandaloneInterfaceSource.ConsoleOpen(counter);
    }
    private bool ReadForeignMenu()
    {
        if (_enabled == 0) return false;
        Guid? selected = FalloutStandaloneInterfaceSource.OwnSelectorCodes.ToArray().Any(code => _active[checked((int)code - 1001)] != 0)
            ? _manager?.Identity : _alternate;
        return selected is not null && selected != _manager?.Identity;
    }
    internal FalloutConsoleActivitySample ReadConsoleActivity(FalloutConsoleActivitySource source)
    {
        RequireCurrent(); source.Validate();
        if (source.EngineSha256 != Source.Main.EngineSha256 || source.RuntimeSha256 != Source.Main.RuntimeSha256)
            throw new InvalidDataException("Console query attempted another selected source interface.");
        return new(source.Identity, "actual-source-interface-constructor/" + _constructor,
            FalloutConsolePresence.Present, _enabled != 0, _console, _consoleCounter);
    }
    internal void EnterUnownedConsumer(string owner)
    {
        RequireOwnerThread(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        _boundary ??= "source-FO3-interface-child-unowned:" + owner; Next();
        throw new NotSupportedException(_boundary);
    }
    internal void RetainFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _failure ??= error.GetType().Name + ": " + (string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message); Next();
    }
    internal void Retire()
    {
        RequireOwnerThread();
        if (_retired) return;
        // Actual source caller must drain first. The native owner performs that
        // guard; retained errors do not skip independent object retirement.
        if (_dialog is { Retired: null }) RetireDialog(_dialog);
        if (_manager is { Retired: null } manager)
        { var retired = manager with { Retired = Next() }; Replace(retired); _manager = retired; _native.Remove(manager.Identity); }
        _alternate = null; _enabled = 0; _retired = true; Next();
    }
    private void ValidatePublication(FalloutStandaloneInterfaceNativePublication publication, FalloutStandaloneInterfaceObjectRole role)
    {
        if (publication is null || publication.Factory == Guid.Empty || publication.Process != _process || publication.Source != Source.Identity ||
            publication.Stack != _stack || publication.Role != role || publication.NativeInstance == 0 ||
            !FalloutAdvancementRuntimeReceipt.Digest(publication.ResourceSha256) || string.IsNullOrWhiteSpace(publication.Owner) ||
            publication.Resource != (role == FalloutStandaloneInterfaceObjectRole.DialogMenu ? "menus/dialog/dialog_menu.xml" : "menus/main/hud_main_menu.xml") ||
            _objects.Any(item => item.Native.Factory == publication.Factory || item.Process == _process &&
                item.Native.NativeInstance == publication.NativeInstance && item.Retired is null))
            throw new InvalidDataException("Native interface publication changed its source role, resource, factory or current lifetime.");
    }
}
