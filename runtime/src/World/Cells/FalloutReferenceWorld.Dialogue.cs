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

    internal FalloutDialogueSpeaker DialogueIdentity(FalloutFormKey reference)
    {
        var subject = Get(DialogueSubject(reference));
        return FalloutDialogueSpeaker.Read(records, subject.Base, subject.Templates);
    }
}
