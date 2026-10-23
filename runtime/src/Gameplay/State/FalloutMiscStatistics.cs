namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutMiscStatisticsSnapshot(IReadOnlyList<int?> Values, string? Error = null)
{
    internal void Validate()
    {
        if (Values is null || Values.Count != FalloutMiscStatistics.Count)
            throw new InvalidDataException("Miscellaneous statistics require exactly 43 count slots.");
        if (Error is not null && string.IsNullOrWhiteSpace(Error))
            throw new InvalidDataException("A miscellaneous statistic failure must retain its error.");
    }
}

// Numeric publication precedes the caller's challenge/UI handoff. A throwing
// handoff retains its count prefix and stops further increments, not reads.
internal sealed class FalloutMiscStatistics
{
    internal const int Count = 43;
    private readonly Action<int, int> _changed;
    private int?[] _values = new int?[Count];
    private bool _publishing;
    internal string? Error { get; private set; }

    internal FalloutMiscStatistics(Action<int, int> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        _changed = changed;
        Array.Fill(_values, (int?)0);
    }

    internal int Read(int index)
    {
        RequireIndex(index);
        return _values[index] ?? throw new NotSupportedException(
            $"Saved miscellaneous statistic {index} has no authoritative count.");
    }

    internal void RequireIncrement(int index)
    {
        RequireIndex(index);
        if (Error is not null)
            throw new InvalidOperationException($"Miscellaneous statistic updates stopped: {Error}");
        if (_values[index] is null)
            throw new NotSupportedException($"Saved miscellaneous statistic {index} has no authoritative count.");
    }

    internal void Increment(int index)
    {
        RequireIncrement(index);
        var next = unchecked(Read(index) + 1);
        var wasPublishing = _publishing;
        _publishing = true;
        _values[index] = next;
        try { _changed(index, 1); }
        catch (Exception error)
        {
            // Neither undo the published count nor repeat an uncertain callback.
            Error ??= $"Statistic {index} callback failed: {error.Message}";
            throw;
        }
        finally { _publishing = wasPublishing; }
    }

    internal FalloutMiscStatisticsSnapshot Capture() =>
        new(Array.AsReadOnly((int?[])_values.Clone()), Error);

    internal void Restore(FalloutMiscStatisticsSnapshot? snapshot)
    {
        if (_publishing)
            throw new InvalidOperationException("Cannot restore miscellaneous statistics during a change callback.");
        var replacement = new int?[Count];
        string? error = null;
        if (snapshot is not null)
        {
            snapshot.Validate();
            // Finish the copy before replacing either the counts or the fault.
            // Caller-owned arrays/lists never become the live state backing.
            for (var index = 0; index < Count; ++index) replacement[index] = snapshot.Values[index];
            error = snapshot.Error;
        }
        // A missing snapshot is unknown history in every slot, not fresh zero.
        _values = replacement;
        Error = error;
    }

    private static void RequireIndex(int index)
    {
        if ((uint)index >= Count)
            throw new ArgumentOutOfRangeException(nameof(index), "Miscellaneous statistic indices are 0 through 42.");
    }
}
