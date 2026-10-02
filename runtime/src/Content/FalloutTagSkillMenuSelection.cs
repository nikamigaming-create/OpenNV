using System.Globalization;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutTagSkillMenuChoice(FalloutNativeSkillIdentity Skill, string Description, string? Icon);

// The menu owns a draft. Only the shared acceptance owner changes player tags.
internal sealed class FalloutTagSkillMenuSelection
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutNativeTagSkillContract _contract;
    private readonly HashSet<FalloutNativeSkillIdentity> _initial;
    private readonly List<FalloutNativeSkillIdentity> _selected;
    private readonly Func<FalloutNativeSkillIdentity, float> _liveValue;
    internal IReadOnlyList<FalloutTagSkillMenuChoice> Choices { get; }
    internal IReadOnlyList<FalloutNativeSkillIdentity> Selected => _selected.ToArray();
    internal int Required => _contract.RequiredCount;
    internal int Remaining => Required - _selected.Count;
    internal bool Complete => Remaining == 0;

    internal FalloutTagSkillMenuSelection(FalloutPluginStack records, FalloutNativeTagSkillContract contract,
        IReadOnlyList<FalloutNativeSkillIdentity> current, Func<FalloutNativeSkillIdentity, float> liveValue)
    {
        FalloutNativeTagSkillResolver.Validate(contract, current, allowUnspent: true);
        if (contract.RequiredCount <= 0 || contract.RequiredCount > contract.Skills.Count ||
            contract.Skills.Select(skill => skill.RuntimeFormId).Distinct().Count() != contract.Skills.Count)
            throw new InvalidDataException("Tag menu count or skill identities are invalid.");
        _records = records; _contract = contract; _initial = [.. current]; _selected = [.. current]; _liveValue = liveValue;
        Choices = contract.Skills.Select(skill =>
        {
            var record = records.GetEffective(records.RuntimeFormKey(skill.RuntimeFormId));
            if (record.Signature != "AVIF") throw new InvalidDataException("Tag menu choice is not an AVIF.");
            var fields = record.ReadSubrecords().ToArray();
            string? Text(string signature)
            {
                var matches = fields.Where(field => field.Signature == signature).ToArray();
                if (matches.Length > 1) throw new InvalidDataException("Tag menu source field is duplicated: " + signature);
                return matches.Length == 0 ? null : FalloutDialogueTopic.Text(matches[0].Data.Span);
            }
            if (Text("EDID") != skill.EditorId || Text("FULL") != skill.DisplayName)
                throw new InvalidDataException("Tag menu identity differs from its winning AVIF.");
            return new FalloutTagSkillMenuChoice(skill, Text("DESC") ?? "", Text("ICON"));
        }).OrderBy(choice => choice.Skill.DisplayName, StringComparer.Ordinal).ToArray();
    }

    internal bool Toggle(FalloutNativeSkillIdentity skill)
    {
        RequireChoice(skill);
        if (_selected.Remove(skill)) return true;
        if (_selected.Count >= Required) return false;
        _selected.Add(skill); return true;
    }
    internal void Reset() => _selected.Clear();
    internal IReadOnlyList<FalloutNativeSkillIdentity> Submit()
    {
        var selected = _selected.OrderBy(skill => skill.RuntimeFormId).ToArray();
        FalloutNativeTagSkillResolver.Validate(_contract, selected);
        return selected;
    }
    internal int Value(FalloutNativeSkillIdentity skill)
    {
        RequireChoice(skill);
        var value = _liveValue(skill);
        var bonus = FalloutGameSettingFloats.Read(_records, "fAVDTagSkillBonus");
        if (!float.IsFinite(value) || !float.IsFinite(bonus)) throw new InvalidDataException("Tag menu skill value is non-finite.");
        value = MathF.Truncate(value) - (_initial.Contains(skill) ? bonus : 0) + (_selected.Contains(skill) ? bonus : 0);
        if (!float.IsFinite(value)) throw new InvalidDataException("Tag menu draft arithmetic is non-finite.");
        return (int)Math.Clamp(value, 0, 100);
    }
    private void RequireChoice(FalloutNativeSkillIdentity skill)
    {
        if (!Choices.Any(choice => choice.Skill == skill)) throw new InvalidDataException("Tag menu choice is outside its source contract.");
    }
    internal static string FormatCounts(string format, params int[] counts)
    {
        var output = new StringBuilder(); var index = 0;
        for (var at = 0; at < format.Length; at++)
        {
            if (format[at] != '%') { output.Append(format[at]); continue; }
            if (++at >= format.Length || format[at] != 'd' || index >= counts.Length)
                throw new NotSupportedException("Tag menu count string requires another source format owner.");
            output.Append(counts[index++].ToString(CultureInfo.InvariantCulture));
        }
        if (index != counts.Length) throw new NotSupportedException("Tag menu count string does not match its source arguments.");
        return output.ToString();
    }
}
