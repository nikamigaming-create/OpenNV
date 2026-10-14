using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutConversationChoice(FalloutFormKey Topic, FalloutFormKey Info, string Text);

// The dialogue owner decides which authored responses and choices are active.
// Audio and menus report completion/input; they never choose the next INFO.
internal sealed partial class FalloutConversation(FalloutPluginStack records, FalloutQuestState quests,
    Func<FalloutCondition, float> evaluate, Action<FalloutDialogueInfo, bool> results, HashSet<FalloutFormKey>? saidInfos = null,
    Func<uint, uint>? random = null,
    Func<FalloutDialogueInfo, bool, FalloutScriptResultReceipt>? ownedResults = null,
    Func<FalloutFormKey>? resultCaller = null, Action? requireGameplayAdmission = null,
    FalloutGlobalState? globals = null)
{
    private readonly Dictionary<FalloutFormKey, FalloutDialogueTopic> _topics = [];
    private readonly FalloutDialogueQuestSelection _selection = new(records, quests, globals);
    private readonly HashSet<FalloutFormKey> _said = saidInfos ?? [], _added = [];
    private FalloutFormKey _speaker;
    private bool _mutating, _ending;
    internal ulong ResultOnlyCompletions { get; private set; }
    internal FalloutDialogueInfo? Info { get; private set; }
    internal int ResponseIndex { get; private set; }
    internal IReadOnlyList<FalloutConversationChoice> Choices { get; private set; } = [];
    internal string Phase { get; private set; } = "closed";
    internal string? Error { get; private set; }
    internal FalloutFormKey SpeakerBase => _speaker;
    internal IReadOnlyList<FalloutFormKey> AddedTopics => _added.ToArray();
    internal FalloutDialogueResponse? Response => Phase == "speaking" ? Info!.Responses[ResponseIndex] : null;
    internal bool IsGoodbye(FalloutFormKey topic) => FalloutDialogueTopic.IsGoodbye(records, topic);

    internal void Start(FalloutFormKey speakerBase, FalloutFormKey topic)
    {
        if (Phase != "closed" || Error is not null) throw new InvalidOperationException("Conversation owner is already active or faulted.");
        Mutate(() =>
        {
            _speaker = speakerBase;
            _ending = FalloutDialogueTopic.IsGoodbye(records, topic);
            Begin(Select(topic) ?? throw new NotSupportedException($"Topic {topic} has no eligible INFO."));
        });
    }

    internal void CompleteResponse()
    {
        if (Phase != "speaking") throw new InvalidOperationException("No dialogue response is playing.");
        Mutate(() =>
        {
            RetireResponsePublication();
            if (++ResponseIndex < Info!.Responses.Count) { PublishResponse(); return; }
            var next = FinishInfo();
            if (next is not null) Begin(next);
        });
    }

    // A silent link cycle yields to the owner instead of recursing or imposing
    // an arbitrary limit on authored effects. The same C# step works headless.
    internal void ContinueResultOnly()
    {
        if (Phase != "continuing" || Info is not { Responses.Count: 0 })
            throw new InvalidOperationException("Conversation has no pending result-only link continuation.");
        Mutate(() =>
        {
            var next = FollowUpOrChoices();
            if (next is not null) Begin(next);
        });
    }

    private FalloutDialogueInfo? FinishInfo()
    {
        var info = Info ?? throw new InvalidOperationException("Conversation lost its entered INFO.");
        Phase = "results";
        if ((info.Flags & 8) == 0) ExecuteConversationResult(info, false);
        requireGameplayAdmission?.Invoke();
        if (info.Responses.Count == 0) ResultOnlyCompletions = checked(ResultOnlyCompletions + 1);
        foreach (var topic in info.AddedTopics) _added.Add(topic);
        return FollowUpOrChoices();
    }

    private FalloutDialogueInfo? FollowUpOrChoices()
    {
        var info = Info ?? throw new InvalidOperationException("Conversation lost its completed INFO.");
        if (_ending) { Phase = "closed"; Choices = []; return null; }
        foreach (var follow in info.FollowUps)
        {
            var next = FalloutDialogueTopic.Decode(records.GetEffective(follow));
            if (!FalloutDialogueTopic.Eligible(next, _speaker, _said, quest => quests.Stage(quest), evaluate, QuestEligible, globals: globals)) continue;
            FalloutDialogueTopic.RequireFlags(next, conversation: true, immediateResults: true, allowResultOnly: true);
            return next;
        }
        if ((info.Flags & 1) != 0) { Phase = "closed"; Choices = []; return null; }
        // Explicit link order is authored in TCLT. PNAM priority orders the
        // discovered default list, not the author's explicit choice list.
        IEnumerable<FalloutFormKey> topics = info.Choices.Count != 0 ? info.Choices : FalloutDialogueTopic.DefaultTopics(records).Concat(_added)
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
        return null;
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
        var silent = new HashSet<FalloutFormKey>();
        while (true)
        {
            requireGameplayAdmission?.Invoke();
            if (info.Responses.Count == 0 && !silent.Add(info.Record.FormKey))
            { Phase = "continuing"; return; }
            Info = info; Choices = []; ResponseIndex = 0; Phase = "results";
            _beginReceipt = _endReceipt = null; BeginResultsEntered = EndResultsEntered = false;
            if ((info.Flags & 4) != 0) _said.Add(info.Record.FormKey);
            ExecuteConversationResult(info, true);
            if ((info.Flags & 8) != 0) ExecuteConversationResult(info, false);
            if (info.Responses.Count != 0) { Phase = "speaking"; PublishResponse(); return; }
            var next = FinishInfo();
            if (next is null) return;
            info = next;
        }
    }

    private FalloutDialogueTopic Topic(FalloutFormKey form)
    {
        if (!_topics.TryGetValue(form, out var topic)) _topics.Add(form, topic = FalloutDialogueTopic.Read(records, form));
        return topic;
    }

    private FalloutDialogueInfo? Select(FalloutFormKey topic) => _selection.Select(Topic(topic), _speaker, _said,
        quest => quests.Stage(quest), evaluate, random, immediateResults: true, allowResultOnly: true);

    private bool QuestEligible(FalloutFormKey quest) => _selection.Eligible(quest, evaluate);

    private void Mutate(Action action)
    {
        if (_mutating || Error is not null) throw new InvalidOperationException("Conversation cannot repeat or recursively execute a failed transition.");
        _mutating = true;
        // Immediate INFO results can leave no later script callback. Admit
        // the transition before speaker, response, topic or SayOnce stores.
        try { requireGameplayAdmission?.Invoke(); action(); }
        catch (Exception error) { Error = error.Message; Phase = "failed"; throw; }
        finally { _mutating = false; }
    }
}
