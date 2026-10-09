using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutSourceCellReferenceLinks? _sourceCellLinks;
    private FalloutSourceCellReferenceIngestion? _sourceCellIngestion;
    private Exception? _sourceCellLinksConstructionFailure;
    internal bool SourceLinkedPlayerConstructed => _sourceLinkedPlayerParentConstructed;
    internal object? SourceCellLinkState => _sourceCellLinks is null && _sourceCellLinksConstructionFailure is null ? null : new
    { links = _sourceCellLinks?.State, constructionFailure = _sourceCellLinksConstructionFailure?.ToString() };
    internal string? SourceCellLinkSaveBlocker => _sourceCellLinksConstructionFailure is not null ?
        "source-CELL-reference-link-construction-failed" : _sourceCellLinks?.SaveBlocker ??
        (_sourceCellLinks is null ? "selected-source-CELL-linked-reference-constructor-unowned" : null);

    internal void ConfigureSourceCellLinks(string stack, IFalloutSourceCellReferenceIngestion? actualIngestion = null,
        FalloutSourceCellReferenceLinksSnapshot? saved = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sourceCellLinks is not null || _sourceCellLinksConstructionFailure is not null)
            throw new InvalidOperationException("Current CELL links cannot replace or retry their world lifetime.", _sourceCellLinksConstructionFailure);
        try
        {
            var declaration = FalloutSourceCellReferenceLinksDeclaration.ForExecutable(_processRuntimeDeclaration?.ExecutableSha256 ??
                throw new NotSupportedException("CELL links have no actual selected source process declaration."));
            if (actualIngestion is null)
                actualIngestion = _sourceCellIngestion = new(
                    FalloutSourceCellLoaderDeclaration.Read(declaration.EngineSha256), records, stack);
            _sourceCellLinks = new(declaration, stack, CellProcesses.ReadSourceQueueReferences,
                ReadSourceLinkedReference, actualIngestion, saved, ReadCombatActorIdentity);
            ConstructSourcePlayerLinkedParent(saved is not null);
        }
        catch (Exception failure) { _sourceCellLinksConstructionFailure = failure; throw; }
    }
    private FalloutSourceCellReferenceLinks SourceCellLinks => _sourceCellLinks ??
        throw new NotSupportedException("Actual selected ordered CELL linked-reference owner is absent.");
    private FalloutCellProcessReference ReadSourceLinkedReference(FalloutFormKey reference)
    {
        if (!records.TryGetEffective(reference, out var record) || record.IsDeleted ||
            record.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS"))
            throw new NotSupportedException("Linked CELL member has no actual winning placed-reference source; canonical Player/dynamic factories remain separate.");
        var cell = FalloutCellSceneReader.ParentCell(record) ??
            throw new InvalidDataException("Linked CELL member has no immutable source ancestry.");
        return CellProcesses.ReadSourceQueueReferences(cell).References.Single(row => row.Reference == reference);
    }

    internal IReadOnlyList<FalloutFormKey> ReadSourceSandboxCellReferences(FalloutFormKey cell)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Exterior traversal/root ordering is a different original consumer.
        // This getter supplies only the actual selected CELL linked list.
        return SourceCellLinks.Read(cell).OrderedReferences.Select(member => member.Reference).ToArray();
    }
    private void CommitSourceLinkedPlacement(FalloutReferenceInstance instance, FalloutReferencePlacement previous,
        FalloutReferencePlacement current, Action publishPlacement)
    {
        if (previous.Cell == current.Cell) { publishPlacement(); return; }
        // Record-only field scopes have neither an owned installation nor a
        // selected process. Their reference-field store is not native CELL
        // publication; complete campaign admission still reports the missing
        // linked-list owner. Owned/source-selected gameplay requires that owner.
        if (records.OwnedSource is null && _processRuntimeDeclaration is null)
        {
            publishPlacement(); return;
        }
        SourceCellLinks.Insert(instance.Reference, previous.Cell, current.Cell, publishPlacement,
            "actual-placed-reference-cross-CELL-ParentCELL-setter");
    }
    private void RetireSourceCellLinks()
    {
        var failures = new List<Exception>();
        try { _sourceCellLinks?.Dispose(); _sourceCellLinks = null; } catch (Exception error) { failures.Add(error); }
        try { _sourceCellIngestion?.Dispose(); _sourceCellIngestion = null; } catch (Exception error) { failures.Add(error); }
        if (_sourceCellLinks is null) { _sourceLinkedPlayerParentConstructed = false; _sourceLinkedPlayerParent = null; }
        if (failures.Count != 0) throw new AggregateException("CELL linked/source readers retain their independent retirement failures.", failures);
    }
}
