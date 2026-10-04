using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal static class FalloutScriptSourceIdentity
{
    internal static string Hash(FalloutPluginRecord script)
    {
        if (script.Signature != "SCPT") throw new InvalidDataException("Script continuation source is not SCPT.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(script.ReadData());
        hash.AppendData(Encoding.UTF8.GetBytes(script.FormKey + "\0"));
        foreach (var field in script.ReadSubrecords().Where(field => field.Signature == "SCRO"))
        {
            if (field.Data.Length != 4) throw new InvalidDataException("Compiled script reference extent is invalid.");
            var key = script.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            hash.AppendData(Encoding.UTF8.GetBytes(key + "\0"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
