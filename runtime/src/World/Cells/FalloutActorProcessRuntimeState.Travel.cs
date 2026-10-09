using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    // Only the original travel calculation supplies this Float32. Neither
    // rest hours, a CELL change nor a source MoveTo invocation supplies it.
    internal Guid EnterTravel(FalloutActorProcessFact<float> calculatedWorldHours, string owner)
    {
        RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (_travel is { Phase: not FalloutPlayerTravelPhase.Complete })
            throw new InvalidOperationException("An earlier actual Player travel invocation still owns its counter.");
        var invocation = Guid.NewGuid(); var sequence = Next();
        _travel = new(invocation, owner, 0, _travelCounter, 0, sequence, sequence,
            FalloutPlayerTravelPhase.Constructed, false, null);
        try
        {
            var calculated = calculatedWorldHours.Require();
            // Both selected writers truncate the stored Float32 into a signed
            // Int32. Invalid source conversion is refused, never clamped.
            if (!float.IsFinite(calculated) || calculated < int.MinValue || (double)calculated >= 2147483648d)
                throw new InvalidDataException("Original Player travel calculation has no signed stored-hour conversion.");
            var count = checked((int)MathF.Truncate(calculated));
            _travelCounter = count;
            _travel = _travel with { InitialHours = count, RemainingHours = count,
                LastChanged = Next(), Phase = count > 0 ? FalloutPlayerTravelPhase.WorldHours : FalloutPlayerTravelPhase.Destination };
            return invocation;
        }
        catch (Exception error)
        {
            _travel = _travel with { Phase = FalloutPlayerTravelPhase.Failed, LastChanged = Next(), Failure = Message(error) };
            throw;
        }
    }

    internal void EnterTravelWorldHour(Guid invocation, string owner)
    {
        RequireNotBusy(); RequireTravel(invocation, owner, FalloutPlayerTravelPhase.WorldHours);
        if (_travelCounter <= 0 || _travel!.ConsumerEntered)
            throw new InvalidOperationException("Player travel cannot enter another source hour before its real consumers return.");
        _travel = _travel with { ConsumerEntered = true, LastChanged = Next() };
        // There is no admitted complete world-hour owner yet. Preserve the
        // exact entered prefix and the original counter; a calendar commit,
        // scheduled callback or CELL publication cannot decrement it.
        const string boundary = "original-travel-directed-cell-projectile-process-magic-world-hour-consumers-unowned";
        _travel = _travel with { Phase = FalloutPlayerTravelPhase.Failed, Failure = boundary, LastChanged = Next() };
        throw new NotSupportedException(boundary);
    }

    internal void EnterTravelDestination(Guid invocation, string owner)
    {
        RequireNotBusy(); RequireTravel(invocation, owner, FalloutPlayerTravelPhase.Destination);
        // The source reset to zero follows the real destination movement and
        // travel-finish consumers. A caller cannot clear an entered operation
        // with cancellation or a mere replacement player CELL.
        const string boundary = "original-travel-destination-movement-and-finish-consumers-unowned";
        _travel = _travel! with { Phase = FalloutPlayerTravelPhase.Failed, Failure = boundary, LastChanged = Next() };
        throw new NotSupportedException(boundary);
    }
    private void RequireTravel(Guid invocation, string owner, FalloutPlayerTravelPhase phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (_travel is null || _travel.Invocation != invocation || _travel.Owner != owner || _travel.Phase != phase || _travel.Failure is not null)
            throw new InvalidDataException("Player travel continuation lost its real invocation or consumed prefix.");
    }
}
