using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// One actual TES current CELL and grid, independent of Player membership,
// pending transfers and presentation. The slots contain current source CELLs.
internal sealed partial class FalloutSourceInitialPlacementCells
{
    internal const string Schema = "opennv-initial-placement-TES-cells/v1";
    private const string GridBoundary = "source-initial-placement-grid-constructor-unreturned";
    private readonly FalloutPluginStack _records;
    private readonly Action<FalloutReferencePositionInvocation> _requireActual;
    private readonly List<FalloutReferencePositionPublication> _publications = [];
    private readonly List<FalloutInitialPlacementCellCall> _calls = [];
    private readonly Dictionary<FalloutInitialPlacementCellLane, string> _writers = new() { [FalloutInitialPlacementCellLane.Grid] = GridBoundary };
    private FalloutFormKey? _currentCell;
    private FalloutFormKey?[] _slots = [];
    private uint _diameter;
    private long _sequence, _fault;
    private int? _thread;
    private Scope? _current;
    private bool _retired;
    internal FalloutInitialPlacementCellsSource Source { get; }
    internal string Stack { get; }
    internal Guid Process { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Constructor { get; }
    internal FalloutInitialPlacementCellCall? GridConstruction { get; private set; }
    internal string? SaveBlocker => _current is not null ? "source-initial-placement-cells-in-flight" : _retired ?
        "source-initial-placement-cells-retired" : _calls.FirstOrDefault(value => !value.Returned)?.Failure ?? _writers.Values.FirstOrDefault();

    internal FalloutSourceInitialPlacementCells(FalloutInitialPlacementCellsSource source, FalloutPluginStack records,
        string stack, Guid process, Guid constructor, Action<FalloutReferencePositionInvocation> requireActual,
        FalloutInitialPlacementCellSnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(requireActual);
        if (process == Guid.Empty || constructor == Guid.Empty) throw new InvalidDataException("TES cells lack their actual field constructor/process.");
        Source = source; Stack = stack; Process = process; Constructor = constructor; _records = records; _requireActual = requireActual;
        if (saved is not null)
        {
            Validate(saved, source, stack);
            if (saved.Process == process || saved.Factory == Factory || saved.Constructor == constructor || saved.Retired)
                throw new InvalidDataException("Cold TES cells reused their actual constructor or retired field owner.");
            _currentCell = saved.CurrentCell; _diameter = saved.Diameter; _slots = saved.Slots.ToArray(); _sequence = saved.Sequence;
            foreach (var cell in _slots.Append(_currentCell).Where(value => value is not null)) RequireCell(cell);
            _writers.Clear(); foreach (var writer in saved.Writers) _writers.Add(writer.Key, writer.Value);
            _publications.AddRange(saved.Publications); _calls.AddRange(saved.Calls.Select(CloneCall));
            GridConstruction = _calls.LastOrDefault(value => value.Kind == "original-grid-constructor");
        }
        _publications.Add(new(Process, Factory, Constructor, saved?.Process, saved?.Factory));
    }
    internal FalloutInitialPlacementCellCall ConstructGrid(FalloutReferencePositionInvocation invocation, FalloutIniValue setting,
        Action<uint>? storeActualIniValue = null)
    {
        if (GridConstruction is not null) throw new InvalidOperationException("TES grid cannot replay or retry its original constructor.");
        return Run(invocation, "original-grid-constructor", scope =>
        {
            scope.Setting = setting; scope.Step = FalloutInitialPlacementCellStep.Setting;
            var value = GridValue(setting); var normalized = FalloutInitialPlacementCellsSource.NormalizeGridDiameter(value);
            if (normalized != value)
            {
                (storeActualIniValue ?? throw new NotSupportedException("source-grid-normalized-main-INI-store-owner-unbound"))(normalized);
                Guard(scope); scope.SettingStored = true;
            }
            Guard(scope); _diameter = normalized; Store(scope, FalloutInitialPlacementCellStep.Diameter, normalized);
            // Exact original allocation extent, with managed allocation failure
            // retained before slot publication. No arbitrary format limit.
            var length = checked((int)checked((ulong)normalized * normalized));
            _slots = new FalloutFormKey?[length]; Store(scope, FalloutInitialPlacementCellStep.Slots, checked((uint)length));
        });
    }
    internal FalloutInitialPlacementCellRead ReadSelectedCell(FalloutReferencePositionInvocation invocation)
    {
        var call = Run(invocation, "initial-placement-selected-CELL", scope =>
        {
            RequireLane(FalloutInitialPlacementCellLane.CurrentCell);
            if (_currentCell is { } cell)
                scope.Read = new(Source.Identity, Stack, Process, Factory, Constructor, _sequence, cell, null, null, cell);
            else
            {
                RequireLane(FalloutInitialPlacementCellLane.Grid);
                var center = _diameter >> 1; var slot = checked((int)(center * _diameter + center));
                scope.Read = new(Source.Identity, Stack, Process, Factory, Constructor, _sequence, null, _diameter, slot, _slots[slot]);
            }
            scope.Step = FalloutInitialPlacementCellStep.Selected;
        });
        return call.Read!;
    }
    internal FalloutInitialPlacementCellCall StoreCurrentCell(FalloutReferencePositionInvocation invocation, FalloutFormKey? cell) =>
        Run(invocation, "actual-current-TES-CELL-store", scope =>
        { RequireCell(cell); _currentCell = cell; Store(scope, FalloutInitialPlacementCellStep.CurrentCell, cell: cell); _writers.Remove(FalloutInitialPlacementCellLane.CurrentCell); });
    internal FalloutInitialPlacementCellCall StoreGridSlot(FalloutReferencePositionInvocation invocation, uint x, uint y, FalloutFormKey? cell) =>
        Run(invocation, "actual-current-grid-CELL-store", scope =>
        {
            RequireLane(FalloutInitialPlacementCellLane.Grid); RequireCell(cell);
            if (x >= _diameter || y >= _diameter) throw new InvalidDataException("Current grid slot is outside its actual source extent.");
            var slot = checked((int)(x * _diameter + y)); _slots[slot] = cell; Store(scope, FalloutInitialPlacementCellStep.GridSlot, index: slot, cell: cell);
        });
    internal void ObserveUnownedWriter(FalloutReferencePositionInvocation invocation, FalloutInitialPlacementCellLane lane, string boundary) =>
        Run(invocation, "observed-current-TES-field-writer", scope =>
        { if (!Enum.IsDefined(lane)) throw new InvalidDataException("Unknown TES writer lane."); ArgumentException.ThrowIfNullOrWhiteSpace(boundary); scope.Invalidated = lane; scope.Writer = boundary; _writers[lane] = boundary; });

    private static uint GridValue(FalloutIniValue setting)
    {
        if (setting.Declaration.Collection != FalloutIniCollection.Main || setting.Declaration.Kind != 'u' ||
            !setting.Declaration.Name.Equals("uGridsToLoad:General", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(setting.Origin) ||
            setting.Number is not { } number || !double.IsFinite(number) || number < 0 || number > uint.MaxValue || number != Math.Truncate(number))
            throw new InvalidDataException("TES grid lacks its actual unsigned Main INI value.");
        return (uint)number;
    }
    private void RequireCell(FalloutFormKey? cell)
    { if (cell is { } key && _records.GetEffective(key) is not { Signature: "CELL", IsDeleted: false }) throw new InvalidDataException("Current TES/grid pointer is not a live source CELL."); }
    private void RequireLane(FalloutInitialPlacementCellLane lane)
    { if (_writers.TryGetValue(lane, out var boundary)) throw new NotSupportedException(boundary); }
    private FalloutInitialPlacementCellCall Run(FalloutReferencePositionInvocation invocation, string kind, Action<Scope> action)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_current is not null) { _fault++; throw new InvalidOperationException("Current TES cells reentered."); }
        var scope = new Scope(invocation, kind, checked(++_sequence), _fault); _current = scope;
        try { Guard(scope); action(scope); Guard(scope); scope.Step = FalloutInitialPlacementCellStep.Returned; return Complete(scope, true, null); }
        catch (Exception error) { Complete(scope, false, string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message); throw; }
        finally { _current = null; }
    }
    private FalloutInitialPlacementCellCall Complete(Scope scope, bool returned, string? failure)
    {
        var call = new FalloutInitialPlacementCellCall(scope.Invocation, Source.Identity, Process, Factory, scope.Entered, checked(++_sequence),
            scope.Kind, scope.Step, Array.AsReadOnly(scope.Stores.ToArray()), returned, failure, scope.Setting, scope.SettingStored, scope.Read, scope.Invalidated, scope.Writer);
        _calls.Add(call);
        if (scope.Kind == "original-grid-constructor")
        { GridConstruction = call; if (returned) _writers.Remove(FalloutInitialPlacementCellLane.Grid); }
        return call;
    }
    private void Store(Scope scope, FalloutInitialPlacementCellStep step, uint? bits = null, int? index = null, FalloutFormKey? cell = null)
    { scope.Step = step; scope.Stores.Add(new(step, checked(++_sequence), bits, index, cell)); }
    private void Guard(Scope scope)
    {
        var invocation = scope.Invocation; invocation.ValidatePackageCaller();
        if (_retired || _current != scope || _fault != scope.Fault || invocation.Call == Guid.Empty || invocation.ReferenceProcess == Guid.Empty ||
            invocation.ReferenceConstructor == Guid.Empty || invocation.CommonProcess == Guid.Empty || invocation.Epoch < 1 ||
            invocation.ThreadId != Environment.CurrentManagedThreadId || _thread is { } thread && thread != invocation.ThreadId)
            throw new InvalidOperationException("TES cells lost the actual source caller or current field scope.");
        _requireActual(invocation);
        if (_retired || _current != scope || _fault != scope.Fault) throw new InvalidOperationException("TES caller guard swallowed reentry.");
        _thread ??= invocation.ThreadId;
    }
    private sealed class Scope(FalloutReferencePositionInvocation invocation, string kind, long entered, long fault)
    {
        internal FalloutReferencePositionInvocation Invocation = invocation;
        internal string Kind = kind; internal long Entered = entered, Fault = fault;
        internal FalloutInitialPlacementCellStep Step;
        internal List<FalloutInitialPlacementCellStore> Stores = [];
        internal FalloutIniValue? Setting; internal bool SettingStored;
        internal FalloutInitialPlacementCellRead? Read;
        internal FalloutInitialPlacementCellLane? Invalidated; internal string? Writer;
    }
}
