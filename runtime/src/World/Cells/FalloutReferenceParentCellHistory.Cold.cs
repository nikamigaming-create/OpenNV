using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceParentCellHistory
{
    private void RestoreHistory(FalloutReferenceParentCellSnapshot saved, long actualRevision, FalloutFormKey? actualCell)
    {
        _ = new FalloutReferenceParentCellValidation(saved);
        if (saved.Retired || saved.Source != Source || saved.Stack != _stack || saved.ExecutableSha256 != _executable ||
            saved.Actor != _actor || saved.Process == _process || saved.Factory == _factory || saved.Constructor == _constructor ||
            saved.Sequence != actualRevision || saved.Cell != actualCell)
            throw new InvalidDataException("Cold ParentCELL changed its actual field, source or fresh publication.");
        _stores.AddRange(saved.Stores); _publications.AddRange(saved.Publications);
        foreach (var store in saved.Stores.Where(value => value.MainInvocation is not null))
            _calls.Add((store.MainInvocation!.Value, store.MainStep!.Value));
        // Restoring the same World scalar records a publication, not a source
        // constructor or Main insertion. Original revisions remain unchanged.
        _publications.Add(new(_process, _factory, _constructor, saved.Process, saved.Factory, actualRevision));
    }
}

internal sealed class FalloutReferenceParentCellValidation
{
    private readonly FalloutReferenceParentCellSnapshot _saved;
    private readonly Dictionary<(Guid Process, Guid Factory, Guid Constructor, long Revision), FalloutFormKey?> _fields = [];
    internal FalloutReferenceParentCellValidation(FalloutReferenceParentCellSnapshot saved)
    {
        saved.Actor.Validate();
        if (saved.Source != FalloutReferenceParentCellHistory.Source || saved.Process == Guid.Empty || saved.Factory == Guid.Empty ||
            saved.Constructor == Guid.Empty || saved.Sequence < 1 || string.IsNullOrWhiteSpace(saved.Stack) ||
            saved.ExecutableSha256.Length != 64 || saved.Actor.EnginePlayer && saved.ExecutableSha256 != saved.Actor.ReferenceSha256 ||
            saved.Publications is not { Count: > 0 } || saved.Stores.Count == 0)
            throw new InvalidDataException("ParentCELL history has no complete retained field publications.");
        var groups = saved.Stores.GroupBy(value => value.Process).ToDictionary(value => value.Key, value => value.ToArray());
        var processes = new HashSet<Guid>(); var factories = new HashSet<Guid>(); var constructors = new HashSet<Guid>();
        var calls = new HashSet<(Guid Main, FalloutMainPlayerCellStep Step)>();
        long revision = 0; FalloutFormKey? cell = null; var failed = false; var storeIndex = 0;
        FalloutReferenceParentCellPublication? previous = null;
        foreach (var publication in saved.Publications)
        {
            if (publication.Process == Guid.Empty || publication.Factory == Guid.Empty || publication.Constructor == Guid.Empty ||
                !processes.Add(publication.Process) || !factories.Add(publication.Factory) || !constructors.Add(publication.Constructor) ||
                publication.PublishedRevision != revision ||
                (previous is null ? publication.PreviousProcess is not null || publication.PreviousFactory is not null :
                    publication.PreviousProcess != previous.Process || publication.PreviousFactory != previous.Factory))
                throw new InvalidDataException("ParentCELL publication changed its actual restored revision or field lifetime.");
            if (previous is not null) _fields.Add((publication.Process, publication.Factory, publication.Constructor, revision), cell);
            foreach (var store in groups.GetValueOrDefault(publication.Process) ?? [])
            {
                if (storeIndex >= saved.Stores.Count || saved.Stores[storeIndex++] != store ||
                    failed || store.Factory != publication.Factory || store.Constructor != publication.Constructor ||
                    store.Actor != saved.Actor || store.Revision != revision + 1 || store.Before != cell ||
                    store.Returned != (store.Failure is null) || store.Returned && !store.Stored ||
                    store.ConstructorStore != (revision == 0) ||
                    store.ConstructorStore && (previous is not null || store.MainInvocation is not null || store.MainStep is not null ||
                        store.After is not null || !store.Stored || !store.Returned) ||
                    !store.ConstructorStore && (store.MainInvocation is not { } main || main == Guid.Empty ||
                        store.MainStep is not (FalloutMainPlayerCellStep.PendingDestination or FalloutMainPlayerCellStep.CellAttach) ||
                        !calls.Add((main, store.MainStep.Value))))
                    throw new InvalidDataException("ParentCELL retained a replayed constructor/Main store or erased a failed prefix.");
                if (store.Stored)
                {
                    revision = store.Revision; cell = store.After;
                    _fields.Add((publication.Process, publication.Factory, publication.Constructor, revision), cell);
                }
                failed = !store.Returned;
            }
            groups.Remove(publication.Process); previous = publication;
        }
        if (groups.Count != 0 || storeIndex != saved.Stores.Count || revision != saved.Sequence || cell != saved.Cell || previous!.Process != saved.Process ||
            previous.Factory != saved.Factory || previous.Constructor != saved.Constructor)
            throw new InvalidDataException("Current ParentCELL changed its complete retained source field history.");
        _saved = saved;
    }
    internal void ValidateRead(FalloutReferenceParentCellRead read)
    {
        if (read.Source != _saved.Source || read.Stack != _saved.Stack || read.Actor != _saved.Actor ||
            read.ExecutableSha256 != _saved.ExecutableSha256 ||
            !_fields.TryGetValue((read.Process, read.Factory, read.Constructor, read.Sequence), out var cell) || cell != read.Cell)
            throw new InvalidDataException("ParentCELL borrow has no identical actual historical or cold field publication.");
    }
}
