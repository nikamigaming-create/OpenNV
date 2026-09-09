namespace OpenNV.Runtime.Content;

internal sealed record FalloutShellCasing(float Speed, float DirectionVariation, float RotationDegrees,
    float RotationVariation, float Lifetime, float CameraDistance)
{
    internal static FalloutShellCasing Read(FalloutPluginStack records)
    {
        float Setting(string suffix) => FalloutGameSettingFloats.Read(records, "fGunShell" + suffix);
        var result = new FalloutShellCasing(Setting("EjectSpeed"), Setting("DirectionRandomize"), Setting("RotateSpeed"),
            Setting("RotateRandomize"), Setting("Lifetime"), Setting("CameraDistance"));
        if (result.Speed < 0 || result.DirectionVariation < 0 || result.RotationVariation < 0 || result.Lifetime <= 0 || result.CameraDistance < 0)
            throw new InvalidDataException("Source casing settings contain an invalid range.");
        return result;
    }
}
