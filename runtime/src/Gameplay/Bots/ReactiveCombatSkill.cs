using System.Numerics;

namespace OpenNV.Runtime.Gameplay.Bots;

// Reusable observation -> ordinary input -> feedback skill. Engine owners decide
// hostility, anatomy, collision, ammo, damage and death; input is never a result.
internal sealed class ReactiveCombatSkill
{
    internal const string SkillId = "source-reference-single-ray-combat/v1";
    internal const int MaximumShots = 12, MaximumMisses = 2;
    internal const float ExecutionLimitSeconds = 60, ReceiptLimitSeconds = 4;
    private readonly ReactiveSteering _steering = new();
    private readonly List<BotCombatReceipt> _receipts = [];
    private readonly VerifiedBotSkillLibrary _skills;
    private BotSkillBinding? _binding;
    private BotCombatObservation? _beforeShot;
    private BotCombatTarget? _beforeTarget;
    private string? _reference, _scene, _weapon, _ammunition, _projectile, _sourceFault;
    private string _phase = "idle";
    private string? _failureKind, _error;
    private Guid _attempt;
    private long _lastSample = -1;
    private float _elapsed, _waiting, _aiming, _obstructed, _stale;
    private int _misses, _recoveries, _reloads, _reloadLoaded, _reloadTotal;
    private bool _pendingObserved, _reusedVerification;
    internal bool Active { get; private set; }
    internal string Phase => _phase;
    internal string? Reference => _reference;
    internal BotCombatFeedback? Feedback { get; private set; }
    internal object State => new
    {
        skill = SkillId,
        active = Active,
        phase = _phase,
        reference = _reference,
        binding = _binding,
        scene = _scene,
        weapon = _weapon,
        ammunition = _ammunition,
        projectile = _projectile,
        elapsedSeconds = _elapsed,
        waitingSeconds = _waiting,
        staleSeconds = _stale,
        obstructionSeconds = _obstructed,
        maximumShots = MaximumShots,
        rangeOwner = "equipped-source-weapon",
        shots = _receipts.Count,
        verifiedContacts = _receipts.Count(receipt => receipt.VerifiedContact),
        aimRecoveries = _recoveries,
        reloads = _reloads,
        reusedVerification = _reusedVerification,
        failureKind = _failureKind,
        error = _error,
        sourceFault = _sourceFault,
        feedback = Feedback
    };

    internal ReactiveCombatSkill(VerifiedBotSkillLibrary skills) => _skills = skills;

    internal static BotCombatTarget? SelectThreat(BotCombatObservation state, string? reference = null,
        bool requireClearSight = false) =>
        state.Targets.Where(target => target.Resident && target.Enabled && target.EligibleThreat && !target.Dead &&
                (reference is null || target.Reference == reference) && Finite(target.Position) &&
                (!requireClearSight || target.BodyRayReference == target.Reference && target.MuzzleRayReference == target.Reference) &&
                (target.BodyPoint is not { } body || !Finite(body) || state.Weapon is not { } weapon ||
                    !float.IsFinite(weapon.RangeMeters) || weapon.RangeMeters <= 0 ||
                    Vector3.Distance(state.Camera, body) <= weapon.RangeMeters))
            .OrderByDescending(target => target.TargetsPlayer)
            .ThenBy(target => Vector3.DistanceSquared(state.Camera, target.Position))
            .ThenBy(target => target.Reference, StringComparer.Ordinal).FirstOrDefault();

    internal void Start(BotCombatObservation state, BotCombatTarget target)
    {
        if (Active) throw new InvalidOperationException("A combat skill already owns ordinary input.");
        if (SelectThreat(state, target.Reference) is null)
            throw new InvalidOperationException("Combat requires an actual resident source threat targeting the player or a protected actor.");
        _attempt = Guid.NewGuid(); _binding = state.Binding; _scene = state.Scene;
        _reference = target.Reference; _sourceFault = target.SourceFault;
        _weapon = state.Weapon?.Reference ?? ""; _ammunition = state.Weapon?.Ammunition ?? "";
        _projectile = state.Weapon?.Projectile;
        _phase = "observing"; _failureKind = _error = null; Feedback = null;
        _elapsed = _waiting = _aiming = _obstructed = _stale = 0;
        _lastSample = -1; _misses = _recoveries = _reloads = 0;
        _receipts.Clear(); _steering.Reset(); _beforeShot = null; _beforeTarget = null;
        _pendingObserved = false;
        _reusedVerification = _skills.IsVerified(state.Binding, _weapon, _ammunition);
        Active = true;
    }

