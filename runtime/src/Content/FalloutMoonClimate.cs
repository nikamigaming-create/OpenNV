using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutMoonClimate(FalloutFormKey Form, string Winner, string SourceSha256, byte PhaseByte,
    byte VolatilityByte)
{
    internal int PhaseLength => PhaseByte & 0x3f;
    internal bool Enabled(FalloutMoonRole role) => role switch
    {
        FalloutMoonRole.Masser => (PhaseByte & 0x80) != 0,
        FalloutMoonRole.Secunda => (PhaseByte & 0x40) != 0,
        _ => throw new InvalidDataException("Climate was queried for another source Moon field."),
    };
    internal static FalloutMoonClimate Read(FalloutPluginRecord record)
    {
        if (record.Signature != "CLMT" || (record.Flags & 0x20) != 0)
            throw new InvalidDataException("Moon climate does not resolve to a living winning CLMT.");
        var declarations = record.ReadSubrecords().Where(field => field.Signature == "TNAM").ToArray();
        if (declarations.Length != 1 || declarations[0].Data.Length != 6)
            throw new InvalidDataException("Moon climate needs its exact single six-byte TNAM declaration.");
        var bytes = declarations[0].Data.Span;
        return new(record.FormKey, record.Plugin.Name, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes[5], bytes[4]);
    }
}
