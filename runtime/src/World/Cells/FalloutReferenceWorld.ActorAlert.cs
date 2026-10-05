using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey, long> _alertRevisions = [];

    private void RequireAlertActor(FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (records.RuntimeFormId(actor) == 0x14)
        {
            if (ActorOverrideSource(actor).Signature != "NPC_")
                throw new InvalidDataException("Player alert state has no winning actor source.");
            return;
        }
        if (records.GetEffective(actor).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("SetAlert requires an actual placed actor or the player.");
        _ = Actor(actor);
    }

    internal bool ActorAlerted(FalloutFormKey actor)
    {
        RequireAlertActor(actor);
        return _actorOverrides.GetValueOrDefault(actor)?.Alerted ?? false;
    }

    internal long ActorAlertRevision(FalloutFormKey actor)
    {
        RequireAlertActor(actor);
        return _alertRevisions.GetValueOrDefault(actor);
    }

    internal void SetActorAlerted(FalloutFormKey actor, bool value)
    {
        RequireAlertActor(actor);
        var previous = Overrides(actor);
        if (previous.Alerted == value) return;
        _actorOverrides[actor] = previous with { Alerted = value };
        _alertRevisions[actor] = checked(_alertRevisions.GetValueOrDefault(actor) + 1);
    }

    internal void SetActorAlert(FalloutFormKey actor, double value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue || value != Math.Truncate(value))
            throw new InvalidDataException("SetAlert requires a signed source integer.");
        // The admitted engine command casts an actual calling reference to an
        // actor; a non-actor REFR completes without changing an actor flag.
        if (records.RuntimeFormId(actor) != 0x14 && records.GetEffective(actor).Signature == "REFR")
        { _ = Get(actor); return; }
        SetActorAlerted(actor, value > 0);
    }

    internal void BindActorAlert(FalloutFormKey actor, FalloutActorActivityState activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        RequireAlertActor(actor);
        activity.BindAlerted(() => ActorAlerted(actor), value => SetActorAlerted(actor, value),
            () => ActorAlertRevision(actor));
    }
}
