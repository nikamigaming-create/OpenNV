using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutPluginStack? _limbRecords;
    private Func<GameplayVitals?>? _limbVitals;
    private int? _crippledLegCount;
    private float _crippledLegSpeedMultiplier = 1;
    private string? _crippledLegMovementError;
    private bool _limbMovementStatePublished;
    private int _publishedCrippledLegCount;
    private float _publishedCrippledLegSpeedMultiplier;
    private string? _publishedCrippledLegMovementError;

    internal object LimbMovementState => new
    {
        crippledLegs = _crippledLegCount,
        speedMultiplier = _crippledLegSpeedMultiplier,
        error = _crippledLegMovementError
    };

    private void ConfigureLimbLocomotion(FalloutPluginStack records, Func<GameplayVitals?> vitals)
    {
        _limbRecords = records ?? throw new ArgumentNullException(nameof(records));
        _limbVitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
    }

    private (bool Left, bool Right) CrippledArms(GameplayVitals vitals)
    {
        var records = _limbRecords ?? throw new InvalidOperationException("Player body-part source is absent.");
        var parts = FalloutBodyPartData.Read(records.GetEffective(records.RuntimeFormKey(0x1d))).Parts;
        var left = parts.Where(part => part.Type is 3 or 4).ToArray();
        var right = parts.Where(part => part.Type is 5 or 6).ToArray();
        if (left.Length == 0 || right.Length == 0)
            throw new NotSupportedException("Weapon spread requires source left and right arm parts.");
        bool Crippled(IEnumerable<FalloutBodyPart> arm) => arm.Any(part =>
        {
            var threshold = vitals.MaximumHitPoints * part.HealthPercent / 100.0f;
            return threshold > 0 && (vitals.LimbDamage?.GetValueOrDefault(part.Type) ?? 0) >= threshold;
        });
        return (Crippled(left), Crippled(right));
    }

    private void PublishLimbMovementState()
    {
        var count = _crippledLegCount ?? -1;
        var error = _crippledLegMovementError ?? string.Empty;
        if (_limbMovementStatePublished && _publishedCrippledLegCount == count &&
            _publishedCrippledLegSpeedMultiplier == _crippledLegSpeedMultiplier && _publishedCrippledLegMovementError == error) return;
        SetMeta("opennv_crippled_leg_count", count);
        SetMeta("opennv_crippled_leg_speed_multiplier", _crippledLegSpeedMultiplier);
        SetMeta("opennv_crippled_leg_movement_error", error);
        _publishedCrippledLegCount = count;
        _publishedCrippledLegSpeedMultiplier = _crippledLegSpeedMultiplier;
        _publishedCrippledLegMovementError = error;
        _limbMovementStatePublished = true;
    }

}
