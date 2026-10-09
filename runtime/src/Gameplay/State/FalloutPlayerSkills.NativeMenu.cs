using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerSkills
{
    internal int NativeSkillSlot(FalloutNativeSkillIdentity skill) => RequireSkill(SkillName(_records, skill));
}
