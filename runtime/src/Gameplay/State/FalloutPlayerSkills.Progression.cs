using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerSkills
{
    internal float ReadSkillBase(int skill) => ReadUnmodifiedSkill(skill);
    internal void WriteSkillBase(int skill, float value) => WriteUnmodifiedSkill(skill, value);
    internal bool IsTagged(int skill) => IsTaggedSkill(skill);

    internal int DisplayedSkill(int skill)
    {
        var current = ReadSkill(skill, FalloutActorValueRead.Current);
        if (!float.IsFinite(current)) throw new InvalidDataException("Level-up skill display is non-finite.");
        return (int)Math.Clamp(current, 0, 100);
    }

    internal static int SkillActorValue(string name)
    {
        if (name.Equals("BigGuns", StringComparison.OrdinalIgnoreCase)) return 33;
        if (name.Equals("SmallGuns", StringComparison.OrdinalIgnoreCase)) return 41;
        var skill = Skills.SingleOrDefault(skill => skill.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return skill.Name is not null ? skill.Value :
            throw new NotSupportedException($"Level-up skill {name} has no declared actor-value identity.");
    }

    // This is the number of ordinary arrow clicks the current rows can accept;
    // the source skill-point award itself is supplied by the advancement owner.
    internal int LevelUpSkillBudget(int requested, int taggedMultiplier)
    {
        if (requested < 0 || taggedMultiplier <= 0)
            throw new InvalidDataException("Level-up skill capacity has invalid inputs.");
        long capacity = 0;
        foreach (var skill in SkillOrder)
        {
            var step = IsTaggedSkill(skill) ? taggedMultiplier : 1;
            var available = 100 - DisplayedSkill(skill);
            capacity += (available + (long)step - 1) / step;
        }
        return (int)Math.Min(requested, capacity);
    }
}
