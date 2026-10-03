namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutNpcDialogueTurn(FalloutFormKey Speaker, FalloutFormKey Listener, FalloutFormKey Topic);

// Link To is a list of NPC continuation topics. Next Speaker selects the
// physical participant; borrowing a talking activator's identity never replaces
// that reference with the remote actor supplying its voice.
internal static class FalloutNpcDialogueLinks
{
    internal static IReadOnlyList<FalloutNpcDialogueTurn> Candidates(byte nextSpeaker, byte flags,
        IReadOnlyList<FalloutFormKey> links, FalloutFormKey speaker, FalloutFormKey listener, FalloutFormKey goodbye)
    {
        if ((flags & 1) != 0) return [];
        var (next, target) = nextSpeaker switch
        {
            0 => (listener, speaker),
            1 => (speaker, listener),
            _ => throw new NotSupportedException($"NPC dialogue Next Speaker {nextSpeaker} has no participant selection owner.")
        };
        IReadOnlyList<FalloutFormKey> topics = links.Count == 0 ? [goodbye] : links;
        return topics.Select(topic => new FalloutNpcDialogueTurn(next, target, topic)).ToArray();
    }
}
