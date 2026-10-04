using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Flat camera zoom uses the winning WEAP angle and the shared setting clock.
// Headset projection remains owned by OpenXR, independently of this flat view.
internal sealed class FalloutWeaponSight(FalloutPluginStack records, float worldReferenceDegrees)
{
    private float _fromDegrees = RequireFov(worldReferenceDegrees);
    private float _targetDegrees = RequireFov(worldReferenceDegrees);
    private double _elapsed;
    internal float HorizontalDegrees { get; private set; } = RequireFov(worldReferenceDegrees);
    internal float TargetDegrees => _targetDegrees;
    internal float TransitionSeconds { get; private set; }

    internal static bool CanAim(FalloutWeaponPresentation? weapon) => weapon is
    {
        SightFieldOfViewDegrees: > 0,
        IsMeleeWeapon: false, IsThrownWeapon: false, IsMine: false
    };

    internal float Advance(double seconds, FalloutWeaponPresentation? weapon, bool aiming)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new InvalidDataException("Weapon sight clock is invalid.");
        var sight = weapon?.SightFieldOfViewDegrees ?? 0;
        if (!float.IsFinite(sight) || sight < 0 || sight >= 180)
            throw new InvalidDataException("Weapon sight field of view is invalid.");
        // GECK's 1..5 sentinel range enables aiming without narrowing the view.
        var target = aiming && CanAim(weapon) && sight > 5 && sight != 75 ? sight : worldReferenceDegrees;
        var duration = FalloutGameSettingFloats.Read(records, "fIronSightsFOVTimeChange");
        if (!float.IsFinite(duration) || duration < 0) throw new InvalidDataException("Weapon sight transition setting is invalid.");
        if (_targetDegrees != target)
        {
            _fromDegrees = HorizontalDegrees;
            _targetDegrees = target;
            _elapsed = 0;
        }
        TransitionSeconds = duration;
        _elapsed += seconds;
        var fraction = duration == 0 ? 1 : Math.Min(1, _elapsed / duration);
        HorizontalDegrees = (float)(_fromDegrees + (_targetDegrees - _fromDegrees) * fraction);
        return HorizontalDegrees;
    }

    private static float RequireFov(float degrees) => float.IsFinite(degrees) && degrees > 0 && degrees < 180
        ? degrees : throw new InvalidDataException("Weapon sight world projection is invalid.");
}
