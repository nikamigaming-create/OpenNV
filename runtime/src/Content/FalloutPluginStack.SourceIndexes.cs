using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal static class FalloutPluginStackSourceIndexes
{
    internal static FalloutActivationParentIndex ReadActivationParentIndex(this FalloutPluginStack records, CancellationToken cancellation = default)
        => records.ReadSourceIndex<FalloutActivationParentIndex>(FalloutActivationParentIndex.Read, cancellation);

    internal static FalloutRadioStationIndex ReadRadioStationIndex(this FalloutPluginStack records, CancellationToken cancellation = default)
        => records.ReadSourceIndex<FalloutRadioStationIndex>(FalloutRadioStationIndex.Read, cancellation);
}
