namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed record ParityNumericTolerance(ParityCategory Category, ulong StableId, double MaximumAbsoluteDelta);
internal sealed record ParityAssessedDelta(ParityFieldDelta Delta, bool WithinDeclaredTolerance);
internal sealed record ParityAssessment(bool Comparable, bool ExactBytesEqual, bool WithinDeclaredTolerances,
    string? AlignmentFailure, IReadOnlyList<ParityAssessedDelta> Deltas);

// Assessment never erases raw deltas or changes exact-byte equality. Missing
// fields, different types and unobserved event alignment cannot be tolerated.
internal sealed class ParityTolerancePolicy
{
    private readonly IReadOnlyDictionary<(ParityCategory, ulong), double> _rules;
    internal ParityTolerancePolicy(IReadOnlyList<ParityNumericTolerance> rules)
    {
        if (rules is null || rules.Any(rule => rule is null || !Enum.IsDefined(rule.Category) || !double.IsFinite(rule.MaximumAbsoluteDelta) ||
                rule.MaximumAbsoluteDelta < 0) || rules.Select(rule => (rule.Category, rule.StableId)).Distinct().Count() != rules.Count)
            throw new InvalidDataException("Parity tolerances must be finite, nonnegative and unique per observed field.");
        _rules = rules.ToDictionary(rule => (rule.Category, rule.StableId), rule => rule.MaximumAbsoluteDelta);
    }
    internal ParityAssessment Assess(ParityComparison comparison)
    {
        var deltas = comparison.Deltas.Select(delta => new ParityAssessedDelta(delta,
            delta.Kind == ParityDeltaKind.ByteMismatch && delta.RetailKind == delta.OpenNvKind &&
            delta.RetailKind is ParityValueKind.Float32 or ParityValueKind.Float64 &&
            delta.NumericDelta is { } number && double.IsFinite(number) &&
            _rules.TryGetValue((delta.Category, delta.StableId), out var maximum) && Math.Abs(number) <= maximum)).ToArray();
        return new(comparison.ComparableState, comparison.ExactStateMatch,
            comparison.ComparableState && deltas.All(delta => delta.WithinDeclaredTolerance), comparison.AlignmentFailure, deltas);
    }
}
