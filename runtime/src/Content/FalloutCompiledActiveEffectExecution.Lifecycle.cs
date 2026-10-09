using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompiledActiveEffectLifetime(bool StartAttempted, bool Started,
    long UpdateDispatches, bool FinishAttempted, bool Finished, string? Failure);

internal sealed partial class FalloutCompiledActiveEffectExecution
{
    internal const ushort StartEvent = 17, FinishEvent = 18, UpdateEvent = 19;
    private FalloutCompiledActiveEffectLifetime _lifetime = new(false, false, 0, false, false, null);
    internal bool Finished => _lifetime.Finished;

    private void RestoreLifecycle(FalloutCompiledActiveEffectSnapshot? state)
    {
        if (state is null) return;
        _lifetime = state.Lifetime ?? throw new InvalidDataException("Current compiled effect lost its event-list lifecycle.");
        RequireEventCounts();
    }

    internal static void RequireEvent(FalloutCompiledScriptProgram program, FalloutCompiledEvent block)
    {
        if (!program.Events.Any(row => ReferenceEquals(row, block)))
            throw new InvalidDataException("Active-effect event belongs to another SCDA owner.");
        if (block.Event is not (StartEvent or UpdateEvent or FinishEvent))
            throw new NotSupportedException($"Compiled active effect {FalloutCompiledScriptEvents.Name(block.Event)} requires its actual producer.");
        if (block.Parameters.IsEmpty || block.Parameters.Length == 2 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(block.Parameters.Span) == 0) return;
        throw new NotSupportedException("Compiled script-effect event parameters have no admitted producer.");
    }

    internal void ExecuteStart(Func<FalloutCompiledActiveEffectInvocation, FalloutCompiledActiveEffectReceipt> execute)
    {
        RequireIdle();
        if (_lifetime.StartAttempted || _events.Any(row => row.State.Attempted))
            throw new NotSupportedException("An attempted active-effect Start cannot replay its compiled prefix.");
        _lifetime = _lifetime with { StartAttempted = true };
        Dispatch(StartEvent, 0, null, execute);
        _lifetime = _lifetime with { Started = true };
    }

    internal void ExecuteUpdate(FalloutScriptedEffectUpdate update,
        Func<FalloutCompiledActiveEffectInvocation, FalloutCompiledActiveEffectReceipt> execute)
    {
        update.Require(this); RequireIdle();
        if (!_lifetime.Started || _lifetime.FinishAttempted)
            throw new InvalidOperationException("ScriptEffectUpdate has no running original event list.");
        _lifetime = _lifetime with { UpdateDispatches = checked(_lifetime.UpdateDispatches + 1) };
        Dispatch(UpdateEvent, update.Seconds, update, execute);
    }

    internal void ExecuteFinish(FalloutScriptedEffectFinish finish,
        Func<FalloutCompiledActiveEffectInvocation, FalloutCompiledActiveEffectReceipt> execute)
    {
        finish.Require(this); RequireIdle();
        if (!_lifetime.Started || _lifetime.FinishAttempted)
            throw new InvalidOperationException("ScriptEffectFinish lacks its one unretired original event list.");
        _lifetime = _lifetime with { FinishAttempted = true };
        Dispatch(FinishEvent, 0, finish, execute);
        // No implicit operand can use this event list after successful Finish.
        // Its exact cells/cursors remain available as immutable cold evidence.
        _lifetime = _lifetime with { Finished = true };
    }

