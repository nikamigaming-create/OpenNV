using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal string ActorDisplayName(FalloutFormKey reference)
    {
        var source = records.GetEffective(ActorBase(reference));
        var selection = reference == records.RuntimeFormKey(0x14) ? null : Actor(reference).Templates;
        return FalloutRecordDisplayName.ReadActor(records, source, selection);
    }
}
