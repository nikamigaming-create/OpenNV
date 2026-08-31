using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicCriticalSelectionContract(
    string Schema,
    string ExactBuild,
    int MinimumPercentRoll,
    int MaximumPercentRoll,
    IReadOnlyList<int> CriticalScoreThresholds,
    int HitLocationCount,
    IReadOnlyList<int> FumbleScoreThresholds,
    int FumbleTypeCount,
    int LuckSpecialIndex,
    int NeutralLuck,
    int FumblePercentPerLuckPoint,
    int PlayerFumbleImmunityDays,
    int TicksPerDay)
{
    internal const string ExpectedSchema = "opennv-classic-critical-selection/v1";

    internal static ClassicCriticalSelectionContract Parse(JsonElement source)
    {
        var result = new ClassicCriticalSelectionContract(
            RequiredString(source, "schema"),
            RequiredString(source, "exactBuild"),
            source.GetProperty("minimumPercentRoll").GetInt32(),
            source.GetProperty("maximumPercentRoll").GetInt32(),
            ReadInts(source, "criticalScoreThresholds"),
            source.GetProperty("hitLocationCount").GetInt32(),
            ReadInts(source, "fumbleScoreThresholds"),
            source.GetProperty("fumbleTypeCount").GetInt32(),
            source.GetProperty("luckSpecialIndex").GetInt32(),
            source.GetProperty("neutralLuck").GetInt32(),
            source.GetProperty("fumblePercentPerLuckPoint").GetInt32(),
            source.GetProperty("playerFumbleImmunityDays").GetInt32(),
            source.GetProperty("ticksPerDay").GetInt32());
        result.Validate();
        return result;
    }

    internal void Validate()
    {
        if (Schema != ExpectedSchema || string.IsNullOrWhiteSpace(ExactBuild) ||
            MinimumPercentRoll <= 0 || MaximumPercentRoll < MinimumPercentRoll ||
            HitLocationCount <= 0 || FumbleTypeCount <= 0 || LuckSpecialIndex < 0 ||
            NeutralLuck <= 0 || FumblePercentPerLuckPoint <= 0 ||
            PlayerFumbleImmunityDays < 0 || TicksPerDay <= 0 ||
            !Ascending(CriticalScoreThresholds) || !Ascending(FumbleScoreThresholds) ||
            CriticalScoreThresholds[^1] != MaximumPercentRoll ||
            FumbleScoreThresholds[^1] >= MaximumPercentRoll)
            throw new InvalidOperationException(
                "Classic critical-selection contract is invalid.");
    }

    private static bool Ascending(IReadOnlyList<int> values) =>
        values.Count > 0 && values.Select((value, index) =>
            index == 0 || value > values[index - 1]).All(value => value);

    private static int[] ReadInts(JsonElement source, string property) =>
        source.GetProperty(property).EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Classic critical-selection contract string is empty: {property}");
    }
}

internal sealed record ClassicCriticalSelection(int HitLocation, int Severity);
internal sealed record ClassicFumbleSelection(int FumbleType, int Severity);

internal static class ClassicCriticalSelector
{
    internal static ClassicCriticalSelection SelectCritical(
        ClassicCriticalSelectionContract contract,
        int hitLocation,
        int percentRoll,
        int criticalUpgradeBonus)
    {
        contract.Validate();
        if (hitLocation < 0 || hitLocation >= contract.HitLocationCount ||
            percentRoll < contract.MinimumPercentRoll ||
            percentRoll > contract.MaximumPercentRoll || criticalUpgradeBonus < 0)
            throw new InvalidOperationException("Classic critical inputs are invalid.");
        var score = percentRoll + criticalUpgradeBonus;
        var severity = FirstThreshold(score, contract.CriticalScoreThresholds);
        return new ClassicCriticalSelection(hitLocation, severity);
    }

    internal static ClassicFumbleSelection? SelectFumble(
        ClassicCriticalSelectionContract contract,
        int fumbleType,
        int percentRoll,
        int luck,
        bool attackerIsPlayer,
        long gameTime)
    {
        contract.Validate();
        if (fumbleType < 0 || fumbleType >= contract.FumbleTypeCount ||
            percentRoll < contract.MinimumPercentRoll ||
            percentRoll > contract.MaximumPercentRoll || luck <= 0 || gameTime < 0)
            throw new InvalidOperationException("Classic fumble inputs are invalid.");
        if (attackerIsPlayer &&
            gameTime / contract.TicksPerDay < contract.PlayerFumbleImmunityDays)
            return null;
        var score = percentRoll -
            contract.FumblePercentPerLuckPoint * (luck - contract.NeutralLuck);
        return new ClassicFumbleSelection(
            fumbleType,
            FirstThreshold(score, contract.FumbleScoreThresholds));
    }

    private static int FirstThreshold(int score, IReadOnlyList<int> thresholds)
    {
        for (var index = 0; index < thresholds.Count; index++)
            if (score <= thresholds[index])
                return index;
        return thresholds.Count;
    }
}
