using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// A transmitter selects INFO against its own reference, while ANAM identifies
// the remote actor supplying a particular line's voice. Neither is a proxy NPC.
internal sealed class FalloutRadioConversation(FalloutPluginStack records, FalloutReferenceWorld world,
    FalloutQuestState quests, Func<FalloutFormKey, FalloutCondition, float> evaluate,
    Func<FalloutFormKey, float> stage, IReadOnlySet<FalloutFormKey> said, Func<uint, uint>? random = null)
{
    private readonly FalloutDialogueQuestSelection _selection = new(records, quests);
    internal FalloutRadioStation? Station { get; private set; }
    internal FalloutFormKey? Topic { get; private set; }
    internal FalloutDialogueInfo? Info { get; private set; }
    internal long CompletedLines { get; private set; }
    internal bool Active => Info is not null;

    internal void Start(FalloutFormKey reference, FalloutFormKey? topic = null)
    {
        var station = FalloutRadioStation.Read(records, records.GetEffective(reference));
        var selectedTopic = topic ?? FalloutDialogueTopic.Find(records, "DIAL", "RadioHello").FormKey;
        RequireRadioTopic(selectedTopic);
        if (Active) throw new NotSupportedException("Interrupting an active radio conversation requires its result/cursor owner.");
        if (world.GetBroadcastState(reference))
            throw new NotSupportedException("Continuous radio generation requires its broadcast scheduler.");
        var info = world.IsEnabled(reference) ? Select(station, selectedTopic) : null;
        if (info is not null) _ = VoiceIdentity(station, info);
        Station = station; Topic = selectedTopic; Info = info;
    }

    // Called exactly once after the line's audio and source end results. Links
    // are re-evaluated against current quest variables, without restarting the
    // request or replaying any source result prefix.
    internal void CompleteLine()
    {
        var completed = Info ?? throw new InvalidOperationException("Radio has no current line to complete.");
        var station = Station ?? throw new InvalidOperationException("Radio lost its source station.");
        if ((completed.Flags & 1) != 0)
        {
            Info = null; ++CompletedLines;
            return;
        }
        IReadOnlyList<FalloutFormKey> links = completed.Choices.Count != 0 ? completed.Choices :
            [FalloutDialogueTopic.Find(records, "DIAL", "RadioGoodbye").FormKey];
        foreach (var link in links.OrderByDescending(link => FalloutDialogueTopic.Priority(records, link)))
        {
            RequireRadioTopic(link);
            var info = Select(station, link);
            if (info is null) continue;
            _ = VoiceIdentity(station, info);
            Topic = link; Info = info; ++CompletedLines;
            return;
        }
        throw new NotSupportedException($"Radio INFO {completed.Record.FormKey} has no eligible source continuation.");
    }

    internal FalloutDialogueSpeaker VoiceIdentity() => VoiceIdentity(
        Station ?? throw new InvalidOperationException("Radio station is absent."),
        Info ?? throw new InvalidOperationException("Radio INFO is absent."));

    private FalloutDialogueSpeaker VoiceIdentity(FalloutRadioStation station, FalloutDialogueInfo info) =>
        info.Speaker is { } actor ? FalloutDialogueSpeaker.Read(records, actor) : world.DialogueIdentity(station.Reference);

    private FalloutDialogueInfo? Select(FalloutRadioStation station, FalloutFormKey topic) => _selection.Select(
        FalloutDialogueTopic.Read(records, topic), station.Base, said, stage, condition => evaluate(station.Reference, condition),
        random, immediateResults: true, radio: true);

    private void RequireRadioTopic(FalloutFormKey topic)
    {
        if (records.GetEffective(topic).Signature != "DIAL" || FalloutDialogueTopic.Type(records, topic) != 7)
            throw new InvalidDataException("Radio conversation topic is not a winning radio DIAL.");
    }
}
