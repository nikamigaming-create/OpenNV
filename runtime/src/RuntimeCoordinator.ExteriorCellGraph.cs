using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private ulong? _nativeSharedGridRoot;
    private Guid? _nativeSharedGridChange;
    private IReadOnlyList<FalloutFormKey>? _nativeSharedGridReleasedCells;
    private long? _nativeSharedGridPlacementRevision;
    private RuntimeNativeCellProcessAttachment SharedGridOwner => _nativeSharedGridRoot is { } root &&
        _nativeCellProcessAttachments.TryGetValue(root, out var owner) ? owner :
        throw new InvalidOperationException("Exterior source graph lost its actual native CELL attachment owner.");

    private IReadOnlySet<FalloutFormKey> PrepareNativeSharedGrid(Node3D root, FalloutExteriorGridScene grid)
    {
        BindActualNativeQueuedCallerThread();
        if (_nativeSharedGridChange is not null) throw new InvalidOperationException("Actual exterior source graph is still entered.");
        var before = _nativeActiveCell ?? throw new InvalidOperationException("Exterior extension has no current source scene.");
        var owner = RequireNativeSourceCellAttachment(root);
        var cells = grid.Cells.Select(cell => cell.FormKey).Append(grid.PersistentCell).Distinct(FalloutFormKeyComparer.Instance).ToArray();
        var identity = owner.PrepareSharedGraph(before, grid.Scene, cells);
        _nativeSharedGridRoot = root.GetInstanceId(); _nativeSharedGridChange = identity;
        _nativeSharedGridPlacementRevision = _nativeReferences!.PlacementRevision;
        return owner.SharedPendingReferences();
    }
    private void BeginNativeSharedGridPublication(Node3D root, FalloutExteriorGridScene grid)
    {
        var owner = SharedGridOwner;
        if (_nativeSharedGridRoot != root.GetInstanceId() ||
            _nativeReferences!.CellProcesses.ReadSharedGraph(owner.Identity)?.Identity != _nativeSharedGridChange)
            throw new InvalidDataException("Exterior publication lost its actual retained source/native invocation.");
        owner.BeginSharedPublication(grid.Scene);
        var change = _nativeReferences.CellProcesses.ReadSharedGraph(owner.Identity)!;
        _nativeSharedGridReleasedCells = change.BeforeEpochs.Keys.Except(change.AfterEpochs.Keys, FalloutFormKeyComparer.Instance).ToArray();
        owner.RetireSharedConsumers(_nativeReferencePresentation!, _nativeReferenceEvents!);
    }
    private void CompleteNativeSharedGridPublication(Node3D root)
    {
        var owner = SharedGridOwner;
        owner.PublishSharedGraph(_nativeReferenceEvents ?? throw new InvalidOperationException("Exterior source publication has no native event owner."),
            _nativeActiveCell ?? throw new InvalidOperationException("Exterior publication lost its actual current source scene."));
        // Queued Load3D returns only after the exact target CELL graph, native
        // actor and auxiliary consumers are genuinely published in this tree.
        PublishExteriorQueuedNpcCallers(root);
        ReleaseNativeSharedGridSourceCells();
        _nativePlacementRevision = _nativeSharedGridPlacementRevision ?? throw new InvalidOperationException("Shared publication lost its actual placement input generation.");
        _nativeSharedGridRoot = null; _nativeSharedGridChange = null; _nativeSharedGridPlacementRevision = null;
    }
    private void RequestNativeSharedGridCancellation()
    {
        if (_nativeSharedGridChange is null) return;
        var owner = SharedGridOwner; var change = _nativeReferences!.CellProcesses.ReadSharedGraph(owner.Identity);
        if (change is null)
        {
            // A terminal publication can still own a failed queued caller or
            // source release. Do not replay it or disguise it as cancellation.
            if (_nativeSharedGridRoot is { } completedRoot && HasNativeQueuedActorCallers(completedRoot))
                throw new NotSupportedException("Completed exterior graph still owns an unreturned actual queued caller.");
            ReleaseNativeSharedGridSourceCells();
            _nativeSharedGridRoot = null; _nativeSharedGridChange = null; _nativeSharedGridPlacementRevision = null; return;
        }
        if (change.Phase == FalloutCellSharedGraphPhase.RetiringRoot) return;
        if (change.Phase == FalloutCellSharedGraphPhase.Publishing)
            throw new NotSupportedException("Actual exterior publication entered its irreversible source/native prefix; retire the real root before replacement.");
        owner.BeginSharedCancellation();
        var cancelled = _nativeReferences.CellProcesses.ReadSharedGraph(owner.Identity)!;
        _nativeSharedGridReleasedCells = cancelled.AfterEpochs.Keys.Except(cancelled.BeforeEpochs.Keys, FalloutFormKeyComparer.Instance).ToArray();
        owner.RetireSharedConsumers(_nativeReferencePresentation!, _nativeReferenceEvents!);
    }
    private bool AdvanceNativeSharedGridCancellation()
    {
        if (_nativeSharedGridChange is null) return false;
        var owner = SharedGridOwner;
        var attachment = _nativeReferences!.CellProcesses.ReadAttachment(owner.Identity);
        if (attachment.Retired)
        {
            // Whole-root cleanup already consumed the same union graph. Its
            // root-retirement receipt is distinct from a successful grid swap.
            _nativeSharedGridRoot = null; _nativeSharedGridChange = null; _nativeSharedGridReleasedCells = null; _nativeSharedGridPlacementRevision = null; return false;
        }
        var change = _nativeReferences.CellProcesses.ReadSharedGraph(owner.Identity);
        if (change?.Phase == FalloutCellSharedGraphPhase.Cancelling)
        {
            ReapExteriorQueuedNpcRetirements();
            if (_nativeSharedGridRoot is { } root && HasNativeQueuedActorCallers(root)) return true;
            owner.CompleteSharedCancellation();
            ReleaseNativeSharedGridSourceCells();
            _nativeSharedGridRoot = null; _nativeSharedGridChange = null; _nativeSharedGridPlacementRevision = null; return false;
        }
        if (change?.Phase is FalloutCellSharedGraphPhase.Publishing or FalloutCellSharedGraphPhase.RetiringRoot || attachment.Failure is not null)
            return true;
        return false;
    }
    private void ReleaseNativeSharedGridSourceCells()
    {
        if (_nativeSharedGridReleasedCells is null) return;
        if (_nativeSharedGridRoot is { } root && HasNativeQueuedActorCallers(root))
            throw new NotSupportedException("Actual shared CELL source release still owns an entered native queued reader/caller.");
        var world = _nativeReferences ?? throw new InvalidOperationException("Shared CELL release lost its source world.");
        foreach (var cell in _nativeSharedGridReleasedCells.ToArray())
        {
            if (world.CellProcesses.ReadPhase(cell).Failure is not null)
                throw new NotSupportedException("Actual retired shared CELL retains its original source failure; release cannot invent completion.");
            world.ReleaseSourceCellExtraProcess(cell, "actual-shared-CELL-source-release-after-native-consumers-and-readers");
            world.CellProcesses.ReleaseSource(cell);
            _nativeLandLastUse.Remove(cell);
            _nativeSharedGridReleasedCells = _nativeSharedGridReleasedCells.Where(value => value != cell).ToArray();
        }
        _nativeSharedGridReleasedCells = null;
    }
    private void RetainNativeSharedGridFailure(Exception error)
    {
        if (_nativeSharedGridRoot is { } root && _nativeCellProcessAttachments.TryGetValue(root, out var owner))
            _nativeReferences!.CellProcesses.FailSharedGraph(owner.Identity, error);
    }
    private void ForgetRetiredNativeSharedGrid(ulong root)
    {
        if (_nativeSharedGridRoot != root) return;
        _nativeSharedGridRoot = null; _nativeSharedGridChange = null; _nativeSharedGridReleasedCells = null; _nativeSharedGridPlacementRevision = null;
    }
    private void RequireNativeSharedGridSettledForCapture()
    {
        if (_nativeSharedGridChange is not null || _nativeSharedGridReleasedCells is not null ||
            _nativeReferences is { } world && _nativePlacementRevision != world.PlacementRevision)
            throw new NotSupportedException("Actual exterior CELL extension/publication/cancellation still owns source/native work before capture.");
    }
    private object? NativeSharedGridState => _nativeSharedGridRoot is { } root && _nativeCellProcessAttachments.TryGetValue(root, out var owner) ? new
    {
        root,
        invocation = _nativeSharedGridChange,
        placementInput = _nativeSharedGridPlacementRevision,
        change = _nativeReferences?.CellProcesses.ReadSharedGraph(owner.Identity),
        pendingSourceRelease = _nativeSharedGridReleasedCells,
        originalTESGrid = "unowned-original-constructor-and-update-producer; presentation-center-is-not-authority",
        parity = "unmeasured"
    } : null;
}
