using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutMainInterfaceSnapshot(string Schema, FalloutMainInterfaceSource Source, string Stack,
    Guid CapturedProcess, Guid Constructor, long Sequence, byte Enabled, uint Mode, uint Context,
    byte DialogByte, IReadOnlyList<uint> Slots, IReadOnlyList<byte> ActiveMenus,
    FalloutStandaloneInterfaceObject? OwnManager, FalloutStandaloneInterfaceObject? Dialog,
    IReadOnlyList<FalloutStandaloneInterfaceObject> Objects, Guid? Alternate,
    FalloutConsolePresence Console, sbyte? ConsoleCounter, string? Failure);

// Native identities are publication leases. They never become source fields
// merely because a product menu exists or the SceneTree happens to be paused.
internal sealed class FalloutMainInterfaceState
{
    internal const string Schema = "opennv-source-Main-interface/v1";
    internal FalloutMainInterfaceSource Source { get; }
    private readonly string _stack;
    private readonly Guid _process, _constructor = Guid.NewGuid();
    private readonly uint[] _slots = new uint[10];
    private readonly byte[] _active = new byte[60];
    private readonly List<FalloutStandaloneInterfaceObject> _objects = [];
    private readonly Dictionary<Guid, Func<bool>> _native = [];
    private FalloutStandaloneInterfaceObject? _manager, _dialog;
    private long _sequence;
    private byte _enabled, _dialogByte;
    private uint _mode = 1, _context;
    private Guid? _alternate = null;
    private FalloutConsolePresence _console = FalloutConsolePresence.Absent;
    private sbyte? _consoleCounter = null;
    private int? _thread;
    private string? _failure;
    private bool _reading, _reentry, _retired;

