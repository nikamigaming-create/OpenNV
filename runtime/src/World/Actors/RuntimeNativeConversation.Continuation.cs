using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal sealed record FalloutPlayerConversationSnapshot(FalloutFormKey? Speaker,
    FalloutFormKey? DialogueSubject, FalloutConversationSnapshot Conversation);

internal partial class RuntimeNativeConversation
{
    internal string? SaveContinuationBlocker
    {
        get
        {
            if (_retired || Error is not null || _retirementFailure is not null || _conversation.Error is not null)
                return "conversation-failure";
            if (_pending.Count != 0) return "conversation-request";
            if (_completed is not null) return "conversation-package-callback";
            if (_nativeResponse is not null || _conversation.Phase == "speaking") return "conversation-response";
            if (_conversation.Phase is not ("closed" or "choices")) return "conversation-results";
            if (_conversation.Phase == "choices" && (_menu is null || _layer is null || _dialogueInput is null ||
                !Godot.GodotObject.IsInstanceValid(_menu) || !_menu.IsInsideTree() ||
                !Godot.GodotObject.IsInstanceValid(_layer) || !_layer.IsInsideTree())) return "conversation-native-view";
            try { _ = _conversation.CaptureContinuation(); return null; }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            { return "conversation-source-receipt:" + error.Message; }
        }
    }
    internal FalloutPlayerConversationSnapshot CaptureContinuation()
    {
        if (SaveContinuationBlocker is { } blocker) throw new NotSupportedException("Saving conversation requires " + blocker + ".");
        var state = _conversation.CaptureContinuation();
        return new(state.Phase == "closed" ? null : _speaker,
            state.Phase == "closed" ? null : _dialogueSubject, state);
    }
    internal void RestoreContinuation(FalloutPlayerConversationSnapshot saved)
    {
        if (_retired || Error is not null || _menu is not null || _pending.Count != 0 || _completed is not null)
            throw new InvalidOperationException("Cold conversation requires a fresh configured native owner.");
        if (saved.Conversation.Phase == "closed")
        {
            if (saved.Speaker is not null || saved.DialogueSubject is not null)
                throw new InvalidDataException("Closed conversation invented a native participant.");
            _conversation.RestoreContinuation(saved.Conversation, _records.RuntimeFormKey(0x14));
            return;
        }
        try
        {
            if (saved.Speaker is not { } speaker || saved.DialogueSubject is not { } subject)
                throw new InvalidDataException("Cold choices lost their actual placed speaker/caller identity.");
            BindConversationSource(speaker);
            if (_dialogueSubject != subject || _identity.Actor != saved.Conversation.SpeakerBase)
                throw new InvalidDataException("Cold dialogue speaker/native source differs from the completed result caller.");
            _conversation.RestoreContinuation(saved.Conversation, subject);
            OpenConversationPresentation();
            // TalkedToPlayer, Begin/End effects and voice selection were already
            // committed. Only the actual current native menu lease is rebuilt.
            BindConversationFacing();
            Present();
        }
        catch (Exception error) { Fail(error); throw; }
    }
}
