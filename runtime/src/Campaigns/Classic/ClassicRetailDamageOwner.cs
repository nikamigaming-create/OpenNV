using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicRetailDamageContract(
    string Schema,
    string ExactBuild,
    int DamageMultiplierDivisor,
    int PercentScale)
{
    internal const string ExpectedSchema = "opennv-classic-retail-damage/v1";

    internal static ClassicRetailDamageContract Parse(JsonElement source)
    {
        var result = new ClassicRetailDamageContract(
            source.GetProperty("schema").GetString() ?? "",
            source.GetProperty("exactBuild").GetString() ?? "",
            source.GetProperty("damageMultiplierDivisor").GetInt32(),
            source.GetProperty("percentScale").GetInt32());
        result.Validate();
        return result;
    }

    internal void Validate()
    {
        if (Schema != ExpectedSchema || string.IsNullOrWhiteSpace(ExactBuild) ||
            DamageMultiplierDivisor <= 0 || PercentScale <= 0)
            throw new InvalidOperationException(
                "Classic retail damage contract is invalid.");
    }
}

internal sealed record ClassicRetailDamageInputs(
    int RolledDamage,
    int OutcomeMultiplier,
    int AmmunitionMultiplier,
    int AmmunitionDivisor,
    int DifficultyPercent,
    int DamageThreshold,
    int DamageResistancePercent,
    int TargetHitPoints);

internal sealed record ClassicRetailDamageResult(
    int DamageApplied,
    int TargetHitPoints,
    bool TargetDefeated);

internal static class ClassicRetailDamageOwner
{
    internal static ClassicRetailDamageResult Resolve(
        ClassicRetailDamageContract contract,
        ClassicRetailDamageInputs inputs)
    {
        contract.Validate();
        if (inputs.RolledDamage < 0 || inputs.OutcomeMultiplier <= 0 ||
            inputs.AmmunitionMultiplier <= 0 || inputs.AmmunitionDivisor <= 0 ||
            inputs.DifficultyPercent < 0 || inputs.DamageThreshold < 0 ||
            inputs.DamageResistancePercent is < 0 ||
            inputs.DamageResistancePercent > contract.PercentScale ||
            inputs.TargetHitPoints <= 0)
            throw new InvalidOperationException(
                "Classic retail damage inputs are invalid.");

        var scaled = checked((long)inputs.RolledDamage * inputs.OutcomeMultiplier);
        scaled = checked(scaled * inputs.AmmunitionMultiplier) /
            inputs.AmmunitionDivisor;
        scaled /= contract.DamageMultiplierDivisor;
        scaled = checked(scaled * inputs.DifficultyPercent) / contract.PercentScale;
        var afterThreshold = Math.Max(0L, scaled - inputs.DamageThreshold);
        var resisted = checked(afterThreshold * inputs.DamageResistancePercent) /
            contract.PercentScale;
        var resolved = checked((int)Math.Max(0L, afterThreshold - resisted));
        var applied = Math.Min(inputs.TargetHitPoints, resolved);
        var remaining = inputs.TargetHitPoints - applied;
        return new ClassicRetailDamageResult(applied, remaining, remaining == 0);
    }
}
