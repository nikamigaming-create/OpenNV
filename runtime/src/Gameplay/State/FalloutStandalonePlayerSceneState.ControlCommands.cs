namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal void ObserveSourceControlCommand(FalloutPlayerControlCommandObservation observation)
    {
        RequireIdle(allowFailure: true); ArgumentNullException.ThrowIfNull(observation); observation.Validate();
        if (_observedControlIds.Contains(observation.Observation))
            throw new InvalidOperationException("Player scene replayed an actual control command observation.");
        var retained = observation with { Arguments = observation.Arguments.ToArray() };
        _movementControl?.Fields.ObserveCommand(retained);
        _observedControlIds.Add(observation.Observation);
        _controlObservations.Add(new(retained, _process, _constructor, Next()));
        if (_movementCall is { Control: not null } call && _movementControl is not null)
            _movementCall = call with { Control = _movementControl.Capture() };
        // Loading may observe the real bootstrap before input exists. It never
        // captures the gameplay thread, derives a word from semantic booleans,
        // or executes earlier source stores/effects during later publication.
    }
    private void RetainControlObservations(FalloutPlayerMovementControlState controls)
    {
        var previous = controls.Fields.Capture().Observations!;
        if (previous.Count > _controlObservations.Count)
            throw new InvalidDataException("Cold Player controls lost actual command observation history.");
        for (var index = 0; index < previous.Count; ++index)
            if (!previous[index].SameCommand(_controlObservations[index].Command))
                throw new InvalidDataException("Cold Player controls changed a retained command's source or original arguments.");
        foreach (var receipt in _controlObservations.Skip(previous.Count)) controls.Fields.ObserveCommand(receipt.Command);
    }
    private static void ValidateControlObservations(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.ControlObservations is not { } history ||
            history.Select(value => value.Command.Observation).Distinct().Count() != history.Count)
            throw new InvalidDataException("Player scene omitted or replayed actual command observations.");
        long last = 0;
        foreach (var receipt in history)
        {
            receipt.Command.Validate();
            if (receipt.Process == Guid.Empty || receipt.Constructor == Guid.Empty || receipt.Retained <= last ||
                receipt.Retained > saved.Sequence || receipt.Process == saved.CapturedProcess && receipt.Constructor != saved.Constructor)
                throw new InvalidDataException("Player command observation lost its actual process/publication sequence.");
            last = receipt.Retained;
        }
        if (saved.MovementCall?.Control?.Fields is not { } fields) return;
        if (fields.Observations!.Count != history.Count ||
            fields.Observations.Where((value, index) => !value.SameCommand(history[index].Command)).Any())
            throw new InvalidDataException("Player control fields promoted stale constructor state after actual observed command writers.");
    }
}
