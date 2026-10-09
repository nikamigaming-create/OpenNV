using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private readonly Dictionary<ulong, RuntimeNativeCellProcessAttachment> _nativeCellProcessAttachments = [];
    private readonly HashSet<ulong> _nativeCellProcessRetirements = [];
    private IDisposable? _nativePlayerProcessCellLease;

    private void EnterNativeSourceCellAttachment(Node3D root, FalloutCellScene scene,
        IReadOnlyList<FalloutFormKey>? sourceCells)
    {
        var world = _nativeReferences ?? throw new InvalidOperationException("CELL attachment has no current reference world.");
        var cells = sourceCells ?? [scene.Cell.FormKey];
        var identity = root.GetInstanceId();
        if (_nativeCellProcessAttachments.ContainsKey(identity))
            throw new InvalidOperationException("Actual native CELL root was constructed twice.");
        var saved = world.CellProcesses.Capture();
        var cold = saved.ColdHandoff?.AwaitingNativeAttachments.Select(previous =>
            saved.Attachments.Single(attachment => attachment.Identity == previous))
            .Where(attachment => attachment.CellEpochs.Keys.ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(cells)).ToArray() ?? [];
        if (cold.Length > 1) throw new InvalidDataException("Actual cold CELL root has ambiguous source attachment identity.");
        var candidate = new RuntimeNativeCellProcessAttachment(world, root, scene, cells,
            cold.Length == 1 ? cold[0].Identity : null);
        _nativeCellProcessAttachments.Add(identity, candidate);
    }
    private RuntimeNativeCellProcessAttachment RequireNativeSourceCellAttachment(Node3D root) =>
        _nativeCellProcessAttachments.TryGetValue(root.GetInstanceId(), out var owner) ? owner :
            throw new NotSupportedException("Actual native CELL factory did not enter the shared source attachment owner.");
    private void RequireNativeSourceCellSelection(Node3D root, FalloutCellScene scene) =>
        _nativeReferences!.CellProcesses.RequireCurrentAttachmentSelection(RequireNativeSourceCellAttachment(root).Identity, scene);
    private void CompleteNativeSourceCellReference(Node3D root, FalloutCellScene cell,
        FalloutPlacedReference reference, int firstChild) =>
        RequireNativeSourceCellAttachment(root).ReferenceReturned(reference, cell.BaseObjects[reference.Base],
            root.GetChildren().Skip(firstChild).ToArray());
    private void FailNativeSourceCellReference(Node3D root, FalloutPlacedReference reference, Exception error, int firstChild) =>
        RequireNativeSourceCellAttachment(root).ReferenceFailed(reference.FormKey, error,
            root.GetChildren().Skip(firstChild).ToArray());
    private void PublishNativeSourceCellAttachment(Node3D root, RuntimeNativeReferenceEvents events)
    {
        var owner = RequireNativeSourceCellAttachment(root);
        if (_nativeReferences!.CellProcesses.ReadAttachment(owner.Identity).RootPublished) return;
        owner.Publish(events);
    }
    private void AttachCurrentNativePlayerCell()
    {
        if (_nativePlayerProcessCellLease is not null) return;
        var player = _nativePlayer ?? throw new InvalidOperationException("Player current CELL has no real native player.");
        var world = _nativeReferences ?? throw new InvalidOperationException("Player current CELL has no retained world.");
        _nativePlayerProcessCellLease = world.BindActualPlayerProcessCell(() =>
        {
            if (!GodotObject.IsInstanceValid(player) || !player.IsInsideTree() || player.IsQueuedForDeletion())
                throw new NotSupportedException("Actual Player current-CELL lifetime is not published or has retired.");
            return _nativeActiveCell?.Cell.FormKey;
        });
        (_nativeOpeningStageDriver ?? throw new InvalidOperationException("Current native CELL capture has no driver."))
            .BindCurrentNativeCellCapture(ObserveNativeCellProcessesForCapture);
    }
    private void ObserveNativeCellProcessesForCapture()
    {
        ObservePendingNativeCellRetirements();
        foreach (var (root, owner) in _nativeCellProcessAttachments)
        {
            var attachment = _nativeReferences!.CellProcesses.ReadAttachment(owner.Identity);
            if (!attachment.Retired)
            {
                if (_nativeCellProcessRetirements.Contains(root))
                    throw new NotSupportedException("Actual CELL native retirement has not completed before capture.");
                owner.ObserveForCapture();
            }
        }
    }
    private void FreeNativeSourceCellRoot(Node3D? root)
    {
        if (root is null || !GodotObject.IsInstanceValid(root))
        {
            ObservePendingNativeCellRetirements(); return;
        }
        var identity = root.GetInstanceId();
        if (!_nativeCellProcessAttachments.TryGetValue(identity, out var owner))
        {
            if (_nativeReferences?.CellProcessesConfigured == true && _nativeReferences.CellProcesses.Capture().Attachments.Any(
                attachment => !attachment.Retired && attachment.NativeRoot == identity))
                throw new NotSupportedException("Failed CELL construction retains its actual native root; its incomplete source operation must retire first.");
            root.Free(); return;
        }
        _ = owner.BeginDetach(); _nativeCellProcessRetirements.Add(identity);
        root.Free();
        ObservePendingNativeCellRetirements();
    }
    private void QueueNativeSourceCellRetirement(Node3D root)
    {
        var owner = RequireNativeSourceCellAttachment(root); _ = owner.BeginDetach();
        _nativeCellProcessRetirements.Add(root.GetInstanceId()); root.QueueFree();
    }
    private void RetireFailedNativeSourceCellRoot(Node3D? root, Exception originalFailure)
    {
        try { FreeNativeSourceCellRoot(root); }
        catch (Exception retirementFailure)
        {
            throw new AggregateException("Failed CELL construction retains its original error and actual retirement failure.",
                originalFailure, retirementFailure);
        }
    }
    private void ObservePendingNativeCellRetirements()
    {
        if (_nativeReferences is null) return;
        foreach (var identity in _nativeCellProcessRetirements.ToArray())
        {
            if (GodotObject.IsInstanceValid(GodotObject.InstanceFromId(identity))) continue;
            var owner = _nativeCellProcessAttachments[identity];
            var attachment = _nativeReferences.CellProcesses.ReadAttachment(owner.Identity);
            foreach (var child in attachment.Children.Where(child => child.Phase != FalloutCellProcessChildPhase.Retired))
                owner.ObserveRetiredChild(child.Source.Reference);
            owner.ObserveRetiredRoot();
            _nativeCellProcessRetirements.Remove(identity);
            _nativeCellProcessAttachments.Remove(identity);
            foreach (var cell in attachment.CellEpochs.Keys)
            {
                // Source data retires only after the actual native consumers.
                // A failed source operation retains its original phase/fault;
                // cancellation cannot invent a successful release receipt.
                var state = _nativeReferences.CellProcesses.ReadPhase(cell);
                if (state.Failure is null)
                {
                    _nativeReferences.ReleaseSourceCellExtraProcess(cell, "actual-CELL-source-release-after-native-retirement");
                    _nativeReferences.CellProcesses.ReleaseSource(cell);
                }
            }
        }
    }
    private void RetireNativeSourceCellAttachments()
    {
        var failures = new List<Exception>();
        foreach (var (identity, owner) in _nativeCellProcessAttachments.ToArray())
        {
            if (_nativeReferences!.CellProcesses.ReadAttachment(owner.Identity).Retired) continue;
            try
            {
                if (GodotObject.InstanceFromId(identity) is Node3D root && GodotObject.IsInstanceValid(root))
                    FreeNativeSourceCellRoot(root);
                else if (!_nativeCellProcessRetirements.Contains(identity))
                    throw new InvalidDataException("Actual native CELL owner retired without entering its source detach transaction.");
            }
            catch (Exception error) { failures.Add(error); }
        }
        try { ObservePendingNativeCellRetirements(); }
        catch (Exception error) { failures.Add(error); }
        if (_nativeReferences?.CellProcessesConfigured == true && _nativeReferences.CellProcesses.Capture().Attachments.Any(
            attachment => !attachment.Retired && attachment.NativeRoot != 0))
            failures.Add(new NotSupportedException("CELL retirement retains a failed or unmatched actual native attachment operation."));
        if (failures.Count > 0) throw new AggregateException("Native CELL retirement retains original outstanding owners/errors.", failures);
        _nativePlayerProcessCellLease?.Dispose(); _nativePlayerProcessCellLease = null;
    }
    private void RequireNativeSourceCellRetirementBeforeWorldRelease()
    {
        ObservePendingNativeCellRetirements();
        if (_nativeCellProcessAttachments.Count != 0 || _nativeCellProcessRetirements.Count != 0)
            throw new NotSupportedException("Native CELL sources still own actual attachment consumers; retire before scene/source teardown.");
        _nativePlayerProcessCellLease?.Dispose(); _nativePlayerProcessCellLease = null;
    }
}
