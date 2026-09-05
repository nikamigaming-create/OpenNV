using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutRegionWeatherSnapshot(FalloutFormKey Region, FalloutFormKey Weather);
internal sealed record FalloutSkyLightingSnapshot(FalloutFormKey Climate, IReadOnlyList<FalloutRegionWeatherSnapshot> Regions);

/// <summary>Shared sky/climate identity and region weather caches; renderers only sample this state.</summary>
internal sealed class FalloutSkyLightingState
{
    private readonly FalloutPluginStack _records;
    private readonly Dictionary<FalloutFormKey, FalloutFormKey> _regions = [];
    private readonly Dictionary<FalloutFormKey, FalloutWeatherLighting> _weather = [];
    private readonly float _daytimeExtension;
    private FalloutClimateLighting _climate;
    private string? _unbound;
    internal string? Unbound => _unbound;
    internal FalloutFormKey DefaultWeather { get; }
    internal FalloutClimateLighting Climate => _climate;
    internal float DaytimeExtension => _daytimeExtension;

    internal FalloutSkyLightingState(FalloutPluginStack records, float daytimeExtension)
    {
        _records = records;
        if (!float.IsFinite(daytimeExtension) || daytimeExtension < 0)
            throw new InvalidDataException("Sky daytime colour extension is invalid.");
        _daytimeExtension = daytimeExtension;
        // Reserved engine bootstrap forms use normal master/override resolution.
        // A region without an initialized weather cache uses the default weather,
        // rather than choosing a weighted REGN entry merely because a light loads.
        DefaultWeather = records.RuntimeFormKey(0x15e);
        _climate = FalloutClimateLighting.Read(records.GetEffective(records.RuntimeFormKey(0x15f)));
        _ = Weather(DefaultWeather);
    }

    internal FalloutSkyLightingSnapshot Capture()
    {
        RequireBound();
        return new(_climate.Form, _regions.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => new FalloutRegionWeatherSnapshot(pair.Key, pair.Value)).ToArray());
    }

    internal void Restore(FalloutSkyLightingSnapshot snapshot)
    {
        ValidateSnapshot(_records, snapshot);
        var climate = FalloutClimateLighting.Read(_records.GetEffective(snapshot.Climate));
        var regions = new Dictionary<FalloutFormKey, FalloutFormKey>();
        foreach (var row in snapshot.Regions)
        {
            RequireRegion(row.Region);
            _ = Weather(row.Weather);
            if (!regions.TryAdd(row.Region, row.Weather)) throw new InvalidDataException("Saved sky repeats a region weather cache.");
        }
        _climate = climate;
        _regions.Clear();
        foreach (var row in regions) _regions.Add(row.Key, row.Value);
        _unbound = null;
    }

    internal static void ValidateSnapshot(FalloutPluginStack records, FalloutSkyLightingSnapshot snapshot)
    {
        _ = FalloutClimateLighting.Read(records.GetEffective(snapshot.Climate));
        if (snapshot.Regions is null) throw new InvalidDataException("Saved sky has no region cache collection.");
        var seen = new HashSet<FalloutFormKey>();
        foreach (var row in snapshot.Regions)
        {
            if (!seen.Add(row.Region) || records.GetEffective(row.Region).Signature != "REGN")
                throw new InvalidDataException("Saved sky repeats a region or refers to another record type.");
            _ = FalloutWeatherLighting.Read(records.GetEffective(row.Weather));
        }
    }

    internal void EnterCell(FalloutCellDefinition cell)
    {
        if ((cell.Flags & 1) == 0)
        {
            MarkUnbound("Exterior climate, weather selection and transitions have no runtime owner yet.");
            return;
        }
        var fields = _records.GetEffective(cell.FormKey).ReadSubrecords().Where(field => field.Signature == "XCCM").ToArray();
        if (fields.Length > 1) throw new InvalidDataException("CELL repeats its climate override.");
        if (fields.Length == 0) return; // An ordinary interior retains the shared sky's existing climate.
        if (fields[0].Data.Length != 4) throw new InvalidDataException("CELL climate override requires one FormID.");
        var record = _records.GetEffective(cell.FormKey);
        var form = record.Plugin.AdjustOptionalFormId(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span));
        if (form is { } value) _climate = FalloutClimateLighting.Read(_records.GetEffective(value));
    }

    internal void MarkUnbound(string reason) => _unbound = reason;

    internal float[] RegionEmittance(FalloutFormKey region, float gameHour)
    {
        RequireBound();
        RequireRegion(region);
        var weather = Weather(_regions.GetValueOrDefault(region, DefaultWeather));
        return weather.Sample(FalloutWeatherTimeWeights.Sample(_climate, gameHour, _daytimeExtension));
    }

    private FalloutWeatherLighting Weather(FalloutFormKey key)
    {
        if (!_weather.TryGetValue(key, out var weather))
            _weather.Add(key, weather = FalloutWeatherLighting.Read(_records.GetEffective(key)));
        return weather;
    }

    private void RequireRegion(FalloutFormKey key)
    {
        if (_records.GetEffective(key).Signature != "REGN") throw new InvalidDataException("Region emittance requires REGN.");
    }

    private void RequireBound()
    {
        if (_unbound is not null) throw new NotSupportedException(_unbound);
    }
}
