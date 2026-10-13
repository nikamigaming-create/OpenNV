namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerControlFieldsState
{
    internal void ObserveCommand(FalloutPlayerControlCommandObservation observation)
    {
        RequireOwner(allowFailure: true); ArgumentNullException.ThrowIfNull(observation); observation.Validate();
        if (!_observedIds.Add(observation.Observation))
            throw new InvalidOperationException("Player controls replayed an actual command observation.");
        _observations.Add(observation with { Arguments = observation.Arguments.ToArray() }); Next();
        // The original command ran through another, still unbound source
        // writer. The old word remains historical; it cannot be read as current
        // or used to derive a later mask/store until that producer is owned.
    }
    private void RequireCurrentWord()
    {
        if (_observations.Count != 0) throw new NotSupportedException(ObservedWriterBoundary);
    }
}
