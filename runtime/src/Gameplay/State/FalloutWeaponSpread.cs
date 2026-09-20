using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed class FalloutWeaponSpread
{
    private const float SourceSpreadDegreeScale = 5.955f;
    private readonly float _runningPenalty, _standingPenalty, _unaimedPenalty, _walkingPenalty;
    private readonly float _crippledArmOneHanded, _crippledArmTwoHanded;
    private readonly float _crippledArmsOneHanded, _crippledArmsTwoHanded;
    private readonly float _wobbleToSkill, _minimumGunSpread, _strengthRequirementPenalty;
    private readonly float _npcMaxGunWobbleAngle;

    internal FalloutWeaponSpread(FalloutPluginStack records)
    {
        _runningPenalty = FalloutGameSettingFloats.Read(records, "fRunningSpreadPenalty");
        _standingPenalty = FalloutGameSettingFloats.Read(records, "fStandingSpreadPenalty");
        _unaimedPenalty = FalloutGameSettingFloats.Read(records, "fUnaimedSpreadPenalty");
        _walkingPenalty = FalloutGameSettingFloats.Read(records, "fWalkingSpreadPenalty");
        _crippledArmOneHanded = FalloutGameSettingFloats.Read(records, "fCrippledArm1HSpreadPenalty");
        _crippledArmTwoHanded = FalloutGameSettingFloats.Read(records, "fCrippledArm2HSpreadPenalty");
        _crippledArmsOneHanded = FalloutGameSettingFloats.Read(records, "fCrippledArms1HSpreadPenalty");
        _crippledArmsTwoHanded = FalloutGameSettingFloats.Read(records, "fCrippledArms2HSpreadPenalty");
        _wobbleToSkill = FalloutGameSettingFloats.Read(records, "fWobbleToSkillConversion");
        _minimumGunSpread = FalloutGameSettingFloats.Read(records, "fMinGunSpreadValue");
        _strengthRequirementPenalty = FalloutGameSettingFloats.Read(records, "fWeapStrengthReqPenalty");
        _npcMaxGunWobbleAngle = FalloutGameSettingFloats.Read(records, "fNPCMaxGunWobbleAngle");
        if (!float.IsFinite(_npcMaxGunWobbleAngle) || _npcMaxGunWobbleAngle < 0)
            throw new InvalidDataException("NPC maximum gun wobble angle is invalid.");
    }

    internal float PlayerMedianDeviationDegrees(FalloutWeaponShot shot, float skill, float strength,
        bool aiming, bool moving, bool running, bool sneaking, bool leftArmCrippled, bool rightArmCrippled,
        IReadOnlyList<FalloutPerkEntry> perks, Func<FalloutCondition, float> evaluateCondition)
    {
        ArgumentNullException.ThrowIfNull(shot);
        ArgumentNullException.ThrowIfNull(perks);
        ArgumentNullException.ThrowIfNull(evaluateCondition);
        if (!float.IsFinite(skill) || skill is < 0 or > 100 || !float.IsFinite(strength) || strength < 0)
            throw new InvalidDataException("Weapon spread actor values are outside runtime bounds.");

        var oneHanded = shot.WeaponAnimationType is 3 or 4 or 10 or 13;
        var twoHanded = shot.WeaponAnimationType is 5 or 6 or 7 or 8 or 9;
        if (!oneHanded && !twoHanded)
            throw new NotSupportedException($"Weapon animation type {shot.WeaponAnimationType} has no source spread hand rule.");

        var armPenalty = oneHanded
            ? (rightArmCrippled ? _crippledArmOneHanded : 0) +
                (leftArmCrippled && rightArmCrippled ? _crippledArmsOneHanded : 0)
            : (leftArmCrippled || rightArmCrippled ? _crippledArmTwoHanded : 0) +
                (leftArmCrippled && rightArmCrippled ? _crippledArmsTwoHanded : 0);
        var movementPenalty = moving ? running ? _runningPenalty : _walkingPenalty : 0;
        var skillMultiplier = 1 - _wobbleToSkill * Math.Min(skill, 100) / 100;
        var strengthPenalty = Math.Max(0, shot.StrengthRequirement - Math.Min(strength, 10)) * _strengthRequirementPenalty;
        // New Vegas uses the strength-requirement setting here as well; the
        // separate skill-requirement setting is present but unused by the game.
        var skillRequirementPenalty = Math.Max(0, shot.SkillRequirement - Math.Min(skill, 100)) *
            .1f * _strengthRequirementPenalty;
        var playerSpread = Math.Max(_minimumGunSpread, skillMultiplier *
            (_unaimedPenalty * (aiming ? 0 : 1) + movementPenalty + _standingPenalty * (sneaking ? 0 : 1) +
                armPenalty + strengthPenalty + skillRequirementPenalty));

        foreach (var perk in perks.Where(perk => perk.Entry == 34))
        {
            perk.RequireActorConditionScope();
            if (perk.Conditions.Count != 0 && !FalloutCondition.AllPass(perk.Conditions, evaluateCondition)) continue;
            playerSpread = perk.Function switch
            {
                1 => perk.Value,
                2 => playerSpread + perk.Value,
                3 => playerSpread * perk.Value,
                _ => throw new NotSupportedException("Calculate Gun Spread perk operation is unbound.")
            };
        }

        var weaponSpread = shot.ResolvedMinimumSpread;
        var weaponAngle = .0125f * weaponSpread * weaponSpread + .125f * weaponSpread;
        var median = Math.Max(0, playerSpread + weaponAngle) * SourceSpreadDegreeScale;
        if (!float.IsFinite(median)) throw new InvalidDataException("Resolved weapon spread is non-finite.");
        return median;
    }

    internal float NpcMedianDeviationDegrees(FalloutWeaponShot shot, float skill, float strength,
        bool moving, bool running, bool sneaking, bool leftArmCrippled, bool rightArmCrippled)
    {
        var playerBase = PlayerMedianDeviationDegrees(shot, skill, strength, aiming: true,
            moving: moving, running: running, sneaking: sneaking, leftArmCrippled: leftArmCrippled,
            rightArmCrippled: rightArmCrippled, Array.Empty<FalloutPerkEntry>(),
            static _ => throw new InvalidOperationException("NPC weapon spread cannot evaluate player perk conditions."));
        var median = playerBase * (1 + _npcMaxGunWobbleAngle);
        if (!float.IsFinite(median)) throw new InvalidDataException("Resolved NPC weapon spread is non-finite.");
        return median;
    }

    // Spread values describe the median radius. Sampling uniformly to twice
    // that radius preserves the authored median while keeping a round cone.
    internal static Vector3 Deviate(Vector3 center, float medianSpreadDegrees, Func<float> nextUnit)
    {
        ArgumentNullException.ThrowIfNull(nextUnit);
        if (!IsFinite(center) || center.LengthSquared() <= float.Epsilon ||
            !float.IsFinite(medianSpreadDegrees) || medianSpreadDegrees < 0)
            throw new InvalidDataException("Weapon spread input is invalid.");
        var forward = center.Normalized();
        if (medianSpreadDegrees == 0) return forward;

        var angleUnit = nextUnit();
        var rotationUnit = nextUnit();
        if (!float.IsFinite(angleUnit) || angleUnit is < 0 or > 1 ||
            !float.IsFinite(rotationUnit) || rotationUnit is < 0 or > 1)
            throw new InvalidDataException("Weapon spread random sample is invalid.");

        var helper = Mathf.Abs(forward.Dot(Vector3.Up)) < .99f ? Vector3.Up : Vector3.Right;
        var right = forward.Cross(helper).Normalized();
        var up = right.Cross(forward).Normalized();
        var angle = Mathf.DegToRad(medianSpreadDegrees * 2 * angleUnit);
        var rotation = MathF.Tau * rotationUnit;
        var sine = Mathf.Sin(angle);
        var result = forward * Mathf.Cos(angle) +
            right * (Mathf.Cos(rotation) * sine) +
            up * (Mathf.Sin(rotation) * sine);
        if (!IsFinite(result) || result.LengthSquared() <= float.Epsilon)
            throw new InvalidDataException("Weapon spread produced an invalid direction.");
        return result.Normalized();
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
