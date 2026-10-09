using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutQueuedReferences
{
    internal FalloutQueuedReferencesSnapshot Capture()
    {
        lock (_gate)
        {
            RequireMutation();
            // A CLR Task, live assembly or native publication invocation has
            // no serializable execution stack. Keep it an explicit blocker;
            // never replace it with an empty new-process map.
            if (SaveBlocker is { } failure) throw new NotSupportedException(failure);
            return new(Schema, _stack, _source.Contract, _process, _sequence,
                _objects.Values.OrderBy(value => value.Entered).ToArray(),
                _map.Select(value => new FalloutQueuedReferenceMapEntry(value.Key, value.Value)).ToArray(), _cold);
        }
    }
    private void Restore(FalloutQueuedReferencesSnapshot snapshot)
    {
        Validate(snapshot);
        if (snapshot.Stack != _stack || snapshot.Contract != _source.Contract || snapshot.CapturedProcess == _process)
            throw new InvalidDataException("Queued-reference cold owner lost its selected source/new-process epoch.");
        foreach (var item in snapshot.Objects)
        {
            if (Identity(item.Source.Reference) != item.Source)
                throw new InvalidDataException("Saved queued-reference value differs from its current source winner/master/factory.");
            _objects.Add(item.Identity, item);
        }
        foreach (var item in snapshot.Map) _map.Add(item.Reference, item.Value);
        _sequence = snapshot.Sequence;
        _cold = new(snapshot.CapturedProcess, _process, Next());
    }
    internal static void Validate(FalloutQueuedReferencesSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Schema != Schema || string.IsNullOrWhiteSpace(snapshot.Stack) || !Hash(snapshot.Contract) ||
            snapshot.CapturedProcess == Guid.Empty || snapshot.Sequence < 0 || snapshot.Objects is null || snapshot.Map is null ||
            snapshot.Objects.Any(value => value is null) || snapshot.Map.Any(value => value is null) ||
            snapshot.Objects.Select(value => value.Identity).Distinct().Count() != snapshot.Objects.Count ||
            snapshot.Map.Select(value => value.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != snapshot.Map.Count ||
            snapshot.Map.Select(value => value.Value).Distinct().Count() != snapshot.Map.Count ||
            snapshot.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != snapshot.CapturedProcess ||
                cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > snapshot.Sequence))
            throw new InvalidDataException("Saved source queued-reference graph is incomplete or has a foreign process epoch.");
        foreach (var item in snapshot.Objects)
        {
            ValidateSource(item.Source);
            if (item.Identity == Guid.Empty || item.Entered < 1 || item.Changed < item.Entered || item.Changed > snapshot.Sequence ||
                string.IsNullOrWhiteSpace(item.Owner) || item.Consumers is null || item.Consumers.Any(value => value is null) ||
                item.Consumers.Select(value => value.Identity).Distinct().Count() != item.Consumers.Count ||
                item.Phase != FalloutQueuedReferencePhase.Destroyed || item.Mapped || item.CallerOwned || item.OpaqueOwnership || item.Boundary is not null ||
                item.Consumers.Any(value => !value.Returned) || item.Failure is not null)
                throw new NotSupportedException("Saved queued reference retains an unowned actual work/native continuation.");
            long prior = item.Entered;
            foreach (var consumer in item.Consumers)
            {
                if (!Enum.IsDefined(consumer.Kind) || consumer.Identity == Guid.Empty || consumer.Process == Guid.Empty ||
                    string.IsNullOrWhiteSpace(consumer.Owner) || consumer.Entered <= prior || consumer.Changed < consumer.Entered ||
                    consumer.Changed > item.Changed || !consumer.Returned || consumer.Failure is not null)
                    throw new InvalidDataException("Saved queued-reference consumer has no exact returned source invocation.");
                // Cancellation can enter while an assembly/publication is
                // still held. Enter order and true return order are distinct.
                prior = consumer.Entered;
            }
            var work = item.Consumers.Where(value => value.Kind != FalloutQueuedReferenceConsumerKind.Cancellation).ToArray();
            if (work.Length > 3 || !work.Select(value => value.Kind).SequenceEqual(new[] {
                    FalloutQueuedReferenceConsumerKind.Read, FalloutQueuedReferenceConsumerKind.Assembly,
                    FalloutQueuedReferenceConsumerKind.Publication }.Take(work.Length)) ||
                work.Zip(work.Skip(1)).Any(pair => pair.Second.Entered <= pair.First.Changed) ||
                item.Consumers.Count(value => value.Kind == FalloutQueuedReferenceConsumerKind.Cancellation) > 1 ||
                item.Consumers.LastOrDefault()?.Kind is not (FalloutQueuedReferenceConsumerKind.Publication or FalloutQueuedReferenceConsumerKind.Cancellation) ||
                item.Consumers.LastOrDefault() is { Kind: FalloutQueuedReferenceConsumerKind.Cancellation } cancellation &&
                    (!item.CancelRequested || work.Any(value => value.Changed >= cancellation.Changed)) ||
                item.Consumers.LastOrDefault()?.Kind == FalloutQueuedReferenceConsumerKind.Publication &&
                    (item.CancelRequested || work.Length != 3))
                throw new InvalidDataException("Saved queued reference invented original consumer retirement.");
        }
        if (snapshot.Map.Count != 0)
            throw new NotSupportedException("Cold queued-reference map has no serialized actual CLR/native continuation owner.");
    }
}
