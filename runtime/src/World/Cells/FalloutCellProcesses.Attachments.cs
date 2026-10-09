using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCellProcesses
{
    internal void CompleteChild(Guid attachment, FalloutCellProcessReference source,
        FalloutCellProcessChildPhase disposition, IReadOnlyList<ulong> actualNativeObjects, string originalOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalOwner);
        var owner = RequireAttachment(attachment);
        Operation(owner.CellEpochs.Keys.ToArray(), originalOwner, () =>
        {
            var index = ChildIndex(owner, source);
            if (owner.NativeRoot == 0 || owner.RootPublished || owner.Retired || owner.Children[index].Phase != FalloutCellProcessChildPhase.Pending ||
                disposition is not (FalloutCellProcessChildPhase.Published or FalloutCellProcessChildPhase.SourceDisabled or FalloutCellProcessChildPhase.SourceNoDraw) ||
                actualNativeObjects is null || actualNativeObjects.Any(identity => identity == 0) ||
                actualNativeObjects.Distinct().Count() != actualNativeObjects.Count ||
                disposition == FalloutCellProcessChildPhase.Published && actualNativeObjects.Count == 0 ||
                disposition != FalloutCellProcessChildPhase.Published && actualNativeObjects.Count != 0)
                throw new InvalidDataException("CELL child completion has no exact pending source/native operation.");
            RequireExclusiveNativeChildren(owner, index, actualNativeObjects);
            var children = owner.Children.ToArray();
            children[index] = children[index] with { Phase = disposition, NativeObjects = actualNativeObjects.ToArray(), Owner = originalOwner };
            _attachments[attachment] = owner with { Children = children };
        });
    }
    internal void FailChild(Guid attachment, FalloutCellProcessReference source, Exception error, string originalOwner,
        IReadOnlyList<ulong>? stillOwnedNativeObjects = null)
    {
        ArgumentNullException.ThrowIfNull(error); ArgumentException.ThrowIfNullOrWhiteSpace(originalOwner);
        var owner = RequireAttachment(attachment); var index = ChildIndex(owner, source);
        var retained = stillOwnedNativeObjects?.ToArray() ?? owner.Children[index].NativeObjects.ToArray();
        if (retained.Any(identity => identity == 0) || retained.Distinct().Count() != retained.Length)
            throw new InvalidDataException("Failed CELL child has invalid still-owned native identities.");
        var failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
        var children = owner.Children.ToArray();
        children[index] = children[index] with { Phase = FalloutCellProcessChildPhase.Failed, NativeObjects = retained,
            Owner = originalOwner, Failure = failure };
        _attachments[attachment] = owner with { Children = children, Failure = owner.Failure ?? failure };
        foreach (var cell in owner.CellEpochs.Keys) _cells[cell] = Require(cell) with { Failure = Require(cell).Failure ?? failure };
    }
    internal void JoinNativeChildConsumers(Guid attachment, FalloutCellProcessReference source,
        IReadOnlyList<ulong> actualNativeObjects, string originalOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalOwner); var owner = RequireAttachment(attachment);
        Operation(owner.CellEpochs.Keys.ToArray(), originalOwner, () =>
        {
            var index = ChildIndex(owner, source); var child = owner.Children[index];
            if (owner.NativeRoot == 0 || owner.RootPublished || owner.Retired || child.Phase is
                FalloutCellProcessChildPhase.Retired or FalloutCellProcessChildPhase.Failed ||
                actualNativeObjects.Any(identity => identity == 0) || actualNativeObjects.Distinct().Count() != actualNativeObjects.Count)
                throw new InvalidDataException("CELL auxiliary consumer crossed its actual pending child/native work.");
            var joined = child.NativeObjects.Concat(actualNativeObjects).Distinct().ToArray();
            RequireExclusiveNativeChildren(owner, index, joined);
            if (child.Phase == FalloutCellProcessChildPhase.Pending && joined.Length == 0)
                throw new NotSupportedException("Actual pending source primitive returned without its native contact consumer.");
            var children = owner.Children.ToArray();
            children[index] = child with { NativeObjects = joined,
                Phase = joined.Length == 0 ? child.Phase : FalloutCellProcessChildPhase.Published,
                Owner = originalOwner };
            _attachments[attachment] = owner with { Children = children };
        });
    }
    internal void PublishRoot(Guid attachment, ulong actualRoot, string originalOwner)
    {
        var owner = RequireAttachment(attachment);
        Operation(owner.CellEpochs.Keys.ToArray(), originalOwner, () =>
        {
            if (owner.NativeRoot != actualRoot || actualRoot == 0 || owner.RootPublished || owner.Retired || owner.Failure is not null ||
                owner.Children.Any(child => child.Phase is FalloutCellProcessChildPhase.Pending or FalloutCellProcessChildPhase.Failed) ||
                owner.CellEpochs.Any(pair => RequireHealthy(pair.Key).Epoch != pair.Value || Require(pair.Key).Phase != FalloutCellProcessPhase.Attaching))
                throw new InvalidDataException("Actual CELL attachment cannot publish an incomplete or foreign root/child graph.");
            for (var index = 0; index < owner.Children.Count; index++)
                RequireExclusiveNativeChildren(owner, index, owner.Children[index].NativeObjects);
            _attachments[attachment] = owner with { RootPublished = true };
            foreach (var cell in owner.CellEpochs.Keys)
                Write(cell, FalloutCellProcessOperation.CompleteAttach, FalloutCellProcessPhase.Attached, attachment, originalOwner);
            if (_cold?.AwaitingNativeAttachments.Contains(attachment) == true)
                _cold = _cold with { AwaitingNativeAttachments = _cold.AwaitingNativeAttachments.Where(identity => identity != attachment).ToArray() };
        });
    }
    internal bool BeginDetach(Guid attachment, ulong actualRoot, string originalOwner)
    {
        var owner = RequireAttachment(attachment); var entered = false;
        Operation(owner.CellEpochs.Keys.ToArray(), originalOwner, () =>
        {
            if (owner.NativeRoot != actualRoot || owner.Retired) throw new InvalidDataException("CELL detach has a foreign/retired native owner.");
            // Original public detach returns without phase writes outside5/6.
            if (owner.CellEpochs.Keys.All(cell => Require(cell).Phase is not
                (FalloutCellProcessPhase.Attaching or FalloutCellProcessPhase.Attached))) return;
            if (owner.CellEpochs.Any(pair => Require(pair.Key).Epoch != pair.Value || Require(pair.Key).Phase is not
                (FalloutCellProcessPhase.Attaching or FalloutCellProcessPhase.Attached)))
                throw new InvalidDataException("CELL group detach crosses another actual source operation.");
            entered = true;
            foreach (var cell in owner.CellEpochs.Keys)
                Write(cell, FalloutCellProcessOperation.BeginDetach, FalloutCellProcessPhase.Detaching, attachment, originalOwner);
        });
        return entered;
    }
    internal void RetireChild(Guid attachment, FalloutCellProcessReference source, string originalOwner)
    {
        var owner = RequireAttachment(attachment); var index = ChildIndex(owner, source);
        Operation(owner.CellEpochs.Keys.ToArray(), originalOwner, () =>
        {
            if (owner.CellEpochs.Keys.Any(cell => Require(cell).Phase != FalloutCellProcessPhase.Detaching))
                throw new InvalidOperationException("Actual CELL child retirement precedes its source detach entry.");
            var children = owner.Children.ToArray();
            children[index] = children[index] with { Phase = FalloutCellProcessChildPhase.Retired, NativeObjects = [] };
            _attachments[attachment] = owner with { Children = children };
        });
    }
    internal void CompleteDetach(Guid attachment, ulong retiredRoot, string originalOwner)
    {
        var owner = RequireAttachment(attachment);
        Operation(owner.CellEpochs.Keys.ToArray(), originalOwner, () =>
        {
            if (owner.NativeRoot != retiredRoot || owner.Retired || owner.Children.Any(child => child.Phase != FalloutCellProcessChildPhase.Retired) ||
                owner.CellEpochs.Any(pair => Require(pair.Key).Epoch != pair.Value || Require(pair.Key).Phase != FalloutCellProcessPhase.Detaching))
                throw new InvalidDataException("CELL detach cannot discard outstanding child/native ownership.");
            _attachments[attachment] = owner with { Retired = true, RootPublished = false, NativeRoot = 0 };
            foreach (var cell in owner.CellEpochs.Keys)
                Write(cell, FalloutCellProcessOperation.CompleteDetach, FalloutCellProcessPhase.DataLoaded, attachment, originalOwner);
        });
    }
    internal FalloutCellProcessAttachment ReadAttachment(Guid identity) => RequireAttachment(identity);
    internal void RequireCurrentAttachmentSelection(Guid identity, FalloutCellScene scene)
    {
        var owner = RequireAttachment(identity);
        Operation(owner.CellEpochs.Keys.ToArray(), "actual-current-native-CELL-selection", () =>
        {
            var current = _source.ValidateCurrentScene(scene, owner.CellEpochs.Keys.ToArray());
            if (!current.Select(child => child.Source).SequenceEqual(owner.Children.Select(child => child.Source)))
                throw new NotSupportedException("Actual shared-root CELL/reference graph mutation has no joined source child lifecycle operation.");
        });
    }
    private void RequireExclusiveNativeChildren(FalloutCellProcessAttachment owner, int child,
        IReadOnlyList<ulong> identities)
    {
        var claimed = identities.ToHashSet();
        foreach (var attachment in _attachments.Values.Where(attachment => !attachment.Retired))
        {
            if (claimed.Contains(attachment.NativeRoot) || attachment.Children.Where((_, index) =>
                attachment.Identity != owner.Identity || index != child).Any(other => other.NativeObjects.Any(claimed.Contains)))
                throw new InvalidDataException("CELL child borrowed a native root or another source child's living ownership.");
        }
    }
    private FalloutCellProcessAttachment RequireAttachment(Guid identity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _attachments.TryGetValue(identity, out var state) && state.Process == _process ? state :
            throw new InvalidDataException("CELL attachment does not belong to this actual process epoch.");
    }
    private static int ChildIndex(FalloutCellProcessAttachment attachment, FalloutCellProcessReference source)
    {
        var matches = attachment.Children.Select((child, index) => (child, index)).Where(row => row.child.Source == source).ToArray();
        return matches.Length == 1 ? matches[0].index : throw new InvalidDataException("CELL child acknowledgement has a foreign/ambiguous source owner.");
    }
}
