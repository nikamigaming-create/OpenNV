using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateConversationShape(FalloutPlayerConversationSnapshot? saved)
    {
        if (saved?.Conversation is not { } state || state.Schema != FalloutConversationSnapshot.ExpectedSchema ||
            state.Phase is not ("closed" or "choices") || state.AddedTopics is null || state.Choices is null)
            throw new InvalidDataException("Current campaign is missing its complete settled conversation owner.");
        if (state.Phase == "closed")
        {
            if (saved.Speaker is not null || saved.DialogueSubject is not null || state.SpeakerBase is not null ||
                state.Info is not null || state.InfoSha256 is not null || state.ResponseIndex != 0 || state.Choices.Count != 0 ||
                state.Begin is not null || state.End is not null)
                throw new InvalidDataException("Closed campaign conversation contains an invented active source/receipt.");
        }
        else if (saved.Speaker is null || saved.DialogueSubject is null || state.SpeakerBase is null || state.Info is null ||
            state.Generation == 0 || state.ResponseIndex <= 0 || state.Begin is null || state.End is null || state.Choices.Count == 0)
            throw new InvalidDataException("Saved choices have no completed real participants/results.");
    }
    private static void ValidateConversationSource(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        var saved = state.Conversation ?? throw new InvalidDataException("Campaign conversation source owner is absent.");
        ValidateConversationShape(saved);
        if (saved.Speaker is { } speaker)
        {
            var reference = records.GetEffective(speaker);
            var actor = records.GetEffective(FalloutDialogueTopic.RequiredForm(reference, "NAME"));
            if (!(reference.Signature == "ACHR" && actor.Signature == "NPC_" ||
                reference.Signature == "ACRE" && actor.Signature == "CREA" ||
                reference.Signature == "REFR" && actor.Signature == "TACT"))
                throw new InvalidDataException("Saved conversation speaker has no winning source dialogue reference/base.");
        }
        saved.Conversation.RequireSource(records, saved.DialogueSubject ?? records.RuntimeFormKey(0x14));
        // Source identity does not prove native residency. Restore binds the
        // actual current speaker, participant/facing and source menu lifetime.
    }
}
