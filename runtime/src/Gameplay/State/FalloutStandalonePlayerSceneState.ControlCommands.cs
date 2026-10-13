using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal FalloutPlayerControlFieldsState ControlFields => _controlFields;
    internal FalloutPlayerThreadContextState ThreadContext => _threadContext;
    internal uint ReadControlCommandMoverFlags(Guid observation)
    {
        if (!_writing) throw new InvalidOperationException("Control mover getter has no actual entered scene command.");
        _ = _controlFields.ReadForEffects(observation);
        return _moverFields.Flags;
    }

    internal void PublishControlCommandInput(FalloutDirectInputState input)
    {
        RequireIdle();
        if (_controlFields.HasActualInput) _controlFields.RequireActualInput(input);
        else _controlFields.BindActualInput(input);
        _thread ??= Environment.CurrentManagedThreadId;
    }

    internal void ApplySourceControlCommand(FalloutPlayerControlCommandObservation observation,
        IFalloutPlayerControlFlagConsumers consumers)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(observation); ArgumentNullException.ThrowIfNull(consumers);
        observation.Validate();
        if (_observedControlIds.Contains(observation.Observation))
            throw new InvalidOperationException("Player scene replayed an actual executed control command.");
        _ = _controlFields.ReadDisabledWord();
        var retained = observation with { Arguments = observation.Arguments.ToArray() };
        _observedControlIds.Add(observation.Observation);
        var receipt = new FalloutPlayerControlCommandReceipt(retained, _process, _constructor, Next(), true);
        _controlObservations.Add(receipt);
        _writing = true; var fault = _reentry;
        try
        {
            _controlFields.ApplyCommand(retained.Observation, retained.Producer,
                new(retained.Enable, retained.Arguments), consumers);
            if (fault != _reentry)
                throw new InvalidOperationException("Source control command swallowed a reentered Player scene call.");
        }
        catch (Exception error) { _failure ??= error.Message; Next(); throw; }
        finally
        {
            _writing = false;
            var fields = _controlFields.Capture();
            _controlObservations[^1] = receipt with { Writer = fields.Call };
            if (_movementCall is { Control: not null } call && _movementControl is not null)
                _movementCall = call with { Control = _movementControl.Capture() };
        }
    }

    internal void ObserveSourceControlCommand(FalloutPlayerControlCommandObservation observation)
    {
        RequireIdle(allowFailure: true); ArgumentNullException.ThrowIfNull(observation); observation.Validate();
        if (_observedControlIds.Contains(observation.Observation))
            throw new InvalidOperationException("Player scene replayed an actual control command observation.");
        var retained = observation with { Arguments = observation.Arguments.ToArray() };
        _controlFields.ObserveCommand(retained);
        _observedControlIds.Add(observation.Observation);
        _controlObservations.Add(new(retained, _process, _constructor, Next()));
        if (_movementCall is { Control: not null } call && _movementControl is not null)
            _movementCall = call with { Control = _movementControl.Capture() };
        // Loading may observe the real bootstrap before input exists. It never
        // captures the gameplay thread, derives a word from semantic booleans,
        // or executes earlier source stores/effects during later publication.
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
            if (receipt.WriterEntered)
            {
                if (receipt.Writer is not { } writer || writer.Observation != receipt.Command.Observation ||
                    writer.Owner != receipt.Command.Producer || writer.Enable != receipt.Command.Enable ||
                    !writer.Arguments.SequenceEqual(receipt.Command.Arguments))
                    throw new InvalidDataException("Actual Player command omitted or changed its entered source writer prefix.");
                FalloutPlayerControlFieldsState.ValidateEffects(writer, FalloutPlayerMovementControlSource.Reviewed(saved.Source.Main), saved.Stack, receipt.Process);
            }
            else if (receipt.Writer is not null)
                throw new InvalidDataException("Unbound observation fabricated an executed Player command writer.");
        }
        var fields = saved.ControlFields ?? throw new InvalidDataException("Player scene omitted its actual early field constructor.");
        FalloutPlayerControlFieldsState.Validate(fields, FalloutPlayerMovementControlSource.Reviewed(saved.Source.Main), saved.Stack);
        if (fields.Process != saved.CapturedProcess)
            throw new InvalidDataException("Player scene borrowed control fields from another process.");
        var unbound = history.Where(value => !value.WriterEntered).ToArray();
        if (fields.Observations!.Count != unbound.Length ||
            fields.Observations.Where((value, index) => !value.SameCommand(unbound[index].Command)).Any())
            throw new InvalidDataException("Player control fields promoted stale constructor state after actual observed command writers.");
        if (saved.MovementCall?.Control?.Fields is { } movementFields &&
            (movementFields.Process == saved.CapturedProcess && movementFields.Factory != fields.Factory || movementFields.Sequence != fields.Sequence ||
                movementFields.DisabledWord != fields.DisabledWord || movementFields.WordAvailable != fields.WordAvailable))
            throw new InvalidDataException("Movement controls replaced their actual early Player field owner.");
    }
}
