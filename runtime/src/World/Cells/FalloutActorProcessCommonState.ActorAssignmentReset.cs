using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    private FalloutActorLinkedCall SetActorAssignmentLinkedReference(FalloutPackageCommandFieldInvocation invocation,
        FalloutPackageExtraReference? desired, FalloutActorLinkedOccurrence occurrence, IFalloutActorPackageAssignmentConsumers consumers)
    {
        var process = ReadSceneQueriesForPackageCommand(invocation); var fields = RequireActorAssignmentFields(invocation);
        RequireActorAssignmentLane(fields, FalloutActorAssignmentLane.LinkedReference); desired?.Validate();
        var before = fields.Values.LinkedReference; var entered = Next(); var index = fields.LinkedCalls.Count;
        var stores = new List<FalloutActorAssignmentStore>();
        var call = new FalloutActorLinkedCall(invocation, fields.Source.Identity, fields.Publication.Factory, Guid.NewGuid(),
            process, occurrence, desired, entered, entered, FalloutActorLinkedStep.Entered, null, before, before == desired,
            [], false, null, false, null);
        try
        {
            Publish(); Step(FalloutActorLinkedStep.Equality);
            if (!call.Equal)
            {
                if (before is not null)
                {
                    Step(FalloutActorLinkedStep.PreviousCleanup);
                    _ = CleanupActorAssignmentLinkedSource(invocation, consumers);
                }
                Step(FalloutActorLinkedStep.PointerStore);
                var store = PackageCommandStore(invocation, consumers.RequireCurrent, currentProcess =>
                {
                    var current = Require(invocation.Actor.Reference); var live = RequireActorAssignmentFields(invocation);
                    var write = new FalloutActorAssignmentStore(invocation, currentProcess, live.Publication.Factory,
                        FalloutActorAssignmentCell.LinkedReference, Next(), null, null, live.Values.LinkedReference, desired);
                    _current[current.Source.Reference] = current with
                    {
                        ActorAssignment = live with
                        { Values = live.Values with { LinkedReference = desired }, Stores = AppendPackageCommandStore(live.Stores, write) },
                        Changed = write.Changed
                    };
                    return write;
                });
                stores.Add(store); Step(FalloutActorLinkedStep.PackageSelection);
                var primary = ReadSourcePackageSlot(invocation.Actor.Reference, invocation.Epoch, FalloutProcessPackageSlot.Primary);
                var selected = primary.Fields.Fields.Package is not null ? primary :
                    ReadSourcePackageSlot(invocation.Actor.Reference, invocation.Epoch, FalloutProcessPackageSlot.Current);
                call = call with { Selected = selected }; Step(FalloutActorLinkedStep.ExtraStore);
                throw new NotSupportedException("source-Actor-linked-selected-package-Extra-direct-writer-unowned");
            }
            if (desired is not null)
            {
                Step(FalloutActorLinkedStep.NonnullEffects);
                throw new NotSupportedException("source-Actor-linked-reference-activation-and-effect-children-unowned");
            }
            Step(FalloutActorLinkedStep.NullCleanup);
            // Equal null still calls the selected High cleanup. Its selected
            // Boolean child is a qualified void return; the null Actor skips
            // the separate Actor cleanup. No pointer/Extra store is executed.
            consumers.RequireCurrent(invocation); call = call with { NullCleanupReturned = true };
            Step(FalloutActorLinkedStep.Returned); call = call with { Returned = true }; Publish(); return call;
        }
        catch (Exception error)
        {
            call = call with
            {
                FailedAt = call.Step,
                Step = FalloutActorLinkedStep.Failed,
                Changed = Next(),
                Stores = Array.AsReadOnly(stores.ToArray()),
                Failure = error.Message
            }; Publish(); throw;
        }
        void Step(FalloutActorLinkedStep step)
        { consumers.RequireCurrent(invocation); call = call with { Step = step, Changed = Next(), Stores = Array.AsReadOnly(stores.ToArray()) }; Publish(); }
        void Publish()
        {
            var current = Require(invocation.Actor.Reference); var live = current.ActorAssignment!; var calls = live.LinkedCalls.ToArray();
            if (calls.Length == index) calls = [.. calls, call]; else calls[index] = call;
            _current[current.Source.Reference] = current with { ActorAssignment = live with { LinkedCalls = Array.AsReadOnly(calls) }, Changed = call.Changed };
        }
    }

    private FalloutActorHighResetCall ResetActorAssignmentHigh(FalloutPackageCommandFieldInvocation invocation,
        IFalloutActorPackageAssignmentConsumers consumers)
    {
        var process = ReadSceneQueriesForPackageCommand(invocation); var fields = RequireActorAssignmentFields(invocation);
        var entered = Next(); var index = fields.HighCalls.Count; var stores = new List<FalloutActorAssignmentStore>();
        var call = new FalloutActorHighResetCall(invocation, fields.Source.Identity, fields.Publication.Factory, Guid.NewGuid(), process,
            entered, entered, FalloutActorHighResetStep.Entered, null, [], null, null, null, false, false, null);
        try
        {
            Publish();
            Store(FalloutActorHighResetStep.ResetMode, FalloutActorAssignmentCell.ResetMode, FalloutActorPackageAssignmentSource.ResetMode);
            Store(FalloutActorHighResetStep.CommonWord, FalloutActorAssignmentCell.CommonWord, 0);
            Store(FalloutActorHighResetStep.IndependentByte, FalloutActorAssignmentCell.IndependentResetByte, 0);
            Store(FalloutActorHighResetStep.FlagTwo, FalloutActorAssignmentCell.CommonFlagTwo, 0);
            Store(FalloutActorHighResetStep.FlagOne, FalloutActorAssignmentCell.CommonFlagOne, 0);
            Store(FalloutActorHighResetStep.FlagForty, FalloutActorAssignmentCell.CommonFlagForty, 0);
            Step(FalloutActorHighResetStep.FlagTwenty);
            call = call with { RequestFlag = StorePackageRequestFlag(invocation, false, consumers.RequireCurrent) };
            Step(FalloutActorHighResetStep.LinkedReference);
            call = call with { Linked = SetActorAssignmentLinkedReference(invocation, null, FalloutActorLinkedOccurrence.SharedReset, consumers) };
            Step(FalloutActorHighResetStep.InlineList);
            var live = RequireActorAssignmentFields(invocation); RequireActorAssignmentLane(live, FalloutActorAssignmentLane.InlineList);
            if (live.Values.InlineValue is not null || live.Values.InlineLink is not null)
                throw new NotSupportedException("source-High-reset-inline-list-payload-free-and-unlink-unowned");
            call = call with { InlineListEmpty = true };
            Step(FalloutActorHighResetStep.Loading);
            call = call with { Loading = CleanupActorAssignmentLinkedSource(invocation, consumers) };
            Step(FalloutActorHighResetStep.Tail);
            foreach (var (cell, bits) in ActorHighResetTail)
                stores.Add(StoreActorAssignmentCell(invocation, cell, bits, consumers.RequireCurrent));
            Step(FalloutActorHighResetStep.Returned); call = call with { Returned = true }; Publish(); return call;
        }
        catch (Exception error)
        {
            call = call with
            {
                FailedAt = call.Step,
                Step = FalloutActorHighResetStep.Failed,
                Changed = Next(),
                Stores = Array.AsReadOnly(stores.ToArray()),
                Failure = error.Message
            }; Publish(); throw;
        }
        void Store(FalloutActorHighResetStep step, FalloutActorAssignmentCell cell, uint bits)
        { Step(step); stores.Add(StoreActorAssignmentCell(invocation, cell, bits, consumers.RequireCurrent)); }
        void Step(FalloutActorHighResetStep step)
        { consumers.RequireCurrent(invocation); call = call with { Step = step, Changed = Next(), Stores = Array.AsReadOnly(stores.ToArray()) }; Publish(); }
        void Publish()
        {
            var current = Require(invocation.Actor.Reference); var live = current.ActorAssignment!; var calls = live.HighCalls.ToArray();
            if (calls.Length == index) calls = [.. calls, call]; else calls[index] = call;
            _current[current.Source.Reference] = current with { ActorAssignment = live with { HighCalls = Array.AsReadOnly(calls) }, Changed = call.Changed };
        }
    }

    private FalloutDataHandlerLoadingRead CleanupActorAssignmentLinkedSource(FalloutPackageCommandFieldInvocation invocation,
        IFalloutActorPackageAssignmentConsumers consumers)
    {
        var read = consumers.ReadDataHandlerLoading(invocation); var fields = RequireActorAssignmentFields(invocation);
        if (read.Source != fields.Source.Identity || read.Stack != _stack || read.RuntimeProcess == Guid.Empty || read.Process == Guid.Empty ||
            read.Factory == Guid.Empty || read.Constructor == Guid.Empty || read.Sequence < 0 ||
            read.Invocation.CommonProcess != invocation.Process || read.Invocation.Actor != invocation.Actor || read.Invocation.Epoch != invocation.Epoch ||
            read.Invocation.PackageCommand != invocation.SourceCommand || read.Invocation.ThreadId != invocation.ThreadId ||
            read.Invocation.Ingress != FalloutDataHandlerLoadingIngress.PackageCleanup || read.Invocation.RuntimeProcess != read.RuntimeProcess)
            throw new InvalidDataException("Linked-source cleanup lost the actual independent shared loading owner.");
        if (read.Value != 0) return read;
        RequireActorAssignmentLane(fields, FalloutActorAssignmentLane.LinkedReference);
        if (fields.Values.LinkedReference is not null)
        {
            RequireActorAssignmentLane(fields, FalloutActorAssignmentLane.LinkedEffectIndex);
            if (fields.Values.LinkedEffectIndex >= 0)
                throw new NotSupportedException("source-Actor-linked-reference-effect-removal-child-unowned");
        }
        RequireActorAssignmentLane(fields, FalloutActorAssignmentLane.OtherEffectActor);
        if (fields.Values.OtherEffectActor is not null)
            throw new NotSupportedException("source-Actor-linked-other-effect-Interface-and-removal-child-unowned");
        consumers.RequireCurrent(invocation); return read;
    }

    private static readonly (FalloutActorAssignmentCell Cell, uint Bits)[] ActorHighResetTail =
    [
        (FalloutActorAssignmentCell.TailByte, 0), (FalloutActorAssignmentCell.TailFirstWord, 0),
        (FalloutActorAssignmentCell.TailIndexWord, ushort.MaxValue), (FalloutActorAssignmentCell.TailFinalWord, 0),
        (FalloutActorAssignmentCell.TailFirstPointer, 0), (FalloutActorAssignmentCell.TailClock, 0),
        (FalloutActorAssignmentCell.TailSecondPointer, 0), (FalloutActorAssignmentCell.TailSecondByte, 0),
        (FalloutActorAssignmentCell.TailThirdPointer, 0), (FalloutActorAssignmentCell.TailThirdByte, 0),
        (FalloutActorAssignmentCell.TailFourthPointer, 0), (FalloutActorAssignmentCell.TailFinalByte, 0)
    ];
}
