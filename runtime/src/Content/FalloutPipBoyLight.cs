using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPipBoyLight(FalloutFormKey Spell, FalloutFormKey Effect, FalloutFormKey Light,
    string SourceHash, uint Magnitude, FalloutLightDefinition Definition)
{
    internal static FalloutPipBoyLight Read(FalloutPluginStack records)
    {
        var spell = FalloutDialogueTopic.Find(records, "SPEL", "PipBoyLight");
        var fields = spell.ReadSubrecords().ToArray();
        var header = fields.Single(field => field.Signature == "SPIT").Data.Span;
        var effectData = fields.Single(field => field.Signature == "EFIT").Data.Span;
        var effectId = fields.Single(field => field.Signature == "EFID").Data.Span;
        if (header.Length != 16 || BinaryPrimitives.ReadUInt32LittleEndian(header) != 4 || header[12] != 0 ||
            effectId.Length != 4 || effectData.Length != 20 || fields.Any(field => field.Signature == "CTDA") ||
            BinaryPrimitives.ReadUInt32LittleEndian(effectData[4..]) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(effectData[8..]) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(effectData[12..]) != 0)
            throw new NotSupportedException("Pip-Boy light requires a permanent self light ability.");
        var effect = records.GetEffective(spell.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(effectId)));
        var data = effect.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
        if (effect.Signature != "MGEF" || data.Length != 72 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data[64..]) != 13 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x08000010)
            throw new NotSupportedException("Pip-Boy ability has an unsupported magic-effect owner.");
        var light = records.GetEffective(effect.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[24..])));
        var definition = FalloutCellSceneReader.ReadLight(light);
        FalloutPlacedLightResolver.RequireStaticPoint(definition, light.FormKey);
        var magnitude = BinaryPrimitives.ReadUInt32LittleEndian(effectData);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(spell.ReadData()); hash.AppendData(effect.ReadData()); hash.AppendData(light.ReadData());
        return new(spell.FormKey, effect.FormKey, light.FormKey, Convert.ToHexString(hash.GetHashAndReset()), magnitude, definition);
    }

    internal float Radius(FalloutPluginStack records)
    {
        var radius = records.NumericSettings.Float("fMagicLightRadiusBase") + Magnitude * records.NumericSettings.Float("fMagicUnitsPerFoot");
        if (!float.IsFinite(radius) || radius <= 0) throw new InvalidDataException("Pip-Boy light radius is invalid.");
        return radius;
    }
}
