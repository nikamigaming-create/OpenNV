using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private sealed class SourceReferencePositionConsumers(FalloutReferenceWorld world) :
        IFalloutReferencePositionConsumers, IFalloutReferenceChangeConsumers
    {
        public Guid Factory => world._sourcePlayerReferencePosition!.Factory;
        public void RequireCurrent(FalloutReferencePositionInvocation invocation) => world.RequireActualReferencePosition(invocation);
        public FalloutReferenceFlagRead ReadReferenceFlags(FalloutReferencePositionInvocation invocation) =>
            (world._sourceReferenceScriptInitializer ?? throw new NotSupportedException("source-reference-flags-owner-unbound"))
                .ReadReferenceFlags(invocation, RequireCurrent);
        public FalloutReferenceParentCellRead ReadCurrentParentCell(FalloutReferencePositionInvocation invocation)
        {
            RequireCurrent(invocation);
            var scene = world.CampaignStandalonePlayerScene;
            return new("actual-World-source-linked-Player-ParentCELL", scene.Stack, world.ActualSourceProcessIdentity,
                world._sourceLinkedPlayerParentFactory, scene.Constructor, invocation.Actor,
                world._sourceLinkedPlayerParentRevision, world.CurrentSourcePlayerLinkedCell, scene.Source.Main.EngineSha256);
        }
        public bool ReadPersistentReference(FalloutReferencePositionInvocation invocation, FalloutReferenceFlagRead flags)
        {
            RequireCurrent(invocation);
            if (flags.Actor != invocation.Actor || flags.Process != invocation.CommonProcess)
                throw new InvalidDataException("Position predicate changed its actual reference flag owner.");
            var file = (world._sourceReferenceFormFiles ??
                throw new NotSupportedException("source-reference-current-form-file-owner-unbound")).ReadCurrentFile(invocation);
            if (file.File is not null || file.Count != 0)
                throw new NotSupportedException("source-reference-current-form-file-DataHandler-persistence-predicate-unbound");
            return (flags.Flags & FalloutReferenceScriptExtraInitializerSource.PlayerPersistenceFlag) != 0;
        }
        public byte ReadBaseFormKind(FalloutReferencePositionInvocation invocation)
        {
            RequireCurrent(invocation);
            if (!world._sourceReferenceBaseConstructed)
                throw new NotSupportedException("source-reference-current-Base-constructor-unbound");
            var record = world._sourceReferenceCurrentBase ??
                throw new NotSupportedException("source-reference-position-current-Base-assignment-unbound");
            if (record.Signature != "NPC_") throw new NotSupportedException("source-reference-position-current-Base-kind-owner-unbound");
            return SourceNpcKind;
        }
        private const byte SourceNpcKind = 0x2a;
        public FalloutFormKey? ReadCellWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey cell)
        { RequireCurrent(invocation); return FalloutCellSceneReader.ReadDefinition(world.NativeSourceRecords, cell).Worldspace; }
        public void DetachWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey worldspace)
        { RequireCurrent(invocation); throw new NotSupportedException("source-reference-position-Worldspace-detach-owner-unbound"); }
        public void AttachWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey worldspace)
        { RequireCurrent(invocation); throw new NotSupportedException("source-reference-position-Worldspace-attach-owner-unbound"); }
        public FalloutReferenceChangeCall ReferenceChanged(FalloutReferencePositionInvocation invocation, uint mask)
        { RequireCurrent(invocation); return world._sourceReferenceChanges!.ReferenceChanged(invocation, mask, false, this); }
        public void ClearStartupChangeSuppression(FalloutReferencePositionInvocation invocation)
        { RequireCurrent(invocation); _ = world._sourceReferenceChanges!.ClearStartupSuppression(invocation); }
        public FalloutSourceThreadReferenceFlagsRead ReadThreadFlags(FalloutReferencePositionInvocation invocation) =>
            world.CampaignStandalonePlayerScene.ThreadContext.ReadReferenceChangeFlags(invocation.Call, Factory,
                () => RequireCurrent(invocation));
        public uint ReadSourceFormId(FalloutReferencePositionInvocation invocation)
        { RequireCurrent(invocation); return world.NativeSourceRecords.RuntimeFormId(invocation.Actor.Reference); }
        public bool ReadDataHandlerSuppression(FalloutReferencePositionInvocation invocation, uint sourceFormId)
        { RequireCurrent(invocation); throw new NotSupportedException("source-reference-change-current-DataHandler-predicate-unbound"); }
        public void ResetCurrentControllerAndModel(FalloutReferencePositionInvocation invocation, FalloutPackageRawPosition position)
        {
            RequireCurrent(invocation);
            using var lease = world.ProcessCommon.BorrowEarlyController(invocation.Actor.Reference, invocation.Epoch);
            if (lease is not null)
            {
                var reset = new FalloutEarlyControllerResetInvocation(Guid.NewGuid(), invocation.Actor,
                    invocation.CommonProcess, world.ProcessRuntime.CharacterControllerSourceProcess, invocation.Epoch,
                    invocation.ReferenceConstructor, invocation.ThreadId);
                lease.Controller.ResetForPosition(reset, new(position.X, position.Y, position.Z), current =>
                {
                    RequireCurrent(invocation);
                    if (current != reset || lease.Controller.Lineage.CommonProcess != invocation.CommonProcess)
                        throw new InvalidDataException("Position reset changed its freshly borrowed actual controller.");
                });
            }
            if (!world._sourceConstructorReferenceModelConstructed || invocation.Ingress is not
                (FalloutReferencePositionIngress.ConstructorStartup or FalloutReferencePositionIngress.ConfiguredStartup))
                throw new NotSupportedException("source-reference-position-current-model-node-writer-unbound");
            if (world._sourceConstructorReferenceModel is not null)
                throw new NotSupportedException("source-reference-position-nonnull-model-translation-owner-unbound");
        }
        public FalloutPackageRawPosition SelectInitialPlacement(FalloutReferencePositionInvocation invocation, FalloutPackageRawPosition supplied)
        {
            RequireCurrent(invocation);
            var selected = (world._sourceInitialPlacementCells ??
                throw new NotSupportedException("source-initial-placement-current-TES-CELL-and-grid-owner-unbound"))
                .ReadSelectedCell(invocation);
            if (selected.Cell is not null)
                throw new NotSupportedException("source-initial-placement-nonnull-CELL-spatial-transform-owner-unbound");
            return supplied;
        }
        public void SetInitialPlacementYaw(FalloutReferencePositionInvocation invocation)
        {
            RequireCurrent(invocation);
            var life = world.ProcessRuntime.Life(invocation.Actor.Reference).Require();
            if (life is not (0 or 4)) return;
            if (ReadBaseFormKind(invocation) != SourceNpcKind)
                throw new NotSupportedException("source-startup-yaw-current-non-NPC-kind-branch-unbound");
            var activity = world.ProcessCommon.ReadPlayerWeaponActivity(invocation.Actor.Reference, invocation.Epoch);
            if (activity.Value != 0) return;
            var mode = world._sourcePlayerReferencePosition!.ReadActorYawMode(invocation);
            if (mode.Mode is >= 4 and <= 10)
                throw new NotSupportedException("source-startup-yaw-current-Actor-mode-child-unbound");
            var argument = (world._sourceEarlyControllerFactory ??
                throw new NotSupportedException("source-startup-yaw-shared-center-owner-unbound")).Shared.ReadCenter().Z;
            world.CampaignStandalonePlayerScene.StoreStartupReferenceYaw(invocation, argument,
                new StartupReferenceYawChange(world, invocation), RequireCurrent);
        }
    }

    private sealed class StartupReferenceYawChange(FalloutReferenceWorld world,
        FalloutReferencePositionInvocation invocation) : IFalloutReferenceOrientationChangeConsumer
    {
        public FalloutPlayerWorldOrientationSource Source => world._standaloneWorldOrientationSource ??
            throw new NotSupportedException("source-startup-yaw-current-world-source-unbound");
        public string Stack => world.CampaignStandalonePlayerScene.Stack;
        public Guid Process => invocation.ReferenceProcess;
        public Guid Factory => world._sourceReferenceChanges!.Factory;
        public FalloutFormKey Reference => invocation.Actor.Reference;
        public void RequireCurrent() => world.RequireActualReferencePosition(invocation);
        public void Changed(Guid call, byte mask)
        {
            RequireCurrent();
            if (call != invocation.Call || mask != FalloutReferencePositionSource.PositionChangeMask)
                throw new InvalidDataException("Startup yaw changed its actual caller or reference mask.");
            _ = world._sourceReferenceChanges!.ReferenceChanged(invocation, mask, false,
                new SourceReferencePositionConsumers(world));
        }
    }
}
