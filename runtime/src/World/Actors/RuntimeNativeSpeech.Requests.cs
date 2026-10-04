using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeSpeech
{
    private sealed record DeferredSpeech(FalloutSayToCommand Command, FalloutFormKey Speaker,
        FalloutFormKey Topic, FalloutFormKey? Target);
    private readonly Dictionary<FalloutFormKey, DeferredSpeech> _deferredRequests = [];
    private FalloutFormKey? _playerDialogueSpeaker;

    internal void BeginPlayerDialogue(FalloutFormKey speaker)
    {
        if (_playerDialogueSpeaker is not null || IsDialogueBusy(speaker))
            throw new InvalidOperationException("The player dialogue participant is already busy.");
        _playerDialogueSpeaker = speaker;
    }

    internal void EndPlayerDialogue(FalloutFormKey speaker)
    {
        if (_playerDialogueSpeaker == speaker) _playerDialogueSpeaker = null;
    }

    private bool DeferScriptedSpeech(FalloutSayToCommand command, FalloutFormKey speaker,
        FalloutFormKey topic, FalloutFormKey? target, Action? packageCompleted, bool npcConversation)
    {
        if (!IsDialogueBusy(speaker)) return false;
        if (packageCompleted is not null || npcConversation || _playerDialogueSpeaker == speaker || _conversationVoice?.Reference == speaker)
            throw new NotSupportedException("Competing package/player dialogue needs its participant arbitration owner.");
        if (!_deferredRequests.TryAdd(speaker, new(command, speaker, topic, target)))
            throw new NotSupportedException("Multiple pending speech commands for one actor need their request arbitration owner.");
        GD.Print($"OPENNV_NATIVE_SPEECH_DEFERRED speaker={speaker} topic={topic} owner=actor-voice-completion");
        return true;
    }

    private void AdvanceDeferredSpeech()
    {
        foreach (var (speaker, request) in _deferredRequests.ToArray())
        {
            if (IsTalking(speaker) || IsNpcDialogueActive(speaker) || _playerDialogueSpeaker == speaker) continue;
            _deferredRequests.Remove(speaker);
            // Select against the current source state after the preceding
            // audio/result/completion event, without a speculative random draw.
            StartCore(request.Command, _stack.GetEffective(speaker), request.Topic, request.Target);
        }
    }
}
