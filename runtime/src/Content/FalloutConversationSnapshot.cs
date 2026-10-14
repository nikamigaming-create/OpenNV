using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutConversationChoiceSnapshot(FalloutFormKey Topic, FalloutFormKey Info,
    string Text, string TopicSha256, string InfoSha256);

internal sealed record FalloutConversationSnapshot(string Schema, string Phase, ulong Generation,
    FalloutFormKey? SpeakerBase, FalloutFormKey? Info, string? InfoSha256, int ResponseIndex,
    IReadOnlyList<FalloutFormKey> AddedTopics, IReadOnlyList<FalloutConversationChoiceSnapshot> Choices,
    FalloutScriptResultReceipt? Begin, FalloutScriptResultReceipt? End, ulong ResultOnlyCompletions = 0)
{
    internal const string ExpectedSchema = "opennv-conversation-continuation/v1";
    internal void RequireSource(FalloutPluginStack records, FalloutFormKey caller)
    {
        if (Schema != ExpectedSchema || Phase is not ("closed" or "choices") || AddedTopics is null || Choices is null ||
            AddedTopics.Distinct().Count() != AddedTopics.Count || Choices.Any(choice => choice is null) ||
            Choices.Select(choice => choice.Topic).Distinct().Count() != Choices.Count ||
            Generation == 0 && ResultOnlyCompletions == 0 && AddedTopics.Count != 0)
            throw new InvalidDataException("Conversation continuation shape/source topics are invalid.");
        foreach (var topic in AddedTopics)
            if (records.GetEffective(topic).Signature != "DIAL")
                throw new InvalidDataException("Added conversation topic is not a winning DIAL.");
        if (Phase == "closed")
        {
            if (SpeakerBase is not null || Info is not null || InfoSha256 is not null || ResponseIndex != 0 ||
                Choices.Count != 0 || Begin is not null || End is not null)
                throw new InvalidDataException("Closed conversation contains an invented active source/receipt.");
            return;
        }
        if (SpeakerBase is not { } speaker || Info is not { } key || Begin is null || End is null || Choices.Count == 0)
            throw new InvalidDataException("Settled conversation lacks its genuine source/committed-result state.");
        if (records.GetEffective(speaker).Signature is not ("NPC_" or "CREA" or "TACT"))
            throw new InvalidDataException("Conversation base speaker has no source dialogue actor type.");
        var record = records.GetEffective(key);
        if (record.Signature != "INFO" || Hash(record.ReadData()) != InfoSha256)
            throw new InvalidDataException("Current conversation INFO differs from its winning source bytes.");
        var info = FalloutDialogueTopic.Decode(record);
        if (info.Responses.Count == 0 ? ResultOnlyCompletions == 0 : Generation == 0)
            throw new InvalidDataException("Settled conversation omitted its actual response or result-only completion owner.");
        if (ResponseIndex != info.Responses.Count || (info.Flags & 1) != 0)
            throw new InvalidDataException("Conversation choices do not follow the completed source responses.");
        FalloutDialogueTopic.RequireFlags(info, conversation: true, randomSelection: true, immediateResults: true, allowResultOnly: true);
        Begin.Require(FalloutScriptScope.Dialogue(record, true), caller);
        End.Require(FalloutScriptScope.Dialogue(record, false), caller);
        IEnumerable<FalloutFormKey> sourceTopics = info.Choices.Count != 0 ? info.Choices :
            FalloutDialogueTopic.DefaultTopics(records).Concat(AddedTopics).Distinct()
                .OrderByDescending(topic => FalloutDialogueTopic.Priority(records, topic));
        var offered = sourceTopics.Distinct().ToArray();
        var previous = -1;
        foreach (var choice in Choices)
        {
            var ordinal = Array.IndexOf(offered, choice.Topic);
            if (ordinal <= previous || Hash(records.GetEffective(choice.Topic).ReadData()) != choice.TopicSha256)
                throw new InvalidDataException("Conversation choice order/source differs from its authored topic list.");
            previous = ordinal;
            var next = FalloutDialogueTopic.Read(records, choice.Topic).Infos.SingleOrDefault(value => value.Record.FormKey == choice.Info)
                ?? throw new InvalidDataException("Conversation choice INFO is not in its winning source topic.");
            if (Hash(next.Record.ReadData()) != choice.InfoSha256)
                throw new InvalidDataException("Conversation choice INFO bytes changed.");
            var fields = records.GetEffective(choice.Topic).ReadSubrecords().Where(field => field.Signature == "FULL").ToArray();
            var prompt = next.Prompt ?? (fields.Length == 1 ? FalloutDialogueTopic.Text(fields[0].Data.Span) :
                throw new InvalidDataException("Conversation choice has no unique source prompt."));
            if (prompt != choice.Text) throw new InvalidDataException("Conversation choice text is not its actual source prompt.");
            FalloutDialogueTopic.RequireFlags(next, conversation: FalloutDialogueTopic.Type(records, choice.Topic) == 0,
                randomSelection: true, immediateResults: true, allowResultOnly: true);
        }
        // Eligibility can change while a menu is displayed. Choose rechecks it
        // through the shared selector; cold validation must not select or draw.
    }
    private static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
