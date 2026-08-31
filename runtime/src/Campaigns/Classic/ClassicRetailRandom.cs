using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicRetailRandomContract(
    string Schema,
    string ExactBuild,
    int MinimumSeed,
    int Modulus,
    int Multiplier,
    int Quotient,
    int Remainder,
    int ShuffleSlots,
    int WarmupSteps,
    int ShuffleIndexMask,
    string SavePolicy)
{
    internal const string ExpectedSchema = "opennv-classic-retail-random/v1";

    internal static ClassicRetailRandomContract Parse(JsonElement source)
    {
        var result = new ClassicRetailRandomContract(
            RequiredString(source, "schema"),
            RequiredString(source, "exactBuild"),
            source.GetProperty("minimumSeed").GetInt32(),
            source.GetProperty("modulus").GetInt32(),
            source.GetProperty("multiplier").GetInt32(),
            source.GetProperty("quotient").GetInt32(),
            source.GetProperty("remainder").GetInt32(),
            source.GetProperty("shuffleSlots").GetInt32(),
            source.GetProperty("warmupSteps").GetInt32(),
            source.GetProperty("shuffleIndexMask").GetInt32(),
            RequiredString(source, "savePolicy"));
        result.Validate();
        return result;
    }

    internal void Validate()
    {
        if (Schema != ExpectedSchema || string.IsNullOrWhiteSpace(ExactBuild) ||
            MinimumSeed <= 0 || Modulus <= MinimumSeed || Multiplier <= 0 ||
            Quotient <= 0 || Remainder <= 0 || ShuffleSlots <= 0 ||
            WarmupSteps < ShuffleSlots ||
            ShuffleIndexMask != ShuffleSlots - 1 ||
            (ShuffleSlots & ShuffleIndexMask) != 0 ||
            SavePolicy != "reset-from-new-seed-on-load")
            throw new InvalidOperationException(
                "Classic retail random contract is invalid.");
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Classic retail random contract string is empty: {property}");
    }
}

internal sealed record ClassicRetailRandomState(
    int Seed,
    int Selector,
    IReadOnlyList<int> Shuffle)
{
    internal void Validate(ClassicRetailRandomContract contract)
    {
        contract.Validate();
        if (Seed < contract.MinimumSeed || Seed >= contract.Modulus ||
            Selector < contract.MinimumSeed || Selector >= contract.Modulus ||
            Shuffle.Count != contract.ShuffleSlots ||
            Shuffle.Any(value => value < contract.MinimumSeed || value >= contract.Modulus))
            throw new InvalidOperationException("Classic retail random state is invalid.");
    }
}

internal sealed record ClassicRetailRandomResult(
    ClassicRetailRandomState State,
    int Value);

internal static class ClassicRetailRandom
{
    internal static ClassicRetailRandomState Reset(
        int seed,
        ClassicRetailRandomContract contract)
    {
        contract.Validate();
        var current = seed < contract.MinimumSeed ? contract.MinimumSeed : seed;
        if (current >= contract.Modulus)
            throw new InvalidOperationException(
                "Classic retail random seed exceeds the exact-build domain.");
        var shuffle = new int[contract.ShuffleSlots];
        for (var step = contract.WarmupSteps - 1; step >= 0; step--)
        {
            current = Step(current, contract);
            if (step < shuffle.Length)
                shuffle[step] = current;
        }
        var result = new ClassicRetailRandomState(current, shuffle[0], shuffle);
        result.Validate(contract);
        return result;
    }

    internal static ClassicRetailRandomResult Next(
        ClassicRetailRandomState state,
        int minimum,
        int maximum,
        ClassicRetailRandomContract contract)
    {
        state.Validate(contract);
        if (minimum > maximum)
            throw new InvalidOperationException(
                "Classic retail random range is reversed.");
        var width = (long)maximum - minimum + 1;
        if (width <= 0 || width > int.MaxValue)
            throw new InvalidOperationException(
                "Classic retail random range exceeds the exact-build domain.");
        var seed = Step(state.Seed, contract);
        var slot = state.Selector & contract.ShuffleIndexMask;
        var selected = state.Shuffle[slot];
        var shuffle = state.Shuffle.ToArray();
        shuffle[slot] = seed;
        var nextState = new ClassicRetailRandomState(seed, selected, shuffle);
        nextState.Validate(contract);
        return new ClassicRetailRandomResult(
            nextState,
            minimum + selected % (int)width);
    }

    private static int Step(int seed, ClassicRetailRandomContract contract)
    {
        var quotient = seed / contract.Quotient;
        var next = contract.Multiplier * (seed - quotient * contract.Quotient) -
            contract.Remainder * quotient;
        if (next < contract.MinimumSeed)
            next += contract.Modulus;
        if (next < contract.MinimumSeed)
            next += contract.Modulus;
        return next;
    }
}
