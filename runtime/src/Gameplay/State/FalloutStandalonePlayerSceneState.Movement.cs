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
    byte? ActorFlag = null);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerMoverFields _moverFields = FalloutPlayerMoverFields.Constructed;
    private byte _actorMovementFlag;
    private Func<FalloutProcessMovementFlagUpdate>? _readAndClearMovementFlags;
    private FalloutPlayerMovementCall? _movementCall;

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
            throw new NotSupportedException("source-Player-movement-current-actor-field-child-unbound");
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
            call.MoverReads.Any(flags => flags != 0) || call.Returned || string.IsNullOrWhiteSpace(call.Failure) ||
            call.Failure != saved.Failure)
            throw new InvalidDataException("Player movement prefix lost its actual invocation, mover factory or retained failure.");
        if (call.ProcessFlags is not { } update)
        {
            if (call.Step != "process-flags" || call.MoverReads.Count != 0 || call.InitialLowWord is not null || call.ActorFlag is not null)
                throw new InvalidDataException("Unreturned movement flag query admitted a later mover child.");
            return;
        }
        var high = update.Before.Level == FalloutDetectionProcessLevel.High;
        if (!SameProcess(update.Before, idle.Before) || update.Before.Changed < idle.Changed || !update.Returned ||
            update.Flags != 0 || (high ? update.Stores != 1 || update.AfterFlags != 0 || update.Changed <= update.Before.Changed
                : update.Stores != 0 || update.AfterFlags is not null || update.Changed != update.Before.Changed) ||
            call.Step != "movement-current-actor-field-child" || call.MoverReads.Count != 2 || call.InitialLowWord != 0 || call.ActorFlag != 0 ||
            call.Failure != "source-Player-movement-current-actor-field-child-unbound")
            throw new InvalidDataException("Movement prefix changed its independent flag clear or repeated source mover reads.");
    }
}