    internal FalloutMainInterfaceState(FalloutMainInterfaceSource source, string stack, Guid process,
        FalloutMainInterfaceSnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty) throw new InvalidDataException("Main interface has no actual process constructor.");
        Source = source; _stack = stack; _process = process;
        if (saved is null) return;
        Validate(saved);
        if (saved.OwnManager is not null || saved.Enabled != 0) RequirePlayable(saved);
        else if (saved.Objects.Count != 0 || saved.Dialog is not null || saved.Failure is not null)
            throw new NotSupportedException("Cold Main interface retains an unreturned native constructor or failed source writer.");
        // A focused constructor-only C# snapshot may remain unpublished.
        // Its SaveBlocker still refuses complete campaign admission, which
        // separately requires RequirePlayable and a fresh native factory.
        if (saved.Source != source || saved.Stack != stack || saved.CapturedProcess == process)
            throw new InvalidDataException("Cold Main interface lost its actual selected source/new process.");
        _sequence = saved.Sequence; _objects.AddRange(saved.Objects);
        // Source objects are reconstructed by the actual native factory. Old
        // pointers remain historical; no menu constructor or writer is replayed.
        _mode = saved.Mode; _context = saved.Context;
    }
    internal Guid Process => _process;
    internal bool Retired => _retired;
    internal string? SaveBlocker => _failure ?? (_reading ? "source-Main-interface-native-observation-entered" :
        _retired ? "source-Main-interface-retired" : _manager is null || _enabled == 0 ?
        "source-Main-interface-native-own-manager-unpublished" : null);
    internal object State => new
    {
        source = Source,
        process = _process,
        constructor = _constructor,
        sequence = _sequence,
        enabled = _enabled,
        mode = _mode,
        context = _context,
        dialog = _dialogByte,
        manager = _manager,
        alternate = _alternate,
        console = _console,
        consoleCounter = _consoleCounter,
        objects = _objects.ToArray(),
        failure = _failure,
        retired = _retired,
        saveBlocker = SaveBlocker
    };

    internal FalloutStandaloneInterfaceObject PublishOwnManager(FalloutStandaloneInterfaceNativePublication publication,
        Func<bool> living)
    {
        Require(); ValidatePublication(publication, FalloutStandaloneInterfaceObjectRole.PipBoyManager);
        if (_manager is not null || !ReadNative(living)) throw new InvalidOperationException("Own manager has no actual returned fresh native factory.");
        _thread = Environment.CurrentManagedThreadId;
        var current = NewObject(publication, living);
        _manager = current; _enabled = 1; Next(); return current;
    }
    internal FalloutStandaloneInterfaceObject ConstructDialog(FalloutStandaloneInterfaceNativePublication publication,
        Func<bool> living)
    {
        Require(); ValidatePublication(publication, FalloutStandaloneInterfaceObjectRole.DialogMenu);
        if (_dialog is { Retired: null } || !ReadNative(living)) throw new InvalidOperationException("Dialog source constructor replaced a living object.");
        var current = NewObject(publication, living); _dialog = current; _dialogByte = 1; Next(); return current;
    }
    private FalloutStandaloneInterfaceObject NewObject(FalloutStandaloneInterfaceNativePublication publication, Func<bool> living)
    {
        var current = new FalloutStandaloneInterfaceObject(Guid.NewGuid(), _process, publication.Role, Next(), null, null, publication);
        _objects.Add(current); _native.Add(current.Identity, living); return current;
    }
    internal void CloseDialog(FalloutStandaloneInterfaceObject current)
    {
        Require(allowFailure: true); RequireDialog(current);
        if (_dialog!.Closed is not null) return;
        _dialogByte = 0; Replace(_dialog = _dialog with { Closed = Next() });
    }
    internal void RetireDialog(FalloutStandaloneInterfaceObject current)
    {
        Require(allowFailure: true); RequireDialog(current);
        if (_dialog!.Retired is not null) return;
        _dialogByte = 0; Replace(_dialog = _dialog with { Retired = Next() }); _native.Remove(current.Identity);
    }
    private void RequireDialog(FalloutStandaloneInterfaceObject current)
    {
        if (_dialog is null || current.Identity != _dialog.Identity || current.Process != _process || current.Native != _dialog.Native)
            throw new InvalidDataException("Dialog callback changed the original current object lifetime.");
    }
    internal int Read(FalloutStandaloneInterfaceQuery query)
    {
        Require();
        if (_enabled != 0 && (_manager is null || !_native.TryGetValue(_manager.Identity, out var living) || !ReadNative(living)))
            throw new InvalidOperationException("Main interface manager lost its actual native publication.");
        if (_dialog is { Retired: null } dialog && (!_native.TryGetValue(dialog.Identity, out var dialogLiving) || !ReadNative(dialogLiving)))
            throw new InvalidOperationException("Main dialog byte outlived its native object.");
        return query switch
        {
            FalloutStandaloneInterfaceQuery.MenuGate => _enabled != 0 && _mode != 1 ? 1 : 0,
            FalloutStandaloneInterfaceQuery.GuiModeTwo => _context == 2 ? 1 : 0,
            FalloutStandaloneInterfaceQuery.FirstPredicate => _enabled != 0 && _dialogByte != 0 ? 1 : 0,
            FalloutStandaloneInterfaceQuery.FinalPredicate => ConsoleOpen() ? 1 : 0,
            FalloutStandaloneInterfaceQuery.ForeignMenu => Foreign() ? 1 : 0,
            FalloutStandaloneInterfaceQuery.ContextKind => FalloutMainInterfaceSource.Kind(_mode),
            _ => throw new ArgumentOutOfRangeException(nameof(query))
        };
    }
    private bool ConsoleOpen()
    {
        if (_enabled == 0 || _console == FalloutConsolePresence.Absent) return false;
        if (_console != FalloutConsolePresence.Present || _consoleCounter is not { } counter)
            throw new NotSupportedException("Actual existing Console class/counter writer is unowned.");
        return counter > 0;
    }
    private bool Foreign()
    {
        Guid? selected = FalloutMainInterfaceSource.OwnSelectorCodes.ToArray().Any(code => _active[checked((int)code - 1001)] != 0)
            ? _manager?.Identity : _alternate;
        return selected is not null && selected != _manager?.Identity;
    }
    internal void EnterUnownedWriter(string owner)
    {
        Require(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var error = new NotSupportedException("source-Main-interface-writer-unowned:" + owner); RetainFailure(error); throw error;
    }
    internal void RetainFailure(Exception error) { _failure ??= error.ToString(); Next(); }
    private long Next() => _sequence = checked(_sequence + 1);
    private void Require(bool allowFailure = false)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_reading) { _reentry = true; throw new InvalidOperationException("Native interface reader cannot mutate or reenter its source owner."); }
        if (_thread is { } thread && thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Main interface lost its actual publication thread.");
        if (!allowFailure && _failure is not null) throw new InvalidOperationException("Main interface retains its failed producer.");
    }
    private bool ReadNative(Func<bool> read)
    {
        ArgumentNullException.ThrowIfNull(read); _reading = true; _reentry = false; var entered = _sequence;
        try
        {
            var current = read();
            if (_reentry || entered != _sequence) throw new InvalidOperationException("Native interface reader swallowed a source mutation/reentry refusal.");
            return current;
        }
        catch (Exception error) { RetainFailure(error); throw; }
        finally { _reading = false; }
    }
    private void Replace(FalloutStandaloneInterfaceObject value) => _objects[_objects.FindIndex(item => item.Identity == value.Identity)] = value;
    private void ValidatePublication(FalloutStandaloneInterfaceNativePublication value, FalloutStandaloneInterfaceObjectRole role)
    {
        if (value is null || value.Factory == Guid.Empty || value.Process != _process || value.Source != Source.Identity ||
            value.Stack != _stack || value.Role != role || value.NativeInstance == 0 ||
            !FalloutAdvancementRuntimeReceipt.Digest(value.ResourceSha256) || string.IsNullOrWhiteSpace(value.Owner) ||
            value.Resource != (role == FalloutStandaloneInterfaceObjectRole.DialogMenu ? "menus/dialog/dialog_menu.xml" : "menus/main/hud_main_menu.xml") ||
            _objects.Any(item => item.Native.Factory == value.Factory || item.Process == _process && item.Native.NativeInstance == value.NativeInstance))
            throw new InvalidDataException("Main native factory changed source, role or original XML/native identity.");
    }
    internal FalloutMainInterfaceSnapshot Capture()
    {
        Require(allowFailure: true);
        foreach (var current in _objects.Where(item => item.Process == _process && item.Retired is null))
            if (!_native.TryGetValue(current.Identity, out var read) || !ReadNative(read))
                throw new NotSupportedException("Main interface capture lost an actual native object lifetime.");
        var value = new FalloutMainInterfaceSnapshot(Schema, Source, _stack, _process, _constructor, _sequence,
            _enabled, _mode, _context, _dialogByte, _slots.ToArray(), _active.ToArray(), _manager, _dialog,
            _objects.ToArray(), _alternate, _console, _consoleCounter, _failure);
        Validate(value); return value;
    }
    internal static void Validate(FalloutMainInterfaceSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Schema != Schema || value.Source is null || string.IsNullOrWhiteSpace(value.Stack) || value.CapturedProcess == Guid.Empty ||
            value.Constructor == Guid.Empty || value.Sequence < 0 || value.Enabled > 1 || value.DialogByte > 1 || value.Slots is not { Count: 10 } ||
            value.ActiveMenus is not { Count: 60 } || value.Objects is null || value.Objects.Any(item => item is null || item.Native is null) ||
            value.Objects.Select(item => item.Identity).Distinct().Count() != value.Objects.Count ||
            value.Failure is not null && string.IsNullOrWhiteSpace(value.Failure))
            throw new InvalidDataException("Main interface omitted original fields or actual publication/failure history.");
        value.Source.Validate();
        if (value.Context != 0 || value.Alternate is not null || value.Console != FalloutConsolePresence.Absent || value.ConsoleCounter is not null ||
            value.Mode != 1 || value.Slots.Any(slot => slot != 0) || value.ActiveMenus.Any(active => active != 0))
            throw new NotSupportedException("Main interface has no owner for the changed menu/context/console writer.");
        foreach (var current in value.Objects)
            if (current.Identity == Guid.Empty || current.Process == Guid.Empty || current.Constructed < 1 || current.Constructed > value.Sequence ||
                current.Native.Process != current.Process || current.Native.Source != value.Source.Identity || current.Native.Stack != value.Stack ||
                current.Role != current.Native.Role || current.Role is not (FalloutStandaloneInterfaceObjectRole.PipBoyManager or FalloutStandaloneInterfaceObjectRole.DialogMenu) ||
                current.Native.Factory == Guid.Empty || current.Native.NativeInstance == 0 || !FalloutAdvancementRuntimeReceipt.Digest(current.Native.ResourceSha256) ||
                string.IsNullOrWhiteSpace(current.Native.Owner) || current.Native.Resource !=
                    (current.Role == FalloutStandaloneInterfaceObjectRole.DialogMenu ? "menus/dialog/dialog_menu.xml" : "menus/main/hud_main_menu.xml") ||
                current.Closed is { } closed && (closed <= current.Constructed || closed > value.Sequence) ||
                current.Retired is { } retired && (retired <= current.Constructed || retired > value.Sequence))
                throw new InvalidDataException("Main interface object omitted exact current/native lifetime.");
        if (value.OwnManager is { } own && (own.Process != value.CapturedProcess || !value.Objects.Contains(own)) ||
            value.Enabled != 0 && value.OwnManager is not { Retired: null } ||
            value.Dialog is { } dialog && (dialog.Process != value.CapturedProcess || !value.Objects.Contains(dialog)) ||
            value.OwnManager is { Role: not FalloutStandaloneInterfaceObjectRole.PipBoyManager } ||
            value.Dialog is { Role: not FalloutStandaloneInterfaceObjectRole.DialogMenu } ||
            value.DialogByte != 0 && value.Dialog is not { Closed: null, Retired: null })
            throw new InvalidDataException("Main interface flags do not belong to the actual current class pointers.");
    }
    internal static void RequirePlayable(FalloutMainInterfaceSnapshot value)
    {
        Validate(value);
        if (value.Failure is not null || value.Enabled != 1 || value.OwnManager is not { Retired: null } ||
            value.Dialog is { Retired: null } || value.DialogByte != 0)
            throw new NotSupportedException("Main interface is not at its genuine returned manager/menu retirement boundary.");
    }
    internal void Retire()
    {
        if (_retired) return; Require(allowFailure: true);
        if (_dialog is { Retired: null } dialog) RetireDialog(dialog);
        if (_manager is { Retired: null } own) { Replace(_manager = own with { Retired = Next() }); _native.Remove(own.Identity); }
        _enabled = 0; _retired = true; Next();
    }
}
