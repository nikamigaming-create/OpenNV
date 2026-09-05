namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutCameraProjection(float VerticalFovDegrees, float NearGameUnits)
{
    internal static FalloutCameraProjection Read(FalloutInstallationSettings settings) => FromReferenceFov(
        settings.Number("Display", settings.Contains("Display", "fDefaultWorldFOV") ? "fDefaultWorldFOV" : "fDefaultFOV"),
        settings.Number("Display", "fNearDistance"));

    // Native world NiCamera frustum slopes and world-to-clip row magnitudes
    // corroborate a horizontal FOV at the engine's 4:3 reference aspect.
    // This is projection policy, independent of a CELL or camera animation.
    internal static FalloutCameraProjection FromReferenceFov(float horizontalDegrees, float nearGameUnits)
    {
        if (!float.IsFinite(horizontalDegrees) || horizontalDegrees <= 0 || horizontalDegrees >= 180 ||
            !float.IsFinite(nearGameUnits) || nearGameUnits <= 0)
            throw new InvalidDataException("Owned world-camera FOV/near distance is invalid.");
        var vertical = 2 * MathF.Atan(MathF.Tan(horizontalDegrees * MathF.PI / 360) * (3f / 4f)) * 180 / MathF.PI;
        return new(vertical, nearGameUnits);
    }
}
