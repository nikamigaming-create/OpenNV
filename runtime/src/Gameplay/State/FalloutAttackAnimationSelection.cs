using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutAttackAnimationSelection
{
    // Native multiple-sequence selection uses one unsigned draw modulo count,
    // with replacement. The OpenNV RNG's stream remains independently owned.
    internal static int Index(int count, Func<uint> next)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        return count == 1 ? 0 : (int)(next() % (uint)count);
    }

    internal static string Select(IReadOnlyList<string> candidates, Func<uint> next, string? retained = null)
    {
        if (candidates.Count == 0 || candidates.Any(string.IsNullOrWhiteSpace) ||
            candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count() != candidates.Count)
            throw new InvalidDataException("Attack selection has no unique source candidate catalog.");
        if (retained is not null)
            return candidates.SingleOrDefault(path => path.Equals(retained, StringComparison.OrdinalIgnoreCase)) ??
                throw new InvalidDataException("Saved attack KF no longer belongs to its winning source group.");
        return candidates[Index(candidates.Count, next)];
    }

    internal static FalloutActorEngagement Bind(FalloutActorEngagement state, IReadOnlyList<string> candidates,
        Func<string, string> sourceHash)
    {
        state.Validate();
        if (state.Action != "attack" || state.Animation is null && (!state.StartPending || state.Seconds != 0))
            throw new InvalidDataException("Attack selection requires a new action or its retained source KF.");
        var random = new FalloutSoundRandomState(state.AttackRandomState ??
            throw new InvalidDataException("Actor attack has no persistent variant RNG."));
        var path = Select(candidates, random.NextUInt32, state.Animation);
        var hash = sourceHash(path);
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || state.AnimationHash is not null &&
            !state.AnimationHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved attack KF differs from its winning source bytes.");
        return state with { Animation = path, AnimationHash = hash, AttackRandomState = random.State };
    }
}
