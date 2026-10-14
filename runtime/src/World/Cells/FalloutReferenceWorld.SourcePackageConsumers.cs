using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private sealed partial class SourcePackageCommandConsumers :
        IFalloutSourcePackageAssignmentConsumers, IFalloutPackageReferenceFieldConsumers
    {
        private readonly FalloutReferenceWorld world;
        private readonly FalloutActorPackageCommandFields fields;
        internal SourcePackageCommandConsumers(FalloutReferenceWorld world, FalloutActorPackageCommandFields fields)
        { this.world = world; this.fields = fields; }
        public FalloutPackageCommandFieldsSource Source => fields.Source;
        public FalloutPackageAssignmentSource AssignmentSource => world._sourcePackageAssignmentSource ??
            throw new NotSupportedException("source-ordered-package-command-selector-unbound");
        public string Stack => fields.Stack;
        public FalloutCombatActorIdentity Actor => fields.Actor;
        public Guid Process => fields.Process;
        public long Epoch => fields.Epoch;
        public Guid Factory => fields.Factory;
        public void RequireCurrent(FalloutPackageCommandFieldInvocation invocation) => world.RequireActualSourcePackageCommand(invocation);

        public FalloutSourceSelectedPackageRead ReadSelectedPackage(FalloutPackageCommandFieldInvocation invocation,
            Action<FalloutSourceSelectedPackageRead> observe)
        {
            RequireCurrent(invocation);
            var primary = world.ProcessCommon.ReadSourcePackageSlot(Actor.Reference, Epoch, FalloutProcessPackageSlot.Primary);
            observe(new(primary, null)); RequireCurrent(invocation);
            // The selected source getter rereads Primary when its first read
            // is nonnull. Current is only read from the first-null arm.
            var selected = primary.Fields.Fields.Package is not null ?
                world.ProcessCommon.ReadSourcePackageSlot(Actor.Reference, Epoch, FalloutProcessPackageSlot.Primary) : ReadCurrentPackage(invocation);
            var read = new FalloutSourceSelectedPackageRead(primary, selected); observe(read); RequireCurrent(invocation); return read;
        }
        public FalloutProcessPackageRead? ReadCurrentPackage(FalloutPackageCommandFieldInvocation invocation)
        {
            RequireCurrent(invocation);
            return world.ProcessCommon.ReadSourcePackageSlot(Actor.Reference, Epoch, FalloutProcessPackageSlot.Current);
        }
        public sbyte ReadPackageKind(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package)
        { RequireCurrent(invocation); return Registry.Require(package).Kind; }
        public void SpecialPackageCallback(FalloutPackageCommandFieldInvocation invocation)
        {
            RequireCurrent(invocation);
            if (!Actor.EnginePlayer) throw new NotSupportedException("source-special-package-non-Player-Actor-callback-unbound");
            // The selected Player override is a source-qualified void return.
        }
        public FalloutProcessSceneQuery? ReadProcess(FalloutPackageCommandFieldInvocation invocation)
        { RequireCurrent(invocation); return world.ProcessCommon.ReadSceneQueriesForPackageCommand(invocation); }
        public void MarkEvent(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package, uint mask)
        { RequireCurrent(invocation); _ = fields.MarkPackageEvent(invocation, package, mask); }
        public void PackageChanged(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package)
        { RequireCurrent(invocation); throw new NotSupportedException("source-process-package-change-callback-consumers-unbound"); }
        public void ClearProcessFlag(FalloutPackageCommandFieldInvocation invocation) =>
            world.ProcessCommon.StorePackageRequestFlag(invocation, false, RequireCurrent);

        public void PackageStarted(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package)
        {
            RequireCurrent(invocation);
            var declaration = FalloutScriptPackage.Read(Registry.Require(package).Record);
            var start = declaration.EventPrograms.GetValueOrDefault("POBA");
            if (start is not null)
            {
                start.ValidateScript();
                if (start.CompiledProgram is { } compiled)
                {
                    if (compiled.LocalCount != 0 || compiled.ResultInstructions().Any())
                        throw new NotSupportedException("source-package-start-compiled-result-command-scope-unbound");
                }
                else if (FalloutDialogueTopic.CodeLines(start.Source).Any())
                    throw new NotSupportedException("source-package-start-original-result-program-unbound");
            }
            if (declaration.Events.GetValueOrDefault("POBA") is { } idle)
            {
                if (world.NativeSourceRecords.GetEffective(idle).Signature != "IDLE")
                    throw new InvalidDataException("Package-start idle has no actual winning IDLE.");
                // A genuine null selected animation context returns before
                // idle effects. It does not skip the subsequent byte store.
                _ = fields.ReadAnimationContext(invocation,
                    () => world.ProcessCommon.ReadSceneQueriesForPackageCommand(invocation),
                    () => world.CampaignStandalonePlayerScene.ReadPackageCommandAnimationView(invocation, RequireCurrent));
                _ = world.ProcessCommon.StorePackageStartByte(invocation, 1, RequireCurrent);
            }
            else
            {
                throw new NotSupportedException("source-package-start-idle-absent-independent-byte-writer-unbound");
            }
            if (start?.Topic is not null) throw new NotSupportedException("source-package-start-topic-voice-consumer-unbound");
            _ = world.ProcessCommon.ClearPackageStartCacheFlag(invocation, FalloutActorProcessCommonState.PackageStartFirstCacheMask, RequireCurrent);
            // This is another actual process and child read, after the first
            // nonnull cached-value child. Neither store reuses the first read.
            _ = ReadProcess(invocation);
            _ = world.ProcessCommon.ClearPackageStartCacheFlag(invocation, FalloutActorProcessCommonState.PackageStartSecondCacheMask, RequireCurrent);
        }
        public int EnsureProcedure(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package)
        {
            RequireCurrent(invocation); var actual = Registry.Require(package);
            if (!actual.ProcedureAvailable) throw new NotSupportedException("source-package-procedure-current-writer-unowned");
            if (actual.Procedure != -1) return actual.Procedure;
            if (FalloutPackageAssignmentSource.PureProcedure(actual.Kind) is not { } procedure)
                throw new NotSupportedException("source-package-kind-target-or-error-procedure-consumers-unbound");
            Registry.PublishProcedure(invocation, actual, procedure, "actual-source-package-kind-table-procedure-store");
            return procedure;
        }
        public void SavePosition(FalloutPackageCommandFieldInvocation invocation)
        { RequireCurrent(invocation); _ = fields.SavePosition(invocation, this); }
        public void SetPackageFlag(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package, uint mask)
        {
            RequireCurrent(invocation);
            if (mask != FalloutSourcePackageAssignment.ScriptPackageRuntimeFlag)
                throw new InvalidDataException("Package command changed its selected runtime flag writer.");
            Registry.PublishScriptAssignedFlag(invocation, Registry.Require(package));
        }
        public void AssignActorPackage(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package, bool primary, bool alternate)
        {
            RequireCurrent(invocation);
            _ = world.ProcessCommon.ExecuteActorPackageAssignment(invocation, package, primary, alternate, this,
                call => world._currentActorPackageAssignment = call);
        }
        public int ReadProcessTier(FalloutPackageCommandFieldInvocation invocation)
        { RequireCurrent(invocation); return world.ProcessCommon.ReadActorAssignmentTier(invocation).Value; }
        public uint StoreProcessClock(FalloutPackageCommandFieldInvocation invocation)
        { RequireCurrent(invocation); throw new NotSupportedException("source-package-command-nonzero-tier-clock-store-unbound"); }
        public void UpdateTierOne(FalloutPackageCommandFieldInvocation invocation)
        { RequireCurrent(invocation); throw new NotSupportedException("source-package-command-tier-one-process-manager-consumers-unbound"); }
        public bool ReadActualPlayerIdentity(FalloutPackageCommandFieldInvocation invocation)
        { RequireCurrent(invocation); return Actor.Reference == world._enginePlayer && Actor.EnginePlayer; }
        public void StorePlayerFourth(FalloutPackageCommandFieldInvocation invocation)
        {
            RequireCurrent(invocation);
            if (world._currentPackageCommand is not { Step: FalloutSourcePackageAssignmentStep.FourthStore, IsPlayer: true } call ||
                call.Invocation != invocation || !call.ReturnedChildren.Contains(FalloutSourcePackageAssignmentStep.ActorAssignment))
                throw new InvalidOperationException("Player Fourth cannot run before the actual returned assignment prefix.");
            world.CampaignStandalonePlayerScene.PlayerInhibition.StorePackageAssignmentFourth(invocation, RequireCurrent);
        }
        public FalloutFormKey? ReadWorldspace(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey? parentCell)
        {
            var read = ReadCurrentReferenceWorldspace(invocation);
            if (read.Parent.Cell != parentCell)
                throw new InvalidDataException("Worldspace query changed its actual current ParentCELL.");
            return read.Worldspace;
        }
        public void ReferenceChanged(FalloutPackageCommandFieldInvocation invocation, uint mask)
        {
            RequireCurrent(invocation);
            _ = world.WithActualReferencePosition(FalloutReferencePositionIngress.PackageCommand,
                current => world._sourceReferenceChanges!.ReferenceChanged(current, mask, false,
                    new SourceReferencePositionConsumers(world)));
        }
        public FalloutReferencePositionRead ReadCurrentReferencePosition(FalloutPackageCommandFieldInvocation invocation)
        {
            RequireCurrent(invocation);
            return world.WithActualReferencePosition(FalloutReferencePositionIngress.PackageCommand,
                current => world._sourcePlayerReferencePosition!.ReadCurrentPosition(current));
        }
        public FalloutReferenceParentCellRead ReadCurrentReferenceParentCell(FalloutPackageCommandFieldInvocation invocation)
        {
            RequireCurrent(invocation);
            return world.WithActualReferencePosition(FalloutReferencePositionIngress.PackageCommand,
                current => world._sourcePlayerReferencePosition!.ReadCurrentParentCell(current,
                    new SourceReferencePositionConsumers(world)));
        }
        public FalloutReferenceWorldspaceRead ReadCurrentReferenceWorldspace(FalloutPackageCommandFieldInvocation invocation)
        {
            RequireCurrent(invocation);
            return world.WithActualReferencePosition(FalloutReferencePositionIngress.PackageCommand,
                current => world._sourcePlayerReferencePosition!.ReadWorldspace(current,
                    new SourceReferencePositionConsumers(world)));
        }
        public FalloutPackageCommandRawRotationRead? ReadRawRotationZ(FalloutPackageCommandFieldInvocation invocation) =>
            world.CampaignStandalonePlayerScene.ReadPackageCommandRawRotationZ(invocation, RequireCurrent);
        private FalloutPackageRuntimeRegistry Registry => world._sourcePackageRegistry ??
            throw new NotSupportedException("source-package-command-mutable-winning-registry-unbound");
    }
}
