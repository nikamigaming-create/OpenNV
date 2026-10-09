using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutCellProcesses? _cellProcesses;
    private FalloutCellProcessDeclaration? _cellProcessDeclaration;
    private string? _cellProcessStack;
    private FalloutCellProcessesSnapshot? _cellProcessRestore;
    private Func<FalloutFormKey?>? _currentPlayerProcessCell;
    private object? _currentPlayerProcessCellLease;
    internal bool CellProcessesConfigured => _cellProcesses is not null;
    internal FalloutCellProcesses CellProcesses => _cellProcesses ??
        throw new NotSupportedException("Actual source CELL load/attachment/retirement owner is absent.");
    internal string? CellProcessSaveBlocker => _cellProcesses is null ? "source-cell-process-owner-absent" : CellProcesses.SaveBlocker;
    internal object? CellProcessState => _cellProcesses?.State;

    internal void ConfigureCellProcesses(FalloutCellProcessDeclaration declaration, string stack,
        FalloutCellProcessesSnapshot? restore = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_cellProcesses is not null) throw new InvalidOperationException("Source CELL process owner is already configured.");
        if (_combatGroupDeclaration?.ExecutableSha256 != declaration.ExecutableSha256 || _combatGroupStackIdentity != stack ||
            records.OwnedSource is { } owned && owned.StackId != stack)
            throw new InvalidDataException("CELL process source differs from the actual reference factory.");
        var candidate = new FalloutCellProcesses(declaration, records, stack, restore, reference => Placement(reference.FormKey));
        _cellProcesses = candidate; _cellProcessDeclaration = declaration; _cellProcessStack = stack; _cellProcessRestore = restore;
    }
    internal void RequireCellProcessBinding(FalloutCellProcessDeclaration declaration, string stack,
        FalloutCellProcessesSnapshot? restore)
    {
        if (_cellProcesses is null || _cellProcessDeclaration != declaration || _cellProcessStack != stack || !ReferenceEquals(_cellProcessRestore, restore))
            throw new InvalidDataException("Attached CELL process lifetime differs from its current source/cold owner.");
    }
    internal IDisposable BindActualPlayerProcessCell(Func<FalloutFormKey?> currentCell)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(currentCell);
        if (_currentPlayerProcessCellLease is not null) throw new InvalidOperationException("Player current-CELL source owner already has a live lease.");
        var lease = new object(); _currentPlayerProcessCellLease = lease; _currentPlayerProcessCell = currentCell;
        return new CurrentPlayerCellLease(this, lease);
    }
    private sealed class CurrentPlayerCellLease(FalloutReferenceWorld world, object lease) : IDisposable
    {
        private bool _retired;
        public void Dispose()
        {
            if (_retired) return; _retired = true;
            if (!ReferenceEquals(world._currentPlayerProcessCellLease, lease)) return;
            world._currentPlayerProcessCellLease = null; world._currentPlayerProcessCell = null;
        }
    }
    internal FalloutActorProcessFact<byte> ReadSourceActorCellPhase(FalloutFormKey actor)
    {
        if (_cellProcesses is null) return new(null, "actual-current-CELL-lifecycle-owner-absent");
        if (actor == _enginePlayer)
        {
            if (_currentPlayerProcessCell is null) return new(null, "actual-current-Player-CELL-producer-absent");
            return CellProcesses.ReadPhase(_currentPlayerProcessCell());
        }
        _ = Actor(actor);
        // Placement is the authoritative current reference state, including
        // retained moves. Source record ancestry is a separate immutable field.
        return CellProcesses.ReadPhase(Placement(actor).Cell);
    }
    internal FalloutActorProcessElection JoinCurrentCellProcess(FalloutActorProcessElection election) =>
        election with { SourceCellPhase = ReadSourceActorCellPhase(election.Actor) };
    internal FalloutActorProcessScheduleObservation JoinCurrentActorUpdate(FalloutActorProcessScheduleObservation observation) =>
        observation with { ActorUpdateEnabled = _actorUpdates is null ?
            new(null, "actual-original-Actor-update-byte-owner-absent") : ReadSourceActorUpdate(observation.Actor) };
    internal FalloutCellProcessesSnapshot CaptureCellProcesses() => CellProcesses.Capture();
    private void RetireCellProcesses()
    {
        // A refused retirement retains every still-owned source/native handle.
        _cellProcesses?.Dispose(); _cellProcesses = null;
        _cellProcessDeclaration = null; _cellProcessStack = null; _cellProcessRestore = null;
        _currentPlayerProcessCellLease = null; _currentPlayerProcessCell = null;
    }
}
