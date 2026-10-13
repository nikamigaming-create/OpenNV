using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Observation identifies this actual host ingress, not the original source
// script invocation. Retaining it never executes or replays a source writer.
internal sealed record FalloutPlayerControlCommandObservation(FalloutFormKey Source, string Producer,
    Guid Observation, bool Enable, IReadOnlyList<bool> Arguments)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Source.OwnerPlugin) || Source.ObjectId > 0x00ffffff ||
            string.IsNullOrWhiteSpace(Producer) || Observation == Guid.Empty || Arguments is null || Arguments.Count > 7)
            throw new InvalidDataException("Observed Player controls lost their actual source, producer, observation identity or original arguments.");
    }
    internal bool SameCommand(FalloutPlayerControlCommandObservation other) => Source == other.Source &&
        Producer == other.Producer && Observation == other.Observation && Enable == other.Enable && Arguments.SequenceEqual(other.Arguments);
}
internal sealed record FalloutPlayerControlCommandReceipt(FalloutPlayerControlCommandObservation Command,
    Guid Process, Guid Constructor, long Retained);
