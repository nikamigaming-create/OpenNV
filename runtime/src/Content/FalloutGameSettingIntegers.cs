using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Content;

internal static class FalloutGameSettingIntegers
{
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, IReadOnlyDictionary<string, uint>> Defaults = new();

    internal static uint Read(FalloutPluginStack records, string name)
    {
        var overrides = records.EffectiveRecords("GMST").Where(record => record.ReadSubrecords().Any(field =>
            field.Signature == "EDID" && FalloutDialogueTopic.Text(field.Data.Span).Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (overrides.Length > 1) throw new InvalidDataException($"Multiple winning GMST identities have EDID {name}.");
        if (overrides.Length == 1)
        {
            var data = overrides[0].ReadSubrecords().Single(field => field.Signature == "DATA").Data;
            if (data.Length != 4) throw new InvalidDataException($"Integer GMST {name} does not contain a DWORD.");
            return BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
        }
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned game settings are unavailable.");
        var defaults = Defaults.GetValue(source, ReadDefaults);
        return defaults.TryGetValue(name, out var value) ? value :
            throw new NotSupportedException($"Owned executable default integer setting is unbound: {name}.");
    }

    private static IReadOnlyDictionary<string, uint> ReadDefaults(RuntimeLiveContentSource source)
    {
        if (source.Game != RuntimeLiveContentSource.FalloutNewVegasGame)
            throw new NotSupportedException("This engine's executable default-integer layout has not been admitted.");
        return FalloutExecutableStringTable.ReadIntegerDefaults(Path.Combine(Path.GetDirectoryName(source.ContentRoot)!, "FalloutNV.exe"));
    }
}