    // Shared, nonmutating admission for both skill execution and optional limits.
    // A safety policy cannot replace an existing target, weapon or firing refusal.
    internal bool CanApplyLimit(BotCombatObservation state, bool newlyAccountedPhysics) =>
        state.Sample >= _lastSample && (newlyAccountedPhysics || state.Sample > _lastSample) &&
        SourceOwnerFailure(state, out _, out _) is null;

    internal bool SourceOwnersValid(BotCombatObservation state) => SourceOwnerFailure(state, out _, out _) is null;

    private (string Kind, string Error)? SourceOwnerFailure(BotCombatObservation state,
        out BotCombatTarget target, out BotCombatWeapon weapon)
    {
        target = null!; weapon = null!;
        var matches = state.Targets.Where(target => target.Reference == _reference).ToArray();
        if (matches.Length != 1 || !matches[0].Resident || !matches[0].Enabled)
            return ("observation-loss", "Combat target lost its unique resident source owner.");
        target = matches[0];
        if (target.Error is { } targetError) return ("engine-owner", targetError);
        if (target.Health is not { } health || !float.IsFinite(health) || !target.Dead && health < 0)
            return ("engine-owner", "Target health is not authoritative.");
        weapon = state.Weapon!;
        if (weapon is null || weapon.Error is not null)
            return ("engine-owner", weapon?.Error ?? "No equipped source weapon owner is available.");
        if (!weapon.Supported)
            return ("skill-limit", weapon.Limit ?? "This skill requires a magazine-fed single instant-ray weapon.");
        if (!float.IsFinite(weapon.RangeMeters) || weapon.RangeMeters <= 0)
            return ("engine-owner", "Equipped source weapon range is invalid.");
        if (weapon.Reference != _weapon || weapon.Ammunition != _ammunition || weapon.Projectile != _projectile)
            return ("observation-loss", "Equipped weapon/ammunition/projectile changed during combat.");
        if (weapon.Loaded < 0 || weapon.Reserve < 0)
            return ("observation-loss", "Ammunition observation is invalid.");
        if (state.Firing.Error is { } firingError) return ("engine-owner", firingError);
        return null;
    }

