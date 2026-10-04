using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal bool GetBroadcastState(FalloutFormKey reference)
    {
        var station = FalloutRadioStation.Read(records, records.GetEffective(reference));
        return Get(reference).BroadcastState ?? station.Continuous;
    }

    internal void SetBroadcastState(FalloutFormKey reference, double value)
    {
        var broadcast = value switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("Radio broadcast state must be zero or one."),
        };
        _ = FalloutRadioStation.Read(records, records.GetEffective(reference));
        Get(reference).BroadcastState = broadcast;
    }
}
