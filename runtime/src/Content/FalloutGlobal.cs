using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutGlobal(FalloutFormKey Form, string EditorId, byte Type, float InitialValue, string SourceSha256)
{
    internal static FalloutGlobal Read(FalloutPluginRecord record)
    {
        if (record.Signature != "GLOB") throw new InvalidDataException("Global source is not GLOB.");
        var fields = record.ReadSubrecords().ToArray();
        if (fields.Any(field => field.Signature is not ("EDID" or "FNAM" or "FLTV")) ||
            fields.GroupBy(field => field.Signature).Any(group => group.Count() != 1))
            throw new NotSupportedException($"GLOB {record.FormKey} has unbound or duplicate fields.");
        var type = fields.SingleOrDefault(field => field.Signature == "FNAM").Data;
        var value = fields.SingleOrDefault(field => field.Signature == "FLTV").Data;
        if (type.Length != 1 || type.Span[0] is not ((byte)'s' or (byte)'l' or (byte)'f') || value.Length != 4)
            throw new InvalidDataException($"GLOB {record.FormKey} has invalid type/value storage.");
        var number = BinaryPrimitives.ReadSingleLittleEndian(value.Span);
        if (!float.IsFinite(number)) throw new InvalidDataException($"GLOB {record.FormKey} is non-finite.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var field in fields)
        {
            hash.AppendData(Encoding.ASCII.GetBytes(field.Signature));
            hash.AppendData(BitConverter.GetBytes(field.Data.Length));
            hash.AppendData(field.Data.Span);
        }
        var editor = fields.SingleOrDefault(field => field.Signature == "EDID");
        return new(record.FormKey, Encoding.Latin1.GetString(editor.Data.Span).TrimEnd('\0'), type.Span[0], number,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }
}
