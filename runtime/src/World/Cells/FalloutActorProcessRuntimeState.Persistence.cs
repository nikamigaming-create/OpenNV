using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    internal FalloutActorProcessRuntimeSnapshot Capture()
    {
        RequireNotBusy(); RequireMainScriptClosureBoundary(retiring: false); RequireMainUtilityBoundary(retiring: false);
        return new(Schema, _source.Contract, _stack, _player, _process, _sequence, _forced, _travelCounter,
            _main.ToArray(), _travel, _actors.Values.ToArray(), _cold, CaptureMainFrame(), CaptureSourceFistp(), CaptureStandaloneMain());
    }
    private void Restore(FalloutActorProcessRuntimeSnapshot saved)
    {
        Validate(saved);
        if (saved.Contract != _source.Contract || saved.Stack != _stack || saved.Player != _player || saved.CapturedProcess == _process)
            throw new InvalidDataException("Cold process runtime inputs differ from their source/new process lifetime.");
        foreach (var actor in saved.Actors)
        {
            if (Identity(actor.Source.Reference) != actor.Source) throw new InvalidDataException("Cold process runtime input lost its exact source winner/master.");
            _actors.Add(actor.Source.Reference, actor);
        }
        _forced = saved.MainForcedProcessing; _travelCounter = saved.PlayerTravelCounter; _sequence = saved.Sequence;
        _main.AddRange(saved.MainOperations); _travel = saved.Travel;
        RestoreMainFrame(saved.MainFrame); RestoreSourceFistp(saved.Fistp);
        _cold = new(saved.CapturedProcess, _process, Next());
        // No source writer, hour, completion or constructor is replayed. A
        // failed/in-flight original invocation remains failed/in-flight.
    }
    internal void RequireActors(IEnumerable<FalloutFormKey> actors)
    {
        RequireNotBusy();
        var expected = actors.ToHashSet(FalloutFormKeyComparer.Instance);
        if (!_actors.Keys.ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(expected))
            throw new InvalidDataException("Current process runtime omitted an actual constructed Actor/Player.");
    }
    internal static void Validate(FalloutActorProcessRuntimeSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Contract is not { Length: 64 } || !saved.Contract.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(saved.Stack) || saved.CapturedProcess == Guid.Empty || saved.Sequence < 0 ||
            saved.MainOperations is null || saved.MainOperations.Any(value => value is null) || saved.Actors is null || saved.Actors.Any(actor => actor is null || actor.Source is null) ||
            saved.Actors.Select(actor => actor.Source.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != saved.Actors.Count)
            throw new InvalidDataException("Process runtime snapshot has no complete source/current lifetime.");
        ValidateRuntimeMainFrame(saved); ValidateRuntimeSourceFistp(saved);
        if (saved.StandaloneMain is { } standalone) ValidateStandaloneMain(standalone, saved);
        var active = 0; long previous = 0; var identities = new HashSet<Guid>();
        foreach (var item in saved.MainOperations)
        {
            if (item is null || item.Invocation == Guid.Empty || !identities.Add(item.Invocation) || !Enum.IsDefined(item.Operation) ||
                !Enum.IsDefined(item.Phase) || string.IsNullOrWhiteSpace(item.Owner) || item.Entered <= previous ||
                item.LastChanged < item.Entered || item.LastChanged > saved.Sequence || item.Before ||
                (item.Phase == FalloutMainProcessPhase.Failed) != (item.Failure is not null) || item.Failure is not null && string.IsNullOrWhiteSpace(item.Failure))
                throw new InvalidDataException("Cold Main input lost its ordered real invocation/completion/failure.");
            if (item.Phase != FalloutMainProcessPhase.Complete) active++;
            previous = item.LastChanged;
        }
        if (active > 1 || saved.MainForcedProcessing != (active == 1))
            throw new InvalidDataException("Cold Main forced bit does not match the actual source writer prefix.");
        if (saved.Travel is { } travel)
        {
            if (travel.Invocation == Guid.Empty || string.IsNullOrWhiteSpace(travel.Owner) || !Enum.IsDefined(travel.Phase) ||
                travel.Entered < 1 || travel.LastChanged < travel.Entered || travel.LastChanged > saved.Sequence ||
                travel.CompletedWorldHours != 0 || travel.RemainingHours != saved.PlayerTravelCounter ||
                (travel.Phase == FalloutPlayerTravelPhase.Failed) != (travel.Failure is not null) ||
                travel.Failure is not null && string.IsNullOrWhiteSpace(travel.Failure) ||
                travel.Phase == FalloutPlayerTravelPhase.Complete || travel.Phase == FalloutPlayerTravelPhase.WorldHours && travel.RemainingHours <= 0 ||
                travel.Phase == FalloutPlayerTravelPhase.Destination && travel.RemainingHours > 0 ||
                travel.ConsumerEntered && (travel.InitialHours <= 0 || travel.Phase != FalloutPlayerTravelPhase.Failed))
                throw new InvalidDataException("Cold Player travel invented a complete/decremented unowned world-hour or lost its real counter.");
        }
        else if (saved.PlayerTravelCounter != 0)
            throw new InvalidDataException("Cold Player counter lost its constructor or actual travel writer.");
        foreach (var actor in saved.Actors)
        {
            actor.Source.Validate();
            if (actor.Source.EnginePlayer != (actor.Source.Reference == saved.Player) || actor.Changed < 1 || actor.Changed > saved.Sequence ||
                string.IsNullOrWhiteSpace(actor.Owner) || actor.Failure is not null && string.IsNullOrWhiteSpace(actor.Failure) ||
                actor.Value is not (null or 0) || (actor.Value is null) != (actor.Failure is not null))
                throw new InvalidDataException("Cold Actor life code lacks an admitted source writer or retained genuine boundary.");
        }
        if (!saved.Actors.Any(actor => actor.Source.Reference == saved.Player && !actor.Retired))
            throw new InvalidDataException("Cold runtime inputs omitted the actual constructed Player.");
        if (saved.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != saved.CapturedProcess ||
            cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > saved.Sequence))
            throw new InvalidDataException("Cold runtime input lost the genuine new-process epoch handoff.");
    }
}
