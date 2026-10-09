using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSkyWeatherModifierSlots(FalloutFormKey Weather, string SourceSha256,
    IReadOnlyList<FalloutFormKey?> Slots)
{
    internal static FalloutSkyWeatherModifierSlots Read(FalloutPluginStack records, FalloutFormKey weather, FalloutSkyTransferDeclaration source)
    {
        source.Validate();
        var record = records.GetEffective(weather);
        if (record.Signature != "WTHR" || record.IsDeleted) throw new InvalidDataException("Sky image modifier source is not a winning WTHR.");
        var fields = record.ReadSubrecords().ToArray();
        if (source.IsStandalone && fields.Any(field => field.Signature is "4IAD" or "5IAD"))
            throw new NotSupportedException("Selected Sky does not declare the extra weather image-modifier channels.");
        var result = new FalloutFormKey?[source.WeatherImageSlots];
        for (var index = 0; index < result.Length; index++)
        {
            // The common ESM reader validates the raw first-byte channel and
            // exposes its decimal logical name. Do not decode it a second time.
            var declarations = fields.Where(field => field.Signature == $"{index}IAD").ToArray();
            if (declarations.Length > 1 || declarations.Length == 1 && declarations[0].Data.Length != 4)
                throw new InvalidDataException("Source WTHR repeats or truncates an image modifier slot.");
            if (declarations.Length != 0)
            {
                result[index] = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(declarations[0].Data.Span));
                if (result[index] is { } form)
                {
                    var modifier = records.GetEffective(form);
                    if (modifier.Signature != "IMAD" || modifier.IsDeleted)
                        throw new InvalidDataException("Weather image slot changed its exact master/winning IMAD owner.");
                }
            }
        }
        return new(weather, Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant(), result);
    }
}
