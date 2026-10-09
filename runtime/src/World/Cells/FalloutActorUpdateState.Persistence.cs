using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorUpdateState
{
    internal FalloutActorUpdateSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new NotSupportedException("An actual actor update mutation is in flight.");
        return new(Schema, _stack, _source.Contract, _process, _sequence, _actors.Values.ToArray(), _receipts.ToArray(), _cold);
    }
    private void Restore(FalloutActorUpdateSnapshot saved)
    {
        if (saved.Schema != Schema || saved.Stack != _stack || saved.Contract != _source.Contract ||
            saved.CapturedProcess == Guid.Empty || saved.Sequence is < 0 or long.MaxValue ||
            saved.Actors is null || saved.Receipts is null)
            throw new InvalidDataException("Actor update continuation has an incomplete or foreign source/process identity.");
        var actorRows = new Dictionary<FalloutFormKey, FalloutActorUpdateEntry>(FalloutFormKeyComparer.Instance);
        foreach (var actor in saved.Actors)
        {
            actor.Source.Validate();
            if (actor.Source != ReadIdentity(actor.Source.Reference) || actor.Value > 1 || actor.Created <= 0 ||
                actor.Created > actor.LastChanged || actor.LastChanged > saved.Sequence ||
                actor.Failure is not null && string.IsNullOrWhiteSpace(actor.Failure) || !actorRows.TryAdd(actor.Source.Reference, actor))
                throw new InvalidDataException("Actor update constructor/current value/source winner is malformed.");
        }
        var previous = 0L;
        var replay = actorRows.ToDictionary(row => row.Key, row => _source.Initial, FalloutFormKeyComparer.Instance);
        var retired = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        foreach (var receipt in saved.Receipts)
        {
            if (receipt.Sequence <= previous || receipt.Sequence > saved.Sequence ||
                !actorRows.TryGetValue(receipt.Actor, out var actor) || receipt.Sequence <= actor.Created ||
                string.IsNullOrWhiteSpace(receipt.Owner) || receipt.Before > 1 || receipt.After > 1 ||
                retired.Contains(receipt.Actor) || replay[receipt.Actor] != receipt.Before)
                throw new InvalidDataException("Actor update mutation history lost its actual order or prefix.");
            var expected = receipt.Operation switch
            {
                FalloutActorUpdateMutation.SetActorsAI => FalloutActorUpdateDeclaration.Normalize(receipt.Argument ??
                    throw new InvalidDataException("SetActorsAI history omitted its actual signed argument.")),
                FalloutActorUpdateMutation.ToggleActorsAI => FalloutActorUpdateDeclaration.Toggle(receipt.Before),
                FalloutActorUpdateMutation.ActorDataLoadReset => _source.Initial,
                FalloutActorUpdateMutation.SourceRetirement => receipt.Before,
                _ => throw new InvalidDataException("Actor update history has an unknown source operation."),
            };
            if (receipt.After != expected || receipt.Operation != FalloutActorUpdateMutation.SetActorsAI && receipt.Argument is not null)
                throw new InvalidDataException("Actor update mutation semantics drifted.");
            if (receipt.Operation == FalloutActorUpdateMutation.SourceRetirement) retired.Add(receipt.Actor);
            replay[receipt.Actor] = receipt.After; previous = receipt.Sequence;
        }
        var lastReceipts = saved.Receipts.GroupBy(row => row.Actor, FalloutFormKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.Last().Sequence, FalloutFormKeyComparer.Instance);
        foreach (var actor in actorRows.Values)
        {
            var last = lastReceipts.TryGetValue(actor.Source.Reference, out var receipt) ? receipt : actor.Created;
            if (actor.Value != replay[actor.Source.Reference] || actor.Retired != retired.Contains(actor.Source.Reference) ||
                actor.LastChanged < last || actor.Failure is null && actor.LastChanged != last)
                throw new InvalidDataException("Actor update current state differs from its real constructor/mutation prefix.");
        }
        if (saved.ColdHandoff is { } handoff && (handoff.PreviousProcess == Guid.Empty || handoff.CurrentProcess != saved.CapturedProcess ||
            handoff.PreviousProcess == handoff.CurrentProcess || handoff.Sequence > saved.Sequence || handoff.Sequence < 0))
            throw new InvalidDataException("Actor update cold process handoff is malformed.");
        foreach (var (key, actor) in actorRows) _actors.Add(key, actor);
        _receipts.AddRange(saved.Receipts); _sequence = saved.Sequence;
        _cold = new(saved.CapturedProcess, _process, Next());
    }
    internal void RequireConstructedActors(IEnumerable<FalloutFormKey> current)
    {
        var expected = current.ToHashSet(FalloutFormKeyComparer.Instance);
        if (!expected.SetEquals(_actors.Keys)) throw new InvalidDataException("Actor update continuation differs from the actual constructed actor set.");
    }
}
