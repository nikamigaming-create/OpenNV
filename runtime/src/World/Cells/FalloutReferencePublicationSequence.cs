namespace OpenNV.Runtime.World.Cells;

internal static class FalloutReferencePublicationSequence
{
    internal static void Validate(IEnumerable<(Guid Process, long? Sequence)> publications,
        IEnumerable<(Guid Process, long Entered, long Changed)> calls, long capturedSequence)
    {
        var groups = calls.GroupBy(value => value.Process).ToDictionary(value => value.Key, value => value.ToArray());
        long sequence = 0;
        foreach (var publication in publications)
        {
            if (publication.Sequence is null)
                throw new NotSupportedException("source-cold-reference-publication-writer-boundary-unbound");
            if (publication.Sequence != sequence)
                throw new InvalidDataException("Reference publication changed its actual retained writer boundary.");
            foreach (var call in groups.GetValueOrDefault(publication.Process) ?? [])
            {
                if (call.Entered <= publication.Sequence || call.Entered <= sequence || call.Changed <= call.Entered)
                    throw new InvalidDataException("Reference writer crossed its publication or another completed writer boundary.");
                sequence = call.Changed;
            }
            groups.Remove(publication.Process);
        }
        if (groups.Count != 0 || sequence != capturedSequence)
            throw new InvalidDataException("Reference publication omitted a writer or its actual current sequence.");
    }
}
