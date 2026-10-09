using System.Runtime.ExceptionServices;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private sealed record NativeMainPlayerTransfer(Guid Request, Guid Main, Guid Process, int Thread,
        FalloutFormKey BeforeCell, ulong BeforeRoot, FalloutFormKey? PublishedCell, ulong? PublishedRoot,
        string Phase, string? Failure);
    private NativeMainPlayerTransfer? _nativeMainPlayerTransfer;
    private readonly CancellationTokenSource _nativeMainPlayerCancellation = new();
    private bool _nativeMainPlayerRetiring;
    private bool _nativeMainPlayerCancellationDisposed;
    private void RequestNativeMainPlayerRetirement()
    {
        _nativeMainPlayerRetiring = true;
        if (!_nativeMainPlayerCancellationDisposed) _nativeMainPlayerCancellation.Cancel();
    }
    private async Task<bool> TransferMainPlayerPending(FalloutMainPlayerCellInvocation invocation,
        FalloutReferenceWorld world, FalloutPlayerPendingRequest request)
    {
        invocation.Require(FalloutMainPlayerCellStep.PendingDestination);
        BindActualNativeQueuedCallerThread(); world.PlayerMoves.SourcePending.Require(request);
        var raw = world.ReadMainPlayerPendingPayload(request);
        if (raw.Target == FalloutPlayerTransferTarget.Empty)
        {
            GD.PushWarning("OPENNV_SOURCE_PLAYER_PENDING_EMPTY_TARGET original-source-assertion no-destination-publication=true");
            return false; // Original assertion returns to the separate furniture tail.
        }
        // Consume only the original constructor/write receipt. The independent
        // argument child must also be owned before any physical publication.
        RequireSourcePlayerRawTransferFactory(raw);
        ResetSourcePlayerTransferSky(invocation, world, request, raw);
        var player = _nativePlayer ?? throw new InvalidOperationException("Pending Player transfer has no actual body.");
        var active = _nativeActiveCell ?? throw new InvalidOperationException("Pending Player transfer has no source current CELL.");
        var root = _nativeCurrentCellRoot ?? throw new InvalidOperationException("Pending Player transfer has no actual root.");
        if (_nativeDoorLoading || _nativeMainPlayerRetiring || _nativeSessionTransitioning || _retiringNativeSession ||
            _nativeMainPlayerTransfer is { Phase: not "returned" })
            throw new InvalidOperationException("Pending Player transfer conflicts with a still-owned original/native transition prefix.");
        var thread = System.Environment.CurrentManagedThreadId;
        _nativeMainPlayerTransfer = new(request.Identity, invocation.Main.Identity, invocation.Main.Process, thread,
            active.Cell.FormKey, root.GetInstanceId(), null, null, "entered", null);
        var previousModal = player.ModalInput;
        _nativeDoorLoading = true;
        Exception? failure = null;
        try
        {
            player.SetModalInput(true); Check();
            switch (request.Kind)
            {
                case FalloutPlayerPendingKind.MoveTo:
                    var placement = await RuntimeNativePlayerMoves.ApplyMainPending(world, invocation, player, active.Cell.FormKey,
                        request, StreamNativePlayerMove);
                    Check(); RequireMainPlayerPublishedTarget(world, placement.Cell);
                    break;
                case FalloutPlayerPendingKind.Door:
                    var door = active.References.SingleOrDefault(value => value.FormKey == request.Door) ??
                        throw new InvalidDataException("Pending door request has no exact active winning source reference.");
                    _ = FalloutDoorDestinationResolver.Resolve(_nativePluginStack!, door);
                    var doorPlacement = ReadSourcePlayerRawPlacement(request);
                    await StreamNativeDoorTransition(door, doorPlacement);
                    Check(); RequireMainPlayerPublishedTarget(world, doorPlacement.Cell);
                    break;
                default:
                    throw new NotSupportedException("source-Player-pending-" + request.Kind + "-destination-layout-and-invoking-consumer-unowned");
            }
            _nativeMainPlayerTransfer = _nativeMainPlayerTransfer! with
            {
                PublishedCell = _nativeActiveCell!.Cell.FormKey,
                PublishedRoot = _nativeCurrentCellRoot!.GetInstanceId(),
                Phase = "published"
            };
            // Native publication and exact outgoing source/native retirement
            // are separate boundaries. No root-count or scheduled QueueFree
            // is accepted as proof that the former consumers have retired.
            var previousRoot = _nativeMainPlayerTransfer!.BeforeRoot;
            while (_nativeCellProcessRetirements.Contains(previousRoot))
            {
                Check(); ObservePendingNativeCellRetirements();
                if (!_nativeCellProcessRetirements.Contains(previousRoot)) break;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); Check();
            }
            RequireMainPlayerPublishedTarget(world, _nativeActiveCell!.Cell.FormKey);
            Check(); _nativeMainPlayerTransfer = _nativeMainPlayerTransfer! with { Phase = "returned" };
        }
        catch (Exception original) { failure = original; }
        finally
        {
            var cleanup = new List<Exception>();
            try { CloseNativeLoadingScreens(); } catch (Exception error) { cleanup.Add(error); }
            try { if (GodotObject.IsInstanceValid(player)) player.SetModalInput(previousModal); }
            catch (Exception error) { cleanup.Add(error); }
            _nativeDoorLoading = false;
            if (cleanup.Count != 0)
            {
                if (failure is not null) cleanup.Insert(0, failure);
                failure = new AggregateException("Main Player transfer retains its original and independent native cleanup failures.", cleanup);
            }
            if (failure is not null)
                _nativeMainPlayerTransfer = _nativeMainPlayerTransfer! with { Phase = "failed", Failure = failure.ToString() };
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return true;
        void Check()
        {
            invocation.Require(FalloutMainPlayerCellStep.PendingDestination);
            _nativeMainPlayerCancellation.Token.ThrowIfCancellationRequested();
            if (System.Environment.CurrentManagedThreadId != thread || !ReferenceEquals(_nativeReferences, world) ||
                !ReferenceEquals(_nativePlayer, player) || _nativeMainPlayerRetiring || _nativeSessionTransitioning || _retiringNativeSession ||
                _nativeMainPlayerTransfer is not { } retained || retained.Request != request.Identity || retained.Main != invocation.Main.Identity)
                throw new OperationCanceledException("Original Player transfer lost its actual thread/campaign/invocation lifetime.");
            world.PlayerMoves.SourcePending.Require(request);
        }
    }
    private void RequireMainPlayerPublishedTarget(FalloutReferenceWorld world, FalloutFormKey cell)
    {
        if (!ReferenceEquals(_nativeReferences, world) || _nativeCurrentCellRoot is not { } root || _nativeActiveCell?.Cell.FormKey != cell ||
            !GodotObject.IsInstanceValid(root) || !root.IsInsideTree() || root.IsQueuedForDeletion())
            throw new InvalidOperationException("Player target has no actual same-campaign/current-CELL/root publication.");
        var owner = RequireNativeSourceCellAttachment(root);
        var attachment = world.CellProcesses.ReadAttachment(owner.Identity);
        if (!attachment.RootPublished || attachment.Retired || attachment.Failure is not null || !attachment.CellEpochs.ContainsKey(cell) ||
            attachment.Children.Any(child => child.Phase is FalloutCellProcessChildPhase.Pending or FalloutCellProcessChildPhase.Failed))
            throw new NotSupportedException("Player target still has incomplete source reference/LAND/native consumers.");
        owner.ObserveForCapture();
    }
    private void AttachMainPlayerTarget(FalloutMainPlayerCellInvocation invocation, FalloutReferenceWorld world,
        FalloutMainPlayerCellTarget target)
    {
        invocation.Require(FalloutMainPlayerCellStep.CellAttach); BindActualNativeQueuedCallerThread();
        var root = _nativeCurrentCellRoot ?? throw new InvalidOperationException("Player CELL attachment has no living native root.");
        var owner = RequireNativeSourceCellAttachment(root);
        var attached = world.CellProcesses.ReadAttachment(owner.Identity);
        if (!attached.RootPublished || attached.Retired || attached.Failure is not null || !attached.CellEpochs.ContainsKey(target.Source.Cell))
            throw new NotSupportedException("Original Player target needs an actual published source CELL graph; shared root residency alone is insufficient.");
        owner.ObserveForCapture();
        if (world.ReadMainPlayerSourceCell(target.Source.Cell).Source != target.Source)
            throw new InvalidDataException("Player attachment changed winning target CELL/world bytes.");
        var previous = _nativeActiveCell ?? throw new InvalidOperationException("Player attachment has no current source graph.");
        // Retain the exact already-published union. Selecting a different
        // active member must not silently narrow a multi-CELL native root.
        var definition = FalloutCellSceneReader.Read(_nativePluginStack!, target.Source.Cell).Cell;
        var scene = previous with { Cell = definition };
        world.CellProcesses.RequireCurrentAttachmentSelection(owner.Identity, scene);
        (_nativeOpeningStageDriver ?? throw new InvalidOperationException("Player target has no source gameplay driver.")).EnterWorldCell(target.Source.Cell);
        SetNativeActiveCell(root, scene);
        RequireMainPlayerPublishedTarget(world, target.Source.Cell);
    }
}
