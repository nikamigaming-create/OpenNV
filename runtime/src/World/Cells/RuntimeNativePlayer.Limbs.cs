using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutPluginStack? _limbRecords;
    private Func<GameplayVitals?>? _limbVitals;
    private FalloutBodyPart? _leftLeg;
    private FalloutBodyPart? _rightLeg;
    private (float One, float Two)? _crippledLegSpeedSettings;
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
        leftLegPart = _leftLeg is null ? null : new { _leftLeg.Type, _leftLeg.Name, _leftLeg.HealthPercent },
        rightLegPart = _rightLeg is null ? null : new { _rightLeg.Type, _rightLeg.Name, _rightLeg.HealthPercent },
        error = _crippledLegMovementError
    };

    private void ConfigureLimbLocomotion(FalloutPluginStack records, Func<GameplayVitals?> vitals)
    {
        _limbRecords = records ?? throw new ArgumentNullException(nameof(records));
        _limbVitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
    }

    private float CrippledLegMovementSpeedMultiplier()
    {
        if (_limbRecords is null || _limbVitals?.Invoke() is not { } vitals) return 1;
        if (_crippledLegMovementError is not null) return 1;

        try
        {
            var records = _limbRecords;
            if (_leftLeg is null || _rightLeg is null)
            {
                var parts = FalloutBodyPartData.Read(records.GetEffective(records.RuntimeFormKey(0x1d))).Parts;
                var left = parts.Where(part => part.HealthPercent > 0 && PlayerBodyRegion(part) == "leftleg").ToArray();
                var right = parts.Where(part => part.HealthPercent > 0 && PlayerBodyRegion(part) == "rightleg").ToArray();
                if (left.Length != 1 || right.Length != 1)
                    throw new NotSupportedException("Player movement requires one source part for each leg.");
                _leftLeg = left[0];
                _rightLeg = right[0];
            }

            var leftThreshold = vitals.MaximumHitPoints * _leftLeg.HealthPercent / 100.0f;
            var rightThreshold = vitals.MaximumHitPoints * _rightLeg.HealthPercent / 100.0f;
            var limbDamage = vitals.LimbDamage;
            var count = (leftThreshold > 0 && (limbDamage?.GetValueOrDefault(_leftLeg.Type) ?? 0) >= leftThreshold ? 1 : 0) +
                (rightThreshold > 0 && (limbDamage?.GetValueOrDefault(_rightLeg.Type) ?? 0) >= rightThreshold ? 1 : 0);
            _crippledLegCount = count;
            if (count == 0)
            {
                _crippledLegSpeedMultiplier = 1;
                _crippledLegMovementError = null;
                PublishLimbMovementState();
                return 1;
            }

            _crippledLegSpeedSettings ??= (
                FalloutGameSettingFloats.Read(records, "fMoveOneCrippledLegSpeedMult"),
                FalloutGameSettingFloats.Read(records, "fMoveTwoCrippledLegsSpeedMult"));
            var multiplier = count == 1 ? _crippledLegSpeedSettings.Value.One : _crippledLegSpeedSettings.Value.Two;
            if (!float.IsFinite(multiplier) || multiplier < 0)
                throw new InvalidDataException("Source crippled-leg movement multiplier is invalid.");
            _crippledLegSpeedMultiplier = multiplier;
            _crippledLegMovementError = null;
            PublishLimbMovementState();
            return multiplier;
        }
        catch (Exception error)
        {
            _crippledLegCount = null;
            _crippledLegSpeedMultiplier = 1;
            _crippledLegMovementError = error.Message;
            PublishLimbMovementState();
            return 1;
        }
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

    private static string? PlayerBodyRegion(FalloutBodyPart part)
    {
        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        var name = Normalize(part.Name);
        var node = Normalize(part.Node);
        var left = name.Contains("left", StringComparison.Ordinal) || node.Contains("bip01l", StringComparison.Ordinal) ||
            name.StartsWith("lleg", StringComparison.Ordinal);
        var right = name.Contains("right", StringComparison.Ordinal) || node.Contains("bip01r", StringComparison.Ordinal) ||
            name.StartsWith("rleg", StringComparison.Ordinal);
        var leg = name.Contains("leg", StringComparison.Ordinal) || name.Contains("thigh", StringComparison.Ordinal) ||
            name.Contains("calf", StringComparison.Ordinal) || name.Contains("foot", StringComparison.Ordinal) ||
            node.Contains("thigh", StringComparison.Ordinal) || node.Contains("calf", StringComparison.Ordinal) ||
            node.Contains("foot", StringComparison.Ordinal);
        return leg && left ? "leftleg" : leg && right ? "rightleg" : null;
    }
}
