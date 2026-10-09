using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal interface IFalloutExteriorCellLoaderTaskConsumers
{
    string SourceContract { get; }
    Guid Process { get; }
    string Owner { get; }
    // The selected TaskManager child receives its original zero argument.
    // Returning from cancellation does not establish task/worker destruction.
    void Cancel(FalloutMainPlayerCellInvocation invocation, FalloutExteriorCellLoaderTask task, int argument);
    bool ObserveTaskRetired(FalloutExteriorCellLoaderTask task);
}

// This is the actual source exterior-loader request owner, constructed only
// after the original map lookup and from a current source CELL caller. Native
// task/TLS/service construction remains a separate dependency, never a CLR
// Task-presence or presentation decoder completion inference.
internal sealed class FalloutExteriorCellLoaderTask : IFalloutExteriorCellLoaderTask
{
    private readonly FalloutMainPlayerPendingSource _source;
    private readonly Guid _process;
    private readonly IFalloutExteriorCellLoaderTaskConsumers? _consumers;
    private bool _entered;
    private long _attempts, _returned, _reentry;
    private string? _failure;
    public FalloutExteriorCellLoaderTaskSource Source { get; }
    internal int SourceBaseConstructorArgument => 0;
    internal uint InitialRequestState => 0;
    internal byte InitialRequestByte => 0;
    public bool Retired
    {
        get
        {
            if (_entered) { _reentry = checked(_reentry + 1); throw new InvalidOperationException("Source task retirement queried during its entered cancellation child."); }
            if (_consumers is null) return false;
            RequireConsumer(); return _consumers.ObserveTaskRetired(this);
        }
    }
    internal object State => new
    {
        source = Source,
        baseConstructorArgument = SourceBaseConstructorArgument,
        constructorRequestState = InitialRequestState,
        constructorRequestByte = InitialRequestByte,
        entered = _entered,
        attempts = _attempts,
        returned = _returned,
        failure = _failure,
        taskManager = _consumers?.Owner,
        nativeWorkerTls = "independent-constructor-and-runtime-consumer-required"
    };
    internal FalloutExteriorCellLoaderTask(FalloutMainPlayerPendingSource source,
        FalloutMainPlayerCellInvocation invocation, uint key, FalloutCellProcessIdentity cell, int x, int y,
        IFalloutExteriorCellLoaderTaskConsumers? consumers)
    {
        source.Validate(); invocation.Require(invocation.Step);
        if (invocation.Step is not (FalloutMainPlayerCellStep.WorldLoad or FalloutMainPlayerCellStep.PendingDestination) ||
            source.Player != invocation.Owner.MainPlayerCellSource || key != source.PackCellKey(x, y) ||
            cell.Worldspace is null)
            throw new InvalidDataException("Exterior request constructor lost its actual original CELL/world/key invocation.");
        _source = source; _process = invocation.Main.Process; _consumers = consumers;
        Source = new(Guid.NewGuid(), key, cell, x, y, invocation.Main.Identity, "actual-source-exterior-loader-request/" + source.Contract);
        if (consumers is not null) RequireConsumer();
    }
    public void RequestSourceCancellation(FalloutMainPlayerCellInvocation invocation)
    {
        invocation.Require(FalloutMainPlayerCellStep.PendingWorldPrelude);
        if (invocation.Main.Process != _process || invocation.Owner.MainPlayerCellSource != _source.Player)
            throw new InvalidDataException("Exterior cancellation changed its actual source task construction process.");
        if (_entered) { _reentry = checked(_reentry + 1); throw new InvalidOperationException("Original exterior task cancellation reentered its real request."); }
        if (_failure is not null) throw new InvalidOperationException("Exterior task retains its original cancellation prefix: " + _failure);
        _entered = true; _attempts = checked(_attempts + 1); var faults = _reentry;
        try
        {
            RequireConsumer(); _consumers!.Cancel(invocation, this, 0);
            invocation.Require(FalloutMainPlayerCellStep.PendingWorldPrelude);
            if (_reentry != faults) throw new InvalidOperationException("TaskManager child swallowed an actual exterior request reentry.");
            _returned = checked(_returned + 1);
        }
        catch (Exception failure)
        {
            _failure ??= string.IsNullOrWhiteSpace(failure.Message) ? failure.GetType().Name : failure.Message; throw;
        }
        finally { _entered = false; }
    }
    private void RequireConsumer()
    {
        if (_consumers is null) throw new NotSupportedException("actual-source-exterior-TaskManager-virtual-task-type-native-worker-TLS-constructor-unowned");
        if (_consumers.SourceContract != _source.Contract || _consumers.Process != _process || string.IsNullOrWhiteSpace(_consumers.Owner))
            throw new InvalidDataException("Exterior task changed its exact selected TaskManager/lifetime provider.");
    }
}
