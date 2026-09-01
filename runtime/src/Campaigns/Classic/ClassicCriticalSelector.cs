using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicCriticalSelectionContract(
    string Schema,
    string ExactBuild,
    int MinimumPercentRoll,
    int MaximumPercentRoll,
    int CriticalUpgradeEnabledAfterDays,
    int CriticalUpgradeMarginDivisor,
    int StatCheckMinimumRoll,
    int StatCheckMaximumRoll,
    IReadOnlyList<int> CriticalScoreThresholds,
    int HitLocationCount,
    IReadOnlyList<int> FumbleScoreThresholds,
    int FumbleTypeCount,
    int LuckSpecialIndex,
    int NeutralLuck,
    int FumblePercentPerLuckPoint,
    int PlayerFumbleImmunityDays,
    int TicksPerDay,
    IReadOnlyDictionary<string, int> DamageFlags,
    IReadOnlyList<ClassicCriticalEffectRow> CriticalRows,
    IReadOnlyList<IReadOnlyList<int>> FumbleRows)
{
    internal const string ExpectedSchema = "opennv-classic-critical-selection/v1";

    internal static ClassicCriticalSelectionContract Parse(JsonElement source)
    {
        var result = new ClassicCriticalSelectionContract(
            RequiredString(source, "schema"),
            RequiredString(source, "exactBuild"),
            source.GetProperty("minimumPercentRoll").GetInt32(),
            source.GetProperty("maximumPercentRoll").GetInt32(),
            source.GetProperty("criticalUpgradeEnabledAfterDays").GetInt32(),
            source.GetProperty("criticalUpgradeMarginDivisor").GetInt32(),
            source.GetProperty("statCheckMinimumRoll").GetInt32(),
            source.GetProperty("statCheckMaximumRoll").GetInt32(),
            ReadInts(source, "criticalScoreThresholds"),
            source.GetProperty("hitLocationCount").GetInt32(),
            ReadInts(source, "fumbleScoreThresholds"),
            source.GetProperty("fumbleTypeCount").GetInt32(),
            source.GetProperty("luckSpecialIndex").GetInt32(),
            source.GetProperty("neutralLuck").GetInt32(),
            source.GetProperty("fumblePercentPerLuckPoint").GetInt32(),
            source.GetProperty("playerFumbleImmunityDays").GetInt32(),
            source.GetProperty("ticksPerDay").GetInt32(),
            source.GetProperty("damageFlags").EnumerateObject()
                .ToDictionary(row => row.Name, row => row.Value.GetInt32(),
                    StringComparer.Ordinal),
            source.GetProperty("criticalRows").EnumerateArray().Select(row =>
                new ClassicCriticalEffectRow(
                    RequiredString(row, "targetKind"),
                    row.GetProperty("hitLocation").GetInt32(),
                    row.GetProperty("severity").GetInt32(),
                    row.GetProperty("damageMultiplier").GetInt32(),
                    row.GetProperty("damageFlags").GetInt32(),
                    row.GetProperty("stat").GetInt32(),
                    row.GetProperty("statModifier").GetInt32(),
                    row.GetProperty("failedStatDamageFlags").GetInt32(),
                    row.GetProperty("successMessageId").GetInt32(),
                    row.GetProperty("failureMessageId").GetInt32())).ToArray(),
            source.GetProperty("fumbleRows").EnumerateArray()
                .Select(row => (IReadOnlyList<int>)row.EnumerateArray()
                    .Select(value => value.GetInt32()).ToArray()).ToArray());
        result.Validate();
        return result;
    }

    internal void Validate()
    {
        if (Schema != ExpectedSchema || string.IsNullOrWhiteSpace(ExactBuild) ||
            MinimumPercentRoll <= 0 || MaximumPercentRoll < MinimumPercentRoll ||
            CriticalUpgradeEnabledAfterDays < 0 || CriticalUpgradeMarginDivisor <= 0 ||
            StatCheckMinimumRoll <= 0 || StatCheckMaximumRoll < StatCheckMinimumRoll ||
            HitLocationCount <= 0 || FumbleTypeCount <= 0 || LuckSpecialIndex < 0 ||
            NeutralLuck <= 0 || FumblePercentPerLuckPoint <= 0 ||
            PlayerFumbleImmunityDays < 0 || TicksPerDay <= 0 ||
            !Ascending(CriticalScoreThresholds) || !Ascending(FumbleScoreThresholds) ||
            CriticalScoreThresholds[^1] != MaximumPercentRoll ||
            FumbleScoreThresholds[^1] >= MaximumPercentRoll ||
            DamageFlags.Count == 0 || DamageFlags.Values.Any(value =>
                value <= 0 || (value & (value - 1)) != 0) ||
            DamageFlags.Values.Distinct().Count() != DamageFlags.Count ||
            CriticalRows.Count == 0 || CriticalRows.Any(row =>
                string.IsNullOrWhiteSpace(row.TargetKind) || row.HitLocation < 0 ||
                row.HitLocation >= HitLocationCount || row.Severity < 0 ||
                row.Severity >= CriticalScoreThresholds.Count + 1 ||
                row.DamageMultiplier <= 0 || row.Stat < -1 ||
                row.SuccessMessageId <= 0 || row.FailureMessageId <= 0 ||
                !KnownFlags(row.DamageFlags | row.FailedStatDamageFlags)) ||
            CriticalRows.Select(row => (row.TargetKind, row.HitLocation, row.Severity))
                .Distinct().Count() != CriticalRows.Count ||
            FumbleRows.Count != FumbleTypeCount || FumbleRows.Any(row =>
                row.Count != FumbleScoreThresholds.Count + 1 ||
                row.Any(flags => !KnownFlags(flags))))
            throw new InvalidOperationException(
                "Classic critical-selection contract is invalid.");
    }

    internal IReadOnlySet<string> DecodeFlags(int flags)
    {
        if (!KnownFlags(flags))
            throw new InvalidOperationException("Classic damage flags are unsupported.");
        return DamageFlags.Where(row => (flags & row.Value) != 0)
            .Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
    }

    private bool KnownFlags(int flags)
    {
        var known = DamageFlags.Values.Aggregate(0, (value, flag) => value | flag);
        return flags >= 0 && (flags & ~known) == 0;
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
internal sealed record ClassicCriticalEffectRow(
    string TargetKind,
    int HitLocation,
    int Severity,
    int DamageMultiplier,
    int DamageFlags,
    int Stat,
    int StatModifier,
    int FailedStatDamageFlags,
    int SuccessMessageId,
    int FailureMessageId);
internal sealed record ClassicCriticalEffect(
    int DamageMultiplier,
    IReadOnlySet<string> DamageFlags,
    int MessageId);
internal sealed record ClassicFumbleEffect(IReadOnlySet<string> DamageFlags);
internal sealed record ClassicCriticalStatCheckResult(
    ClassicRetailRandomState RandomState,
    int Roll,
    int Margin,
    bool Succeeded);

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

    internal static ClassicCriticalEffect ResolveCriticalEffect(
        ClassicCriticalSelectionContract contract,
        string targetKind,
        ClassicCriticalSelection selection,
        bool? statCheckSucceeded)
    {
        var row = SelectCriticalEffectRow(contract, targetKind, selection);
        if (row.Stat == -1)
        {
            if (statCheckSucceeded is not null)
                throw new InvalidOperationException(
                    "Classic critical effect has no source stat check.");
            return new ClassicCriticalEffect(
                row.DamageMultiplier,
                contract.DecodeFlags(row.DamageFlags),
                row.SuccessMessageId);
        }
        if (statCheckSucceeded is null)
            throw new InvalidOperationException(
                "Classic critical effect requires an explicit source stat-check result.");
        var failed = !statCheckSucceeded.Value;
        return new ClassicCriticalEffect(
            row.DamageMultiplier,
            contract.DecodeFlags(row.DamageFlags |
                (failed ? row.FailedStatDamageFlags : 0)),
            failed ? row.FailureMessageId : row.SuccessMessageId);
    }

    internal static ClassicCriticalStatCheckResult RollStatCheck(
        ClassicCriticalSelectionContract criticalContract,
        ClassicRetailRandomContract randomContract,
        ClassicRetailRandomState randomState,
        ClassicCriticalEffectRow row,
        int targetStat)
    {
        criticalContract.Validate();
        randomContract.Validate();
        if (criticalContract.ExactBuild != randomContract.ExactBuild ||
            row.Stat < 0 || targetStat < 0)
            throw new InvalidOperationException(
                "Classic critical stat-check inputs are invalid.");
        var result = ClassicRetailRandom.Next(
            randomState,
            criticalContract.StatCheckMinimumRoll,
            criticalContract.StatCheckMaximumRoll,
            randomContract);
        var margin = checked(targetStat + row.StatModifier - result.Value);
        return new ClassicCriticalStatCheckResult(
            result.State,
            result.Value,
            margin,
            margin >= 0);
    }

    internal static ClassicCriticalEffectRow SelectCriticalEffectRow(
        ClassicCriticalSelectionContract contract,
        string targetKind,
        ClassicCriticalSelection selection)
    {
        contract.Validate();
        return contract.CriticalRows.SingleOrDefault(candidate =>
            candidate.TargetKind == targetKind &&
            candidate.HitLocation == selection.HitLocation &&
            candidate.Severity == selection.Severity) ??
            throw new InvalidOperationException(
                "Classic critical effect row is outside the admitted exact-build subset.");
    }

    internal static ClassicFumbleEffect ResolveFumbleEffect(
        ClassicCriticalSelectionContract contract,
        ClassicFumbleSelection selection)
    {
        contract.Validate();
        if (selection.FumbleType < 0 || selection.FumbleType >= contract.FumbleRows.Count ||
            selection.Severity < 0 ||
            selection.Severity >= contract.FumbleRows[selection.FumbleType].Count)
            throw new InvalidOperationException("Classic fumble effect row is unsupported.");
        return new ClassicFumbleEffect(contract.DecodeFlags(
            contract.FumbleRows[selection.FumbleType][selection.Severity]));
    }

    private static int FirstThreshold(int score, IReadOnlyList<int> thresholds)
    {
        for (var index = 0; index < thresholds.Count; index++)
            if (score <= thresholds[index])
                return index;
        return thresholds.Count;
    }
}
