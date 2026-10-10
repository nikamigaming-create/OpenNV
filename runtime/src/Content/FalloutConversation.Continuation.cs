using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutConversationResponseLease(ulong Generation, FalloutFormKey Info,
    int Response, string InfoSha256, string ResponseSha256);

internal sealed partial class FalloutConversation
{
    private FalloutConversationResponseLease? _responseLease;
    private bool _responseEntered;
    private FalloutScriptResultReceipt? _beginReceipt, _endReceipt;
    internal ulong ResponseGeneration { get; private set; }
    internal bool BeginResultsEntered { get; private set; }
    internal bool EndResultsEntered { get; private set; }
    internal FalloutConversationResponseLease? PendingResponse => _responseEntered ? _responseLease : null;

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private void PublishResponse()
    {
        var info = Info ?? throw new InvalidOperationException("Conversation lost its actual selected INFO.");
        if (Phase != "speaking" || (uint)ResponseIndex >= info.Responses.Count)
            throw new InvalidDataException("Conversation published a response outside its source INFO.");
        ResponseGeneration = checked(ResponseGeneration + 1);
        _responseLease = new(ResponseGeneration, info.Record.FormKey, ResponseIndex,
            Hash(info.Record.ReadData()), Hash(info.Responses[ResponseIndex].SourceBytes));
        _responseEntered = false;
    }
    internal FalloutConversationResponseLease EnterResponse()
    {
        var lease = _responseLease ?? throw new InvalidOperationException("Conversation has no source response publication.");
        RequireResponse(lease);
        if (_responseEntered) throw new InvalidOperationException("The current source response already entered its voice owner.");
        _responseEntered = true;
        return lease;
    }
    internal void RequireResponse(FalloutConversationResponseLease lease)
    {
        if (Error is not null || Phase != "speaking" || lease != _responseLease || Info is not { } info ||
            lease.Generation != ResponseGeneration || lease.Info != info.Record.FormKey || lease.Response != ResponseIndex ||
            lease.InfoSha256 != Hash(records.GetEffective(lease.Info).ReadData()) ||
            lease.ResponseSha256 != Hash(info.Responses[ResponseIndex].SourceBytes))
            throw new InvalidOperationException("Dialogue completion differs from the current source response generation.");
    }
    internal void CompleteResponse(FalloutConversationResponseLease lease)
    {
        RequireResponse(lease);
        if (!_responseEntered) throw new InvalidOperationException("Dialogue response completion has no actual entered voice owner.");
        CompleteResponse();
    }
    private void RetireResponsePublication() { _responseEntered = false; _responseLease = null; }
    internal void RetainExternalFailure(Exception error)
    {
        Error ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
        Phase = "failed";
    }
    private void ExecuteConversationResult(FalloutDialogueInfo info, bool begin)
    {
        if (begin) BeginResultsEntered = true; else EndResultsEntered = true;
        if (ownedResults is null) { results(info, begin); return; }
        var scope = FalloutScriptScope.Dialogue(info.Record, begin);
        scope.RequireSource(records.GetEffective(info.Record.FormKey));
        var caller = (resultCaller ?? throw new InvalidOperationException("Owned conversation results have no real caller producer."))();
        var receipt = ownedResults(info, begin);
        receipt.Require(scope, caller);
        if (begin) _beginReceipt = receipt; else _endReceipt = receipt;
    }
    internal FalloutConversationSnapshot CaptureContinuation()
    {
        if (_mutating || Error is not null || Phase is not ("closed" or "choices") || _responseEntered)
            throw new NotSupportedException("Conversation capture requires a settled source choice or closed owner; active results/audio remain unowned.");
        var added = _added.ToArray();
        if (Phase == "closed") return new(FalloutConversationSnapshot.ExpectedSchema, Phase,
            ResponseGeneration, null, null, null, 0, added, [], null, null);
        var info = Info ?? throw new InvalidOperationException("Settled choices lost their actual INFO owner.");
        if (_beginReceipt is null || _endReceipt is null || _ending || ResponseIndex != info.Responses.Count)
            throw new NotSupportedException("Settled choices have no genuine completed Begin/End result receipts.");
        var choices = Choices.Select(choice => new FalloutConversationChoiceSnapshot(choice.Topic, choice.Info,
            choice.Text, Hash(records.GetEffective(choice.Topic).ReadData()), Hash(records.GetEffective(choice.Info).ReadData()))).ToArray();
        var saved = new FalloutConversationSnapshot(FalloutConversationSnapshot.ExpectedSchema, Phase,
            ResponseGeneration, _speaker, info.Record.FormKey, Hash(info.Record.ReadData()), ResponseIndex,
            added, choices, _beginReceipt, _endReceipt);
        saved.RequireSource(records, (resultCaller ?? throw new InvalidOperationException("Conversation continuation has no result caller."))());
        return saved;
    }
    internal void RestoreContinuation(FalloutConversationSnapshot saved, FalloutFormKey caller)
    {
        if (Phase != "closed" || ResponseGeneration != 0 || Error is not null || _mutating || _responseEntered)
            throw new InvalidOperationException("Conversation continuation requires a fresh source owner.");
        saved.RequireSource(records, caller);
        ResponseGeneration = saved.Generation;
        foreach (var added in saved.AddedTopics) _added.Add(added);
        if (saved.Phase == "closed") return;
        _speaker = saved.SpeakerBase!.Value;
        Info = FalloutDialogueTopic.Decode(records.GetEffective(saved.Info!.Value));
        ResponseIndex = saved.ResponseIndex;
        Choices = saved.Choices.Select(choice => new FalloutConversationChoice(choice.Topic, choice.Info, choice.Text)).ToArray();
        _beginReceipt = saved.Begin; _endReceipt = saved.End;
        BeginResultsEntered = EndResultsEntered = true;
        _ending = false; Phase = "choices";
        // No Select, RNG draw, result effect, voice start or text key replay.
    }
}
