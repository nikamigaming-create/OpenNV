using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerAnimationPreparationState
{
    internal FalloutPlayerAnimationPreparationCall? Capture()
    {
        if (_busy) { ++_fault; throw new InvalidOperationException("Preparation capture reentered its actual child."); }
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Preparation capture changed its actual thread.");
        if (LastCall is { } call) Validate(call, Source, Stack);
        return LastCall;
    }

    internal static void Validate(FalloutPlayerAnimationPreparationCall call, FalloutPlayerPostAnimationSource source, string stack)
    {
        source.Validate();
        if (call is null || call.Frame is null || call.Frame.Caller is null || call.Frame.Common is null || string.IsNullOrWhiteSpace(stack))
            throw new InvalidDataException("Preparation omitted its actual entered source call/frame.");
        var frame = call.Frame;
        if (call.Source != source.Identity || call.Stack != stack || call.Factory == Guid.Empty || call.Ordinal < 1 ||
            call.Entered < 1 || call.Changed <= call.Entered || !Enum.IsDefined(call.Step) ||
            frame.Caller.Main == Guid.Empty || frame.Caller.Invocation == Guid.Empty || frame.Caller.Ordinal < 1 ||
            frame.PlayerProcess == Guid.Empty || frame.PlayerConstructor == Guid.Empty || frame.Prelude == Guid.Empty || frame.ThreadId < 1 ||
            frame.Common.Process == Guid.Empty || frame.Common.Stack != stack || !frame.Common.Actor.EnginePlayer || frame.Common.Epoch < 1 ||
            frame.Common.ExecutableSha256 != source.Tail.Scene.Main.EngineSha256 ||
            call.Returned != (call.Failure is null) || call.Failure is { Length: 0 } ||
            (call.Returned ? call.Step != FalloutPlayerAnimationPreparationStep.Returned || call.FailedAt is not null :
                call.Step != FalloutPlayerAnimationPreparationStep.Failed || call.FailedAt is not { } failed || !Enum.IsDefined(failed) ||
                failed is FalloutPlayerAnimationPreparationStep.Returned or FalloutPlayerAnimationPreparationStep.Failed))
            throw new InvalidDataException("Preparation lost its actual source/frame or invented a complete return.");
        var stop = call.Life?.Result == true ? FalloutPlayerAnimationPreparationStep.Life :
            call.Activity is { Value: not 0 } ? FalloutPlayerAnimationPreparationStep.Activity :
            call.View is { Value: 0 } ? FalloutPlayerAnimationPreparationStep.View :
            call.Controller is { } controller && (call.Selected?.Context is null || controller.Controller is null)
                ? FalloutPlayerAnimationPreparationStep.Controller : (FalloutPlayerAnimationPreparationStep?)null;
        if (call.Returned && stop is null || !call.Returned && stop is { } early && call.FailedAt!.Value > early)
            throw new InvalidDataException("Preparation skipped a mandatory nonnull child or passed a genuine earlier return.");
        var at = call.Returned ? stop!.Value : call.FailedAt!.Value;
        Child(FalloutPlayerAnimationPreparationStep.Life, call.Life);
        Child(FalloutPlayerAnimationPreparationStep.Activity, call.Activity);
        Child(FalloutPlayerAnimationPreparationStep.View, call.View);
        Child(FalloutPlayerAnimationPreparationStep.SelectedAnimation, call.Selected);
        Child(FalloutPlayerAnimationPreparationStep.Controller, call.Controller);
        Child(FalloutPlayerAnimationPreparationStep.SelectedSlot, call.SelectedSlot);
        Child(FalloutPlayerAnimationPreparationStep.SelectedGroup, call.SelectedGroup);
        Child(FalloutPlayerAnimationPreparationStep.FirstAnimation, call.First);
        Child(FalloutPlayerAnimationPreparationStep.FirstSlot, call.FirstSlot);
        Child(FalloutPlayerAnimationPreparationStep.FirstGroup, call.FirstGroup);
        if (call.Returned && AtChild(at) is null || at > FalloutPlayerAnimationPreparationStep.Life && call.Life?.Result is null)
            throw new InvalidDataException("Preparation omitted a passed real getter or returned at an unavailable child.");
        if (call.Life is { } life)
        {
            Common(life.Process);
            if (life.Raw is null || life.Argument || string.IsNullOrWhiteSpace(life.Raw.Owner) ||
                life.Result is { } result && (life.Raw.Value is not { } raw || life.Raw.Failure is not null ||
                    result != FalloutPlayerFollowingContinuation.LifePredicate(raw, false)))
                throw new InvalidDataException("Preparation replaced its actual Life(false) result.");
        }
        if (call.Activity is { } activity)
        {
            Common(activity.Process);
            if (activity.Value is < sbyte.MinValue or > sbyte.MaxValue)
                throw new InvalidDataException("Preparation activity changed its actual signed source byte width.");
        }
        if (call.View is { } view) View(view);
        if (call.Selected is { } selected)
        {
            if (selected.Source != source.Identity || selected.Stack != stack || selected.FieldsProcess != frame.Common.Process ||
                selected.FieldsFactory == Guid.Empty || selected.Constructor != frame.PlayerConstructor || selected.Epoch != frame.Common.Epoch ||
                selected.FieldsSequence < 0 || selected.Frame != frame || !Enum.IsDefined(selected.Selection) ||
                (selected.Override is not null ? selected.Selection != FalloutPackageAnimationSelection.Override || selected.View is not null ||
                    selected.FirstRead || selected.First is not null || selected.Common is not null || selected.Context != selected.Override :
                    selected.View is null || selected.FirstRead != (selected.View.Value == 0) || !selected.FirstRead && selected.First is not null ||
                    selected.Selection != FalloutActorPackageCommandFields.SelectAnimation(selected.View.Value, null, selected.First)))
                throw new InvalidDataException("Preparation changed the original override/view/first/Common selection law.");
            if (selected.View is { } read) View(read);
            if (selected.Selection == FalloutPackageAnimationSelection.Process)
            {
                if (selected.Common is not { } common || selected.Context != common.AnimationContext)
                    throw new InvalidDataException("Preparation omitted or changed its actual Common pointer getter.");
                Common(common);
            }
            else if (selected.Common is not null || selected.Selection == FalloutPackageAnimationSelection.FirstPerson && selected.Context != selected.First)
                throw new InvalidDataException("Preparation aliased an independent skipped Common or first pointer getter.");
        }
        if (call.Controller is { } controllerRead)
        {
            Common(controllerRead.Context.Process);
            if (controllerRead.Context.Context != controllerRead.Controller || controllerRead.ContactFlags is not null || controllerRead.Contacts is not null ||
                (controllerRead.Controller is null || call.Selected?.Context is null) &&
                    (controllerRead.Factory is not null || controllerRead.RuntimeProcess is not null || controllerRead.Epoch is not null) ||
                at > FalloutPlayerAnimationPreparationStep.Controller && (controllerRead.Controller is null || controllerRead.Factory is null ||
                    controllerRead.Factory == Guid.Empty || controllerRead.RuntimeProcess is null || controllerRead.RuntimeProcess == Guid.Empty ||
                    controllerRead.Epoch != frame.Common.Epoch))
                throw new InvalidDataException("Preparation replaced the mandatory real controller pointer/borrow.");
        }
        Slot(call.SelectedSlot, call.Selected?.Context);
        if (call.SelectedSlot is { } selectedSlot &&
            (call.Selected!.Selection == FalloutPackageAnimationSelection.Process && selectedSlot.Construction.Role != FalloutLoadedAnimationRole.Common ||
                call.Selected.Selection == FalloutPackageAnimationSelection.FirstPerson && selectedSlot.Construction.Role != FalloutLoadedAnimationRole.FirstPerson))
            throw new InvalidDataException("Preparation aliased its independent Common/First context allocation roles.");
        Group(call.SelectedGroup, call.SelectedSlot, FalloutPlayerAnimationPreparationStep.SelectedGroup);
        if (call.SelectedSlot is { Selected: not null } && at > FalloutPlayerAnimationPreparationStep.SelectedSlot ||
            call.FirstSlot is { Selected: not null } && at > FalloutPlayerAnimationPreparationStep.FirstSlot)
            throw new InvalidDataException("Preparation skipped the required current sequence transition body.");
        if (call.First is { } first && (first.Source != source.Tail.Movement.Mover.Controller.Fields.Identity || first.Stack != stack ||
            first.Process != frame.Common.Process || first.Factory == Guid.Empty || first.Constructor != frame.PlayerConstructor ||
            first.Actor != frame.Common.Actor || first.Epoch != frame.Common.Epoch || first.Sequence < 0 ||
            at > FalloutPlayerAnimationPreparationStep.FirstAnimation && first.Context is null))
            throw new InvalidDataException("Preparation changed its independent mandatory direct First pointer.");
        Slot(call.FirstSlot, call.First?.Context);
        if (call.FirstSlot is { } directFirstSlot && directFirstSlot.Construction.Role != FalloutLoadedAnimationRole.FirstPerson)
            throw new InvalidDataException("Preparation aliased the mandatory direct First context.");
        Group(call.FirstGroup, call.FirstSlot, FalloutPlayerAnimationPreparationStep.FirstGroup);

        object? AtChild(FalloutPlayerAnimationPreparationStep step) => step switch
        {
            FalloutPlayerAnimationPreparationStep.Life => call.Life,
            FalloutPlayerAnimationPreparationStep.Activity => call.Activity,
            FalloutPlayerAnimationPreparationStep.View => call.View,
            FalloutPlayerAnimationPreparationStep.Controller => call.Controller,
            _ => null,
        };
        void Child(FalloutPlayerAnimationPreparationStep step, object? value)
        {
            if (at > step && value is null || at < step && value is not null)
                throw new InvalidDataException("Preparation omitted a passed child or fabricated an unvisited suffix.");
        }
        void Common(FalloutProcessSceneQuery current) => RequireCommon(current, frame);
        void View(FalloutPlayerViewRead read)
        {
            if (read.Source != source.Tail.Scene.Identity || read.Stack != stack || read.Process != frame.PlayerProcess ||
                read.Constructor != frame.PlayerConstructor || read.Sequence < 0 || read.Frame != frame || read.Load is not null)
                throw new InvalidDataException("Preparation borrowed another actual current view field.");
        }
        void Slot(FalloutAnimationPreparationSlotRead? slot, Guid? context)
        {
            if (slot is null) return;
            if (slot.Source != source.Identity || slot.Frame != frame || slot.Context != context || slot.Factory == Guid.Empty ||
                slot.Sequence < 0 || slot.Slot != FalloutPlayerPostAnimationSource.PreparationGroupSlot)
                throw new InvalidDataException("Preparation substituted its actual current sequence slot/context.");
            Construction(slot.Construction, slot.Context, slot.Factory);
        }
        void Group(FalloutAnimationPreparationGroupRead? group, FalloutAnimationPreparationSlotRead? slot,
            FalloutPlayerAnimationPreparationStep groupStep)
        {
            if (group is null) return;
            if (slot is null || group.Source != source.Identity || group.Frame != frame || group.Context != slot.Context ||
                group.Factory != slot.Factory || group.Sequence < slot.Sequence || group.Slot != slot.Slot || group.Construction != slot.Construction)
                throw new InvalidDataException("Preparation changed its actual current group owner/read order.");
            Construction(group.Construction, group.Context, group.Factory);
            if (at > groupStep && (byte)group.Group is FalloutPlayerPostAnimationSource.FirstTimedPreparationGroup or
                FalloutPlayerPostAnimationSource.SecondTimedPreparationGroup)
                throw new InvalidDataException("Preparation passed its required timed-group stop child.");
        }
        void Construction(FalloutSourceAnimationContextConstruction construction, Guid context, Guid factory)
        {
            if (construction is null || !construction.Returned || construction.Source != source.Tail.Identity ||
                construction.Identity != context || construction.Factory != factory || !Enum.IsDefined(construction.Role) ||
                construction.GroupBuckets != FalloutPlayerReturnTailSource.AnimationGroupBuckets ||
                construction.Invocation.ReferenceProcess != frame.PlayerProcess ||
                construction.Invocation.ReferenceConstructor != frame.PlayerConstructor || construction.Invocation.CommonProcess != frame.Common.Process ||
                construction.Invocation.Actor != frame.Common.Actor || construction.Invocation.Epoch != frame.Common.Epoch ||
                construction.Invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad)
                throw new InvalidDataException("Preparation replaced its actual independently constructed AnimData object.");
        }
    }
}
