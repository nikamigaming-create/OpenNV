using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal enum ClassicSkillDifficulty
{
    Easy,
    Normal,
    Hard,
}

internal sealed record ClassicSkillRule(
    string Id,
    int BaseValue,
    int SpecialMultiplier,
    IReadOnlyList<int> SpecialIndices,
    int SourceBonusMultiplier,
    bool DifficultyAdjusted);

internal sealed record ClassicSkillContract(
    string Schema,
    string ExactBuild,
    int SpecialCount,
    int MinimumSpecial,
    int MaximumSpecial,
    int TaggedFlatBonus,
    int MaximumSkill,
    int EasyAdjustment,
    int NormalAdjustment,
    int HardAdjustment,
    IReadOnlyList<ClassicSkillRule> Skills)
{
    internal const string ExpectedSchema = "opennv-classic-skill/v1";

    internal static ClassicSkillContract Parse(JsonElement source)
    {
        var difficulty = source.GetProperty("difficultyAdjustments");
        var result = new ClassicSkillContract(
            RequiredString(source, "schema"),
            RequiredString(source, "exactBuild"),
            source.GetProperty("specialCount").GetInt32(),
            source.GetProperty("minimumSpecial").GetInt32(),
            source.GetProperty("maximumSpecial").GetInt32(),
            source.GetProperty("taggedFlatBonus").GetInt32(),
            source.GetProperty("maximumSkill").GetInt32(),
            difficulty.GetProperty("easy").GetInt32(),
            difficulty.GetProperty("normal").GetInt32(),
            difficulty.GetProperty("hard").GetInt32(),
            source.GetProperty("skills").EnumerateArray().Select(row =>
                new ClassicSkillRule(
                    RequiredString(row, "id"),
                    row.GetProperty("baseValue").GetInt32(),
                    row.GetProperty("specialMultiplier").GetInt32(),
                    row.GetProperty("specialIndices").EnumerateArray()
                        .Select(value => value.GetInt32()).ToArray(),
                    row.GetProperty("sourceBonusMultiplier").GetInt32(),
                    row.GetProperty("difficultyAdjusted").GetBoolean())).ToArray());
        result.Validate();
        return result;
    }

    internal void Validate()
    {
        if (Schema != ExpectedSchema || string.IsNullOrWhiteSpace(ExactBuild) ||
            SpecialCount <= 0 || MinimumSpecial <= 0 || MaximumSpecial < MinimumSpecial ||
            TaggedFlatBonus < 0 || MaximumSkill <= 0 || Skills.Count == 0 ||
            Skills.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() !=
                Skills.Count ||
            Skills.Any(rule =>
                string.IsNullOrWhiteSpace(rule.Id) || rule.BaseValue < 0 ||
                rule.SpecialMultiplier < 0 || rule.SourceBonusMultiplier <= 0 ||
                rule.SpecialIndices.Count == 0 ||
                rule.SpecialIndices.Any(index => index < 0 || index >= SpecialCount)))
            throw new InvalidOperationException("Classic skill contract is invalid.");
    }

    internal int DifficultyAdjustment(ClassicSkillDifficulty difficulty) => difficulty switch
    {
        ClassicSkillDifficulty.Easy => EasyAdjustment,
        ClassicSkillDifficulty.Normal => NormalAdjustment,
        ClassicSkillDifficulty.Hard => HardAdjustment,
        _ => throw new InvalidOperationException("Classic skill difficulty is unsupported."),
    };

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Classic skill contract string is empty: {property}");
    }
}

internal sealed record ClassicSkillInputs(
    IReadOnlyList<int> Special,
    int SourceBonus,
    bool Tagged,
    int? TraitAdjustment,
    int? PerkAdjustment,
    ClassicSkillDifficulty Difficulty);

internal static class ClassicSkillOwner
{
    internal static int Resolve(
        ClassicSkillContract contract,
        string skillId,
        ClassicSkillInputs inputs)
    {
        contract.Validate();
        if (inputs.Special.Count != contract.SpecialCount ||
            inputs.Special.Any(value =>
                value < contract.MinimumSpecial || value > contract.MaximumSpecial) ||
            inputs.SourceBonus < 0 || inputs.TraitAdjustment is null ||
            inputs.PerkAdjustment is null)
            throw new InvalidOperationException("Classic skill inputs are invalid.");
        var rule = contract.Skills.SingleOrDefault(
            candidate => candidate.Id == skillId) ??
            throw new InvalidOperationException($"Classic skill is unsupported: {skillId}");
        var sourceBonus = rule.SourceBonusMultiplier * inputs.SourceBonus;
        var value = rule.BaseValue +
            rule.SpecialMultiplier * rule.SpecialIndices.Sum(index => inputs.Special[index]) +
            sourceBonus +
            (inputs.Tagged ? sourceBonus + contract.TaggedFlatBonus : 0) +
            inputs.TraitAdjustment.Value +
            inputs.PerkAdjustment.Value +
            (rule.DifficultyAdjusted
                ? contract.DifficultyAdjustment(inputs.Difficulty)
                : 0);
        return Math.Min(value, contract.MaximumSkill);
    }
}
