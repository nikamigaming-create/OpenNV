namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInhibitionState
{
    internal FalloutPlayerInhibitionRead ReadLateFrameLane(FalloutPlayerSourceCaller caller,
        FalloutPlayerInhibitionLane lane)
    {
        if (lane is not (FalloutPlayerInhibitionLane.First or FalloutPlayerInhibitionLane.Second))
            throw new InvalidDataException("Late Player frame selected an unrelated inhibition lane.");
        RequireOwner();
        _reading = true;
        var fault = _fault;
        try
        {
            RequireCaller(FalloutPlayerInhibitionConsumer.LateFrame, caller);
            var current = _bytes[(int)lane];
            if (!current.Available) throw new NotSupportedException(ObservedWriterBoundary + ":" + lane);
            var read = new FalloutPlayerInhibitionRead(_source.Identity, _stack, _process, _factory,
                caller, FalloutPlayerInhibitionConsumer.LateFrame, _sequence, lane, current.Value);
            _requireActualCaller(FalloutPlayerInhibitionConsumer.LateFrame, caller);
            if (_thread != Environment.CurrentManagedThreadId || _fault != fault || _sequence != read.Revision)
                throw new InvalidOperationException("Late inhibition read changed its actual current owner or swallowed reentry.");
            return read;
        }
        finally { _reading = false; }
    }
}
