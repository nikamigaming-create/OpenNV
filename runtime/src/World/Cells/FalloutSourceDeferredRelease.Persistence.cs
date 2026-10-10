using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceDeferredRelease
{
    internal string? SaveBlocker
    {
        get
        {
            lock (_gate) return _retired ? "actual-source-deferred-release-retired" :
                _flushing || _enteredObjects.Count != 0 || _validating != 0 ? "actual-source-deferred-release-child-entered" :
                _error is not null ? "actual-source-deferred-release:" + _error :
                _retained.Count != 0 || _queues.Values.Any(queue => queue.Slots.Count != 0) ?
                    "actual-source-deferred-object-native-cold-reconstruction-unowned" : null;
        }
    }

    private FalloutDeferredReleaseQueueSnapshot ReadQueue(Queue queue) => new(queue.Role, queue.Capacity,
        (uint)queue.Slots.Count, (uint)queue.Slots.Count(slot => slot is not null),
        queue.Slots.Select(slot => slot?.Receipt.Loan).ToArray());

    internal object State
    {
        get
        {
            lock (_gate) return new
            {
                source = _source,
                stack = _stack,
                process = _process,
                constructor = _constructor,
                enabled = _enabled,
                sequence = _sequence,
                recursiveOwnerThread = _ownerThread,
                recursiveDepth = _depth,
                flushing = _flushing,
                retired = _retired,
                queues = Enum.GetValues<FalloutDeferredReleaseRole>().Select(role => ReadQueue(_queues[role])).ToArray(),
                retainedObjects = _retained.Values.Select(loan => loan.Receipt).ToArray(),
                last = _last,
                cold = _cold,
                failureType = _failureType,
                error = _error,
                sourceNativeFactories = "actual-class-detach-reference-virtual-allocation-providers-required",
                externalEnableWriters = "independent-source-load-phase-disable-enable-caller-required",
                sourceFNV = "independent-real-caller-TLS-skip-mask-and-try-lock-required",
                saveBlocker = SaveBlocker
            };
        }
    }

    internal FalloutSourceDeferredReleaseSnapshot Capture()
    {
        lock (_gate)
        {
            Require(allowFailure: true);
            if (_retained.Count != 0 || _queues.Values.Any(queue => queue.Slots.Count != 0))
                throw new NotSupportedException("Living deferred native objects/references have no admitted cold reconstruction factory.");
            var result = new FalloutSourceDeferredReleaseSnapshot(Schema, _source, _player, _stack,
                _process, _constructor, _sequence, _enabled,
                Enum.GetValues<FalloutDeferredReleaseRole>().Select(role => ReadQueue(_queues[role])).ToArray(),
                [], _last, _failureType, _error, _cold);
            Validate(result); return result;
        }
    }

    private void Restore(FalloutSourceDeferredReleaseSnapshot saved)
    {
        Validate(saved);
        if (saved.Player != _player || saved.Source != _source || saved.Stack != _stack || saved.Process == _process)
            throw new InvalidDataException("Cold deferred-release owner changed its selected source or retained the previous current process.");
        _enabled = saved.Enabled; _sequence = saved.Sequence; _failureType = saved.FailureType; _error = saved.Error;
        _cold = new(saved.Process, _process, Next());
        // Only the actual empty containers can reconstruct today. Every native
        // allocation, iterator/thread handle and old returned flush stays in
        // the captured epoch; none certifies this fresh current constructor.
    }

    internal static void Validate(FalloutSourceDeferredReleaseSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Source is null || saved.Player is null ||
            string.IsNullOrWhiteSpace(saved.Stack) || saved.Process == Guid.Empty || saved.Constructor == Guid.Empty ||
            saved.Sequence < 0 || saved.Enabled != saved.Source.InitialEnabled || saved.Queues is null ||
            saved.RetainedObjects is null || saved.RetainedObjects.Count != 0 ||
            (saved.Error is null) != (saved.FailureType is null) || saved.Error is not null && string.IsNullOrWhiteSpace(saved.Error))
            throw new InvalidDataException("Deferred continuation omitted current source constructor/enable/ownership fields.");
        saved.Source.Validate(saved.Player);
        var roles = Enum.GetValues<FalloutDeferredReleaseRole>();
        if (saved.Queues.Count != roles.Length || saved.Queues.Any(queue => queue is null) ||
            saved.Queues.Select(queue => queue.Role).Distinct().Count() != roles.Length ||
            saved.Queues.Any(queue => !Enum.IsDefined(queue.Role) || queue.Slots is null ||
                queue.Capacity != 0 || queue.End != 0 || queue.Occupied != 0 || queue.Slots.Count != 0))
            throw new InvalidDataException("Cold source release arrays cannot contain old native pointers or omitted queue roles.");
        if (saved.Last is { } last && (last.Constructor != saved.Constructor || last.ManagedThread <= 0 ||
            last.Retirement != (last.Main is null) || last.Main == Guid.Empty || last.EnabledBefore != saved.Source.InitialEnabled ||
            last.CompletedQueues is < 0 or > 6 || last.ReturnedObjects < 0 || last.Changed < 1 || last.Changed > saved.Sequence ||
            last.Complete && (last.CompletedQueues != 6 || last.Error is not null) ||
            !last.Complete && last.Error is null || (last.Error is null) != (last.FailureType is null)))
            throw new InvalidDataException("Deferred continuation invented an actual returned flush or caller epoch.");
        if (saved.Cold is { } cold && (cold.CurrentProcess != saved.Process || cold.PreviousProcess == Guid.Empty ||
            cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > saved.Sequence))
            throw new InvalidDataException("Deferred continuation lost its genuine new-process handoff.");
    }
}
