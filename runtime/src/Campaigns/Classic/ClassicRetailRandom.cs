using System.Text.Json;
using System.Runtime.InteropServices;
using System.Numerics;

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
    string SavePolicy,
    ClassicRetailExternalSeedContract ExternalSeed)
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
            RequiredString(source, "savePolicy"),
            ClassicRetailExternalSeedContract.Parse(
                source.GetProperty("externalSeed")));
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
        ExternalSeed.Validate();
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

internal sealed record ClassicRetailExternalSeedContract(
    string Source,
    uint MixerMultiplier,
    uint MixerIncrement,
    int OutputShift,
    uint OutputMask,
    int WordsPerSeed,
    int HighWordShift,
    uint NewGameSeedUnsigned,
    string NewGamePolicy,
    string LoadPolicy)
{
    internal const string WinmmTimeGetTime = "winmm-timeGetTime-u32";

    internal static ClassicRetailExternalSeedContract Parse(JsonElement source) => new(
        source.GetProperty("source").GetString() ?? "",
        source.GetProperty("mixerMultiplier").GetUInt32(),
        source.GetProperty("mixerIncrement").GetUInt32(),
        source.GetProperty("outputShift").GetInt32(),
        source.GetProperty("outputMask").GetUInt32(),
        source.GetProperty("wordsPerSeed").GetInt32(),
        source.GetProperty("highWordShift").GetInt32(),
        source.GetProperty("newGameSeedUnsigned").GetUInt32(),
        source.GetProperty("newGamePolicy").GetString() ?? "",
        source.GetProperty("loadPolicy").GetString() ?? "");

    internal void Validate()
    {
        var unsignedBitCount = BitOperations.PopCount(uint.MaxValue);
        var signedBitCount = BitOperations.PopCount((uint)int.MaxValue);
        if (Source != WinmmTimeGetTime || MixerMultiplier == 0 ||
            OutputShift < 0 || OutputShift >= unsignedBitCount || OutputMask == 0 ||
            WordsPerSeed != 2 || HighWordShift < 0 || HighWordShift >= signedBitCount ||
            ((ulong)OutputMask << HighWordShift | OutputMask) > int.MaxValue ||
            NewGameSeedUnsigned <= int.MaxValue ||
            NewGamePolicy != "explicit-seed-minimum-clamp" ||
            LoadPolicy != "next-two-mixer-words")
            throw new InvalidOperationException(
                "Classic retail external-seed contract is invalid.");
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

internal sealed record ClassicRetailSeedState(uint MixerState, int ResetCount);
internal sealed record ClassicRetailSeededRandom(
    ClassicRetailSeedState SeedState,
    ClassicRetailRandomState RandomState,
    int Seed);

internal static class ClassicRetailSeedOwner
{
    internal static ClassicRetailSeededRandom InitializeFromExactBuildClock(
        ClassicRetailRandomContract contract)
    {
        if (!OperatingSystem.IsWindows() ||
            contract.ExternalSeed.Source != ClassicRetailExternalSeedContract.WinmmTimeGetTime)
            throw new PlatformNotSupportedException(
                "The FO2 exact-build seed clock is available only through WinMM.");
        return Initialize(TimeGetTime(), contract);
    }

    internal static ClassicRetailSeededRandom Initialize(
        uint elapsedMilliseconds,
        ClassicRetailRandomContract contract) =>
        NextReset(new ClassicRetailSeedState(elapsedMilliseconds, 0), contract);

    internal static ClassicRetailSeededRandom ResetForLoad(
        ClassicRetailSeedState state,
        ClassicRetailRandomContract contract)
    {
        if (state.ResetCount <= 0)
            throw new InvalidOperationException(
                "Classic retail load reset requires initialized seed state.");
        return NextReset(state, contract);
    }

    internal static ClassicRetailSeededRandom ResetForNewGame(
        ClassicRetailSeedState state,
        ClassicRetailRandomContract contract)
    {
        contract.Validate();
        if (state.ResetCount <= 0)
            throw new InvalidOperationException(
                "Classic retail new-game reset requires initialized seed state.");
        var seed = unchecked((int)contract.ExternalSeed.NewGameSeedUnsigned);
        return new ClassicRetailSeededRandom(
            state with { ResetCount = checked(state.ResetCount + 1) },
            ClassicRetailRandom.Reset(seed, contract),
            contract.MinimumSeed);
    }

    private static ClassicRetailSeededRandom NextReset(
        ClassicRetailSeedState state,
        ClassicRetailRandomContract contract)
    {
        contract.Validate();
        var mixer = state.MixerState;
        var first = NextMixerWord(ref mixer, contract.ExternalSeed);
        var second = NextMixerWord(ref mixer, contract.ExternalSeed);
        var seed = checked((int)((first << contract.ExternalSeed.HighWordShift) + second));
        return new ClassicRetailSeededRandom(
            new ClassicRetailSeedState(mixer, checked(state.ResetCount + 1)),
            ClassicRetailRandom.Reset(seed, contract),
            seed);
    }

    private static uint NextMixerWord(
        ref uint state,
        ClassicRetailExternalSeedContract contract)
    {
        state = unchecked(state * contract.MixerMultiplier + contract.MixerIncrement);
        return state >> contract.OutputShift & contract.OutputMask;
    }

    [DllImport("winmm.dll", EntryPoint = "timeGetTime")]
    private static extern uint TimeGetTime();
}

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
