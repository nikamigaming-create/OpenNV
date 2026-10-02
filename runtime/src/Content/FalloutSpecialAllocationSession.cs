namespace OpenNV.Runtime.Content;

// These callbacks belong to the authoritative actor-value owner. A current
// value getter or a detached SPECIAL draft cannot implement this contract.
internal sealed record FalloutSpecialAllocationBinding(
    string Owner, Func<int, float> ReadPermanent, Action<int, int> WriteBaseInteger);

internal sealed class FalloutSpecialAllocationSession
{
    internal const int FirstActorValue = 5;
    internal const int AttributeCount = 7;
    internal const int Minimum = 1;
    internal const int Maximum = 10;
    private readonly FalloutSpecialAllocationBinding _binding;
    internal int Budget { get; }
    internal string Owner => _binding.Owner;

    internal FalloutSpecialAllocationSession(int budget, FalloutSpecialAllocationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (string.IsNullOrWhiteSpace(binding.Owner) || binding.ReadPermanent is null || binding.WriteBaseInteger is null)
            throw new NotSupportedException("Permanent SPECIAL reads and immediate base writes have no bound actor-value owner.");
        if (budget is < AttributeCount * Minimum or > AttributeCount * Maximum)
            throw new NotSupportedException("SPECIAL allocation budget is outside the admitted source range.");
        Budget = budget; _binding = binding;
    }

    internal IReadOnlyList<int> Values => Enumerable.Range(FirstActorValue, AttributeCount).Select(Read).ToArray();
    internal int Remaining => checked(Budget - Values.Sum());
    internal bool CanFinish => Remaining == 0;

    internal bool Change(int actorValue, int direction)
    {
        if (actorValue is < FirstActorValue or >= FirstActorValue + AttributeCount)
            throw new ArgumentOutOfRangeException(nameof(actorValue));
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var values = Values;
        if (direction > 0 && values.Sum() >= Budget) return false;
        var previous = values[actorValue - FirstActorValue];
        var next = direction > 0 ? Math.Min(checked(previous + 1), Maximum) : Math.Max(checked(previous - 1), Minimum);
        // The source writes this absolute integer to the BASE slot, then reads
        // permanent values again. It does not compensate for permanent buffs.
        _binding.WriteBaseInteger(actorValue, next);
        _ = Values;
        return true;
    }

    private int Read(int actorValue)
    {
        var value = _binding.ReadPermanent(actorValue);
        if (!float.IsFinite(value)) throw new NotSupportedException("Permanent SPECIAL value is non-finite or unbound.");
        // The native integer actor-value getter floors before converting.
        return checked((int)MathF.Floor(value));
    }
}
