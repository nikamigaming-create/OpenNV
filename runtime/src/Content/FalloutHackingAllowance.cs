using System.Numerics;

namespace OpenNV.Runtime.Content;

internal static class FalloutHackingAllowance
{
    internal static uint Calculate(IReadOnlyList<FalloutHackingWord> words,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (words.Count > 20) throw new NotSupportedException("Hacking allowance exceeds the source candidate extent.");
        cancellation.ThrowIfCancellationRequested();
        var matrix = new int[words.Count, words.Count];
        foreach (var word in words)
            if (word is null || word.Length != words[0].Length)
                throw new InvalidDataException("Hacking allowance requires equally sized source candidates.");
        for (var row = 0; row < words.Count; ++row)
        {
            for (var column = 0; column < words.Count; ++column)
            {
                var likeness = FalloutHackingRules.Likeness(words[row].Bytes, words[column].Bytes);
                if (row != column && likeness == words[row].Length)
                    throw new InvalidDataException("Hacking allowance cannot admit duplicate candidates.");
                matrix[row, column] = row == column ? -1 : likeness;
            }
        }
        var memo = new Dictionary<uint, uint>();
        return Resolve((1u << words.Count) - 1);

        uint Resolve(uint retained)
        {
            cancellation.ThrowIfCancellationRequested();
            if (retained == 0) return 0;
            if (memo.TryGetValue(retained, out var saved)) return saved;
            var groups = new List<uint[]>();
            var minimum = int.MaxValue;
            for (var row = 0; row < words.Count; ++row)
            {
                if ((retained & (1u << row)) == 0) continue;
                var outcomes = new uint[32];
                for (var column = 0; column < words.Count; ++column)
                    if ((retained & (1u << column)) != 0 && row != column)
                        outcomes[matrix[row, column]] |= 1u << column;
                minimum = Math.Min(minimum, outcomes.Max(value => BitOperations.PopCount(value)));
                groups.Add(outcomes);
            }
            if (minimum <= 2) return memo[retained] = (uint)minimum + 1;
            var result = (uint)BitOperations.PopCount(retained);
            var outcomeCount = (int)result;
            foreach (var outcomes in groups)
            {
                if (outcomes.Max(value => BitOperations.PopCount(value)) != minimum) continue;
                var worst = 0u;
                // The source visits candidate-count outcome indices, even when
                // a longer word admits a higher likeness class in this matrix.
                for (var likeness = 0; likeness < outcomeCount; ++likeness)
                {
                    var subset = outcomes[likeness];
                    if (BitOperations.PopCount(subset) == minimum)
                        worst = Math.Max(worst, Resolve(subset));
                }
                if (worst != 0) result = Math.Min(result, worst);
                if (result <= 2) break;
            }
            return memo[retained] = result + 1;
        }
    }
}
