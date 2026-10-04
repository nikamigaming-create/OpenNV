using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal object TalkingActivatorBindings => _instances.Values.Where(instance => instance.TalkingActivatorActor is not null)
        .Select(instance => new { reference = instance.Reference.ToString(), actor = instance.TalkingActivatorActor!.Value.ToString() }).ToArray();

    internal void SetTalkingActivatorActor(FalloutFormKey reference, FalloutFormKey? actor)
    {
        var instance = Get(reference);
        if (records.GetEffective(reference).Signature != "REFR" || records.GetEffective(instance.Base).Signature != "TACT")
            throw new InvalidDataException("SetTalkingActivatorActor target is not a placed talking activator.");
        if (actor is { } selected) _ = Actor(selected);
        instance.TalkingActivatorActor = actor;
    }

    internal FalloutFormKey DialogueSubject(FalloutFormKey reference) => Get(reference).TalkingActivatorActor ?? reference;

    internal bool GetTalkedToPlayer(FalloutFormKey reference)
    {
        var instance = Get(reference);
        if (records.GetEffective(instance.Base).Signature is not ("NPC_" or "CREA" or "TACT"))
            throw new InvalidDataException("Talked-to-player query requires its actor or talking activator history owner.");
        return instance.TalkedToPlayer;
    }

    internal FalloutFormKey? GetLinkedRef(FalloutFormKey reference)
    {
        var source = records.GetEffective(reference);
        if (source.Signature is not ("REFR" or "ACHR" or "ACRE") || source.IsDeleted)
            throw new InvalidDataException("Linked-reference query requires a live placed source reference.");
        var linked = FalloutPatrolRoute.Linked(source);
        if (linked is { } target && records.RuntimeFormId(target) != 0x14 &&
            (records.GetEffective(target).Signature is not ("REFR" or "ACHR" or "ACRE") || records.GetEffective(target).IsDeleted))
            throw new InvalidDataException("Source linked reference does not identify a live placed target.");
        return linked;
    }

    internal FalloutDialogueSpeaker DialogueIdentity(FalloutFormKey reference)
    {
        var subject = Get(DialogueSubject(reference));
        return FalloutDialogueSpeaker.Read(records, subject.Base, subject.Templates);
    }
}
