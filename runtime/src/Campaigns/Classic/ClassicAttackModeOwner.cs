using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicAttackModeContract(
    string Id,
    int HitMode,
    int SkillIndex,
    int MinimumDamage,
    string MaximumDamageDerivedStat,
    int MaximumDamageBonus,
    int MaximumRangeHexes,
    int ActionPointCost,
    int AnimationCode,
    int DamageType,
    int CriticalFailureType,
    int AmmunitionPerAttack)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || HitMode < 0 || SkillIndex < 0 ||
            MinimumDamage < 0 || string.IsNullOrWhiteSpace(MaximumDamageDerivedStat) ||
            MaximumDamageBonus < 0 || MaximumRangeHexes <= 0 ||
            ActionPointCost <= 0 || AnimationCode < 0 || DamageType < 0 ||
            CriticalFailureType < 0 || AmmunitionPerAttack < 0)
            throw new InvalidOperationException("Classic attack-mode contract is invalid.");
    }
}

internal sealed record ClassicAttackModeCatalog(
    string Schema,
    string ExactBuild,
    IReadOnlyList<ClassicAttackModeContract> Modes)
{
    internal const string ExpectedSchema = "opennv-classic-attack-modes/v1";

    internal static ClassicAttackModeCatalog Parse(JsonElement source)
    {
        var result = new ClassicAttackModeCatalog(
            RequiredString(source, "schema"),
            RequiredString(source, "exactBuild"),
            source.GetProperty("modes").EnumerateArray().Select(row =>
                new ClassicAttackModeContract(
                    RequiredString(row, "id"),
                    row.GetProperty("hitMode").GetInt32(),
                    row.GetProperty("skillIndex").GetInt32(),
                    row.GetProperty("minimumDamage").GetInt32(),
                    RequiredString(row, "maximumDamageDerivedStat"),
                    row.GetProperty("maximumDamageBonus").GetInt32(),
                    row.GetProperty("maximumRangeHexes").GetInt32(),
                    row.GetProperty("actionPointCost").GetInt32(),
                    row.GetProperty("animationCode").GetInt32(),
                    row.GetProperty("damageType").GetInt32(),
                    row.GetProperty("criticalFailureType").GetInt32(),
                    row.GetProperty("ammunitionPerAttack").GetInt32())).ToArray());
        result.Validate();
        return result;
    }

    internal void Validate()
    {
        if (Schema != ExpectedSchema || string.IsNullOrWhiteSpace(ExactBuild) ||
            Modes.Count == 0 || Modes.Select(mode => mode.Id).Distinct(StringComparer.Ordinal)
                .Count() != Modes.Count || Modes.Select(mode => mode.HitMode).Distinct().Count() !=
                Modes.Count)
            throw new InvalidOperationException("Classic attack-mode catalog is invalid.");
        foreach (var mode in Modes)
            mode.Validate();
    }

    internal ClassicAttackModeContract Require(string id) =>
        Modes.SingleOrDefault(mode => mode.Id == id) ??
        throw new InvalidOperationException("Classic attack mode is not admitted.");

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Classic attack-mode string is empty: {property}");
    }
}

internal static class ClassicAttackModeOwner
{
    internal static ClassicAttackSource PrepareSource(
        ClassicAttackModeContract mode,
        int derivedMaximumDamage)
    {
        mode.Validate();
        if (derivedMaximumDamage < 0)
            throw new InvalidOperationException(
                "Classic attack-mode derived damage is invalid.");
        return new ClassicAttackSource(
            mode.Id,
            mode.MinimumDamage,
            checked(derivedMaximumDamage + mode.MaximumDamageBonus),
            mode.DamageType,
            mode.MaximumRangeHexes,
            mode.ActionPointCost,
            mode.AnimationCode,
            ClassicAttackOwner.EngineRollRequired);
    }
}
