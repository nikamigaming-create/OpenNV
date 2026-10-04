using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerTagSkillsSnapshot(IReadOnlyList<FalloutNativeSkillIdentity?> Slots);

// Indexed tags are player state. The creation menu's required selection count
// does not constrain script writes to the engine's four slots.
internal sealed class FalloutPlayerTagSkills
{
    internal const int SlotCount = 4;
    private readonly FalloutNativeTagSkillContract _contract;
    private readonly Dictionary<string, FalloutNativeSkillIdentity> _skills;
    private readonly FalloutNativeSkillIdentity?[] _slots;

    internal FalloutPlayerTagSkills(FalloutPluginStack records, FalloutNativeTagSkillContract contract,
        FalloutPlayerTagSkillsSnapshot? snapshot = null, IReadOnlyList<FalloutNativeSkillIdentity>? legacy = null)
    {
        _contract = contract;
        _skills = contract.Skills.ToDictionary(skill => FalloutPlayerSkills.SkillName(records, skill), StringComparer.OrdinalIgnoreCase);
        snapshot ??= FromLegacy(legacy ?? []);
        Validate(snapshot);
        if (snapshot.Slots.Any(skill => skill is not null && !contract.Skills.Contains(skill)))
            throw new InvalidDataException("Player tag slot differs from its winning AVIF identity.");
        _slots = snapshot.Slots.ToArray();
    }

    internal IReadOnlyList<FalloutNativeSkillIdentity> Selection => _slots.OfType<FalloutNativeSkillIdentity>()
        .DistinctBy(skill => skill.RuntimeFormId).ToArray();

    internal bool IsTagged(string name) => _skills.TryGetValue(SkillName(name), out var skill) && _slots.Contains(skill);

    internal void Set(string name, double index)
    {
        if (!double.IsFinite(index) || index != Math.Truncate(index) || index is < 0 or >= SlotCount)
            throw new NotSupportedException("SetPlayerTagSkill requires an integer slot from zero through three.");
        if (!_skills.TryGetValue(SkillName(name), out var skill))
            throw new NotSupportedException($"SetPlayerTagSkill actor value {name} has no winning skill AVIF owner.");
        _slots[(int)index] = skill;
    }

    internal void AcceptMenu(IReadOnlyList<FalloutNativeSkillIdentity> selection)
    {
        FalloutNativeTagSkillResolver.Validate(_contract, selection);
        var snapshot = FromLegacy(selection);
        Array.Copy(snapshot.Slots.ToArray(), _slots, SlotCount);
    }

    internal FalloutPlayerTagSkillsSnapshot Capture() => new(_slots.ToArray());

    internal static FalloutPlayerTagSkillsSnapshot FromLegacy(IReadOnlyList<FalloutNativeSkillIdentity> selection)
    {
        if (selection.Count > SlotCount || selection.Any(skill => skill is null) ||
            selection.Select(skill => skill.RuntimeFormId).Distinct().Count() != selection.Count)
            throw new InvalidDataException("Legacy player tags cannot be represented as four distinct indexed slots.");
        var slots = new FalloutNativeSkillIdentity?[SlotCount];
        for (var index = 0; index < selection.Count; ++index) slots[index] = selection[index];
        return new(slots);
    }

    internal static void Validate(FalloutPlayerTagSkillsSnapshot snapshot, IReadOnlyList<FalloutNativeSkillIdentity>? selection = null)
    {
        if (snapshot.Slots is null || snapshot.Slots.Count != SlotCount || snapshot.Slots.Any(skill => skill is not null &&
                (skill.RuntimeFormId == 0 || string.IsNullOrWhiteSpace(skill.EditorId) || string.IsNullOrWhiteSpace(skill.DisplayName))))
            throw new InvalidDataException("Saved player tag skills require four valid indexed slots.");
        if (selection is not null && !snapshot.Slots.OfType<FalloutNativeSkillIdentity>().DistinctBy(skill => skill.RuntimeFormId)
                .OrderBy(skill => skill.RuntimeFormId).SequenceEqual(selection.OrderBy(skill => skill.RuntimeFormId)))
            throw new InvalidDataException("Saved indexed player tags differ from their skill membership projection.");
    }

    private static string SkillName(string name)
    {
        if (name.Length >= 2 && name[0] == '"' && name[^1] == '"') name = name[1..^1];
        return name.Equals("SmallGuns", StringComparison.OrdinalIgnoreCase) ? "Guns" : name;
    }
}
