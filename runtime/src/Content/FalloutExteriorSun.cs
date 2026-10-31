using System.Numerics;

namespace OpenNV.Runtime.Content;

// Climate intervals and winning game settings own the outdoor directional
// light. Its trajectory is independent of the sun sprite's visibility/glare.
internal sealed record FalloutExteriorSun(float TransitionHours, float XExtreme, float YExtreme, float ZExtreme)
{
    internal static FalloutExteriorSun Read(FalloutPluginStack records)
    {
        float Setting(string name) => FalloutGameSettingFloats.ReadRetained(records, name, nameof(FalloutExteriorSun));
        return new(Setting("fSunAlphaTransTime"), Setting("fSunXExtreme"),
            Setting("fSunYExtreme"), Setting("fSunZExtreme"));
    }

    internal Vector3 RayDirection(FalloutClimateLighting climate, float hour)
    {
        var dawn = (climate.SunriseStart + climate.SunriseEnd) * .5f - TransitionHours * .5f;
        var dusk = (climate.SunsetStart + climate.SunsetEnd) * .5f + TransitionHours * .5f;
        var day = dusk - dawn;
        var night = 24f - day;
        if (!float.IsFinite(hour) || hour < 0 || hour > 24 ||
            !float.IsFinite(TransitionHours) || TransitionHours < 0 ||
            !float.IsFinite(dawn) || !float.IsFinite(dusk) || day <= 0 || night <= 0 ||
            !float.IsFinite(XExtreme) || !float.IsFinite(YExtreme) || !float.IsFinite(ZExtreme))
            throw new InvalidDataException("Exterior sun settings or climate intervals are invalid.");
        var x = hour >= dawn && hour <= dusk
            ? XExtreme * (1f - 2f * (hour - dawn) / day)
            : XExtreme * (2f * (hour > dusk ? hour - dusk : hour + 24f - dusk) / night - 1f);
        var ray = new Vector3(-x, YExtreme, ZExtreme);
        if (!float.IsFinite(ray.LengthSquared()) || ray.LengthSquared() <= 0)
            throw new InvalidDataException("Exterior sun direction is not finite and nonzero.");
        return Vector3.Normalize(ray);
    }
}
