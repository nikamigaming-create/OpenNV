using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// A real first-party read Task holds the original queued object until it has
// returned. Cancelling its token neither completes that Task nor retires an
// entered native assembly. Native callbacks are entered only by the owning
// presentation thread; no worker continuation destroys Godot objects.
internal interface IFalloutQueuedReferenceRead
{
    Task ReadTask { get; }
    bool Retired { get; }
    void RequestCancellation();
    void RetireAfterReadReturned(Action retireEnteredNative);
}

internal sealed class FalloutQueuedReferenceRead<T> : IFalloutQueuedReferenceRead
{
    private readonly FalloutQueuedReferences _queue;
    private readonly CancellationTokenSource _cancellation;
    private readonly Guid _identity, _read;
    private readonly string _owner;
    private readonly int _presentationThread = Environment.CurrentManagedThreadId;
    private Guid? _assembly, _publication, _cancellationConsumer;
    private bool _cancelRequested, _nativeRetirementReturned, _cancellationReturned, _consumersRetired, _retired;
    private Exception? _retirementFailure;
    internal Guid Identity => _identity;
    internal Task<T> ReadTask { get; }
    internal bool Retired => _retired;
    internal Exception? RetirementFailure => _retirementFailure;
    Task IFalloutQueuedReferenceRead.ReadTask => ReadTask;
    bool IFalloutQueuedReferenceRead.Retired => Retired;
    void IFalloutQueuedReferenceRead.RequestCancellation() => RequestCancellation();
    void IFalloutQueuedReferenceRead.RetireAfterReadReturned(Action retireEnteredNative) => RetireAfterReadReturned(retireEnteredNative);

    internal FalloutQueuedReferenceRead(FalloutQueuedReferences queue, Guid identity, string owner,
        Func<CancellationToken, Task<T>> read, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(queue); ArgumentNullException.ThrowIfNull(read);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        _queue = queue; _identity = identity; _owner = owner;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        try { _read = queue.EnterConsumer(identity, FalloutQueuedReferenceConsumerKind.Read, owner + ":read"); }
        catch { _cancellation.Dispose(); throw; }
        Task<T> task;
        try { task = read(_cancellation.Token) ?? throw new InvalidOperationException("Queued source reader returned no actual Task."); }
        catch (Exception error) { task = Task.FromException<T>(error); }
        ReadTask = ReturnActualRead(task);
    }
    private async Task<T> ReturnActualRead(Task<T> task)
    {
        Exception? failure = null;
        try { return await task.ConfigureAwait(false); }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            try { _queue.ReturnConsumer(_identity, _read, _owner + ":read", failure); }
            catch (Exception returnFailure)
            {
                if (failure is not null) throw new AggregateException("Actual queued read and its source return both failed.", failure, returnFailure);
                throw;
            }
        }
    }
    internal T BeginAssembly()
    {
        RequireCurrent();
        if (_cancelRequested || _assembly is not null || !ReadTask.IsCompleted)
            throw new InvalidOperationException("Queued native assembly has no single completed original read.");
        var result = ReadTask.GetAwaiter().GetResult();
        _assembly = _queue.EnterConsumer(_identity, FalloutQueuedReferenceConsumerKind.Assembly, _owner + ":assembly");
        return result;
    }
    internal void AssemblyTransferred()
    {
        RequireCurrent();
        if (_assembly is not { } assembly || _publication is not null || _cancelRequested)
            throw new InvalidOperationException("Queued native assembly has no actual ownership transfer.");
        _queue.ReturnConsumer(_identity, assembly, _owner + ":assembly");
        _assembly = null;
        _publication = _queue.EnterConsumer(_identity, FalloutQueuedReferenceConsumerKind.Publication, _owner + ":publication");
    }
    internal void PublicationReturned()
    {
        RequireCurrent();
        if (_publication is not { } publication || _cancelRequested)
            throw new InvalidOperationException("Queued native publication has no actual entered consumer.");
        _queue.ReturnConsumer(_identity, publication, _owner + ":publication");
        _publication = null;
        _queue.ConsumersRetired(_identity, _owner + ":publication-returned");
        _consumersRetired = true;
        RetireMapAndCaller();
    }
    internal void RequestCancellation()
    {
        if (_retired || _cancelRequested) return;
        _queue.RequestCancellation(_identity, _owner + ":cancellation-requested");
        _cancelRequested = true;
        try { _cancellation.Cancel(); }
        catch (Exception error) { RetainRetirementFailure(error); throw; }
    }

    // Caller must await ReadTask before invoking this on the presentation
    // thread. Failed native retirement retains the actual callback owner and
    // entered consumer so the same call can be retried; no clean marker is
    // emitted by a failed Dispose or a merely cancelled token.
    internal void RetireAfterReadReturned(Action retireEnteredNative)
    {
        RequirePresentationThread();
        if (_retired) return;
        ArgumentNullException.ThrowIfNull(retireEnteredNative);
        if (_consumersRetired) { RetireMapAndCaller(); return; }
        RequestCancellation();
        if (!ReadTask.IsCompleted)
            throw new NotSupportedException("Queued read cancellation has not returned its real Task yet.");
        try
        {
            // Observe a failed Task without replacing its retained source
            // failure. Successful I/O does not certify native retirement.
            try { _ = ReadTask.GetAwaiter().GetResult(); }
            catch (Exception error) { _retirementFailure ??= error; }
            if (!_consumersRetired)
            {
                _cancellationConsumer ??= _queue.EnterConsumer(_identity, FalloutQueuedReferenceConsumerKind.Cancellation, _owner + ":retirement");
                if (!_nativeRetirementReturned)
                {
                    retireEnteredNative();
                    _nativeRetirementReturned = true;
                }
            }
            if (_assembly is { } assembly)
            {
                _queue.ReturnConsumer(_identity, assembly, _owner + ":assembly", _retirementFailure);
                _assembly = null;
            }
            if (_publication is { } publication)
            {
                _queue.ReturnConsumer(_identity, publication, _owner + ":publication", _retirementFailure);
                _publication = null;
            }
            if (!_consumersRetired)
            {
                if (!_cancellationReturned)
                {
                    _queue.ReturnConsumer(_identity, _cancellationConsumer!.Value, _owner + ":retirement", _retirementFailure);
                    _cancellationReturned = true;
                }
                _queue.ConsumersRetired(_identity, _owner + ":retirement-returned");
                _consumersRetired = true;
            }
            RetireMapAndCaller();
        }
        catch (Exception error) { RetainRetirementFailure(error); throw; }
    }
    private void RetireMapAndCaller()
    {
        // A stale read cannot remove a newly queued object with the same key.
        // The exact value identity remains held until all real children exit.
        _queue.RemoveMatched(SourceReference(), _identity, _owner + ":matched-map-retirement");
        _queue.ReleaseCaller(_identity, _owner + ":caller-retirement");
        _cancellation.Dispose(); _retired = true;
    }
    private FalloutFormKey SourceReference() => _queue.ReferenceOf(_identity);
    private void RetainRetirementFailure(Exception error)
    {
        _retirementFailure ??= error;
        _queue.RetainFailure(_identity, error);
    }
    private void RequireCurrent()
    {
        RequirePresentationThread(); ObjectDisposedException.ThrowIf(_retired, this);
    }
    private void RequirePresentationThread()
    {
        if (Environment.CurrentManagedThreadId != _presentationThread)
            throw new InvalidOperationException("Actual queued-reference native children must be consumed and retired by their owning presentation thread.");
    }
}