    internal BotSkillStep Tick(BotCombatObservation state, float seconds)
    {
        if (!Active) return new(default, true);
        try
        {
            if (!float.IsFinite(seconds) || seconds <= 0) return Refuse("bot-policy", "Combat frame duration is invalid.");
            if (state.Binding.InputMode is not ("flat" or "simulator"))
                return Refuse("input-adapter", "Physical headset combat automation is unbound.");
            if (state.Binding != _binding || state.Scene != _scene)
                return Refuse("observation-loss", "Combat source/build/native generation or scene changed.");
            if (state.Defeated) return Refuse("safety-stop", "Player died; no combat outcome is accepted.");
            if (state.Paused || state.Loading || state.ModalInput)
                return Refuse("cancelled", state.Loading ? "Loading cancelled combat input." :
                    state.Paused ? "Pause cancelled combat input." : "Modal player input cancelled combat.");
            if (state.ExecutionFault is { } execution) return Refuse("engine-owner", execution);
            if (!state.LookingEnabled || !state.FightingEnabled || !state.CollisionResident)
                return Refuse("engine-owner", "Source fighting/looking controls or native player collision are unavailable.");
            if (!Finite(state.Camera) || !Finite(state.Forward) || !Finite(state.Muzzle) || !Finite(state.MuzzleForward) ||
                state.Forward.LengthSquared() < .9f || state.MuzzleForward.LengthSquared() < .9f)
                return Refuse("observation-loss", "Combat camera/projectile pose is invalid.");
            if (!float.IsFinite(state.HitPoints) || !float.IsFinite(state.MaximumHitPoints) || state.MaximumHitPoints <= 0)
                return Refuse("engine-owner", "Player vitals have no finite authoritative owner.");
            if (state.HitPoints <= Math.Max(1, state.MaximumHitPoints * .15f))
                return Refuse("safety-stop", "Player health requires ordinary healing or retreat.");
            if (state.Sample < _lastSample) return Refuse("observation-loss", "Combat observation revision regressed.");
            _elapsed += seconds;
            if (_elapsed > ExecutionLimitSeconds) return Refuse("bot-policy", "Combat exceeded its bounded execution time.");
            if (state.Sample == _lastSample)
            {
                _stale += seconds;
                if (_stale > 1) return Refuse("observation-loss", "No fresh native physics observation for combat.");
                return new(default);
            }
            _lastSample = state.Sample; _stale = 0;
            var ownerFailure = SourceOwnerFailure(state, out var target, out var weapon);
            _sourceFault = target?.SourceFault ?? _sourceFault;
            if (ownerFailure is { } failure) return Refuse(failure.Kind, failure.Error);
            var health = target!.Health!.Value;
            if (_phase == "awaiting-shot") return ObserveShot(state, target, weapon, seconds);
            if (target.Dead || health == 0)
                return Refuse("receipt-refusal", "Target death has no new attributable player shot/contact/death receipt.");
            if (!target.EligibleThreat) return Refuse("safety-stop", "The source target is no longer an eligible combat threat.");
            if (target.BodyPoint is not { } body || !Finite(body) ||
                target.BodyOwner != "source-BPNT-current-skeleton-pose")
                return Refuse("engine-owner", "Target has no actual source BPNT posed body point.");
            if (Vector3.Distance(state.Camera, body) > weapon.RangeMeters)
                return Refuse("safety-stop", "Target left this bounded skill's source-supported range; ordinary navigation is required.");
            if (_phase == "awaiting-reload")
            {
                _waiting += seconds;
                if (weapon.Action is null && weapon.Loaded > _reloadLoaded &&
                    weapon.Loaded + weapon.Reserve == _reloadTotal && state.Firing.Shots == _beforeShot!.Firing.Shots)
                { _phase = "aiming"; _waiting = _aiming = 0; _beforeShot = null; }
                else
                {
                    if (_waiting > 6) return Refuse("receipt-refusal", "Ordinary reload produced no settled conserved ammunition receipt.");
                    return new(default);
                }
            }
            if (weapon.Action is not null || state.Firing.PendingImpacts != 0)
            {
                _waiting += seconds;
                if (_waiting > ReceiptLimitSeconds)
                    return Refuse("engine-owner", "The previous native weapon action/impact did not settle.");
                return new(default);
            }
            _waiting = 0;
            if (!weapon.Drawn)
                return Refuse("safety-stop", "Draw/equip the actual carried weapon with ordinary input before combat.");
            if (_receipts.Count >= MaximumShots)
                return Refuse("bot-policy", "Target remains alive after the bounded actual shot sequence.");
            if (weapon.Loaded == 0)
            {
                if (weapon.Reserve <= 0) return Refuse("safety-stop", "No carried source ammunition remains; ordinary inventory or retreat is required.");
                _reloads++; _reloadLoaded = weapon.Loaded; _reloadTotal = weapon.Loaded + weapon.Reserve;
                _beforeShot = state; _phase = "awaiting-reload"; _waiting = 0;
                return new(new(false, 0, 0) { Combat = new(Reload: true) });
            }
            if (target.BodyRayReference != _reference || target.MuzzleRayReference != _reference)
            {
                _obstructed += seconds;
                if (_obstructed > 1)
                    return Refuse("bot-policy", "Native body/projectile ray is obstructed; ordinary capsule repositioning is required: " +
                        (target.Obstruction ?? "no exact source contact"));
                return Aim(state, body, seconds, false);
            }
            _obstructed = 0;
            _aiming += seconds;
            if (_aiming > 8) return Refuse("bot-policy", "Ordinary look/aim did not converge on the actual posed body.");
            var origin = state.Binding.InputMode == "simulator" ? state.Muzzle : state.Camera;
            var forward = state.Binding.InputMode == "simulator" ? state.MuzzleForward : state.Forward;
            var direction = body - origin;
            if (direction.LengthSquared() <= .0001f) return Refuse("observation-loss", "The combat body point overlaps the aiming origin.");
            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(forward), Vector3.Normalize(direction)), -1, 1));
            var aimReady = state.Binding.InputMode == "simulator" || weapon.Aiming;
            if (angle > .025f || state.AimedReference != _reference || !aimReady)
                return Aim(state, body, seconds, true);
            _beforeShot = state; _beforeTarget = target; _pendingObserved = false;
            _phase = "awaiting-shot"; _waiting = 0;
            // Do not turn the camera or controller after accepting its live ray.
            return new(new(false, 0, 0) { Combat = new(Aim: true, Fire: true) });
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        { return Refuse("bot-policy", error.Message); }
    }

    private BotSkillStep Aim(BotCombatObservation state, Vector3 body, float seconds, bool ironSights)
    {
        _phase = "aiming";
        var intent = _steering.Step(state.Camera, state.Forward, body, false, seconds);
        return new(intent with { AimAt = body, Combat = new(Aim: ironSights) });
    }

    private BotSkillStep ObserveShot(BotCombatObservation state, BotCombatTarget target, BotCombatWeapon weapon, float seconds)
    {
        _waiting += seconds;
        var before = _beforeShot!;
        if (state.Firing.Shots < before.Firing.Shots || state.Firing.Shots > before.Firing.Shots + 1)
            return Refuse("receipt-refusal", "Shot counters regressed or another discharge crossed the isolated ordinary fire receipt.");
        if (state.Firing.Shots == before.Firing.Shots)
        {
            if (target.Dead) return Refuse("safety-stop", "Target died without the requested player discharge; no player kill is attributed.");
            if (_waiting > ReceiptLimitSeconds) return Refuse("receipt-refusal", "Fire input produced no actual shot/ammunition receipt.");
            return HoldingAim();
        }
        var shot = state.Firing.Last;
        if (shot is null || shot.Ordinal != state.Firing.Shots || shot.Weapon != _weapon ||
            shot.Ammunition != _ammunition || shot.Projectile != _projectile || shot.Projectiles != 1 ||
            weapon.Loaded != before.Weapon!.Loaded - 1 || weapon.Reserve != before.Weapon.Reserve)
            return Refuse("receipt-refusal", "Actual shot/source/ammunition receipt does not match this ordinary fire attempt.");
        if (shot.PendingAtDischarge is < 0 or > 1 || state.Firing.PendingImpacts is < 0 or > 1)
            return Refuse("receipt-refusal", "The single-ray skill observed ambiguous impact ownership.");
        _pendingObserved |= state.Firing.PendingImpacts == 1;
        if (state.Firing.PendingImpacts != 0 || weapon.Action is not null)
        {
            if (_waiting > ReceiptLimitSeconds) return Refuse("engine-owner", "Actual shot action/impact did not settle within its receipt bound.");
            return HoldingAim();
        }
        BotCombatImpact? impact = shot.ImmediateImpact;
        if (impact is null && shot.PendingAtDischarge == 1 &&
            (_pendingObserved || state.Firing.ImpactToken != before.Firing.ImpactToken))
            impact = state.Firing.LastImpact;
        var contact = shot.Reference == _reference && impact is { Error: null, HealthDamage: > 0 } &&
            impact.Reference == _reference && impact.Weapon == _weapon && impact.Projectile == _projectile && impact.HitEventMarked;
        var receipt = new BotCombatReceipt(before.Sample, state.Sample, before.Firing.Shots, state.Firing.Shots,
            _reference!, _weapon!, _ammunition!, _projectile!, before.Weapon!.Loaded, weapon.Loaded,
            before.Weapon.Reserve, weapon.Reserve, impact?.HealthBefore ?? _beforeTarget!.Health!.Value,
            impact?.HealthAfter ?? target.Health!.Value,
            contact ? impact!.HealthDamage : 0, contact, contact && impact!.Died, target.Dead, shot.Collider,
            impact?.Error, new(before.Sample, _beforeTarget!.BodyOwner!, BotCombatObservation.Coordinates(_beforeTarget.BodyPoint!.Value),
                BotCombatObservation.Coordinates(before.Camera), BotCombatObservation.Coordinates(before.Forward),
                BotCombatObservation.Coordinates(before.Muzzle), BotCombatObservation.Coordinates(before.MuzzleForward),
                _beforeTarget.BodyRayReference!, _beforeTarget.MuzzleRayReference!, before.AimedReference!),
            _beforeTarget.Health!.Value, target.Health!.Value);
        _receipts.Add(receipt); _beforeShot = null; _beforeTarget = null;
        if (!receipt.VerifiedShot) return Refuse("receipt-refusal", "A delivered fire edge is not verified ammunition/shot progress.");
        if (contact && (impact!.HealthBefore is null || impact.HealthAfter is null || !receipt.VerifiedContact))
            return Refuse("receipt-refusal", "Observed target health does not agree with this actual source damage receipt.");
        if (target.Dead || impact?.Died == true)
        {
            var hitAlreadyDead = shot.Reference == _reference && impact is
            { Error: null, HealthBefore: <= 0, HealthAfter: <= 0, HealthDamage: 0, Died: false } &&
                impact.Reference == _reference && impact.Weapon == _weapon && impact.Projectile == _projectile;
            if (target.Dead && impact?.Died != true &&
                (receipt.VerifiedContact || shot.Reference != _reference || hitAlreadyDead))
                return Refuse("safety-stop", "Target died without an attributable player killing shot.");
            if (!receipt.VerifiedDeath || MathF.Abs(target.Health!.Value - receipt.HealthAfter) > Math.Max(.001f, MathF.Abs(receipt.HealthAfter) * .00001f))
                return Refuse("receipt-refusal", "Actor death lacks the matching actual source contact/damage/death receipt.");
            Finish(true, "death-observed", null, null);
            return new(default, true);
        }
        if (!receipt.VerifiedContact)
        {
            if (shot.Reference == _reference)
                return Refuse("engine-owner", "The actual target contact has no fresh attributed damage/Hit continuation receipt.");
            _recoveries++; _misses++;
            if (!float.IsFinite(shot.DistanceMeters) || shot.DistanceMeters < Vector3.Distance(before.Muzzle, BeforeTargetPoint(before)) - .3f ||
                _misses > MaximumMisses)
                return Refuse("bot-policy", "Actual shot obstruction or repeated miss requires ordinary repositioning.");
        }
        else _misses = 0;
        _phase = "aiming"; _waiting = _aiming = 0; _steering.Reset();
        return new(default);
    }

    private static BotSkillStep HoldingAim() => new(new(false, 0, 0) { Combat = new(Aim: true) });

    private Vector3 BeforeTargetPoint(BotCombatObservation before) =>
        before.Targets.Single(target => target.Reference == _reference).BodyPoint ?? before.Camera;

    internal void Cancel(string reason) { if (Active) Finish(false, "cancelled", "cancelled", reason); }
    internal void Abort(string kind, string error) { if (Active) Finish(false, "blocked", kind, error); }

    // A new scene needs a fresh goal and target observation. Never abandon a
    // shot or reload whose ordinary input still needs its receipt.
    internal bool CanSuspendForSceneChange(BotCombatObservation state) => Active && state.Binding == _binding &&
        state.Scene != _scene && state.Sample > _lastSample && _beforeShot is null &&
        state.Firing.PendingImpacts == 0 && state.Firing.Error is null && state.ExecutionFault is null &&
        !state.Paused && !state.Loading && !state.Defeated && !state.ModalInput && state.CollisionResident;

    private BotSkillStep Refuse(string kind, string error)
    {
        Finish(false, kind == "cancelled" ? "cancelled" : "blocked", kind, error);
        return new(default, true);
    }

    private void Finish(bool success, string phase, string? kind, string? error)
    {
        Active = false; _phase = phase; _failureKind = kind; _error = error; _steering.Reset();
        Feedback = new(_attempt, SkillId, _binding!, _scene!, _reference!, _weapon!, _ammunition!,
            success, phase, kind, error, _recoveries, _reloads, _reusedVerification, _receipts.ToArray(), _sourceFault);
    }

    private static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
