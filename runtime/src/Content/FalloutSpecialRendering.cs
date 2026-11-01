using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Serialized special-render metadata is independent of reference enable state.
internal sealed record FalloutSpecialRendering(uint? Flags, uint? OpaqueData)
{
    internal bool Imposter => (Flags.GetValueOrDefault() & 2u) != 0;

    internal static FalloutSpecialRendering? Read(FalloutPluginRecord record)
    {
        uint? ReadField(string signature)
        {
            var fields = record.ReadSubrecords().Where(field => field.Signature == signature).ToArray();
            if (fields.Length == 0) return null;
            if (fields.Length != 1 || fields[0].Data.Length != 4)
                throw new InvalidDataException($"Reference {record.FormKey} has invalid {signature} extent/count.");
            return BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span);
        }
        var flags = ReadField("XSRF"); var data = ReadField("XSRD");
        return flags is null && data is null ? null : new(flags, data);
    }
}
