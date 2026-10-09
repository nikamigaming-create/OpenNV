using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutLevelUpPerkState(FalloutFormKey Form, int Rank, int MaximumRank, bool Enabled, bool Selected);

internal sealed partial class FalloutLevelUpMenuSession
{
    internal ulong Generation => _generation;
    internal FalloutPlayerAdvancementSource Source => _source;
    internal int DisplayedSkill(int skill)
    {
        RequireSkillRow(skill);
        var value = _binding.DisplayedSkill(skill);
        if (value is < 0 or > 100) throw new InvalidDataException("Level-up displayed skill is outside its admitted range.");
        return value;
    }
    internal float UnmodifiedSkill(int skill)
    {
        RequireSkillRow(skill);
        var value = _binding.ReadUnmodifiedSkill(skill);
        return float.IsFinite(value) ? value : throw new InvalidDataException("Level-up unmodified skill is non-finite.");
    }
    internal bool CanChangeSkill(int skill, int direction)
    {
        RequireSkillRow(skill);
        if (direction is not (-1 or 1)) throw new InvalidDataException("Level-up arrow has no source direction.");
        return Error is null && Page == FalloutLevelUpPage.Skills && (direction > 0
            ? Assigned < Budget && DisplayedSkill(skill) < 100
            : _deltas.GetValueOrDefault(skill) > 0 && DisplayedSkill(skill) > 0);
    }
    internal FalloutLevelUpPerkState PerkState(FalloutFormKey form)
    {
        var choice = _binding.Perks.SingleOrDefault(choice => choice.Form == form) ??
            throw new InvalidDataException("Level-up perk query is outside its source rows.");
        var rank = _binding.PerkRank(form);
        if (rank < 0 || rank > choice.MaximumRank) throw new InvalidDataException("Level-up acquired perk rank differs from its declaration.");
        return new(form, rank, choice.MaximumRank,
            Error is null && rank < choice.MaximumRank && _binding.PerkEnabled(form), _selected?.Form == form);
    }
    private void RequireSkillRow(int skill)
    {
        if (!_binding.SkillOrder.Contains(skill)) throw new InvalidDataException("Level-up skill query is outside its source rows.");
    }
}
