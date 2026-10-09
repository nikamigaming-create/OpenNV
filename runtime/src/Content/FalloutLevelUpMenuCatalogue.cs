using System.Security.Cryptography;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutLevelUpSkillRow(int ActorValue, FalloutFormKey Form, string SourceSha256,
    string Name, string Description, string? Icon);

internal sealed class FalloutLevelUpMenuCatalogue
{
    internal IReadOnlyList<FalloutLevelUpSkillRow> Skills { get; }
    internal IReadOnlyList<FalloutLevelUpPerkSource> Perks { get; }
    private readonly FalloutPluginStack _records;
    private readonly Dictionary<FalloutFormKey, FalloutPluginRecord> _owners = [];

    internal FalloutLevelUpMenuCatalogue(FalloutPluginStack records, Func<FalloutNativeSkillIdentity, int> slot)
    {
        _records = records;
        Skills = FalloutNativeTagSkillResolver.ResolveSkills(records).Select(identity =>
        {
            var source = records.GetEffective(records.RuntimeFormKey(identity.RuntimeFormId));
            var fields = source.ReadSubrecords().ToArray();
            string? Text(string signature, bool required = false)
            {
                var matches = fields.Where(field => field.Signature == signature).ToArray();
                if (matches.Length == 0 && !required) return null;
                if (matches.Length != 1) throw new InvalidDataException("Level-up skill text is absent or repeated: " + signature);
                var text = FalloutDialogueTopic.Text(matches[0].Data.Span);
                if (required && string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Level-up skill name is empty.");
                return text;
            }
            if (Text("EDID", true) != identity.EditorId || Text("FULL", true) != identity.DisplayName)
                throw new InvalidDataException("Level-up skill differs from its winning AVIF identity.");
            return new FalloutLevelUpSkillRow(slot(identity), source.FormKey,
                Convert.ToHexString(SHA256.HashData(source.ReadData())).ToLowerInvariant(), identity.DisplayName,
                Text("DESC") ?? "", Text("ICON"));
        }).OrderBy(row => row.Name, StringComparer.Ordinal).ToArray();
        if (Skills.Count == 0 || Skills.Select(row => row.ActorValue).Distinct().Count() != Skills.Count)
            throw new InvalidDataException("Level-up skill rows have no unique selected engine slots.");

        Perks = records.EffectiveRecords("PERK").Where(record => !record.IsDeleted)
            .Where(record =>
            {
                var declaration = FalloutPerkDeclaration.Read(record);
                return !declaration.Trait && declaration.Playable && !declaration.Hidden && declaration.Ranks != 0;
            }).Select(record =>
            {
                _owners.Add(record.FormKey, record);
                return FalloutLevelUpPerkSource.Read(records, record.FormKey);
            })
            .OrderBy(perk => perk.Name, StringComparer.Ordinal).ToArray();
        if (Perks.Any(perk => string.IsNullOrWhiteSpace(perk.Name)))
            throw new InvalidDataException("A visible level-up PERK has no winning source name.");
    }

    internal IReadOnlyList<FalloutLevelUpPerkChoice> Choices => Perks.Select(perk =>
        new FalloutLevelUpPerkChoice(perk.Form, perk.SourceSha256, perk.MaximumRank)).ToArray();

    internal bool Eligible(FalloutFormKey form, int gainedLevel, Func<FalloutFormKey, int> rank,
        Func<FalloutCondition, float> evaluate)
    {
        var perk = Perks.SingleOrDefault(perk => perk.Form == form) ??
            throw new InvalidDataException("Level-up eligibility does not own this source PERK.");
        // Winning plugin records are immutable in this stack. Retain their
        // exact owners/decoded metadata instead of hashing each row every frame.
        if (!ReferenceEquals(_records.GetEffective(form), _owners[form]))
            throw new InvalidDataException("Level-up eligibility source changed while its request was active.");
        var acquired = rank(form);
        if (gainedLevel < 2 || acquired < 0 || acquired > perk.MaximumRank)
            throw new InvalidDataException("Level-up eligibility has an invalid level or acquired rank.");
        return gainedLevel >= perk.MinimumLevel && acquired < perk.MaximumRank &&
            FalloutCondition.AllPass(perk.Conditions, evaluate, evaluateRunOn: true);
    }

    internal void RequireSession(FalloutLevelUpMenuSession session)
    {
        if (!session.Skills.SequenceEqual(Skills.Select(row => row.ActorValue)) ||
            !session.Perks.SequenceEqual(Choices))
            throw new InvalidDataException("Native level-up rows differ from the authoritative source menu request.");
    }
}
