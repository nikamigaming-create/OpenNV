using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal sealed record NativeConversationVoiceLease(FalloutFormKey Speaker, FalloutFormKey Subject,
    long Generation, FalloutConversationResponseLease Response);

internal partial class RuntimeNativeSpeech
{
    internal NativeConversationVoiceLease StartConversationResponse(FalloutFormKey speaker, FalloutFormKey subject,
        FalloutDialogueSpeaker identity, FalloutConversation source, FalloutConversationResponseLease response,
        Action<NativeConversationVoiceLease> completed)
    {
        source.RequireResponse(response);
        NativeConversationVoiceLease? lease = null;
        StartResponse(speaker, source.Info!, response.Response, () =>
        {
            var actual = lease ?? throw new InvalidOperationException("Conversation voice returned before its native generation was published.");
            var voice = ExistingConversationChannel(actual.Speaker);
            if (voice.Generation != actual.Generation || voice.DialogueSubject != actual.Subject ||
                voice.ResponseIndex != actual.Response.Response)
                throw new InvalidOperationException("Conversation completion returned from a different native voice generation.");
            source.RequireResponse(actual.Response);
            completed(actual);
        }, subject, identity);
        lease = new(speaker, subject, ExistingConversationChannel(speaker).Generation, response);
        return lease;
    }
    internal void SkipConversationResponse(NativeConversationVoiceLease lease, FalloutConversation source)
    {
        if (Error is not null) throw new InvalidOperationException(Error);
        source.RequireResponse(lease.Response);
        RequireConversationVoice(lease).Pcm?.RequireHealthy();
        SkipResponse();
    }
    private Voice RequireConversationVoice(NativeConversationVoiceLease lease)
    {
        var voice = ExistingConversationChannel(lease.Speaker);
        if (voice != _conversationVoice || voice.Generation != lease.Generation || voice.DialogueSubject != lease.Subject ||
            voice.Info?.Record.FormKey != lease.Response.Info || voice.ResponseIndex != lease.Response.Response ||
            voice.ResponseCompleted is null)
            throw new InvalidOperationException("Dialogue action has no matching current native voice/source lease.");
        return voice;
    }
    private Voice ExistingConversationChannel(FalloutFormKey speaker) => _channels.TryGetValue(speaker, out var voice)
        ? voice : throw new InvalidOperationException("Dialogue action lost its original native voice channel.");
    internal void AbandonConversationResponse(NativeConversationVoiceLease lease)
    {
        var voice = RequireConversationVoice(lease);
        // Interruption retires this actual voice; it never runs INFO End or a
        // package completion. The caller retains the source failure prefix.
        voice.ResponseCompleted = null; voice.Advance = false;
        var errors = new List<Exception>();
        void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
        Attempt(voice.Player.Stop);
        Attempt(() => ClearResponseSound(voice));
        Attempt(() => RetireRadioPcm(voice));
        Attempt(() => EndListenerAnimation(voice));
        if (voice.Speaker is { } speaker && Godot.GodotObject.IsInstanceValid(speaker))
        {
            Attempt(speaker.EndResponseAnimation);
            Attempt(speaker.ClearSpeechFace);
        }
        voice.Info = null; voice.Lip = null; voice.LipWeights = [];
        _conversationVoice = null;
        if (errors.Count != 0) throw new AggregateException("Dialogue native voice interruption retirement failed.", errors);
    }
}
