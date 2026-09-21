using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal static class NativeReferenceObservation
{
    internal static byte[] Serialize(FalloutPlacedReference reference, FalloutBaseObjectDefinition baseObject, string disposition)
    {
        var identity = reference.FormKey.ToString();
        var baseIdentity = reference.Base.ToString();
        var model = baseObject.ModelPath ?? string.Empty;
        // Preserve the canonical length-prefixed UTF-8 and little-endian
        // Float32 bytes in one allocation, without per-field byte arrays or
        // a growing stream copied again for every resident reference.
        var bytes = new byte[checked(7 * sizeof(int) + sizeof(uint) + sizeof(float) *
            (reference.Position.Length + reference.RotationRadians.Length + 1) +
            Encoding.UTF8.GetByteCount(identity) + Encoding.UTF8.GetByteCount(reference.EditorId) +
            Encoding.UTF8.GetByteCount(baseIdentity) + Encoding.UTF8.GetByteCount(baseObject.Signature) +
            Encoding.UTF8.GetByteCount(baseObject.EditorId) + Encoding.UTF8.GetByteCount(disposition) + Encoding.UTF8.GetByteCount(model))];
        var offset = 0;
        void Text(string value)
        {
            var length = Encoding.UTF8.GetBytes(value, bytes.AsSpan(offset + sizeof(int)));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), length);
            offset += sizeof(int) + length;
        }
        void Float(float value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
            offset += sizeof(float);
        }
        Text(identity); Text(reference.EditorId); Text(baseIdentity);
        Text(baseObject.Signature); Text(baseObject.EditorId); Text(disposition);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), reference.Flags); offset += sizeof(uint);
        foreach (var value in reference.Position) Float(value);
        foreach (var value in reference.RotationRadians) Float(value);
        Float(reference.Scale);
        Text(model);
        return bytes;
    }
}
