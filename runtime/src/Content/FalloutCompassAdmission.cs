namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompassAdmissionCandidate(FalloutFormKey Reference, float DistanceSquared, bool? Admitted);
internal sealed record FalloutCompassAdmissionResult(IReadOnlyList<FalloutFormKey> Included, IReadOnlyList<FalloutFormKey> UnresolvedCapacity);

internal static class FalloutCompassAdmission
{
    internal static FalloutCompassAdmissionResult Select(IReadOnlyList<FalloutCompassAdmissionCandidate> candidates, int capacity)
    {
        if (capacity < 0 || candidates.Any(value => !float.IsFinite(value.DistanceSquared) || value.DistanceSquared < 0) ||
            candidates.Select(value => value.Reference).Distinct().Count() != candidates.Count)
            throw new InvalidDataException("Compass capacity projection has invalid source candidates.");
        var possible = candidates.Where(value => value.Admitted != false).ToArray();
        var included = new List<FalloutCompassAdmissionCandidate>();
        var unresolved = new List<FalloutFormKey>();
        foreach (var candidate in candidates.Where(value => value.Admitted == true))
        {
            // Detection is never inferred from distance. A known-admitted tick
            // is safe only if every possible nearer/tied admission still fits.
            // Native's comparator has no tie-breaker: do not invent one here.
            if (possible.Count(value => value.DistanceSquared <= candidate.DistanceSquared) <= capacity) included.Add(candidate);
            else unresolved.Add(candidate.Reference);
        }
        IEnumerable<FalloutCompassAdmissionCandidate> ordered = included;
        if (possible.Length > capacity) ordered = included.OrderBy(value => value.DistanceSquared);
        return new(ordered.Select(value => value.Reference).ToArray(), unresolved);
    }
}
