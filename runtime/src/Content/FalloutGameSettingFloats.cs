using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Content;

internal static class FalloutGameSettingFloats
{
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, IReadOnlyDictionary<string, float>> Defaults = new();

    internal static float Read(FalloutPluginStack records, string name)
    {
        var overrides = records.EffectiveRecords("GMST").Where(record => record.ReadSubrecords().Any(field =>
            field.Signature == "EDID" && FalloutDialogueTopic.Text(field.Data.Span).Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (overrides.Length > 1) throw new InvalidDataException($"Multiple winning GMST identities have EDID {name}.");
        if (overrides.Length == 1)
        {
            var data = overrides[0].ReadSubrecords().Single(field => field.Signature == "DATA").Data;
            if (data.Length != 4) throw new InvalidDataException($"Float GMST {name} does not contain a Float32.");
            var number = BinaryPrimitives.ReadSingleLittleEndian(data.Span);
            return float.IsFinite(number) ? number : throw new InvalidDataException($"Float GMST {name} is not finite.");
        }
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned game settings are unavailable.");
        var defaults = Defaults.GetValue(source, content =>
        {
            if (content.Game != RuntimeLiveContentSource.FalloutNewVegasGame)
                throw new NotSupportedException("This engine's executable default-float layout has not been admitted.");
            return FalloutExecutableStringTable.ReadFloatDefaults(Path.Combine(Path.GetDirectoryName(content.ContentRoot)!, "FalloutNV.exe"));
        });
        return defaults.TryGetValue(name, out var value) ? value :
            throw new NotSupportedException($"Owned executable default float setting is unbound: {name}.");
    }
}
