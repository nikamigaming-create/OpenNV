using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    internal FalloutActorPackageAssignmentCall ExecuteActorPackageAssignment(FalloutPackageCommandFieldInvocation invocation,
        FalloutFormKey requested, bool primary, bool alternate, IFalloutActorPackageAssignmentConsumers consumers,
        Action<FalloutActorPackageAssignmentCall> observe)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(consumers); ArgumentNullException.ThrowIfNull(observe);
        consumers.Source.Validate(); FalloutPackageRuntimeRegistry.RequireInvocation(invocation);
        var fields = RequireActorAssignmentFields(invocation);
        if (fields.Source != consumers.Source || consumers.Stack != _stack)
            throw new InvalidDataException("Actor package assignment borrowed another actual source or winning stack.");
        if (_actorAssignmentInvocation is not null)
        { ++_actorAssignmentFault; throw new InvalidOperationException("Actor package assignment reentered its original graph."); }
        if (!_actorAssignmentCommands.TryGetValue((invocation.Actor.Reference, invocation.Epoch), out var commands))
            throw new InvalidOperationException("Actor package assignment did not bind its current constructor/history.");
        if (!commands.Add(invocation.SourceCommand)) throw new InvalidOperationException("Actor package assignment replayed its original command.");
        _actorAssignmentInvocation = invocation; var fault = _actorAssignmentFault; var index = fields.Calls.Count;
        var returned = new List<FalloutActorAssignmentStep>(); var statuses = new List<FalloutReferenceActorYawModeRead>();
        var entered = Next();
        var call = new FalloutActorPackageAssignmentCall(invocation, fields.Source.Identity, fields.Publication.Factory,
            requested, primary, alternate, entered, entered, FalloutActorAssignmentStep.Entered, null, null, [], StatusReads: []);
        try
        {
            Publish();
            Child(FalloutActorAssignmentStep.Process, () =>
            {
                var process = consumers.ReadCurrentProcess(invocation);
                if (process is not null) RequireActorAssignmentProcess(process, invocation);
                call = call with { InitialProcess = process };
            });
            if (call.InitialProcess is null) return Return();
            var registry = _packageRegistry ?? throw new NotSupportedException("source-Actor-package-winning-registry-unbound");
            var package = registry.Require(requested);
            Child(FalloutActorAssignmentStep.Procedure, () =>
            {
                if (!package.ProcedureAvailable) throw new NotSupportedException("source-package-runtime-procedure-current-writer-unowned");
                var procedure = package.Procedure;
                if (procedure == -1)
                {
                    procedure = consumers.EnsureCurrentProcedure(invocation, requested);
                    if (!package.ProcedureAvailable || package.Procedure != procedure)
                        throw new InvalidDataException("Actor package procedure callback did not store the same winning mutable PACK.");
                }
                call = call with { Procedure = procedure, Kind = package.Kind };
            });
            if (!FalloutActorPackageAssignmentSource.SkipsStatus(package.Kind))
            {
                uint first = 0;
                Child(FalloutActorAssignmentStep.Status, () => first = Status());
                if (first != 0)
                {
                    Child(FalloutActorAssignmentStep.ExceptionalStatus, () =>
                    {
                        if (package.Kind == 21 && Status() == 4) return;
                        var four = Status();
                        if (four == 4 || Status() == 9)
                            throw new NotSupportedException("source-Actor-package-nonzero-status-selected-cleanup-unowned");
                        throw new NotSupportedException("source-Actor-package-nonzero-status-process-kind-and-action-child-unowned");
                    });
                }
            }
            if (alternate)
                Child(FalloutActorAssignmentStep.Alternate, () => throw new NotSupportedException("source-Actor-package-alternate-runtime-flag-writer-unowned"));
            if (primary)
                Child(FalloutActorAssignmentStep.Primary, () => throw new NotSupportedException("source-Actor-package-primary-assignment-arm-unowned"));
            Child(FalloutActorAssignmentStep.Adjustment, () => call = call with { Adjustment = ReadActorAssignmentAdjustment(invocation) });
            if (FalloutActorPackageAssignmentSource.IsPositiveAdjustment(call.Adjustment!.Value))
            {
                Child(FalloutActorAssignmentStep.AdjustmentReread, () => call = call with { Adjustment = ReadActorAssignmentAdjustment(invocation) });
                Child(FalloutActorAssignmentStep.PositiveAdjustment, () =>
                    throw new NotSupportedException("source-Actor-package-positive-adjustment-accumulation-and-Primary-cleanup-unowned"));
            }
            if (package.Kind != 18)
            {
                Child(FalloutActorAssignmentStep.AssignmentByteRead, () => call = call with { AssignmentByte = Byte() });
                Child(FalloutActorAssignmentStep.AssignmentByteStore, () =>
                {
                    var stored = consumers.StoreAssignmentByteZero(invocation);
                    if (!stored.Returned || stored.Invocation.PackageCommand != invocation.SourceCommand || stored.Invocation.Actor != invocation.Actor ||
                        stored.Source != fields.Source.Reference.Identity || stored.Stores is not [{ Step: FalloutReferencePositionStep.ActorAssignmentByte, Bits: 0 }])
                        throw new InvalidDataException("Actor Current arm lost its same reference byte-zero writer.");
                    call = call with { ByteStore = stored };
                });
                Child(FalloutActorAssignmentStep.Eligibility, () =>
                {
                    if (!invocation.Actor.EnginePlayer)
                        throw new NotSupportedException("source-Actor-package-current-eligibility-selected-class-unqualified");
                    // The selected Player child is a source-qualified true
                    // return. No alternative Actor callback is entered.
                });
            }
            Child(FalloutActorAssignmentStep.ExtraDerivation, () =>
            {
                var extra = consumers.DeriveCurrentPackageExtra(invocation, requested);
                call = call with { Extra = extra };
                var currentTarget = registry.ReadCurrentTarget(invocation, package);
                if (extra.Source != fields.Source.Identity || extra.Package != requested || extra.RecordSha256 != package.RecordSha256 ||
                    !extra.CurrentTargetAvailable || extra.Failure is not null ||
                    extra.TargetKind is not (0 or 3) && extra.Extra is not null || extra.TargetRead != currentTarget ||
                    extra.TargetKind != currentTarget.Values?.Kind)
                    throw new NotSupportedException(extra.Failure ?? "source-Actor-package-target-Extra-current-owner-unbound");
                extra.Extra?.Validate();
            });
            Child(FalloutActorAssignmentStep.CurrentSetter, () =>
            {
                var mutation = SetSourcePackageState(invocation, FalloutProcessPackageSlot.Current, requested, call.Extra!.Extra, consumers);
                if (mutation.Step != FalloutPackageStateStep.Returned)
                    throw new InvalidOperationException("Actor package setter did not return its real Current owner.");
                call = call with { PackageMutation = mutation };
            });
            Child(FalloutActorAssignmentStep.ActorCallback, () =>
            {
                // This direct source child has no effects and returns its
                // one operand. It is qualified with the original callsite.
            });
            byte postByte = 0;
            Child(FalloutActorAssignmentStep.PostAssignmentByte, () => postByte = Byte().Value);
            if (postByte == 0)
            {
                FalloutProcessSceneQuery? process = null;
                Child(FalloutActorAssignmentStep.PostProcess, () =>
                {
                    process = consumers.ReadCurrentProcess(invocation);
                    if (process is not null) RequireActorAssignmentProcess(process, invocation);
                });
                if (process is not null)
                {
                    Child(FalloutActorAssignmentStep.CommonAnimation, () =>
                    {
                        var current = ReadSceneQueriesForPackageCommand(invocation);
                        RequireActorAssignmentProcess(current, invocation); call = call with { AnimationProcess = current };
                    });
                    if (call.AnimationProcess!.AnimationContext is not null)
                    {
                        uint value = 0;
                        Child(FalloutActorAssignmentStep.AnimationStatus, () => value = Status());
                        var mark = value == 0;
                        if (!mark)
                        {
                            Child(FalloutActorAssignmentStep.AnimationStatusNine, () => value = Status()); mark = value == 9;
                            if (!mark) { Child(FalloutActorAssignmentStep.AnimationStatusFour, () => value = Status()); mark = value == 4; }
                        }
                        if (mark && package.Kind != 28)
                            Child(FalloutActorAssignmentStep.AnimationMark, () => throw new NotSupportedException("source-High-package-animation-mask800-current-field-writer-unbound"));
                    }
                }
            }
            Child(FalloutActorAssignmentStep.LinkedReference, () => call = call with
            { Linked = SetActorAssignmentLinkedReference(invocation, null, FalloutActorLinkedOccurrence.ActorAssignment, consumers) });
            Child(FalloutActorAssignmentStep.HighReset, () => call = call with { HighReset = ResetActorAssignmentHigh(invocation, consumers) });
            Child(FalloutActorAssignmentStep.MoverByte, () => postByte = Byte().Value);
            if (postByte == 0)
            {
                Child(FalloutActorAssignmentStep.BasePredicate, () =>
                {
                    var basis = consumers.ReadCurrentBaseForMover(invocation);
                    if (string.IsNullOrWhiteSpace(basis.Source) || basis.Sequence < 0 || basis.Form.ObjectId == 0 ||
                        basis.Kind != FalloutActorPackageAssignmentSource.CreatureKind && basis.CreatureFlags is not null)
                        throw new InvalidDataException("Actor mover gate borrowed a resolved identity or unrelated creature flags.");
                    call = call with { Base = basis };
                });
                if (!FalloutActorPackageAssignmentSource.SkipsMover(call.Base!.Kind, call.Base.CreatureFlags))
                {
                    Child(FalloutActorAssignmentStep.MoverReset, () =>
                    {
                        var join = new FalloutActorMoverResetJoin(call.PackageMutation!, call.Linked!.MoverReceipt, call.HighReset!.MoverReceipt);
                        consumers.ResetActualMover(invocation, join); call = call with { MoverReturned = true };
                    });
                }
            }
            Child(FalloutActorAssignmentStep.Followers, () =>
            {
                var extra = consumers.ReadFollowerExtra(invocation); call = call with { Followers = extra };
                if (extra.Actor != invocation.Actor || extra.Process == Guid.Empty || extra.Factory == Guid.Empty || extra.Constructor == Guid.Empty ||
                    extra.Source != FalloutReferenceFollowerSource.Reviewed(fields.Source.Reference).Identity ||
                    extra.Sequence < 0 || extra.Present != (extra.Extra is not null) || !extra.Present && extra.Payload is not null ||
                    extra.ReferenceInvocation is not { } reference || reference.Actor != invocation.Actor ||
                    reference.ReferenceProcess != extra.Process || reference.ReferenceConstructor != extra.Constructor ||
                    reference.CommonProcess != invocation.Process || reference.PackageCommand != invocation.SourceCommand ||
                    reference.Ingress != FalloutReferencePositionIngress.PackageCommand || reference.ThreadId != invocation.ThreadId ||
                    reference.Epoch != invocation.Epoch)
                    throw new InvalidDataException("Actor followers lost their actual independent Extra membership owner.");
                if (extra.Payload is not null)
                    throw new NotSupportedException("source-Actor-package-follower-ordered-payload-and-process-services-unowned");
            });
            return Return();
        }
        catch (Exception error)
        {
            call = call with
            {
                FailedAt = call.Step,
                Step = FalloutActorAssignmentStep.Failed,
                Changed = Next(),
                Failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message,
                ReturnedChildren = Array.AsReadOnly(returned.ToArray()),
                StatusReads = Array.AsReadOnly(statuses.ToArray())
            };
            _actorAssignmentFailure ??= call.Failure; Publish(); throw;
        }
        finally { _actorAssignmentInvocation = null; }

        uint Status()
        {
            var read = consumers.ReadAssignmentStatus(invocation);
            if (read.Source != fields.Source.Reference.Identity || read.Stack != _stack || read.Actor != invocation.Actor ||
                read.Process == Guid.Empty || read.Factory == Guid.Empty || read.Constructor == Guid.Empty || read.Sequence < 0)
                throw new InvalidDataException("Actor package status did not borrow the same raw yaw-selection authority.");
            statuses.Add(read); call = call with { StatusReads = Array.AsReadOnly(statuses.ToArray()) }; Publish(); return read.Mode;
        }
        FalloutReferenceActorAssignmentByteRead Byte()
        {
            var read = consumers.ReadAssignmentByte(invocation);
            if (read.Source != fields.Source.Reference.Identity || read.Stack != _stack || read.Actor != invocation.Actor ||
                read.Process == Guid.Empty || read.Factory == Guid.Empty || read.Constructor == Guid.Empty || read.Sequence < 0)
                throw new InvalidDataException("Actor package assignment byte borrowed another reference owner.");
            return read;
        }
        void Guard()
        {
            if (_actorAssignmentInvocation != invocation || _actorAssignmentFault != fault)
                throw new InvalidOperationException("Actor assignment swallowed reentry or replaced its entered command.");
            consumers.RequireCurrent(invocation);
            if (_actorAssignmentFault != fault) throw new InvalidOperationException("Actor assignment callback swallowed reentry.");
        }
        void Child(FalloutActorAssignmentStep step, Action operation)
        {
            call = call with { Step = step, Changed = Next() }; Publish(); Guard(); operation(); Guard();
            returned.Add(step); call = call with { Changed = Next(), ReturnedChildren = Array.AsReadOnly(returned.ToArray()) }; Publish();
        }
        void Publish()
        {
            var current = Require(invocation.Actor.Reference); var live = current.ActorAssignment!;
            var calls = live.Calls.ToArray();
            if (calls.Length == index) calls = [.. calls, call]; else calls[index] = call;
            _current[current.Source.Reference] = current with { ActorAssignment = live with { Calls = Array.AsReadOnly(calls) }, Changed = call.Changed };
            _lastActorAssignment = call; observe(call);
        }
        FalloutActorPackageAssignmentCall Return()
        {
            Guard(); call = call with
            {
                Step = FalloutActorAssignmentStep.Returned,
                Changed = Next(),
                Returned = true,
                ReturnedChildren = Array.AsReadOnly(returned.ToArray()),
                StatusReads = Array.AsReadOnly(statuses.ToArray())
            };
            Publish(); return call;
        }
    }

    private void RequireActorAssignmentProcess(FalloutProcessSceneQuery process, FalloutPackageCommandFieldInvocation invocation)
    {
        if (process.Process != invocation.Process || process.Actor != invocation.Actor || process.Epoch != invocation.Epoch ||
            process.Stack != _stack || process.ExecutableSha256 != _source.ExecutableSha256 || process.GameplayOwnership == Guid.Empty)
            throw new InvalidDataException("Actor assignment borrowed a foreign current process publication.");
    }
}
