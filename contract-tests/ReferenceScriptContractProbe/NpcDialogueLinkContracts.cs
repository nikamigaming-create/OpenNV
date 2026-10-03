using OpenNV.Runtime.Content;

internal static class NpcDialogueLinkContracts
{
    internal static void Run()
    {
        var speaker = new FalloutFormKey("links.esm", 1); var listener = new FalloutFormKey("links.esm", 2);
        var first = new FalloutFormKey("links.esm", 3); var second = new FalloutFormKey("links.esm", 4);
        var goodbye = new FalloutFormKey("links.esm", 5);
        var reply = FalloutNpcDialogueLinks.Candidates(0, 0, [first, second], speaker, listener, goodbye);
        if (reply.Count != 2 || reply[0] != new FalloutNpcDialogueTurn(listener, speaker, first) ||
            reply[1] != new FalloutNpcDialogueTurn(listener, speaker, second))
            throw new InvalidDataException("NPC continuation lost participant reversal or authored links.");
        var same = FalloutNpcDialogueLinks.Candidates(1, 0, [second], listener, speaker, goodbye).Single();
        if (same != new FalloutNpcDialogueTurn(listener, speaker, second) ||
            FalloutNpcDialogueLinks.Candidates(0, 1, [first], speaker, listener, goodbye).Count != 0 ||
            FalloutNpcDialogueLinks.Candidates(0, 0, [], speaker, listener, goodbye).Single() != new FalloutNpcDialogueTurn(listener, speaker, goodbye))
            throw new InvalidDataException("NPC Self, Goodbye or implicit source Goodbye continuation differs.");
        try { FalloutNpcDialogueLinks.Candidates(2, 0, [first], speaker, listener, goodbye); throw new InvalidDataException("Unknown participant selection was admitted."); }
        catch (NotSupportedException) { }
        Console.WriteLine("OPENNV_NPC_DIALOGUE_LINKS_PASS target=true self=true authoredOrder=true goodbye=true implicitGoodbye=true unknownSpeakerRefused=true");
    }
}
