namespace OpenNV.Runtime.Content;

// All three owners are mandatory. In particular the allowance calculator has
// not been replaced by a constant, and source clock units are supplied explicitly.
internal sealed record FalloutHackingGenerationOwners(Func<uint, uint> Random, Func<uint> Clock,
    uint RetryWindowTicks, Func<IReadOnlyList<FalloutHackingWord>, uint> CandidateAllowance,
    CancellationToken Cancellation);

internal sealed class FalloutHackingCandidates
{
    internal IReadOnlyList<FalloutHackingWord> Words { get; }
    internal FalloutHackingWord Answer { get; }
    internal uint MaximumAttempts { get; }
    internal int RequestedCount { get; }
    internal string DictionaryHash { get; }

    private FalloutHackingCandidates(List<FalloutHackingWord> words, FalloutHackingWord answer,
        uint attempts, int requested, string dictionaryHash)
    {
        Words = Array.AsReadOnly(words.ToArray()); Answer = answer;
        MaximumAttempts = attempts; RequestedCount = requested; DictionaryHash = dictionaryHash;
    }

    internal static FalloutHackingCandidates Generate(FalloutHackingDictionary dictionary,
        FalloutHackingRequest request, Func<float, float> applyScienceBonus,
        FalloutHackingGenerationOwners owners)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(owners.Random);
        ArgumentNullException.ThrowIfNull(owners.Clock);
        ArgumentNullException.ThrowIfNull(owners.CandidateAllowance);
        if (owners.RetryWindowTicks == 0)
            throw new InvalidDataException("Hacking generation requires a qualified positive retry window.");
        owners.Cancellation.ThrowIfCancellationRequested();
        var length = request.WordLength;
        var requested = request.RequestedWords(applyScienceBonus);
        var pool = dictionary.Words.Where(word => word.Length == length).ToList();
        if (pool.Count == 0) throw new InvalidDataException("Owned dictionary has no words of the source answer length.");
        for (var pass = 0; pass < 3; ++pass) FalloutHackingRules.Order(pool, owners.Random);
        var selected = new List<FalloutHackingWord>();
        var zeroAdmission = 0u;
        var classLimit = unchecked(((uint)requested - 2u) / (uint)length + 1u);
        var attempts = 0u;
        while (true)
        {
            var deadline = unchecked(owners.Clock() + owners.RetryWindowTicks);
            while (owners.Clock() < deadline)
            {
                owners.Cancellation.ThrowIfCancellationRequested();
                attempts = unchecked(attempts + 1);
                if (attempts == 0) break;
                selected.Clear();
                FalloutHackingRules.Order(pool, owners.Random);
                foreach (var candidate in pool)
                {
                    owners.Cancellation.ThrowIfCancellationRequested();
                    if (selected.Count >= requested) break;
                    var accepted = true;
                    var frequencies = new uint[8];
                    foreach (var previous in selected)
                    {
                        var likeness = FalloutHackingRules.Likeness(candidate.Bytes, previous.Bytes);
                        if (likeness == length) { accepted = false; break; }
                        if (selected.Count < requested - 1)
                        {
                            if (likeness == 0 && FalloutHackingRules.Draw(owners.Random, 10) >= zeroAdmission)
                            { accepted = false; break; }
                        }
                        else if (likeness < frequencies.Length && ++frequencies[likeness] > classLimit)
                        { accepted = false; break; }
                    }
                    if (accepted) selected.Add(candidate);
                }
                if (selected.Count == requested) break;
            }
            if (selected.Count == requested) break;
            if (++zeroAdmission == 10)
            {
                zeroAdmission = 0;
                classLimit = unchecked(classLimit + 1);
                if (classLimit > (uint)requested / 2) break;
            }
            owners.Cancellation.ThrowIfCancellationRequested();
        }
        if (selected.Count == 0)
            throw new NotSupportedException("Source candidate generation retained no answer; no fallback puzzle is permitted.");
        for (var pass = 0; pass < 4; ++pass) FalloutHackingRules.Order(selected, owners.Random);
        var answer = selected[(int)FalloutHackingRules.Draw(owners.Random, (uint)selected.Count)];
        var allowance = owners.CandidateAllowance(Array.AsReadOnly(selected.ToArray()));
        return new(selected, answer, Math.Max(4u, allowance), requested, dictionary.SourceHash);
    }
}
