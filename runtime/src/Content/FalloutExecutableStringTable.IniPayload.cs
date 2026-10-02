namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    // The compiler assembles four constant byte components in one stack local.
    // Admit the complete data flow, including every mask and the final local,
    // before retaining its nonnumeric descriptor payload. No setting identity
    // or executable location supplies a value.
    internal static uint ReadPackedIniPayload(ReadOnlySpan<byte> preceding, byte local)
    {
        if (preceding.Length < 67) throw new NotSupportedException("Owned packed INI initializer is truncated.");
        var code = preceding[^67..];
        if (code[0] != 0xb8 || !code.Slice(5, 6).SequenceEqual(new byte[] { 0xc1, 0xe0, 24, 0x89, 0x45, local }) ||
            code[11] != 0xb9 || !code.Slice(16, 2).SequenceEqual(new byte[] { 0x81, 0xe1 }) || U32(code, 18) != 255 ||
            !code.Slice(22, 9).SequenceEqual(new byte[] { 0xc1, 0xe1, 16, 0x0b, 0x4d, local, 0x89, 0x4d, local }) ||
            code[31] != 0xba || !code.Slice(36, 2).SequenceEqual(new byte[] { 0x81, 0xe2 }) || U32(code, 38) != 255 ||
            !code.Slice(42, 9).SequenceEqual(new byte[] { 0xc1, 0xe2, 8, 0x0b, 0x55, local, 0x89, 0x55, local }) ||
            code[51] != 0xb8 || code[56] != 0x25 || U32(code, 57) != 255 ||
            !code.Slice(61, 6).SequenceEqual(new byte[] { 0x0b, 0x45, local, 0x89, 0x45, local }))
            throw new NotSupportedException("Owned packed INI initializer has an unbound component or local.");
        return U32(code, 1) << 24 | (U32(code, 12) & 255) << 16 | (U32(code, 32) & 255) << 8 | U32(code, 52) & 255;
    }
}
