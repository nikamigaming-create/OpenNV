using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    // Admit the constructor's one intervening call only when the complete
    // selected method writes the two procedure fields and no cursor/scalar.
    // Executable source pointers prove associations; they never leave this
    // reader as native callable addresses or persisted runtime authority.
    private static void RequireBinaryProcedureInitialization(uint method, Image image)
    {
        if (!image.IsExecutableExtent(method)) throw new InvalidDataException("Binary procedure initialization is outside its original code.");
        var body = image.Read(method, 128); var at = 0;
        if (!body.AsSpan(0, 6).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d }) ||
            unchecked((sbyte)body[6]) >= 0)
            throw new NotSupportedException("Selected binary procedure receiver declaration is unowned.");
        var receiver = body[6]; at = 7;
        if (body[at] != 0x0f || body[at + 1] != 0xb6 || (body[at + 2] & 0xc7) != 0x45 || body[at + 3] != 8)
            throw new NotSupportedException("Selected binary procedure selector lacks its actual Boolean argument.");
        var selector = body[at + 2] >> 3 & 7; at += 4;
        if (body[at] != 0x85 || body[at + 1] != (0xc0 | selector << 3 | selector) || body[at + 2] != 0x74)
            throw new NotSupportedException("Selected binary procedure selector has unowned branch behavior.");
        var otherwise = checked(at + 4 + unchecked((sbyte)body[at + 3])); at += 4;
        at = Pair(at);
        if (body[at] != 0xeb) throw new NotSupportedException("Binary procedure branches have no common complete extent.");
        var end = checked(at + 2 + unchecked((sbyte)body[at + 1])); at += 2;
        if (at != otherwise) throw new NotSupportedException("Binary procedure alternative does not own the exact branch target.");
        if (Pair(at) != end || end < 0 || end > body.Length - 6 ||
            !body.AsSpan(end, 6).SequenceEqual(new byte[] { 0x8b, 0xe5, 0x5d, 0xc2, 4, 0 }))
            throw new NotSupportedException("Binary procedure initialization does not end after its two complete field pairs.");
        return;

        int Pair(int position)
        {
            foreach (var field in new[] { 8, 12 })
            {
                if (position < 0 || position > body.Length - 10 || body[position] != 0x8b ||
                    (body[position + 1] & 0xc7) != 0x45 || body[position + 2] != receiver)
                    throw new NotSupportedException("Binary procedure field lost its selected this receiver.");
                var register = body[position + 1] >> 3 & 7; position += 3;
                if (body[position] != 0xc7 || body[position + 1] != (0x40 | register) || body[position + 2] != field)
                    throw new NotSupportedException("Binary procedure initialization writes an unowned native field.");
                var procedure = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(position + 3, 4));
                if (!image.IsExecutableExtent(procedure))
                    throw new InvalidDataException("Binary procedure association is not backed by original executable source.");
                position += 7;
            }
            return position;
        }
    }
}
