using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Pending source event marks belong to the reference, including while its
// presentation is unloaded. Revisions retain marks raised during dispatch.
internal sealed record FalloutReferenceGrabEvents(FalloutFormKey Reference, long Revision, long Grab, long Release)
{
    internal bool HasPending => Grab != 0 || Release != 0;
    internal void Validate(FalloutFormKey reference)
    {
        if (Reference != reference || Revision <= 0 || Revision == long.MaxValue || Grab < 0 || Release < 0 || Grab > Revision || Release > Revision || Grab != 0 && Grab == Release)
            throw new InvalidDataException("Saved grab/release event marks are invalid.");
    }
}

internal sealed partial class FalloutReferenceWorld
{
    internal void MarkPlayerGrab(FalloutFormKey reference, bool grabbed)
    {
        var instance = Get(reference);
        if (instance.Deleted || instance.DeletePending) return;
        var previous = instance.GrabEvents ?? new(reference, 0, 0, 0);
        if (previous.Revision >= long.MaxValue - 1) throw new InvalidOperationException("Grab/release event revisions are exhausted.");
        var revision = checked(previous.Revision + 1);
        instance.GrabEvents = grabbed ? previous with { Revision = revision, Grab = revision }
            : previous with { Revision = revision, Release = revision };
    }

    internal void ConsumeGrabEvents(FalloutFormKey reference, FalloutReferenceGrabEvents batch)
    {
        var instance = Get(reference);
        batch.Validate(reference);
        var current = instance.GrabEvents ?? throw new InvalidOperationException("Grab event admission lost its reference owner.");
        instance.GrabEvents = current with
        {
            Grab = current.Grab == batch.Grab ? 0 : current.Grab,
            Release = current.Release == batch.Release ? 0 : current.Release,
        };
    }
}
