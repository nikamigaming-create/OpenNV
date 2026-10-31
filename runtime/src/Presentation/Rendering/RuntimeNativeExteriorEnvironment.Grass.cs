using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeExteriorEnvironment
{
    private FalloutFormKey? _grassWeather;
    private float _grassWind;

    internal RuntimeNativeGrassFrame GrassFrame(Camera3D camera, FalloutInstallationSettings settings, float start, float fade)
    {
        var weather = _sky.ActiveWeather;
        if (_grassWeather != weather.Form)
        {
            _grassWind = FalloutWeatherMotion.Read(_records.GetEffective(weather.Form),
                FalloutGameSettingFloats.ReadRetained(_records, "fWeatherCloudSpeedMax", nameof(RuntimeNativeGrassOwner))).WindSpeed;
            _grassWeather = weather.Form;
        }
        var hour = _hour();
        var weights = FalloutWeatherTimeWeights.Sample(_sky.Climate, hour, _sky.DaytimeExtension, weather.TimeSamples);
        var color = weather.Sample(weights, 4);
        var ray = _sunSource.RayDirection(_sky.Climate, hour);
        var minimum = settings.Number("Grass", "fGrassWindMagnitudeMin");
        var maximum = settings.Number("Grass", "fGrassWindMagnitudeMax");
        // The shared sky commits weather immediately. This follows that owner;
        // weather cross-fade support remains a shared sky capability gap.
        return new(hour, new(0, 1), minimum + (maximum - minimum) * _grassWind,
            _ambient, new(-ray.X, -ray.Y, -ray.Z), new(color[0], color[1], color[2]), ImageSpace?.RawTraits[11] ?? 1,
            _fog, new(_range.Y, _range.Y - _range.X, _range.Z), new(start, fade),
            new(camera.Near / _units, camera.Far / _units));
    }
}
