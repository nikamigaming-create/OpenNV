namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutQueuedTaskPriorities
{
    internal IReadOnlyList<FalloutSourceTaskCohortEntry> ReadSourceManagerTasks()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_busy) throw new NotSupportedException("Actual source task key/state consumer must return before manager iteration.");
            return _tasks.Values.Select(task => new FalloutSourceTaskCohortEntry(task.Identity, task.Key,
                task.State, task.Retired, task.Failure, task.Boundary)).ToArray();
        }
    }
}
