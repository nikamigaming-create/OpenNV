using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// Actor's source PlayerMover is an independent C# gameplay owner. These cells
// are neither physical-controller flags nor a presentation locomotion state.
internal sealed record FalloutPlayerMoverFields(uint VectorX, uint VectorY, uint VectorZ,
    uint Flags, uint SecondaryFlags, uint TimerBits)
{
    internal static FalloutPlayerMoverFields Constructed => new(0, 0, 0, 0, 0, 0xbf800000);
}
internal sealed record FalloutPlayerMovementCall(Guid Main, long MainOrdinal, long Entered, long Changed,
    FalloutProcessMovementFlagUpdate? ProcessFlags,
    IReadOnlyList<uint> MoverReads, uint? InitialLowWord, string Step, bool Returned, string? Failure,
    byte? ActorFlag = null, FalloutPlayerLivingPhysicsSnapshot? Physics = null,
    FalloutPlayerMovementControlSnapshot? Control = null);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerMoverFields _moverFields = FalloutPlayerMoverFields.Constructed;
    private byte _actorMovementFlag;
    private Func<FalloutProcessMovementFlagUpdate>? _readAndClearMovementFlags;
    private IFalloutPlayerLivingPhysicsOwner? _movementPhysics;
    private FalloutPlayerMovementCall? _movementCall;

    internal void BindMovementPhysics(IFalloutPlayerLivingPhysicsOwner owner)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(owner);
        if (_movementPhysics is not null)
            throw new InvalidOperationException("Player movement physics already has its actual current Actor owner.");
        owner.RequireCurrent();
        RequirePhysicsIdentity(owner.Capture(), owner);
        _movementPhysics = owner; _thread ??= Environment.CurrentManagedThreadId;
    }

    private void RequirePhysicsIdentity(FalloutPlayerLivingPhysicsSnapshot snapshot, IFalloutPlayerLivingPhysicsOwner owner)
    {
        snapshot.Validate();
        if (owner.Process != _process || owner.Source != Source.Identity || owner.Stack != _stack ||
            owner.Factory == Guid.Empty || snapshot.CapturedProcess != owner.Process || snapshot.Source != owner.Source ||
            snapshot.Stack != owner.Stack || snapshot.Factory != owner.Factory)
            throw new InvalidOperationException("Player movement physics changed its actual source/process/factory owner.");
    }

    internal void BindMovementFlags(Func<FalloutProcessMovementFlagUpdate> readAndClear)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(readAndClear);
        if (_readAndClearMovementFlags is not null)
            throw new InvalidOperationException("Player movement flags already have their actual current process owner.");
        _readAndClearMovementFlags = readAndClear;
    }

    private void UpdateMovementPrefix(Guid main, long ordinal)
    {
        var entered = Next(); var fault = _reentry;
        _movementCall = new(main, ordinal, entered, entered, null, [], null, "process-flags", false, null);
        try
        {
            var idle = _idleAnimationCall?.Result ??
                throw new InvalidOperationException("Player movement lost its returned actual idle update.");
            var update = (_readAndClearMovementFlags ??
                throw new NotSupportedException("source-Player-current-process-movement-flag-getter-and-clear-unbound"))();
            Check();
            if (!SameProcess(update.Before, idle.Before) || update.Before.Changed < idle.Changed || !update.Returned ||
                (update.Before.Level == FalloutDetectionProcessLevel.High
                    ? update.Stores != 1 || update.AfterFlags != 0 || update.Changed <= update.Before.Changed
                    : update.Stores != 0 || update.Flags != 0 || update.AfterFlags is not null || update.Changed != update.Before.Changed))
                throw new InvalidDataException("Player movement flag clear changed its current process or source store order.");
            _movementCall = _movementCall with { ProcessFlags = update, Changed = Next() };
            if ((update.Flags & 4) != 0)
            {
                Step("process-bit-four");
                throw new NotSupportedException("source-Player-process-movement-bit-four-action-predicate-unbound");
            }
            if ((update.Flags & 8) != 0)
            {
                Step("process-bit-eight");
                throw new NotSupportedException("source-Player-process-movement-bit-eight-action-predicate-unbound");
            }
            Step("mover-first"); var initial = ReadMover();
            _movementCall = _movementCall with { InitialLowWord = initial & 0xffff, Changed = Next() };
            Step("mover-second"); var current = ReadMover();
            if ((current & 0x400) != 0)
            {
                Step("mover-turning");
                throw new NotSupportedException("source-Player-movement-turning-input-and-timer-owners-unbound");
            }
            Step("actor-movement-flag");
            _movementCall = _movementCall with { ActorFlag = _actorMovementFlag, Changed = Next() };
            if (_actorMovementFlag != 0)
                throw new NotSupportedException("source-Actor-movement-flag-current-producer-unbound");
            // The constructed Player state skips the independent locomotion
            // timer arms. No input/movement writer is inferred from Godot.
            Step("movement-current-actor-field-child");
            var physics = _movementPhysics ??
                throw new NotSupportedException("source-Player-movement-current-actor-field-child-unbound");
            try { physics.RequireCurrent(); Check(); physics.Update(main, ordinal); Check(); }
            finally
            {
                var snapshot = physics.Capture();
                _movementCall = _movementCall with { Physics = snapshot, Changed = Next() };
                RequirePhysicsIdentity(snapshot, physics);
            }
            if (_movementCall.Physics?.Frame is not { Returned: true, Failure: null } frame ||
                frame.Main != main || frame.MainOrdinal != ordinal)
                throw new InvalidDataException("Actor physical child did not retain its actual returned Main invocation.");
            RequireReturnedPhysics(_movementCall.Physics);
            Step("movement-control-child");
            if (_clock?.ArgumentBits is not { } argument || _inputManager is not { } input)
                throw new InvalidOperationException("Player controls lost their stored scene argument or actual current input owner.");
            var controls = _movementControl ??
                throw new InvalidOperationException("Player controls have no actual input-thread publication.");
            try
            {
                controls.ExecutePrefix(main, ordinal, argument,
                (ushort)(current & FalloutPlayerMovementControlSource.MoverMask), input,
                    invocation: _prelude?.Invocation ?? throw new InvalidOperationException("Movement lost its entered Prelude invocation.")); Check();
            }
            finally
            {
                _movementCall = _movementCall with { Control = controls.Capture(), Changed = Next() };
            }
            _movementCall = _movementCall with { Step = "returned", Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _movementCall = _movementCall with { Changed = Next(), Failure = error.Message }; throw;
        }
        uint ReadMover()
        {
            Check();
            if (_moverFields != FalloutPlayerMoverFields.Constructed)
                throw new NotSupportedException("source-PlayerMover-movement-field-producer-unbound");
            var flags = _moverFields.Flags;
            _movementCall = _movementCall with
            { MoverReads = _movementCall.MoverReads.Append(flags).ToArray(), Changed = Next() };
            return flags;
        }
        void Step(string step) => _movementCall = _movementCall with { Step = step, Changed = Next() };
        void Check()
        {
            RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
            if (_reentry != fault) throw new InvalidOperationException("Movement consumer swallowed a Player owner reentry refusal.");
        }
    }

    private static void ValidateMovementCall(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.MoverFields != FalloutPlayerMoverFields.Constructed || saved.ActorMovementFlag != 0)
            throw new InvalidDataException("Player mover lost its actual independent constructor cells.");
        if (saved.MovementCall is not { } call)
        {
            if (saved.IdleAnimationCall is { Returned: true })
                throw new InvalidDataException("Returned idle update omitted its actual following movement prefix.");
            return;
        }
        if (saved.IdleAnimationCall is not { Returned: true, Result: { } idle } parent ||
            parent.Main != call.Main || parent.MainOrdinal != call.MainOrdinal || call.Entered <= parent.Changed ||
            call.Changed <= call.Entered || call.Changed > saved.Sequence || call.MoverReads is null || call.MoverReads.Count > 2 ||
            call.MoverReads.Any(flags => flags != 0) || call.Returned != (call.Failure is null) ||
            call.Failure is { } movementFailure && (string.IsNullOrWhiteSpace(movementFailure) || movementFailure != saved.Failure) ||
            call.Returned != (call.Step == "returned"))
            throw new InvalidDataException("Player movement prefix lost its actual invocation, mover factory or retained failure.");
        if (call.ProcessFlags is not { } update)
        {
            if (call.Step != "process-flags" || call.MoverReads.Count != 0 || call.InitialLowWord is not null || call.ActorFlag is not null ||
                call.Physics is not null || call.Control is not null)
                throw new InvalidDataException("Unreturned movement flag query admitted a later mover child.");
            return;
        }
        var high = update.Before.Level == FalloutDetectionProcessLevel.High;
        if (!SameProcess(update.Before, idle.Before) || update.Before.Changed < idle.Changed || !update.Returned ||
            update.Flags != 0 || (high ? update.Stores != 1 || update.AfterFlags != 0 || update.Changed <= update.Before.Changed
                : update.Stores != 0 || update.AfterFlags is not null || update.Changed != update.Before.Changed) ||
            call.Step is not ("movement-current-actor-field-child" or "movement-control-child" or "returned") ||
            call.MoverReads.Count != 2 || call.InitialLowWord != 0 || call.ActorFlag != 0)
            throw new InvalidDataException("Movement prefix changed its independent flag clear or repeated source mover reads.");
        if (call.Physics is not { } physics)
        {
            if (call.Step != "movement-current-actor-field-child" || call.Control is not null ||
                call.Failure != "source-Player-movement-current-actor-field-child-unbound")
                throw new InvalidDataException("Unbound Actor physics admitted its following control child or lost its actual refusal.");
            return;
        }
        ValidateMovementPhysics(saved, call, physics);
        if (call.Step == "movement-current-actor-field-child")
        {
            if (call.Control is not null || physics.Frame is { Returned: false, Failure: { } failure } && failure != call.Failure)
                throw new InvalidDataException("Failed Actor physics admitted control input or lost its reached physical failure.");
            return;
        }
        RequireReturnedPhysics(physics);
        if (call.Control is not { } control || control.Call is not { } child || saved.InputManagerQuery is not { } input ||
            saved.Clock?.ArgumentBits is not { } argument)
            throw new InvalidDataException("Player movement omitted its actual following control invocation.");
        FalloutPlayerMovementControlState.Validate(control);
        if (control.Source != FalloutPlayerMovementControlSource.Reviewed(saved.Source.Main) || control.Stack != saved.Stack ||
            control.CapturedProcess != input.Process || control.Retired || child.Main != call.Main || child.MainOrdinal != call.MainOrdinal ||
            saved.Prelude?.Invocation != child.Invocation || child.Invocation == Guid.Empty ||
            child.ArgumentBits != argument || child.MoverWord != (ushort)(saved.MoverFields.Flags & FalloutPlayerMovementControlSource.MoverMask) ||
            control.Failure != call.Failure || child.Returned != call.Returned || child.InputFactory is { } factory && (factory != input.Factory ||
                child.InputWindow != input.Window || child.InputProvider != input.Provider) ||
            child.MouseReads.Any(read => read.Sample != input.Sample || read.Revision != input.Revision))
            throw new InvalidDataException("Player controls changed their actual input/source owner, scene argument or masked mover word.");
        if (child.InhibitionReads is { Count: > 0 } reads)
        {
            var source = FalloutPlayerFollowingContinuationSource.Reviewed(control.Source);
            var owner = saved.Inhibition ?? throw new InvalidDataException("Movement omitted its shared inhibition field owner.");
            foreach (var read in reads)
            {
                FalloutPlayerInhibitionState.RequireHistoricalPublication(owner, source, saved.Stack, read.Process, read.Factory);
                if (read.Revision > owner.Sequence || read.Caller.Invocation != child.Invocation ||
                    read.Caller.Main != call.Main || read.Caller.Ordinal != call.MainOrdinal)
                    throw new InvalidDataException("Movement inhibition lost its actual shared field revision or caller.");
            }
        }
    }

    private static void ValidateMovementPhysics(FalloutStandalonePlayerSceneSnapshot saved,
        FalloutPlayerMovementCall call, FalloutPlayerLivingPhysicsSnapshot physics)
    {
        physics.Validate();
        if (saved.InputManagerQuery is not { } input || physics.Source != saved.Source.Identity || physics.Stack != saved.Stack ||
            physics.CapturedProcess != input.Process || physics.Frame is { } frame &&
                (frame.Main != call.Main || frame.MainOrdinal != call.MainOrdinal))
            throw new InvalidDataException("Actor physics lost its actual Player source, current process or Main child.");
        if (physics.Admission is not { } admission) return;
        if (admission.Source.Main != saved.Source.Main || admission.Actor != call.ProcessFlags!.Before.Actor.Reference ||
            admission.Model is { } model && model.Process != physics.CapturedProcess ||
            admission.Activities.Any(query => !SameProcess(query.Process, call.ProcessFlags.Before)))
            throw new InvalidDataException("Actor physical admission borrowed another actor, model or process lease.");
    }

    private static void RequireReturnedPhysics(FalloutPlayerLivingPhysicsSnapshot physics)
    {
        if (!physics.CompleteCapture)
            throw new NotSupportedException("source-Player-living-physics-current-capture-unavailable:" +
                string.Join(";", physics.CaptureRefusals!.Select(refusal => refusal.Lane + ":" + refusal.Message)));
        if (physics.Frame is not { Returned: true, Step: "returned", Failure: null } || physics.Failure is not null ||
            physics.Admission is not { Returned: true, Register: not null } ||
            physics.Utility.Phase != (physics.Utility.Systems.Count == 0 ? "zero-system-update-completed" : "update-completed") ||
            !physics.Admission.Register.Value && (physics.Utility.Registered || physics.Utility.CollisionGroup is not null ||
                physics.Utility.Systems.Count != 0 || physics.Utility.PendingSystems.Count != 0))
            throw new InvalidDataException("Actor physics did not return its actual admission-selected utility update.");
    }
}
