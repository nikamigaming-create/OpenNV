namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceTaskManager
{
    internal FalloutSourceTaskManagerSnapshot Capture()
    {
        lock (_gate)
        {
            Require(allowFailure: true);
            if (_heldReferences.Count != 0 || _buckets.Any(bucket => bucket.Count != 0) || ReadCohort().Tasks.Any(task => !task.Retired))
                throw new NotSupportedException("Active source tasks/iterator references lack genuine native cold reconstruction.");
            var result = new FalloutSourceTaskManagerSnapshot(Schema, _source, _player, _stack, _process, _constructor,
                _sequence, _threads.Values.ToArray(), _last, _failureType, _error, _cold);
            Validate(result); return result;
        }
    }

    private void Restore(FalloutSourceTaskManagerSnapshot saved)
    {
        Validate(saved);
        if (saved.Player != _player || saved.Source != _source || saved.Stack != _stack || saved.Process == _process)
            throw new InvalidDataException("Task-manager cold construction changed its actual source/current epoch.");
        _sequence = saved.Sequence; _failureType = saved.FailureType; _error = saved.Error;
        _cold = new(saved.Process, _process, Next());
        // Current constructor really zeroes all buckets and creates a fresh
        // iterator table. Old process/thread slots and last-return receipts
        // cannot certify any newly constructed caller or living task.
    }

    internal static void Validate(FalloutSourceTaskManagerSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Source is null || saved.Player is null ||
            string.IsNullOrWhiteSpace(saved.Stack) || saved.Process == Guid.Empty || saved.Constructor == Guid.Empty ||
            saved.Sequence < 0 || saved.Threads is null || saved.Threads.Count >= saved.Source.IteratorSlots ||
            (saved.Error is null) != (saved.FailureType is null) || saved.Error is not null && string.IsNullOrWhiteSpace(saved.Error))
            throw new InvalidDataException("Task-manager continuation omitted genuine source constructor/lifetime state.");
        saved.Source.Validate(saved.Player);
        if (saved.Threads.Any(thread => thread is null || thread.ManagedThread <= 0 || thread.Slot < 1 ||
                thread.Slot >= saved.Source.IteratorSlots || thread.Process != saved.Process || thread.Constructor != saved.Constructor ||
                thread.Changed < 1 || thread.Changed > saved.Sequence) ||
            saved.Threads.Select(thread => thread.ManagedThread).Distinct().Count() != saved.Threads.Count ||
            saved.Threads.Select(thread => thread.Slot).Distinct().Count() != saved.Threads.Count)
            throw new InvalidDataException("Task-manager snapshot fabricated a current iterator/thread registration.");
        if (saved.Last is { } call)
        {
            if (call.Constructor != saved.Constructor || call.Main == Guid.Empty || call.Retirement != (call.Main is null) ||
                !saved.Threads.Any(thread => thread.ManagedThread == call.ManagedThread) || call.ExaminedBuckets < 0 ||
                call.ExaminedBuckets > saved.Source.BucketCount || call.Visited is null || call.Visited.Any(id => id == Guid.Empty) ||
                call.Visited.Distinct().Count() != call.Visited.Count || call.Returned < 0 || call.Returned > call.Visited.Count ||
                call.Changed < 1 || call.Changed > saved.Sequence || call.Complete &&
                    (call.ExaminedBuckets != saved.Source.BucketCount || call.Returned != call.Visited.Count || call.Error is not null) ||
                !call.Complete && call.Error is null || (call.Error is null) != (call.FailureType is null))
                throw new InvalidDataException("Task-manager snapshot invented a completed original reset prefix.");
        }
        if (saved.Cold is { } cold && (cold.CurrentProcess != saved.Process || cold.PreviousProcess == Guid.Empty ||
            cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > saved.Sequence))
            throw new InvalidDataException("Task-manager snapshot lost its actual new-process constructor handoff.");
    }
}
