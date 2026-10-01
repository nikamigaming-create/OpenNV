using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeSkillIdentity(
    uint RuntimeFormId,
    string EditorId,
    string DisplayName);

internal sealed record FalloutNativeTagSkillContract(
    IReadOnlyList<FalloutNativeSkillIdentity> Skills,
    int RequiredCount,
    short PsychStage,
    short PsychCompletedStage,
    short TagMenuStage);

internal static partial class FalloutNativeTagSkillResolver
{
    private const string QuestEditorId = "VCG01";
    private const short PsychStage = 80;
    private const short PsychCompletedStage = 85;
    private const short TagMenuStage = 90;
    private static readonly string[] SkillEditorIds =
    [
        "AVBarter", "AVEnergyWeapons", "AVExplosives", "AVLockpick", "AVMedicine",
        "AVMeleeWeapons", "AVRepair", "AVScience", "AVSmallGuns", "AVSneak", "AVSpeech", "AVThrowing", "AVUnarmed",
    ];
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FalloutPluginStack, Dictionary<uint, string>> Slots = new();

    // Built-in actor-value slots retain the base master's form identity. A later
    // override can rename the AVIF; FULL and ANAM are presentation strings.
    internal static string ActorValueName(FalloutPluginStack stack, FalloutNativeSkillIdentity skill) =>
        SlotBindings(stack).TryGetValue(skill.RuntimeFormId, out var name) ? name :
            throw new NotSupportedException("Skill AVIF has no built-in actor-value slot.");

    private static Dictionary<uint, string> SlotBindings(FalloutPluginStack stack) => Slots.GetValue(stack, records =>
    {
        var definitions = records.Plugins[0].Plugin.Records.Where(record => record.Signature == "AVIF")
            .Select(record => (Record: record, EditorId: ReadEditorId(record)))
            .Where(value => SkillEditorIds.Contains(value.EditorId, StringComparer.OrdinalIgnoreCase))
            .GroupBy(value => value.EditorId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(value => value.Record).ToArray(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<uint, string>();
        foreach (var id in SkillEditorIds)
        {
            if (!definitions.TryGetValue(id, out var matching) || matching.Length != 1)
                throw new InvalidDataException($"Base master actor-value slot {id} has no unique AVIF definition.");
            var name = id switch { "AVSmallGuns" => "Guns", "AVThrowing" => "Survival", _ => id[2..] };
            result.Add(records.RuntimeFormId(matching[0].FormKey), name);
        }
        return result;
    });

    internal static FalloutNativeTagSkillContract Resolve(
        FalloutPluginStack stack,
        FalloutOpeningControlGraph controls)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(controls);
        var tagSource = controls.Stage(QuestEditorId, TagMenuStage).Source;
        var commandMatches = SetTagSkills().Matches(tagSource).Cast<Match>().ToArray();
        if (commandMatches.Length != 1 ||
            !int.TryParse(commandMatches[0].Groups["count"].Value, out var requiredCount) ||
            commandMatches[0].Groups["mode"].Value != "1")
            throw new InvalidDataException(
                $"Native {QuestEditorId}:{TagMenuStage} SetTagSkills contract is unsupported.");

        _ = controls.Stage(QuestEditorId, PsychStage);
        _ = controls.Stage(QuestEditorId, PsychCompletedStage);
        var quest = controls.Quests[QuestEditorId].Values.First().Quest;
        var terminalResults = stack.EffectiveRecords("INFO").Where(record =>
        {
            var links = record.ReadSubrecords().Where(value => value.Signature == "QSTI").ToArray();
            if (links.Length != 1 || links[0].Data.Length != sizeof(uint) ||
                record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(links[0].Data.Span)) != quest)
                return false;
            return record.ReadSubrecords().Where(value => value.Signature == "SCTX")
                .Select(value => ReadAscii(record, value.Data.Span))
                .Any(source => SetStage().Matches(source).Cast<Match>().Any(match =>
                    match.Groups["quest"].Value.Equals(
                        QuestEditorId, StringComparison.OrdinalIgnoreCase) &&
                    match.Groups["stage"].Value == PsychCompletedStage.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)));
        }).ToArray();
        if (terminalResults.Length == 0)
            throw new InvalidDataException(
                $"Native psych-test INFO graph has no {QuestEditorId}:{PsychCompletedStage} terminal result.");

        var skills = new List<FalloutNativeSkillIdentity>();
        foreach (var slot in SlotBindings(stack))
        {
            var record = stack.GetEffective(stack.RuntimeFormKey(slot.Key));
            if (record.Signature != "AVIF") throw new InvalidDataException("Built-in skill winner is not AVIF.");
            var full = ReadText(record, "FULL");
            skills.Add(new FalloutNativeSkillIdentity(
                slot.Key, ReadEditorId(record), full));
        }
        if (requiredCount <= 0 || requiredCount >= skills.Count)
            throw new InvalidDataException("Native tag-skill selection count is unsupported.");
        return new FalloutNativeTagSkillContract(
            skills,
            requiredCount,
            PsychStage,
            PsychCompletedStage,
            TagMenuStage);
    }

    internal static void Validate(
        FalloutNativeTagSkillContract contract,
        IReadOnlyList<FalloutNativeSkillIdentity> selection, bool allowUnspent = false)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(selection);
        if ((allowUnspent ? selection.Count > contract.RequiredCount : selection.Count != contract.RequiredCount) ||
            selection.Select(value => value.RuntimeFormId).Distinct().Count() != selection.Count ||
            selection.Any(value => !contract.Skills.Contains(value)))
            throw new InvalidDataException(
                "Native campaign tag skills differ from the live SetTagSkills/AVIF contract.");
    }

    private static string ReadEditorId(FalloutPluginRecord record) => ReadText(record, "EDID");

    private static string ReadText(FalloutPluginRecord record, string signature)
    {
        var rows = record.ReadSubrecords().Where(value => value.Signature == signature).ToArray();
        if (rows.Length != 1)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} requires one {signature}; found {rows.Length}.");
        var bytes = rows[0].Data.Span;
        var end = bytes.IndexOf((byte)0);
        if (end != bytes.Length - 1 || end == 0 ||
            bytes[..end].IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new InvalidDataException(
                $"Native {record.Signature} {record.FormKey} {signature} is not null-terminated ASCII.");
        return Encoding.ASCII.GetString(bytes[..end]);
    }

    private static string ReadAscii(FalloutPluginRecord record, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new InvalidDataException($"Native INFO {record.FormKey} SCTX is not ASCII.");
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    [GeneratedRegex(@"\bSetTagSkills\s+(?<count>\d+)\s+(?<mode>\d+)\s*;?", RegexOptions.IgnoreCase)]
    private static partial Regex SetTagSkills();

    [GeneratedRegex(@"\bSetStage\s+(?<quest>[A-Za-z_][A-Za-z0-9_]*)\s+(?<stage>\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SetStage();
}
