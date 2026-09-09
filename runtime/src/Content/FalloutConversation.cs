using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutConversationChoice(FalloutFormKey Topic, FalloutFormKey Info, string Text);

// The dialogue owner decides which authored responses and choices are active.
// Audio and menus report completion/input; they never choose the next INFO.
internal sealed class FalloutConversation(FalloutPluginStack records, FalloutQuestState quests,
    Func<FalloutCondition, float> evaluate, Action<FalloutDialogueInfo, bool> results, HashSet<FalloutFormKey>? saidInfos = null)
{
    private readonly Dictionary<FalloutFormKey, FalloutDialogueTopic> _topics = [];
    private readonly Dictionary<FalloutFormKey, (int Priority, IReadOnlyList<FalloutCondition> Conditions)> _questHeaders = [];
    private readonly HashSet<FalloutFormKey> _said = saidInfos ?? [], _added = [];
    private FalloutFormKey _speaker;
    private bool _mutating, _ending;
    internal FalloutDialogueInfo? Info { get; private set; }
    internal int ResponseIndex { get; private set; }
    internal IReadOnlyList<FalloutConversationChoice> Choices { get; private set; } = [];
    internal string Phase { get; private set; } = "closed";
    internal string? Error { get; private set; }
    internal FalloutDialogueResponse? Response => Phase == "speaking" ? Info!.Responses[ResponseIndex] : null;

    internal void Start(FalloutFormKey speakerBase, FalloutFormKey topic)
    {
        if (Phase != "closed" || Error is not null) throw new InvalidOperationException("Conversation owner is already active or faulted.");
        _speaker = speakerBase;
        Mutate(() =>
        {
            _ending = FalloutDialogueTopic.IsGoodbye(records, topic);
            Begin(Select(topic) ?? throw new NotSupportedException($"Topic {topic} has no eligible INFO."));
        });
    }

    internal void CompleteResponse()
    {
        if (Phase != "speaking") throw new InvalidOperationException("No dialogue response is playing.");
        Mutate(() =>
        {
            if (++ResponseIndex < Info!.Responses.Count) return;
            Phase = "results";
            results(Info, false);
            foreach (var topic in Info.AddedTopics) _added.Add(topic);
            if (_ending) { Phase = "closed"; Choices = []; return; }
            foreach (var follow in Info.FollowUps)
            {
                var next = FalloutDialogueTopic.Decode(records.GetEffective(follow));
                if (!FalloutDialogueTopic.Eligible(next, _speaker, _said, quest => quests.Stage(quest), evaluate, QuestEligible)) continue;
                FalloutDialogueTopic.RequireFlags(next, conversation: true);
                Begin(next);
                return;
            }
            if ((Info.Flags & 1) != 0) { Phase = "closed"; Choices = []; return; }
            // Explicit link order is authored in TCLT. PNAM priority orders the
            // discovered default list, not the author's explicit choice list.
            IEnumerable<FalloutFormKey> topics = Info.Choices.Count != 0 ? Info.Choices : FalloutDialogueTopic.DefaultTopics(records).Concat(_added)
                .Distinct().OrderByDescending(topic => FalloutDialogueTopic.Priority(records, topic));
            var choices = new List<FalloutConversationChoice>();
            foreach (var topic in topics.Distinct())
            {
                if (Select(topic) is not { } next) continue;
                var fields = Topic(topic).Topic.ReadSubrecords().Where(field => field.Signature == "FULL").ToArray();
                var text = next.Prompt ?? (fields.Length == 1 ? FalloutDialogueTopic.Text(fields[0].Data.Span) :
                    throw new InvalidDataException($"Topic {topic} has no unique prompt."));
                choices.Add(new(topic, next.Record.FormKey, text));
            }
            if (choices.Count == 0) throw new NotSupportedException("Conversation has no eligible authored topics or goodbye response.");
            Choices = choices; Phase = "choices";
        });
    }

    internal void Choose(FalloutFormKey topic)
    {
        if (Phase != "choices" || !Choices.Any(choice => choice.Topic == topic))
            throw new InvalidOperationException("Selected topic was not offered by the current conversation.");
        Mutate(() =>
        {
            _ending = FalloutDialogueTopic.IsGoodbye(records, topic);
            Begin(Select(topic) ?? throw new InvalidOperationException("Selected dialogue is no longer eligible."));
        });
    }

    private void Begin(FalloutDialogueInfo info)
    {
        Info = info; Choices = []; ResponseIndex = 0; Phase = "results";
        if ((info.Flags & 4) != 0) _said.Add(info.Record.FormKey);
        results(info, true);
        Phase = "speaking";
    }

    private FalloutDialogueTopic Topic(FalloutFormKey form)
    {
        if (!_topics.TryGetValue(form, out var topic)) _topics.Add(form, topic = FalloutDialogueTopic.Read(records, form));
        return topic;
    }

    private FalloutDialogueInfo? Select(FalloutFormKey topic) => Topic(topic).Select(_speaker, _said, quest => quests.Stage(quest), evaluate,
        QuestEligible,
        quest => Header(quest).Priority, conversation: !FalloutDialogueTopic.IsGoodbye(records, topic));

    private bool QuestEligible(FalloutFormKey quest) => quests.IsRunning(quest) &&
        FalloutCondition.AllPass(Header(quest).Conditions, evaluate, evaluateRunOn: true);

    private (int Priority, IReadOnlyList<FalloutCondition> Conditions) Header(FalloutFormKey quest)
    {
        if (_questHeaders.TryGetValue(quest, out var header)) return header;
        var record = records.GetEffective(quest);
        var fields = record.ReadSubrecords().TakeWhile(field => field.Signature is not ("INDX" or "QOBJ")).ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data;
        if (data.Length is not (2 or 8)) throw new InvalidDataException("Dialogue quest header extent is invalid.");
        header = (data.Span[1], fields.Where(field => field.Signature == "CTDA").Select(field => FalloutCondition.Read(record, field.Data.Span)).ToArray());
        _questHeaders.Add(quest, header);
        return header;
    }

    private void Mutate(Action action)
    {
        if (_mutating || Error is not null) throw new InvalidOperationException("Conversation cannot repeat or recursively execute a failed transition.");
        _mutating = true;
        try { action(); }
        catch (Exception error) { Error = error.Message; Phase = "failed"; throw; }
        finally { _mutating = false; }
    }
}
