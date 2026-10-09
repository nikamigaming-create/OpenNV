using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private IDisposable? _nativeMainPlayerLease;
    private NativeMainPlayerConsumers? _nativeMainPlayerConsumers;
    private Action<FalloutPlayerPendingReplacement>? _nativeMainPlayerReplacement;
    private FalloutReferenceWorld? _nativeMainPlayerBoundWorld;
    private void BindNativeMainPlayerCell()
    {
        var world = _nativeReferences ?? throw new InvalidOperationException("Main Player binding has no actual world.");
        if (!world.CampaignMainPlayerCellConstructed) return; // The source-absent family has no promoted FNV Main segment.
        BindActualNativeQueuedCallerThread();
        if (_nativeMainPlayerLease is not null) throw new InvalidOperationException("Main Player native children already own a living lease.");
        var consumers = new NativeMainPlayerConsumers(this, world, world.CampaignMainPlayerCellSource);
        _nativeMainPlayerLease = world.BindCampaignMainPlayerCell(consumers, "actual-native-Player-CELL/" + GetInstanceId());
        _nativeMainPlayerConsumers = consumers;
        _nativeMainPlayerBoundWorld = world;
        _nativeMainPlayerReplacement = replacement => GD.PushWarning("OPENNV_SOURCE_PLAYER_PENDING_REPLACED " +
            "revision=" + replacement.Revision + " released=" + replacement.Released + " stored=" + replacement.Stored + " owner=" + replacement.Owner);
        world.PlayerMoves.SourcePending.Replaced += _nativeMainPlayerReplacement;
        if (world.MainPlayerColdRootToRebind is { } previous)
        {
            RequireMainPlayerPublishedTarget(world, previous);
            world.RebindColdMainPlayerRoot(previous, _nativeCurrentCellRoot!.GetInstanceId());
        }
    }
    private void RetireNativeMainPlayerCell()
    {
        // Refusal retains the still-owned native/source lease for a later safe
        // cleanup attempt. Native caller cancellation/drain happens first.
        _nativeMainPlayerLease?.Dispose(); _nativeMainPlayerLease = null; _nativeMainPlayerConsumers = null;
        if (_nativeMainPlayerReplacement is not null && _nativeMainPlayerBoundWorld is { } world)
            world.PlayerMoves.SourcePending.Replaced -= _nativeMainPlayerReplacement;
        _nativeMainPlayerReplacement = null; _nativeMainPlayerBoundWorld = null;
        if (!_nativeMainPlayerCancellationDisposed) { _nativeMainPlayerCancellation.Dispose(); _nativeMainPlayerCancellationDisposed = true; }
    }
    private sealed class NativeMainPlayerConsumers(RuntimeCoordinator coordinator, FalloutReferenceWorld world,
        FalloutMainPlayerCellSource source) : IFalloutMainPlayerCellConsumers
    {
        public FalloutMainPlayerCellSource Source => source;
        public string Owner => "actual-native-source-Player-CELL/" + coordinator.GetInstanceId();
        private void Require(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellStep step)
        {
            invocation.Require(step); coordinator.BindActualNativeQueuedCallerThread();
            if (!ReferenceEquals(coordinator._nativeReferences, world) || coordinator._nativeMainPlayerConsumers != this ||
                coordinator._nativePlayer is not { } player || !GodotObject.IsInstanceValid(player) ||
                !player.IsInsideTree() || player.IsQueuedForDeletion())
                throw new InvalidOperationException("Main Player child lost its actual campaign/player/native publication epoch.");
        }
        private NotSupportedException Unowned(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellStep step, string owner)
        { Require(invocation, step); return new NotSupportedException("source-Main-Player:" + owner); }
        public void PendingWorldPrelude(FalloutMainPlayerCellInvocation invocation)
        { Require(invocation, FalloutMainPlayerCellStep.PendingWorldPrelude); world.CampaignPlayerPendingConsumers.ResetExteriorLoaders(invocation); }
        public void ReleasePendingOwnedChild(FalloutMainPlayerCellInvocation invocation)
        { Require(invocation, FalloutMainPlayerCellStep.PendingOwnedChildRelease); world.CampaignPlayerPendingConsumers.ReleaseOwnedChild(invocation); }
        public Task<bool> TransferPending(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request)
        {
            Require(invocation, FalloutMainPlayerCellStep.PendingDestination);
            return coordinator.TransferMainPlayerPending(invocation, world, request);
        }
        public void StorePendingSceneScalar(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request)
        {
            Require(invocation, FalloutMainPlayerCellStep.PendingSceneScalar); _ = world.ReadMainPlayerPendingPayload(request);
            world.CampaignPlayerPendingConsumers.StoreScalar(invocation, request, coordinator.RequireSourcePlayerPendingController(invocation));
        }
        public void InvokePendingCallback(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request)
        {
            Require(invocation, FalloutMainPlayerCellStep.PendingCallback); _ = world.ReadMainPlayerPendingPayload(request);
            world.CampaignPlayerPendingConsumers.InvokeCallback(invocation, request, coordinator.RequireSourcePlayerPendingCallback);
        }
        public void PendingFurniture(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request)
        {
            Require(invocation, FalloutMainPlayerCellStep.PendingFurniture); _ = world.ReadMainPlayerPendingPayload(request);
            coordinator.ConsumeMainPlayerPendingFurniture(invocation, world, request);
        }
        public void RetirePendingDeferredChildren(FalloutMainPlayerCellInvocation invocation) =>
            throw Unowned(invocation, FalloutMainPlayerCellStep.PendingDeferredDestruction, "original-TLS-deferred-destruction-manager-unowned");
        public bool PendingFlagQueries(FalloutMainPlayerCellInvocation invocation)
        {
            Require(invocation, FalloutMainPlayerCellStep.PendingFlagQueries);
            return world.CampaignPlayerPendingConsumers.ReadFinalFlagQueries(invocation, coordinator.ReadSourcePlayerPostNullManagerWord);
        }
        public void PendingFlagChild(FalloutMainPlayerCellInvocation invocation)
        { Require(invocation, FalloutMainPlayerCellStep.PendingFlagChild); world.CampaignPlayerPendingConsumers.StoreFinalFlag(invocation); }
        public bool HeldInterface(FalloutMainPlayerCellInvocation invocation) =>
            throw Unowned(invocation, FalloutMainPlayerCellStep.HeldInterfaceQuery, "actual-interface-signed-kind-two-producer-unowned");
        public void HeldChild(FalloutMainPlayerCellInvocation invocation) =>
            throw Unowned(invocation, FalloutMainPlayerCellStep.HeldChild, "held-Player-child-unowned");
        public bool SceneMode(FalloutMainPlayerCellInvocation invocation) =>
            throw Unowned(invocation, FalloutMainPlayerCellStep.SceneMode, "independent-original-Main-scene-mode-byte-unowned");
        public bool ScenePresent(FalloutMainPlayerCellInvocation invocation) =>
            throw Unowned(invocation, FalloutMainPlayerCellStep.SceneRead, "selected-Player-first-person-selector-or-TLS-common-3D-getter-unowned");
        public void SceneClock(FalloutMainPlayerCellInvocation invocation) =>
            throw Unowned(invocation, FalloutMainPlayerCellStep.SceneClock, "original-Float32-times-wide-scene-clock-and-increment-prefix-unowned");
        public void SceneChild(FalloutMainPlayerCellInvocation invocation, bool alternate) =>
            throw Unowned(invocation, FalloutMainPlayerCellStep.SceneChild, alternate ? "original-alternate-scene-child-unowned" : "actual-Player-virtual-update-child-unowned");
        public FalloutMainPlayerSourceCell? ParentCell(FalloutMainPlayerCellInvocation invocation)
        { Require(invocation, FalloutMainPlayerCellStep.ParentCellRead); return world.ReadMainPlayerParentCell(invocation); }
        public FalloutMainPlayerSourcePosition Position(FalloutMainPlayerCellInvocation invocation)
        {
            Require(invocation, FalloutMainPlayerCellStep.PositionRead);
            var player = coordinator._nativePlayer!;
            var raw = player.GlobalPosition / player.UnitsToMeters;
            return new(BitConverter.SingleToUInt32Bits(raw.X), BitConverter.SingleToUInt32Bits(-raw.Z), BitConverter.SingleToUInt32Bits(raw.Y),
                new(null, "actual-original-calling-thread-FISTP-rounding-producer-unowned"));
        }
        public FalloutMainPlayerCellTarget? TargetCell(FalloutMainPlayerCellInvocation invocation,
            FalloutMainPlayerSourceCell before, FalloutMainPlayerSourcePosition position)
        { Require(invocation, FalloutMainPlayerCellStep.TargetCellRead); return world.ReadMainPlayerTargetCell(invocation, before, position); }
        public Task LoadTarget(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target, FalloutMainPlayerSourcePosition position) =>
            throw Unowned(invocation, FalloutMainPlayerCellStep.WorldLoad, "original-world-load-and-two-independent-TES-byte-queue-child-unowned");
        public void SetWorldBracket(FalloutMainPlayerCellInvocation invocation, bool value)
        { Require(invocation, value ? FalloutMainPlayerCellStep.WorldBracketSet : FalloutMainPlayerCellStep.WorldBracketClear); invocation.Owner.StoreMainPlayerWorldBracket(invocation, value); }
        public void SetPlayerBracket(FalloutMainPlayerCellInvocation invocation, bool value)
        { Require(invocation, value ? FalloutMainPlayerCellStep.PlayerBracketSet : FalloutMainPlayerCellStep.PlayerBracketClear); invocation.Owner.StoreMainPlayerMovementBracket(invocation, value); }
        public void AttachCell(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target)
        {
            Require(invocation, FalloutMainPlayerCellStep.CellAttach);
            coordinator.AttachMainPlayerTarget(invocation, world, target);
        }
        public void StoreRoot(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target)
        {
            Require(invocation, FalloutMainPlayerCellStep.RootStore);
            coordinator.RequireMainPlayerPublishedTarget(world, target.Source.Cell);
            invocation.Owner.StoreMainPlayerRootBinding(invocation, target.Source.Cell, coordinator._nativeCurrentCellRoot!.GetInstanceId());
        }
        public void OptionalTreeChild(FalloutMainPlayerCellInvocation invocation) =>
            throw Unowned(invocation, FalloutMainPlayerCellStep.OptionalTreeChild, "actual-source-optional-tree-manager-child-unowned");
    }
}
