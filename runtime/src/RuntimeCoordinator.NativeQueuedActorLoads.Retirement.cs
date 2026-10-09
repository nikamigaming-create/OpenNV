using Godot;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private IReadOnlyList<Task> RequestNativeQueuedActorCallerRetirement(ulong? root = null)
    {
        BindActualNativeQueuedCallerThread(); var errors = new List<Exception>(); var tasks = new List<Task>();
        foreach (var caller in _nativeQueuedActorCallers.Where(pair => root is null || pair.Key == root)
            .SelectMany(pair => pair.Value.Values).ToArray())
        {
            caller.CancelRequested = true;
            if (caller.Load is not { } load) continue;
            tasks.Add(load.ReadTask);
            try { if (!load.Retired) load.RequestCancellation(); }
            catch (Exception error) { caller.Failure ??= error; errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual native caller cancellation retains original failures and live owners.", errors);
        return tasks;
    }
    private void RetireReturnedNativeQueuedActorCallers()
    {
        if (_nativeQueuedActorCallers.Count == 0) return;
        BindActualNativeQueuedCallerThread();
        var errors = new List<Exception>();
        foreach (var (root, callers) in _nativeQueuedActorCallers.ToArray())
        {
            foreach (var caller in callers.Values.ToArray())
            {
                if (!caller.CancelRequested && caller.Failure is null) continue;
                if (caller.Load is not { } load)
                {
                    // The shared registry retains any source constructor
                    // failure. There is no returned native loader to invent.
                    if (!GodotObject.IsInstanceValid(caller.Root)) callers.Remove(caller.Reference);
                    continue;
                }
                try
                {
                    load.RetireDetachedTransferredActor();
                    if (!load.CanRetireNow) continue;
                    load.Dispose(); ReleaseRetiredExteriorNpcOwner(caller); callers.Remove(caller.Reference);
                }
                catch (Exception error) { caller.Failure ??= error; errors.Add(error); }
            }
            if (callers.Count == 0) _nativeQueuedActorCallers.Remove(root);
        }
        if (errors.Count != 0) throw new AggregateException("Actual native caller retirement retains every failed read/native/provider prefix.", errors);
    }
    private void RequireNativeQueuedActorCallersRetired()
    {
        RetireReturnedNativeQueuedActorCallers();
        if (_nativeQueuedActorCallers.Count != 0)
            throw new NotSupportedException("Actual queued NPC callers still retain read/assembly/transferred native publication owners.");
    }
    private void RequireNativeQueuedActorCallersSettledForCapture()
    {
        if (_nativeQueuedActorCallers.Count != 0)
            throw new NotSupportedException("Actual source/native queued actor publication has not returned before capture.");
    }
    private void RetireEnteredNativeQueuedCellRoot(Node3D root, RuntimeNativeCellProcessAttachment owner, bool queued)
    {
        var errors = new List<Exception>();
        // Cancelling a read and destroying its actual native root are
        // independent operations. Keep both original failures if necessary.
        try { _ = RequestNativeQueuedActorCallerRetirement(root.GetInstanceId()); }
        catch (Exception error) { errors.Add(error); }
        try
        {
            _ = owner.BeginDetach(); _nativeCellProcessRetirements.Add(root.GetInstanceId());
            if (queued) root.QueueFree(); else root.Free();
        }
        catch (Exception error) { errors.Add(error); }
        if (!queued)
            try { ObservePendingNativeCellRetirements(); }
            catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0)
            throw new AggregateException("Actual CELL root/read retirement retains independent source and native failures.", errors);
    }
}
