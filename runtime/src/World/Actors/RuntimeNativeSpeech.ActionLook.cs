using Godot;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeSpeech
{
    private sealed class SpeechActionLook(RuntimeNativeNpc? speaker, RuntimeNativeNpc? listener)
    {
        internal RuntimeNativeNpc? Speaker { get; } = speaker;
        internal RuntimeNativeNpc? Listener { get; } = listener;

        internal void Retire()
        {
            if (GodotObject.IsInstanceValid(Speaker) && ReferenceEquals(Speaker, Listener))
                Speaker!.EndSpeechActionLook(this, reciprocalSelf: true);
            else
            {
                if (GodotObject.IsInstanceValid(Listener)) Listener!.EndSpeechActionLook(this);
                if (GodotObject.IsInstanceValid(Speaker)) Speaker!.EndSpeechActionLook(this);
            }
        }
    }

    private void BeginSpeechActionLook(Voice voice)
    {
        if (voice.CommandKind != "SayTo" || voice.Listener is not { } target) return;
        var speaker = voice.Speaker;
        var listener = target == _stack.RuntimeFormKey(0x14) ? null : ResolveSpeaker(target) as RuntimeNativeNpc;
        var request = new SpeechActionLook(speaker, listener);
        // Validate both actual process bindings before either target is changed.
        speaker?.RequireSpeechActionLook(request, target);
        listener?.RequireSpeechActionLook(request, voice.Reference);
        voice.ActionLook = request;
        speaker?.BeginSpeechActionLook(request, target);
        listener?.BeginSpeechActionLook(request, voice.Reference);
        // Player view input, creature head presentation, and conditional body
        // turning are separate owners. Do not turn a camera or suspend a PACK.
    }

    private static void EndSpeechActionLook(Voice voice, SpeechActionLook? request)
    {
        if (request is null) return;
        request.Retire();
        if (ReferenceEquals(voice.ActionLook, request)) voice.ActionLook = null;
    }
}
