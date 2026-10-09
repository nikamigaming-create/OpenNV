using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutPlayerRawTransferWriter { MoveTo, Door }
internal sealed record FalloutPlayerTransferVector(uint X, uint Y, uint Z)
{
    internal static FalloutPlayerTransferVector Read(IReadOnlyList<float> values)
    {
        if (values.Count != 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Player transfer vector has no three finite source cells.");
        return new(BitConverter.SingleToUInt32Bits(values[0]), BitConverter.SingleToUInt32Bits(values[1]),
            BitConverter.SingleToUInt32Bits(values[2]));
    }
    internal float[] Values => [BitConverter.UInt32BitsToSingle(X), BitConverter.UInt32BitsToSingle(Y), BitConverter.UInt32BitsToSingle(Z)];
    internal void Validate()
    {
        if (Values.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Player transfer vector lost its finite source values.");
    }
}

// A captured placement is a value at the actual request writer, not an input
// for re-querying the destination during consumption or cold continuation.
internal sealed record FalloutPlayerRawTransferReceipt(FalloutPlayerRawTransferSource Source,
    FalloutPlayerRawTransferWriter Writer, Guid Allocation, FalloutFormKey RequestSource, string RequestSha256,
    FalloutFormKey Target, string TargetSha256, FalloutCellProcessIdentity ParentCell, byte ParentCellFlags,
    FalloutPlayerTransferVector TargetPosition, FalloutPlayerTransferVector TargetRotation,
    FalloutPlayerTransferVector Offsets, string PlacementOwner);
