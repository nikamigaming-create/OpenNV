using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed partial record FalloutFollowPackage
{
    internal void RequireActorTarget(FalloutPluginStack records, FalloutFormKey actor)
    {
        if (Target == actor) throw new NotSupportedException("Follow cannot bind its own actor as its target.");
        if (Target == records.RuntimeFormKey(0x14)) return;
        var reference = records.GetEffective(Target);
        if (reference.Signature is not ("ACHR" or "ACRE"))
            throw new NotSupportedException("Follow reference target requires its non-actor selection owner.");
        var baseForm = FalloutDialogueTopic.RequiredForm(reference, "NAME");
        if (records.GetEffective(baseForm).Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("Follow target reference has no winning actor base.");
    }

    internal void ValidateContinuation(FalloutPluginStack records, FalloutFormKey actor, FalloutFollowProgress progress)
    {
        RequireActorTarget(records, actor);
        progress.Validate();
        if (progress.Target != Target || progress.Election is null)
            throw new InvalidDataException("Saved Follow differs from its original fixed target or has no actual election observation.");
    }
}