    private void Dispatch(ushort kind, float seconds, object? producer,
        Func<FalloutCompiledActiveEffectInvocation, FalloutCompiledActiveEffectReceipt> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        try
        {
            for (var ordinal = 0; ordinal < _events.Length; ++ordinal)
            {
                var row = _events[ordinal];
                if (row.Source.Event != kind) continue;
                if (row.State.Attempted)
                {
                    if (kind != UpdateEvent || !row.Cursor.State.Completed || row.State.Failure is not null ||
                        row.State.Receipt?.Disposition != "completed")
                        throw new NotSupportedException("Active-effect event cannot replay its unretired prefix.");
                    row.Cursor = new(row.Flow.InitialCursor);
                }
                row.State = row.State with
                {
                    Attempted = true,
                    Cycle = checked(row.State.Cycle + 1),
                    SecondsBits = BitConverter.SingleToUInt32Bits(seconds),
                    Cursor = Copy(row.Cursor.State),
                    Receipt = null,
                    LastReachedOffset = null,
                    Failure = null
                };
                _entered = new(this, ordinal, row.Cursor, seconds, producer);
                try
                {
                    var receipt = execute(_entered);
                    if (!ReferenceEquals(receipt.Invocation, _entered))
                        throw new InvalidDataException("Active-effect execution returned another invocation's receipt.");
                    receipt.Require();
                    if (receipt.Retired.Disposition != "completed" || !row.Cursor.State.Completed)
                        throw new NotSupportedException("Script-effect event did not retire its complete original SCDA suffix.");
                    row.State = row.State with
                    {
                        Cursor = Copy(row.Cursor.State),
                        Receipt = receipt.Retired,
                        LastReachedOffset = receipt.LastReachedOffset
                    };
                }
                catch (FalloutCompiledActiveEffectFailure failure)
                {
                    failure.Receipt.Require();
                    if (!ReferenceEquals(failure.Receipt.Invocation, _entered))
                        throw new InvalidDataException("Failed effect receipt belongs to another invocation.", failure);
                    row.State = row.State with
                    {
                        Cursor = Copy(row.Cursor.State),
                        Receipt = failure.Receipt.Retired,
                        LastReachedOffset = failure.Receipt.LastReachedOffset,
                        Failure = failure.Message
                    };
                    throw;
                }
                catch (Exception failure)
                {
                    row.State = row.State with { Cursor = Copy(row.Cursor.State), Failure = failure.Message };
                    throw;
                }
                finally { _entered = null; }
            }
        }
        catch (Exception failure)
        {
            _lifetime = _lifetime with { Failure = failure.Message };
            throw;
        }
    }

    private void RequireIdle()
    {
        if (_entered is not null) throw new InvalidOperationException("The actual active-effect event is reentrant.");
        if (_lifetime.Failure is { } retained) throw new NotSupportedException(retained);
        if (_lifetime.Finished) throw new InvalidOperationException("The original event-list owner has retired.");
    }

    internal void RequireLifecycle(bool started, string? error)
    {
        RequireEventCounts();
        if (started != _lifetime.Started || error is null && _lifetime.Failure is not null ||
            !started && error is null && _lifetime.StartAttempted)
            throw new InvalidDataException("Active-effect state lost its genuine event-list lifecycle/failure.");
    }

    private void RequireEventCounts()
    {
        ValidateLifetime(_lifetime);
        foreach (var row in _events)
        {
            var expected = row.Source.Event switch
            {
                StartEvent => _lifetime.StartAttempted ? 1L : 0L,
                FinishEvent => _lifetime.FinishAttempted ? 1L : 0L,
                UpdateEvent => _lifetime.UpdateDispatches,
                _ => throw new InvalidDataException("Effect owns an unrelated event.")
            };
            if (row.State.Cycle > expected || _lifetime.Failure is null && row.State.Cycle != expected ||
                row.State.Cycle != 0 && row.State.Failure is null && row.State.Receipt?.Disposition != "completed")
                throw new InvalidDataException("Effect event-list counts differ from its genuine source dispatch prefix.");
        }
    }

    internal static void ValidateLifetime(FalloutCompiledActiveEffectLifetime? state)
    {
        if (state is null || state.UpdateDispatches < 0 || state.Started && !state.StartAttempted ||
            !state.Started && (state.UpdateDispatches != 0 || state.FinishAttempted || state.Finished) ||
            state.Finished && !state.FinishAttempted || state.FinishAttempted && !state.Finished && state.Failure is null ||
            state.StartAttempted && !state.Started && state.Failure is null || state.Failure is { Length: 0 })
            throw new InvalidDataException("Current compiled effect has an invalid Start/Update/Finish prefix.");
    }
}
