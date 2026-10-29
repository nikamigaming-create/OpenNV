using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed class FalloutWeaponSpread
{
    private const float SourceSpreadDegreeScale = 5.955f;
    private readonly FalloutPluginStack _records;
    private float RunningPenalty => Setting("fRunningSpreadPenalty");
    private float StandingPenalty => Setting("fStandingSpreadPenalty");
    private float UnaimedPenalty => Setting("fUnaimedSpreadPenalty");
    private float WalkingPenalty => Setting("fWalkingSpreadPenalty");
    private float CrippledArmOneHanded => Setting("fCrippledArm1HSpreadPenalty");
    private float CrippledArmTwoHanded => Setting("fCrippledArm2HSpreadPenalty");
    private float CrippledArmsOneHanded => Setting("fCrippledArms1HSpreadPenalty");
    private float CrippledArmsTwoHanded => Setting("fCrippledArms2HSpreadPenalty");
    private float WobbleToSkill => Setting("fWobbleToSkillConversion");
    private float MinimumGunSpread => Setting("fMinGunSpreadValue");
    private float StrengthRequirementPenalty => Setting("fWeapStrengthReqPenalty");
    private float Setting(string name) => FalloutGameSettingFloats.Read(_records, name);

    internal FalloutWeaponSpread(FalloutPluginStack records)
    {
        _records = records;
        _ = new[] { RunningPenalty, StandingPenalty, UnaimedPenalty, WalkingPenalty, CrippledArmOneHanded,
            CrippledArmTwoHanded, CrippledArmsOneHanded, CrippledArmsTwoHanded, WobbleToSkill, MinimumGunSpread, StrengthRequirementPenalty };
        _ = NpcMaximumGunWobbleAngle();
    }

    private float NpcMaximumGunWobbleAngle()
    {
        var angle = Setting("fNPCMaxGunWobbleAngle");
        return angle >= 0 ? angle : throw new InvalidDataException("NPC maximum gun wobble angle is invalid.");
    }

    internal float PlayerMedianDeviationDegrees(FalloutWeaponShot shot, float skill, float strength,
        bool aiming, bool moving, bool running, bool sneaking, bool leftArmCrippled, bool rightArmCrippled,
        IReadOnlyList<FalloutPerkEntry> perks, Func<FalloutCondition, float> evaluateCondition)
    {
        var playerSpread = ActorPenalty(shot, skill, strength, aiming, moving, running, sneaking,
            leftArmCrippled, rightArmCrippled, perks, evaluateCondition);
        var weaponSpread = shot.ResolvedMinimumSpread;
        var weaponAngle = .0125f * weaponSpread * weaponSpread + .125f * weaponSpread;
        var median = Math.Max(0, playerSpread + weaponAngle) * SourceSpreadDegreeScale;
        if (!float.IsFinite(median)) throw new InvalidDataException("Resolved weapon spread is non-finite.");
        return median;
    }

    private float ActorPenalty(FalloutWeaponShot shot, float skill, float strength,
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

        // One-arm and both-arm settings are alternative states, not additive
        // penalties. This owner serves both player modes and NPC weapons.
        var bothArms = leftArmCrippled && rightArmCrippled;
        var armPenalty = oneHanded
            ? bothArms ? CrippledArmsOneHanded : rightArmCrippled ? CrippledArmOneHanded : 0
            : bothArms ? CrippledArmsTwoHanded : leftArmCrippled || rightArmCrippled ? CrippledArmTwoHanded : 0;
        var movementPenalty = moving ? running ? RunningPenalty : WalkingPenalty : 0;
        var skillMultiplier = 1 - WobbleToSkill * Math.Min(skill, 100) / 100;
        var strengthPenalty = Math.Max(0, shot.StrengthRequirement - Math.Min(strength, 10)) * StrengthRequirementPenalty;
        // New Vegas uses the strength-requirement setting here as well; the
        // separate skill-requirement setting is present but unused by the game.
        var skillRequirementPenalty = Math.Max(0, shot.SkillRequirement - Math.Min(skill, 100)) *
            .1f * StrengthRequirementPenalty;
        var playerSpread = Math.Max(MinimumGunSpread, skillMultiplier *
            (UnaimedPenalty * (aiming ? 0 : 1) + movementPenalty + StandingPenalty * (sneaking ? 0 : 1) +
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

        if (!float.IsFinite(playerSpread)) throw new InvalidDataException("Actor spread penalty is non-finite.");
        return playerSpread;
    }

    internal float PlayerVatsWobbleDegrees(FalloutWeaponShot shot, float skill, float strength,
        bool leftArmCrippled, bool rightArmCrippled, IReadOnlyList<FalloutPerkEntry> perks,
        Func<FalloutCondition, float> evaluateCondition) => ActorPenalty(shot, skill, strength,
        aiming: true, moving: false, running: false, sneaking: false, leftArmCrippled, rightArmCrippled,
        perks, evaluateCondition) * NpcMaximumGunWobbleAngle();

    internal float NpcLaunchExtentRadians(FalloutWeaponShot shot, float skill, float strength,
        bool moving, bool running, bool sneaking, bool leftArmCrippled, bool rightArmCrippled,
        FalloutActorProjectileLaunch process, IReadOnlyList<FalloutPerkEntry> perks)
    {
        ArgumentNullException.ThrowIfNull(process);
        process.Validate();
        var penalty = ActorPenalty(shot, skill, strength, true, moving, running, sneaking,
            leftArmCrippled, rightArmCrippled, perks,
            static _ => throw new NotSupportedException("NPC launch perk condition evaluation is unbound."));
        var extent = Mathf.DegToRad(shot.MinimumSpread + shot.Spread * process.LegacySpreadFactor);
        foreach (var effect in shot.AmmoEffects.Where(effect => effect.Type == FalloutAmmoEffect.Spread))
            extent = effect.Apply(extent);
        extent += Mathf.DegToRad(penalty * NpcMaximumGunWobbleAngle());
        extent += process.AimChaseRadians;
        if (!float.IsFinite(extent)) throw new InvalidDataException("NPC launch extent is non-finite.");
        return extent;
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

    internal static Vector3 DeviateLaunch(Vector3 center, float angularExtentRadians, Func<float> nextUnit)
    {
        ArgumentNullException.ThrowIfNull(nextUnit);
        if (!IsFinite(center) || center.LengthSquared() <= float.Epsilon ||
            !float.IsFinite(angularExtentRadians))
            throw new InvalidDataException("Projectile launch spread input is invalid.");

        var direction = center.Normalized();
        if (!IsFinite(direction) || direction.LengthSquared() <= float.Epsilon)
            throw new InvalidDataException("Projectile launch direction cannot be normalized.");
        // Launch consumes both draws even when the angular extent is zero.
        var radiusUnit = nextUnit();
        var azimuthUnit = nextUnit();
        if (!float.IsFinite(radiusUnit) || radiusUnit is < 0 or > 1 ||
            !float.IsFinite(azimuthUnit) || azimuthUnit is < 0 or > 1)
            throw new InvalidDataException("Projectile launch spread random sample is invalid.");

        var radius = angularExtentRadians * radiusUnit;
        var azimuth = MathF.Tau * azimuthUnit;
        // Convert the direction into clockwise source pitch/yaw, perturb those
        // angles, then apply the shared source (X,Z,-Y) coordinate convention.
        var pitch = -MathF.Asin(Math.Clamp(direction.Y, -1, 1)) + radius * MathF.Sin(azimuth);
        // The source heading rule chooses -pi/2 when both horizontal components
        // are zero; retain that choice before applying the random yaw offset.
        var yaw = direction.X == 0 && direction.Z == 0
            ? -MathF.PI / 2 : MathF.Atan2(direction.X, -direction.Z);
        yaw += radius * MathF.Cos(azimuth);
        var horizontal = MathF.Cos(pitch);
        var result = new Vector3(MathF.Sin(yaw) * horizontal, -MathF.Sin(pitch),
            -MathF.Cos(yaw) * horizontal);
        if (!IsFinite(result) || result.LengthSquared() <= float.Epsilon)
            throw new InvalidDataException("Projectile launch spread produced an invalid direction.");
        return result.Normalized();
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
