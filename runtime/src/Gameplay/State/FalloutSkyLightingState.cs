using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutRegionWeatherSnapshot(FalloutFormKey Region, FalloutFormKey Weather);
internal sealed record FalloutSkyLightingSnapshot(FalloutFormKey Climate, IReadOnlyList<FalloutRegionWeatherSnapshot> Regions,
    FalloutFormKey? ForcedWeather = null, FalloutFormKey? ExteriorWeather = null, ulong? RandomState = null,
    FalloutFormKey? ClimateWeather = null);

/// <summary>Shared sky/climate identity and region weather caches; renderers only sample this state.</summary>
internal sealed class FalloutSkyLightingState
{
    private readonly FalloutPluginStack _records;
    private readonly Dictionary<FalloutFormKey, FalloutFormKey> _regions = [];
    private readonly Dictionary<FalloutFormKey, FalloutWeatherLighting> _weather = [];
    private readonly float _daytimeExtension;
    private FalloutClimateLighting _climate;
    private string? _unbound;
    private readonly FalloutSoundRandomState _random = new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
    internal FalloutFormKey? ExteriorWeather { get; private set; }
    private FalloutFormKey? _climateWeather;
    internal FalloutWeatherLighting ActiveWeather => Weather(ForcedWeather ?? ExteriorWeather ?? DefaultWeather);
    internal string? Unbound => _unbound;
    internal FalloutFormKey DefaultWeather { get; }
    internal FalloutClimateLighting Climate => _climate;
    internal float DaytimeExtension => _daytimeExtension;
    internal FalloutFormKey? ForcedWeather { get; private set; }
    internal void ForceWeather(FalloutFormKey weather)
    {
        _ = Weather(weather);
        ForcedWeather = weather;
    }
    internal void ReleaseWeatherOverride() => ForcedWeather = null;

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
            .Select(pair => new FalloutRegionWeatherSnapshot(pair.Key, pair.Value)).ToArray(), ForcedWeather, ExteriorWeather, _random.State, _climateWeather);
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
        ForcedWeather = snapshot.ForcedWeather;
        ExteriorWeather = snapshot.ExteriorWeather;
        _climateWeather = snapshot.ClimateWeather;
        if (snapshot.RandomState is { } random) _random.Restore(random);
        _regions.Clear();
        foreach (var row in regions) _regions.Add(row.Key, row.Value);
        _unbound = null;
    }

    internal static void ValidateSnapshot(FalloutPluginStack records, FalloutSkyLightingSnapshot snapshot)
    {
        _ = FalloutClimateLighting.Read(records.GetEffective(snapshot.Climate));
        if (snapshot.ForcedWeather is { } forced) _ = FalloutWeatherLighting.Read(records.GetEffective(forced));
        if (snapshot.ClimateWeather is { } climateWeather) _ = FalloutWeatherLighting.Read(records.GetEffective(climateWeather));
        if (snapshot.ExteriorWeather is { } exterior)
        {
            _ = FalloutWeatherLighting.Read(records.GetEffective(exterior));
            if (snapshot.RandomState is null) throw new InvalidDataException("Saved exterior sky has no random continuation.");
        }
        if (snapshot.Regions is null) throw new InvalidDataException("Saved sky has no region cache collection.");
        var seen = new HashSet<FalloutFormKey>();
        foreach (var row in snapshot.Regions)
        {
            if (!seen.Add(row.Region) || records.GetEffective(row.Region).Signature != "REGN")
                throw new InvalidDataException("Saved sky repeats a region or refers to another record type.");
            _ = FalloutWeatherLighting.Read(records.GetEffective(row.Weather));
        }
    }

    internal void EnterCell(FalloutCellDefinition cell, FalloutGlobalState? globals = null, float[]? position = null)
    {
        if ((cell.Flags & 1) == 0)
        {
            var world = cell.Worldspace ?? throw new InvalidDataException("Exterior CELL has no worldspace.");
            var climate = FalloutExteriorClimate.Resolve(_records, world);
            var changed = _climate.Form != climate;
            _climate = FalloutClimateLighting.Read(_records.GetEffective(climate));
            if (changed || _climateWeather is null)
                _climateWeather = FalloutExteriorClimate.SelectWeather(_records, climate, globals, _random);
            ExteriorWeather = _climateWeather;
            if (position is { Length: 3 } && FalloutExteriorClimate.WeatherRegion(_records, cell, position[0], position[1]) is { } region)
            {
                if (!_regions.TryGetValue(region, out var selected))
                    _regions.Add(region, selected = FalloutExteriorClimate.SelectWeather(_records, region, globals, _random));
                ExteriorWeather = selected;
            }
            _unbound = null;
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
        var weather = Weather(ForcedWeather ?? _regions.GetValueOrDefault(region, DefaultWeather));
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
