using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    // Decoder/assembly capacity and retained source publication are distinct.
    // A finished body releases its decode slot while the same queued object
    // remains owned by the real grid transaction until actual publication.
    private readonly HashSet<NativeQueuedActorCaller> _nativeGridNpcPublications = [];
    private sealed class ExteriorNpcPreparation : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        internal readonly NativeQueuedActorCaller Caller;
        internal FalloutNpcAppearance Appearance { get; }
        internal Task ReadTask => (Caller.Load ??
            throw new InvalidOperationException("Actual exterior native queued constructor did not return.")).ReadTask;
        internal bool Transferred;
        private bool _disposed;
        internal ExteriorNpcPreparation(RuntimeCoordinator owner, Node3D root, FalloutNpcAppearance appearance,
            CancellationToken cancellation)
        {
            Appearance = appearance; _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            try { Caller = owner.BeginNativeQueuedNpc(root, appearance, "actual-exterior-prepared-reference-Load3D", _cancellation.Token); }
            catch (Exception original)
            {
                try { _cancellation.Cancel(); }
                catch (Exception cleanup) { throw new AggregateException("Actual exterior source construction and cancellation both failed.", original, cleanup); }
                finally { _cancellation.Dispose(); }
                throw;
            }
        }
        internal bool Advance(RuntimeCoordinator owner, FalloutCellScene cell, out RuntimeNativeNpc? actor)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var ready = owner.AdvanceNativeQueuedNpc(Caller, cell, out actor);
            if (ready) Transferred = true;
            return ready;
        }
        internal void RequestCancellation()
        {
            if (_disposed) return;
            Caller.CancelRequested = true;
            var errors = new List<Exception>();
            try { _cancellation.Cancel(); } catch (Exception error) { errors.Add(error); }
            try { if (Caller.Load is { Retired: false } load) load.RequestCancellation(); }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0)
            {
                var failure = new AggregateException("Actual exterior token and source-cancellation consumers retain their failures.", errors);
                Caller.Failure ??= failure; Caller.Load?.RetainCallerFailure(failure);
                throw failure;
            }
        }
        public void Dispose()
        {
            if (_disposed) return;
            if (!ReadTask.IsCompleted)
                throw new NotSupportedException("Exterior decode-token provider still owns an entered actual source read.");
            _cancellation.Dispose(); _disposed = true;
        }
    }
    private bool ReleaseExteriorNpc(ExteriorNpcPreparation? preparation, bool cancel)
    {
        if (preparation is null) return true;
        if (preparation.Transferred && !cancel && preparation.Caller.Failure is null)
        {
            preparation.Dispose(); _nativeGridNpcPreparations.Remove(preparation);
            // Its actual publication owner remains in the separate set.
            return true;
        }
        preparation.RequestCancellation();
        var load = preparation.Caller.Load;
        if (load is not null && !load.CanRetireNow) return false;
        load?.Dispose(); preparation.Dispose();
        ForgetNativeQueuedActorCaller(preparation.Caller);
        _nativeGridNpcPreparations.Remove(preparation); _nativeGridNpcPublications.Remove(preparation.Caller);
        return true;
    }
    private void RequestExteriorQueuedNpcRetirement()
    {
        var errors = new List<Exception>();
        foreach (var preparation in _nativeGridNpcPreparations.ToArray())
            try { preparation.RequestCancellation(); } catch (Exception error) { errors.Add(error); }
        foreach (var caller in _nativeGridNpcPublications.ToArray())
        {
            caller.CancelRequested = true;
            try { if (caller.Load is { Retired: false } load) load.RequestCancellation(); }
            catch (Exception error) { caller.Failure ??= error; errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual exterior caller cancellation retains every failed child/owner.", errors);
    }
    private void ReapExteriorQueuedNpcRetirements()
    {
        foreach (var preparation in _nativeGridNpcPreparations.ToArray())
            if (preparation.Caller.CancelRequested && preparation.ReadTask.IsCompleted &&
                (preparation.Caller.Load is null || preparation.Caller.Load.CanRetireNow))
                _ = ReleaseExteriorNpc(preparation, cancel: true);
        RetireReturnedNativeQueuedActorCallers();
        _nativeGridNpcPublications.RemoveWhere(caller => caller.Load?.Retired == true);
    }
    private void ReleaseRetiredExteriorNpcOwner(NativeQueuedActorCaller caller)
    {
        if (caller.Load is not { Retired: true })
            throw new InvalidOperationException("Exterior token publication still owns its actual source/native reader.");
        foreach (var preparation in _nativeGridNpcPreparations.Where(value => ReferenceEquals(value.Caller, caller)).ToArray())
        {
            preparation.Dispose(); _nativeGridNpcPreparations.Remove(preparation);
        }
        _nativeGridNpcPublications.Remove(caller);
    }
    private void PublishExteriorQueuedNpcCallers(Node3D root)
    {
        // The current attachment must first record the actual grid/reference
        // graph and native children. A renderer residency set is no receipt.
        foreach (var caller in _nativeGridNpcPublications.Where(caller => caller.RootIdentity == root.GetInstanceId()).ToArray())
        {
            if (caller.CancelRequested || caller.Failure is not null)
                throw new NotSupportedException("Actual exterior publication retains a cancelled or failed source caller.");
            var load = caller.Load ?? throw new InvalidOperationException("Actual exterior source constructor did not return.");
            try
            {
                load.PublicationReturned(); load.Dispose(); ForgetNativeQueuedActorCaller(caller);
                _nativeGridNpcPublications.Remove(caller);
            }
            catch (Exception error) { RetainNativeQueuedCallerFailure(caller, error); throw; }
        }
    }
    private void ForgetNativeQueuedActorCaller(NativeQueuedActorCaller caller)
    {
        if (!_nativeQueuedActorCallers.TryGetValue(caller.RootIdentity, out var callers) ||
            !callers.TryGetValue(caller.Reference, out var current) || !ReferenceEquals(current, caller) ||
            caller.Load is { Retired: false })
            throw new InvalidDataException("Actual native caller retirement lost its exact retained publication/epoch object.");
        callers.Remove(caller.Reference);
        if (callers.Count == 0) _nativeQueuedActorCallers.Remove(caller.RootIdentity);
    }
}
