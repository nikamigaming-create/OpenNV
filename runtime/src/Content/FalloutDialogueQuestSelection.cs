namespace OpenNV.Runtime.Content;

// Quest-level conditions apply to both scripted speech and player dialogue.
// Only immutable headers are cached; every selection reads the live quest and
// evaluates conditions in that request's speaker/listener context.
internal sealed class FalloutDialogueQuestSelection(FalloutPluginStack records, FalloutQuestState quests)
{
    private readonly Dictionary<FalloutFormKey, (int Priority, IReadOnlyList<FalloutCondition> Conditions)> _headers = [];

    internal bool Eligible(FalloutFormKey quest, Func<FalloutCondition, float> evaluate) => quests.IsRunning(quest) &&
        FalloutCondition.AllPass(Header(quest).Conditions, evaluate, evaluateRunOn: true);

    internal int Priority(FalloutFormKey quest) => Header(quest).Priority;

    internal FalloutDialogueInfo? Select(FalloutDialogueTopic topic, FalloutFormKey speakerBase,
        IReadOnlySet<FalloutFormKey> said, Func<FalloutFormKey, float> stage, Func<FalloutCondition, float> evaluate,
        Func<uint, uint>? random = null) => topic.Select(speakerBase, said, stage, evaluate,
            quest => Eligible(quest, evaluate), Priority,
            conversation: FalloutDialogueTopic.Type(records, topic.Topic.FormKey) == 0, random: random);

    private (int Priority, IReadOnlyList<FalloutCondition> Conditions) Header(FalloutFormKey quest)
    {
        if (_headers.TryGetValue(quest, out var header)) return header;
        var record = records.GetEffective(quest);
        if (record.Signature != "QUST") throw new InvalidDataException("Dialogue quest is not QUST.");
        var fields = record.ReadSubrecords().TakeWhile(field => field.Signature is not ("INDX" or "QOBJ")).ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data;
        if (data.Length is not (2 or 8)) throw new InvalidDataException("Dialogue quest header extent is invalid.");
        header = (data.Span[1], fields.Where(field => field.Signature == "CTDA").Select(field => FalloutCondition.Read(record, field.Data.Span)).ToArray());
        _headers.Add(quest, header);
        return header;
    }
}
