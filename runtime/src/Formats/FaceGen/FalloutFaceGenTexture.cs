namespace OpenNV.Runtime.Formats.FaceGen;

/// <summary>Encodes an additive SCM delta for the Gamebryo FaceGen base-mod sampler.</summary>
internal static class FalloutFaceGenTexture
{
    internal static byte[] EncodeBaseMod(FalloutFaceGenTextureDelta delta)
    {
        if (delta.Width <= 0 || delta.Height <= 0 ||
            (long)delta.Width * delta.Height * 3 != delta.Rgb.Length)
            throw new InvalidDataException("FaceGen color delta has an invalid image extent.");
        var rgba = new byte[checked(delta.Width * delta.Height * 4)];
        for (var y = 0; y < delta.Height; y++)
            for (var x = 0; x < delta.Width; x++)
            {
                var input = (y * delta.Width + x) * 3;
                var output = ((delta.Height - y - 1) * delta.Width + x) * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var value = delta.Rgb[input + channel];
                    if (!float.IsFinite(value)) throw new InvalidDataException("FaceGen color delta is non-finite.");
                    // The source shader decodes 2 * (sample - 0.5). Therefore
                    // a delta in byte color units is encoded as (255 + delta)/2.
                    // SCM rows and the NIF texture coordinate origin are opposite.
                    rgba[output + channel] = (byte)Math.Clamp(MathF.Round((255 + value) * 0.5f), 0, 255);
                }
                rgba[output + 3] = 255;
            }
        return rgba;
    }
}
