using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private sealed partial class SourcePackageCommandConsumers : IFalloutActorPackageAssignmentConsumers
    {
        FalloutActorPackageAssignmentSource IFalloutActorPackageAssignmentConsumers.Source =>
            world._sourceActorPackageAssignment ?? throw new NotSupportedException("source-Actor-package-assignment-original-graph-unbound");
        public FalloutProcessSceneQuery? ReadCurrentProcess(FalloutPackageCommandFieldInvocation invocation) => ReadProcess(invocation);
        public int EnsureCurrentProcedure(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package) => EnsureProcedure(invocation, package);
        public FalloutReferenceActorYawModeRead ReadAssignmentStatus(FalloutPackageCommandFieldInvocation invocation)
        {
            RequireCurrent(invocation);
            return world.WithActualReferencePosition(FalloutReferencePositionIngress.PackageCommand,
                current => world._sourcePlayerReferencePosition!.ReadActorYawMode(current));
        }
        public FalloutReferenceActorAssignmentByteRead ReadAssignmentByte(FalloutPackageCommandFieldInvocation invocation)
        {
            RequireCurrent(invocation);
            return world.WithActualReferencePosition(FalloutReferencePositionIngress.PackageCommand,
                current => world._sourcePlayerReferencePosition!.ReadActorAssignmentByte(current));
        }
        public FalloutReferencePositionCall StoreAssignmentByteZero(FalloutPackageCommandFieldInvocation invocation)
        {
            RequireCurrent(invocation);
            return world.WithActualReferencePosition(FalloutReferencePositionIngress.PackageCommand,
                current => world._sourcePlayerReferencePosition!.StoreActorAssignmentByteZero(current));
        }
        public FalloutActorPackageExtraRead DeriveCurrentPackageExtra(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package)
        {
            RequireCurrent(invocation); var current = Registry.Require(package);
            var target = Registry.ReadCurrentTarget(invocation, current); RequireCurrent(invocation);
            var kind = target.Values?.Kind;
            if (target.Object is null || kind is not (0 or 3) || kind == 0 && target.PayloadForm is null)
                return new(world._sourceActorPackageAssignment!.Identity, package, current.RecordSha256, kind, null, true,
                    TargetRead: target);
            return new(world._sourceActorPackageAssignment!.Identity, package, current.RecordSha256, kind, null, false,
                kind == 3 ? "source-package-target-Actor-Extra81-current-payload-owner-unbound" :
                    "source-package-target-nonnull-current-reference-payload-owner-unbound", target);
        }
        public FalloutDataHandlerLoadingRead ReadDataHandlerLoading(FalloutPackageCommandFieldInvocation invocation) =>
            world.ReadActualDataHandlerLoading(invocation);
        public FalloutActorAssignmentBaseRead ReadCurrentBaseForMover(FalloutPackageCommandFieldInvocation invocation)
        {
            RequireCurrent(invocation);
            var basis = world._sourceReferenceCurrentBase ?? throw new NotSupportedException("source-Actor-package-current-Base-unbound");
            if (world._sourceReferenceBaseAssignment is not { Returned: true, PointerStored: true })
                throw new NotSupportedException("source-Actor-package-current-Base-assignment-prefix-unreturned");
            return world.WithActualReferencePosition(FalloutReferencePositionIngress.PackageCommand, current =>
                new FalloutActorAssignmentBaseRead("actual-shared-current-reference-Base-field", basis.FormKey,
                    new SourceReferencePositionConsumers(world).ReadBaseFormKind(current), null,
                    world._sourcePlayerReferencePosition!.ReadCurrentBorrowSequence(current)));
        }
        public void ResetActualMover(FalloutPackageCommandFieldInvocation invocation, FalloutActorMoverResetJoin join)
        {
            RequireCurrent(invocation);
            if (world._enteredActorMoverJoin is not null || world._currentActorPackageAssignment is not
                { Step: FalloutActorAssignmentStep.MoverReset } call || call.Invocation != invocation)
                throw new InvalidOperationException("Actor mover reset left its actual entered assignment child.");
            world._enteredActorMoverJoin = join;
            try { world.CampaignStandalonePlayerScene.ResetActorMoverForPackageAssignment(invocation); }
            finally { world._enteredActorMoverJoin = null; }
        }
        public FalloutActorFollowerExtraRead ReadFollowerExtra(FalloutPackageCommandFieldInvocation invocation)
        {
            RequireCurrent(invocation);
            var source = world._sourceReferenceFollower ?? throw new NotSupportedException("source-Actor-package-follower-lookup-source-unbound");
            return world.WithActualReferencePosition(FalloutReferencePositionIngress.PackageCommand,
                current => world._sourcePlayerReferencePosition!.ReadFollowerExtra(current, source));
        }
        public FalloutPackageRuntimeObject? ReadPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation)
        { RequireCurrent(invocation); throw new NotSupportedException("source-package-current-Player-special-package-field-unbound"); }
        public void ClearPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject expected)
        { RequireCurrent(invocation); throw new NotSupportedException("source-package-Player-special-package-clear-owner-unbound"); }
        public FalloutPackageGameHourRead ReadGameHour(FalloutPackageCommandFieldInvocation invocation) => world.ReadStandalonePackageGameHour(invocation);
        public Guid ConstructContext(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package)
        { RequireCurrent(invocation); throw new NotSupportedException("source-package-nonnull-context-construction-owner-unbound"); }
        public void DestroyContext(FalloutPackageCommandFieldInvocation invocation, Guid context)
        { RequireCurrent(invocation); throw new NotSupportedException("source-package-nonnull-context-destruction-owner-unbound"); }
        public void DeleteTemporaryPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package)
        { RequireCurrent(invocation); throw new NotSupportedException("source-package-temporary-form-deletion-owner-unbound"); }
        public void RequireExtra(FalloutPackageCommandFieldInvocation invocation, FalloutPackageExtraReference extra)
        { RequireCurrent(invocation); throw new NotSupportedException("source-package-nonnull-target-Extra-runtime-lifetime-owner-unbound"); }
    }
}
