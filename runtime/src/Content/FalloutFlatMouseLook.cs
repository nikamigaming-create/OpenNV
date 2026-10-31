namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutMouseLookScale(float Horizontal, float Vertical);

// Native flat look follows the effective profile and the camera owner's
// reference-aspect FOV, including sight zoom. Tracked poses are independent.
internal sealed class FalloutFlatMouseLook(FalloutNumericIniSettings settings, float defaultReferenceFov)
{
    internal float Sensitivity { get; } = settings.Float("fMouseSensitivity:Controls");
    internal bool InvertY { get; } = settings.Boolean("bInvertYValues:Controls");
    internal float DefaultReferenceFov { get; } = defaultReferenceFov;

    internal FalloutMouseLookScale Resolve(float currentReferenceFov)
    {
        if (!float.IsFinite(Sensitivity) || Sensitivity < 0 ||
            !float.IsFinite(DefaultReferenceFov) || DefaultReferenceFov <= 0 || DefaultReferenceFov >= 180 ||
            !float.IsFinite(currentReferenceFov) || currentReferenceFov <= 0 || currentReferenceFov >= 180)
            throw new InvalidDataException("Owned flat mouse sensitivity or camera FOV is invalid.");
        var gain = Sensitivity * MathF.Min(1, currentReferenceFov / DefaultReferenceFov);
        return new(gain, InvertY ? -gain : gain);
    }
}
