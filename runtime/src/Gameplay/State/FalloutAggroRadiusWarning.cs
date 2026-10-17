using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutAggroRadiusWarning(FalloutFormKey Target, double Seconds = 0,
    int Warnings = 1, double RemainingSeconds = 0, string? Animation = null, string? AnimationSha256 = null)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Target.OwnerPlugin) || Target.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            !double.IsFinite(Seconds) || Seconds < 0 || Warnings < 1 ||
            !double.IsFinite(RemainingSeconds) || RemainingSeconds < 0 ||
            (Animation is null) != (AnimationSha256 is null) || Animation is not null && string.IsNullOrWhiteSpace(Animation) ||
            AnimationSha256 is not null && (AnimationSha256.Length != 64 || !AnimationSha256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Aggro radius warning state is invalid.");
    }

    internal (FalloutAggroRadiusWarning State, bool Attack) Advance(double delta, float interval, int maximumWarnings)
    {
        Validate();
        if (!double.IsFinite(delta) || delta < 0 || !float.IsFinite(interval) || interval <= 0 || maximumWarnings < 0)
            throw new InvalidDataException("Aggro radius warning timing is invalid.");
        var remaining = RemainingSeconds - delta;
        var warnings = Warnings;
        while (remaining <= 0 && warnings < maximumWarnings)
        {
            warnings++;
            remaining += interval;
        }
        var attack = maximumWarnings == 0 || remaining <= 0 && warnings >= maximumWarnings;
        return (this with { Seconds = Seconds + delta, Warnings = warnings, RemainingSeconds = Math.Max(0, remaining) }, attack);
    }
}
