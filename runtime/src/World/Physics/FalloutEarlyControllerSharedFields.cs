using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Physics;

// These are actual source loader cells with distinct current-writer barriers.
// The original standard center is the shared zero vector, not Actor position.
internal sealed class FalloutEarlyControllerSharedFields
{
    private bool _centerAvailable = true, _slopeAvailable = true, _helperAvailable = true;
    internal string? SaveBlocker => !_centerAvailable ? "source-standard-controller-center-current-writer-unbound" :
        !_slopeAvailable ? "source-controller-shared-slope-writer-unbound" :
        !_helperAvailable ? "source-controller-helper-flag-current-writer-unbound" : null;
    internal FalloutEarlyControllerSharedFields(FalloutEarlyControllerSource source) { source.Validate(); }
    internal void Restore(FalloutEarlyControllerSharedFieldsSnapshot saved)
    { _centerAvailable = saved.CenterAvailable; _slopeAvailable = saved.SlopeAvailable; _helperAvailable = saved.HelperFlagAvailable; }
    internal FalloutPhysicalVector3 ReadCenter() => _centerAvailable ? FalloutEarlyControllerSource.InitialStandardCenter :
        throw new NotSupportedException("source-standard-controller-center-current-writer-unbound");
    internal uint ReadSlope() => _slopeAvailable ? FalloutEarlyControllerSource.InitialSlopeBits :
        throw new NotSupportedException("source-controller-shared-slope-writer-unbound");
    internal bool ReadHelperFlag() => _helperAvailable ? FalloutEarlyControllerSource.InitialHelperFlag != 0 :
        throw new NotSupportedException("source-controller-helper-flag-current-writer-unbound");
    internal void ObserveUnownedWriter(string field)
    {
        switch (field)
        {
            case "center": _centerAvailable = false; break;
            case "slope": _slopeAvailable = false; break;
            case "helperFlag": _helperAvailable = false; break;
            default: throw new ArgumentException("Unknown source controller shared field.", nameof(field));
        }
    }
    internal FalloutEarlyControllerSharedFieldsSnapshot Capture() => new(FalloutEarlyControllerSource.InitialStandardCenter,
        FalloutEarlyControllerSource.InitialSlopeBits, FalloutEarlyControllerSource.InitialHelperFlag,
        _centerAvailable, _slopeAvailable, _helperAvailable);
    internal uint ActorScale(uint currentReferenceScaleBits, FalloutPluginRecord? actualCurrentBase,
        Func<FalloutPluginRecord, uint> readCurrentBaseHeight)
    {
        ArgumentNullException.ThrowIfNull(readCurrentBaseHeight);
        // The early Player helper precedes source base-record assignment.
        // Final resolved Actor identity does not populate this nullable field.
        if (actualCurrentBase is null || actualCurrentBase.Signature is not ("NPC_" or "CREA")) return currentReferenceScaleBits;
        var height = readCurrentBaseHeight(actualCurrentBase);
        return BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Multiply(BitConverter.UInt32BitsToSingle(currentReferenceScaleBits),
            BitConverter.UInt32BitsToSingle(height)));
    }
}
