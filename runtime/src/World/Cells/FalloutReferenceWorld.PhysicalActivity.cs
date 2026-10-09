using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal int GetSleeping(FalloutFormKey reference, Func<FalloutFormKey, int>? live = null)
    {
        RequirePhysicalActor(reference);
        var phase = (live ?? throw new NotSupportedException($"GetSleeping actor {reference} has no actual physical sleep owner."))(reference);
        return phase is >= 0 and <= 4 ? phase : throw new InvalidDataException("Sleeping query returned an invalid original phase.");
    }
    internal int GetKnockedState(FalloutFormKey reference, Func<FalloutFormKey, int>? live = null)
    {
        RequirePhysicalActor(reference);
        var state = (live ?? throw new NotSupportedException($"GetKnockedState actor {reference} has no actual physical process owner."))(reference);
        return state is 0 or 1 ? state : throw new InvalidDataException("Knocked-state query returned an invalid selected source result.");
    }
    private void RequirePhysicalActor(FalloutFormKey reference)
    {
        if (records.RuntimeFormId(reference) == 0x14) return;
        _ = Actor(reference);
        if (records.GetEffective(reference).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("Physical query subject is not an authoritative actor reference.");
    }
}
