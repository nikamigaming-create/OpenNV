namespace OpenNV.Runtime.Content;

internal enum FalloutSandboxActivity
{
    Furniture,
    Sleep,
    Eat,
    Wander,
    IdleMarker,
    Dialogue
}

// These are package activity exclusions, independent of the ordinary package
// flags and its schedule/conditions. An empty mask permits every activity.
internal readonly record struct FalloutSandboxActivities(ushort Exclusions)
{
    internal static FalloutSandboxActivities Read(FalloutPluginRecord package)
    {
        var data = FalloutPackageData.Read(package);
        if (data.Procedure != 12) throw new InvalidDataException("Package is not Sandbox.");
        var mask = data.SpecificFlags ?? 0;
        if ((mask & ~0x3f) != 0)
            throw new NotSupportedException("Sandbox has unknown activity exclusion flags.");
        return new(mask);
    }

    internal bool Allows(FalloutSandboxActivity activity) => (Exclusions & Bit(activity)) == 0;

    private static int Bit(FalloutSandboxActivity activity) => activity switch
    {
        FalloutSandboxActivity.Furniture => 0x10,
        FalloutSandboxActivity.Sleep => 2,
        FalloutSandboxActivity.Eat => 1,
        FalloutSandboxActivity.Wander => 0x20,
        FalloutSandboxActivity.IdleMarker => 8,
        FalloutSandboxActivity.Dialogue => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(activity))
    };

    internal static (float Minimum, float Maximum) DurationBounds(FalloutNumericGameSettings settings,
        byte energy, FalloutSandboxActivity activity)
    {
        var suffix = activity switch
        {
            FalloutSandboxActivity.Furniture or FalloutSandboxActivity.Sleep => "Furniture",
            FalloutSandboxActivity.Eat => "Eating",
            FalloutSandboxActivity.Wander or FalloutSandboxActivity.Dialogue => "Wandering",
            FalloutSandboxActivity.IdleMarker => "IdleMarker",
            _ => throw new ArgumentOutOfRangeException(nameof(activity))
        };
        var basis = settings.Float("fSandboxDurationBase") * settings.Float("fSandboxDurationMult" + suffix);
        var energyScale = settings.Float("fSandboxEnergyMult") * settings.Float("fSandboxEnergyMult" + suffix);
        var duration = basis * (1 + energy * energyScale);
        var range = settings.Float("fSandboxDurationRangeMult");
        var minimum = duration * (1 - range);
        var maximum = duration * (1 + range);
        if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum < 0 || maximum < minimum)
            throw new NotSupportedException("Sandbox settings produce an unsupported activity duration range.");
        return (minimum, maximum);
    }
}

// Preference history belongs to the actor's active package, not the rendered
// scene. Candidate admission and contextual adjustments precede the draw; this
// owner never treats a missing activity executor as an ineligible candidate.
internal sealed class FalloutSandboxPreferences
{
    private readonly byte[] _weights;

    internal FalloutSandboxPreferences() => _weights = Enumerable.Repeat((byte)128, 6).ToArray();

    internal FalloutSandboxPreferences(IReadOnlyList<byte> retained)
    {
        if (retained.Count != 6 || retained.Any(value => value == 0))
            throw new InvalidDataException("Sandbox preference history requires six nonzero activity weights.");
        _weights = retained.ToArray();
    }

    internal byte[] Capture() => (byte[])_weights.Clone();

    internal int Weight(FalloutSandboxActivity activity)
    {
        var index = (int)activity;
        if (index < 0 || index >= _weights.Length) throw new ArgumentOutOfRangeException(nameof(activity));
        return _weights[index];
    }

    internal int[] ActivityWeights(FalloutSandboxActivity? scheduledActivity, bool foodAvailable,
        bool furnitureAvailable, bool dialogueSuppressed)
    {
        if (scheduledActivity is { } scheduled)
        {
            if (scheduled is not (FalloutSandboxActivity.Sleep or FalloutSandboxActivity.Eat))
                throw new InvalidDataException("Sandbox daily activity is neither sleeping nor eating.");
            var exclusive = new int[6];
            exclusive[(int)scheduled] = 10;
            return exclusive;
        }
        var result = _weights.Select(value => (int)value).ToArray();
        // Sleeping is selected by its daily window, not by ambient preference.
        // A negative activity weight is retained until candidate multiplication;
        // the candidate's final selection weight is then clamped to zero.
        result[(int)FalloutSandboxActivity.Sleep] = 0;
        result[(int)FalloutSandboxActivity.Eat] += (foodAvailable ? 20 : 0) - (furnitureAvailable ? 0 : 15);
        if (dialogueSuppressed) result[(int)FalloutSandboxActivity.Dialogue] = 0;
        return result;
    }

    internal static int CandidateWeight(int sourceWeight, int activityWeight)
    {
        if (sourceWeight < 0) throw new InvalidDataException("Sandbox source candidate weight is negative.");
        return Math.Max(0, checked(sourceWeight * activityWeight));
    }

    internal void Selected(FalloutSandboxActivity activity)
    {
        _ = Weight(activity);
        for (var index = 0; index < _weights.Length; index++)
            if (_weights[index] < byte.MaxValue) _weights[index]++;
        _weights[(int)activity] = 1;
    }

    internal static int Choose(IReadOnlyList<int> candidateWeights, uint ticket)
    {
        if (candidateWeights.Any(weight => weight < 0))
            throw new InvalidDataException("Sandbox candidate weight is negative.");
        var total = candidateWeights.Aggregate(0L, (sum, weight) => checked(sum + weight));
        if (total > int.MaxValue || ticket >= total)
            throw new InvalidDataException("Sandbox selection ticket is outside its eligible candidate weights.");
        for (var index = 0; index < candidateWeights.Count; index++)
        {
            var weight = (uint)candidateWeights[index];
            if (ticket < weight) return index;
            ticket -= weight;
        }
        throw new InvalidOperationException("Sandbox weighted selection exhausted its candidates.");
    }
}
