namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutQueuedReferences
{
    internal FalloutSourceTaskCohort ReadSourceTaskManagerCohort()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_callback) throw new NotSupportedException("Actual queued source factory must return before task-manager cohort iteration.");
            var tasks = TaskPriorities.ReadSourceManagerTasks();
            if (!tasks.Select(task => task.Identity).ToHashSet().SetEquals(_objects.Keys))
                throw new InvalidDataException("Source manager cohort omitted an actual queued task constructor or retained object.");
            foreach (var task in tasks)
            {
                var reference = _objects[task.Identity];
                if (task.Retired != (reference.Phase == FalloutQueuedReferencePhase.Destroyed) ||
                    task.Retired && (reference.Mapped || reference.CallerOwned || reference.Consumers.Any(consumer => !consumer.Returned)))
                    throw new InvalidDataException("Source-task retirement disagrees with its actual map/caller/native consumers.");
            }
            // State and key come from their actual field owner. Reader/native
            // phases neither manufacture a scheduler state nor certify a
            // task-manager map insertion. Live missing joins fail at reset.
            return new(_stack, _source.ExecutableSha256, tasks);
        }
    }
}
