using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Physics;

internal sealed class FalloutControllerContactState
{
    internal FalloutControllerContactSnapshot Snapshot { get; }
    internal FalloutControllerContactState(FalloutControllerContactSnapshot saved) { Snapshot = saved; }
    internal FalloutControllerContactState(FalloutPhysicalVector4 up, uint slopeBits, uint stepBits)
    {
        var slope = BitConverter.UInt32BitsToSingle(slopeBits); var step = BitConverter.UInt32BitsToSingle(stepBits);
        if (!float.IsFinite(slope) || !float.IsFinite(step))
            throw new NotSupportedException("source-controller-contact-exceptional-numeric-input-unbound");
        var angle = FalloutPhysicalFloat32.Multiply(slope, BitConverter.UInt32BitsToSingle(FalloutEarlyControllerSource.DegreesToRadiansBits));
        var value = TrigFields(BitConverter.SingleToUInt32Bits(angle), stepBits);
        Snapshot = new(up, slopeBits, BitConverter.SingleToUInt32Bits(angle), value.CosineBits, value.SineBits,
            stepBits, value.CoefficientBits, 0, 0, FalloutEarlyControllerSource.NumericalBoundary);
    }
    internal static (uint CosineBits, uint SineBits, uint CoefficientBits) TrigFields(uint angleBits, uint stepBits)
    {
        var angle = BitConverter.UInt32BitsToSingle(angleBits); var step = BitConverter.UInt32BitsToSingle(stepBits);
        if (!float.IsFinite(angle) || !float.IsFinite(step))
            throw new NotSupportedException("source-controller-contact-exceptional-numeric-input-unbound");
        // The double calls are independent. Each result stores to Float32
        // before the separately stored divide and multiply, including pi/2's
        // small negative cosine. Canonical table/control bytes are separate.
        var cosine = (float)Math.Cos((double)angle); var sine = (float)Math.Sin((double)angle);
        var ratio = FalloutPhysicalFloat32.Divide(step, sine);
        var coefficient = FalloutPhysicalFloat32.Multiply(ratio, cosine);
        return (BitConverter.SingleToUInt32Bits(cosine), BitConverter.SingleToUInt32Bits(sine), BitConverter.SingleToUInt32Bits(coefficient));
    }
    internal static uint MaximumSlopeCosine(uint angleBits)
    {
        var angle = BitConverter.UInt32BitsToSingle(angleBits);
        if (!float.IsFinite(angle)) throw new NotSupportedException("source-proxy-exceptional-slope-input-unbound");
        return BitConverter.SingleToUInt32Bits((float)Math.Cos((double)angle));
    }
}
