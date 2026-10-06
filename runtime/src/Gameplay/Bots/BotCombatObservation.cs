using System.Numerics;

namespace OpenNV.Runtime.Gameplay.Bots;

internal sealed record BotSkillBinding(string Build, string Source, string InputMode, string Generation);

internal sealed record BotCombatBindings(string FireAction, string AimAction, string ReloadAction, string PauseAction);

internal sealed record BotCombatTarget(string Reference, Vector3 Position, Vector3? BodyPoint,
    string? BodyOwner, bool Resident, bool Enabled, bool TargetsPlayer, bool Dead, float? Health,
    string? BodyRayReference, string? MuzzleRayReference, string? Obstruction, string? Error = null,
    string? SourceFault = null);

internal sealed record BotCombatWeapon(string Reference, string? Ammunition, string? Projectile,
    bool Drawn, bool Aiming, int Loaded, int Reserve, float RangeMeters, bool Supported,
    string? Action = null, string? Error = null, string? Limit = null);

internal sealed record BotCombatImpact(string Reference, string Weapon, string Projectile,
    float HealthDamage, bool Died, bool HitEventMarked, string? Error = null);

internal sealed record BotCombatShot(long Ordinal, string Weapon, string? Ammunition, string Projectile,
    string? Reference, string? Collider, float DistanceMeters, int Projectiles, int PendingAtDischarge,
    BotCombatImpact? ImmediateImpact = null);

internal sealed record BotCombatFiring(long Shots, int PendingImpacts, BotCombatShot? Last,
    BotCombatImpact? LastImpact, string? ImpactToken, string? Error = null);

internal sealed record BotCombatObservation(long Sample, string Scene, BotSkillBinding Binding,
    Vector3 Camera, Vector3 Forward, Vector3 Muzzle, Vector3 MuzzleForward, string? AimedReference,
    bool Paused, bool Loading, bool Defeated, bool ModalInput, bool LookingEnabled, bool FightingEnabled,
    bool CollisionResident, float HitPoints, float MaximumHitPoints, BotCombatBindings Bindings,
    BotCombatWeapon? Weapon, BotCombatFiring Firing, IReadOnlyList<BotCombatTarget> Targets,
    string? ExecutionFault = null)
{
    internal bool CanPause => !Paused && !Loading && !Defeated && !ModalInput;
    internal object State => new
    {
        Sample,
        Scene,
        Binding,
        camera = Coordinates(Camera),
        forward = Coordinates(Forward),
        muzzle = Coordinates(Muzzle),
        muzzleForward = Coordinates(MuzzleForward),
        AimedReference,
        Paused,
        Loading,
        Defeated,
        ModalInput,
        LookingEnabled,
        FightingEnabled,
        CollisionResident,
        HitPoints,
        MaximumHitPoints,
        Bindings,
        Weapon,
        Firing,
        ExecutionFault,
        targets = Targets.Select(target => new
        {
            target.Reference,
            position = Coordinates(target.Position),
            bodyPoint = target.BodyPoint is { } body ? Coordinates(body) : null,
            target.BodyOwner,
            target.Resident,
            target.Enabled,
            target.TargetsPlayer,
            target.Dead,
            target.Health,
            target.BodyRayReference,
            target.MuzzleRayReference,
            target.Obstruction,
            target.Error,
            target.SourceFault
        }).ToArray()
    };

    internal static float[] Coordinates(Vector3 value) => [value.X, value.Y, value.Z];
}

internal readonly record struct BotCombatInput(bool Aim = false, bool Fire = false, bool Reload = false);

internal sealed record BotCombatAimEvidence(long Sample, string BodyOwner, float[] BodyPoint, float[] Camera,
    float[] Forward, float[] Muzzle, float[] MuzzleForward, string BodyRayReference, string MuzzleRayReference,
    string AimedReference)
{
    internal bool Matches(string reference, string mode)
    {
        if (BodyOwner != "source-BPNT-current-skeleton-pose" || BodyRayReference != reference ||
            MuzzleRayReference != reference || AimedReference != reference ||
            !Valid(BodyPoint) || !Valid(Camera) || !Valid(Forward) || !Valid(Muzzle) || !Valid(MuzzleForward)) return false;
        var origin = mode == "simulator" ? Vector(Muzzle) : Vector(Camera);
        var forward = mode == "simulator" ? Vector(MuzzleForward) : Vector(Forward);
        var direction = Vector(BodyPoint) - origin;
        return direction.LengthSquared() > .0001f && forward.LengthSquared() >= .9f &&
            Vector3.Dot(Vector3.Normalize(forward), Vector3.Normalize(direction)) >= MathF.Cos(.0251f);
    }

    private static bool Valid(float[]? values) => values is { Length: 3 } && values.All(float.IsFinite);
    private static Vector3 Vector(float[] values) => new(values[0], values[1], values[2]);
}

internal sealed record BotCombatReceipt(long BeforeSample, long Sample, long BeforeShots, long Shot,
    string Reference, string Weapon, string Ammunition, string Projectile,
    int LoadedBefore, int LoadedAfter, int ReserveBefore, int ReserveAfter,
    float HealthBefore, float HealthAfter, float HealthDamage, bool Contact, bool Died, bool Dead,
    string? Collider, string? Error = null, BotCombatAimEvidence? Aim = null)
{
    internal bool VerifiedShot => Error is null && Sample > BeforeSample && Shot == BeforeShots + 1 &&
        LoadedBefore > 0 && LoadedAfter == LoadedBefore - 1 && ReserveBefore >= 0 && ReserveAfter == ReserveBefore;
    internal bool VerifiedContact => VerifiedShot && Contact && !string.IsNullOrWhiteSpace(Collider) &&
        float.IsFinite(HealthBefore) && float.IsFinite(HealthAfter) && float.IsFinite(HealthDamage) &&
        HealthBefore > 0 && HealthDamage > 0 && HealthAfter < HealthBefore &&
        MathF.Abs(HealthBefore - HealthAfter - HealthDamage) <= Math.Max(.001f, HealthBefore * .00001f);
    internal bool VerifiedDeath => VerifiedContact && Died && Dead && HealthAfter <= 0;
}

internal sealed record BotCombatFeedback(Guid Attempt, string Skill, BotSkillBinding Binding, string Scene,
    string Reference, string Weapon, string Ammunition, bool Success, string Phase, string? FailureKind,
    string? Error, int AimRecoveries, int Reloads, bool ReusedVerification,
    IReadOnlyList<BotCombatReceipt> Receipts, string? SourceFault = null);

internal readonly record struct BotSkillStep(SteeringIntent Input, bool Finished = false);
