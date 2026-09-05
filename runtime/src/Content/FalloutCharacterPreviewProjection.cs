using System.Numerics;

namespace OpenNV.Runtime.Content;

// Character-creation camera policy. These endpoints are engine conventions,
// independent of any NPC or cell. The actor supplies its authored height, and
// zoom comes from the owned Interface setting or the current user input.
internal readonly record struct FalloutCharacterPreviewProjection(Vector3 Translation, float Rotation, float Slope)
{
    internal static FalloutCharacterPreviewProjection Read(FalloutInstallationSettings settings, float actorHeight,
        float zoom, float rotation)
    {
        if (!float.IsFinite(actorHeight) || actorHeight <= 0 || !float.IsFinite(zoom) ||
            zoom is < 0 or > 1 || !float.IsFinite(rotation)) throw new ArgumentOutOfRangeException(nameof(zoom));
        var point = Vector3.Lerp(new(-4.7f, 55, -0.84f), new(-11.4f, 135, -0.8f), zoom);
        point.Z *= actorHeight;
        var fov = settings.Number("Display", "fDefaultFOV") * settings.Number("RenderedTerminal", "fRenderedTerminalFOV");
        if (!float.IsFinite(fov) || fov is <= 0 or >= 90) throw new InvalidDataException("Character preview FOV is invalid.");
        return new(point, rotation, MathF.Tan(fov * MathF.PI / 180));
    }
}
