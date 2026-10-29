namespace OpenNV.Runtime.Gameplay.Bots;

internal sealed partial class ReactiveReferenceBot
{
    internal const string CombatObservationIdentity = "$resident-source-threats";
    private readonly VerifiedBotSkillLibrary _combatSkills;
    private readonly Action? _persistSkills;
    private ReactiveCombatSkill? _combat;
    private BotCombatObservation? _combatObservation;
    private bool _combatTakeover, _pauseAfter, _combatFeedbackPublished;
    private int _combatCount;
    private System.Numerics.Vector3? _combatRetryPosition;
    private string? _phaseBeforeCombat, _failureKind;
    private float? _minimumCombatHitPoints, _maximumCombatSimulationSeconds;
    private float _combatSimulationSeconds;
    private long _combatPolicySample = -1, _combatPhysicsSample = -1;
    private BotSkillBinding? _combatSessionBinding;
    private string? _combatSessionScene;
    private bool _terminalInputReleased;
    private sealed record CombatPolicyStop(long Sample, float HitPoints, float SimulationSeconds, string Reason);
    private CombatPolicyStop? _combatPolicyStop;

    internal static void ValidateCombatLimits(string mode, float? minimumHitPoints, float? maximumSimulationSeconds)
    {
        if ((minimumHitPoints.HasValue || maximumSimulationSeconds.HasValue) && mode != "combat")
            throw new ArgumentException("Combat limits require explicit combat mode.");
        if (minimumHitPoints is { } health && (!float.IsFinite(health) || health <= 0) ||
            maximumSimulationSeconds is { } seconds && (!float.IsFinite(seconds) || seconds <= 0))
            throw new ArgumentException("Combat limits must be finite and positive.");
    }

    private bool TickCombat(BotObservation observation, float seconds)
    {
        var combat = observation.Combat;
        if (combat is null)
        {
            if (_mode == "combat" || _combat?.Active == true)
            { Fail("No authoritative combat observation adapter is available.", "engine-owner"); return true; }
            return false;
        }
        if (combat.Defeated)
        { Fail("Player died; combat input and outcome verification stopped.", "safety-stop"); return true; }
        if (_mode == "combat" && combat.ExecutionFault is { } fault)
        { Fail(fault, "engine-owner"); return true; }
        if (_combat?.Active != true)
        {
            if (_mode != "combat" && !_combatTakeover) return false;
            if (combat.Paused || combat.Loading || combat.ModalInput)
            {
                if (_mode == "combat")
                {
                    ReleaseBotControls(false); _phase = "waiting-for-combat-control";
                    if (!combat.Paused && !combat.Loading && (_controlWaitSeconds += seconds) > ControlWaitLimitSeconds)
                        Fail("Modal player control did not settle within the combat admission bound.", "engine-owner");
                }
                return _mode == "combat";
            }
            _controlWaitSeconds = 0;
            if (combat.Binding.InputMode is not ("flat" or "simulator"))
            { Fail("Physical headset bot control is unbound; desktop input is never injected into XR.", "input-adapter"); return true; }
            var target = ReactiveCombatSkill.SelectThreat(combat, _mode == "combat" ? _goalReference : null,
                requireClearSight: _mode != "combat");
            if (_mode != "combat" && _combatRetryPosition is { } retryPosition)
            {
                if (FlatDistance(retryPosition, observation.Position) >= 1.5f)
                { _combatRetryPosition = null; _combatCount = 0; }
                else return false;
            }
            if (target is null)
            {
                if (_mode == "combat")
                { Fail("No eligible resident source threat targets the player or a protected actor; no victory is inferred.", "safety-stop"); return true; }
                return false;
            }
            CancelRoute(); _path = null; _steering.Reset();
            _input(default, false);
            _phaseBeforeCombat = _phase;
            _combat = new(_combatSkills); _combat.Start(combat, target); _combatFeedbackPublished = false;
            _combatSessionBinding ??= combat.Binding; _combatSessionScene ??= combat.Scene;
            _motionPosition = observation.Position; _motionlessSeconds = _stalled = 0;
        }
        if (_mode != "combat" && _combat!.CanSuspendForSceneChange(combat))
        {
            _input(default, false);
            _combat.Cancel("Scene changed; reacquire the source goal and threat before resuming.");
            PublishCombatFeedback();
            _combat = null;
            if (_phaseBeforeCombat is "awaiting-interaction" or "awaiting-route-door")
                _phase = _phaseBeforeCombat;
            else Replan("replanning-after-combat-scene-change");
            return true;
        }
        if (StopAtCombatLimit(combat)) return true;
        var step = _combat.Tick(combat, seconds);
        _input(step.Input, false); _phase = "combat-" + _combat.Phase;
        if (!step.Finished) return true;
        var feedback = _combat.Feedback!;
        if (!feedback.Success && feedback.FailureKind != "cancelled")
        {
            if (_mode != "combat" && (feedback.FailureKind == "safety-stop" || feedback.Error?.Contains("repositioning") == true))
            {
                PublishCombatFeedback();
                if (++_combatCount > 3)
                { Fail("Combat takeover repeatedly failed to make progress; ordinary review is required.", "bot-policy"); return true; }
                _combatRetryPosition = observation.Position;
                _combat = null;
                if (_phaseBeforeCombat is "awaiting-interaction" or "awaiting-route-door")
                    _phase = _phaseBeforeCombat;
                else Replan("combat-suspended");
                // This tick observed only combat state. The original goal's
                // position and residency must be reacquired on the next tick.
                return true;
            }
            Fail(feedback.Error ?? "Combat stopped without attributable gameplay receipts.", feedback.FailureKind ?? "bot-policy");
            return true;
        }
        _combatCount = 0;
        _combatRetryPosition = null;
        if (_mode == "combat")
        {
            Complete(feedback.Success ? "combat-observed" : "combat-cancelled");
            PublishCombatFeedback();
            return true;
        }
        PublishCombatFeedback();
        // Keep source goal, range, pending activation and route-door ownership.
        // Reacquire NAVM/capsule segments only after returning to ordinary travel.
        if (_phaseBeforeCombat is "awaiting-interaction" or "awaiting-route-door")
            _phase = _phaseBeforeCombat;
        else Replan(feedback.Success ? "replanning-after-combat" : "combat-suspended");
        return feedback.Success;
    }

    internal void AdvanceCombatPhysics(float seconds)
    {
        if (_reference is null || _mode != "combat" || !HasCombatLimits || _combat?.Active != true) return;
        try
        {
            if (!float.IsFinite(seconds) || seconds <= 0)
                throw new ArgumentException("Combat physics duration must be finite and positive.");
            _combatObservation = _observe(CombatObservationIdentity).Combat;
            if (_combatObservation is not { } combat)
            { Fail("No authoritative combat physics observation adapter is available.", "engine-owner"); return; }
            if (!CombatLimitSourceReady(combat) || combat.Sample <= _combatPhysicsSample || combat.Sample < _combatPolicySample)
                return;
            _combatPhysicsSample = combat.Sample;
            _combatSimulationSeconds += seconds;
            StopAtCombatLimit(combat, accountedPhysics: true);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or KeyNotFoundException or System.Text.Json.JsonException)
        {
            _combatObservation = null;
            Fail("Combat physics observation failed: " + error.Message,
                error is IOException or UnauthorizedAccessException ? "input-adapter" :
                error is NotSupportedException or KeyNotFoundException ||
                error.Message.StartsWith("Gameplay execution stopped:", StringComparison.Ordinal) ? "engine-owner" : "bot-policy");
        }
    }

    private bool HasCombatLimits => _minimumCombatHitPoints.HasValue || _maximumCombatSimulationSeconds.HasValue;

    // Invalid source state remains owned by the existing skill's refusal path.
    private bool CombatLimitSourceReady(BotCombatObservation combat) =>
        !combat.Paused && !combat.Loading && !combat.ModalInput && !combat.Defeated &&
        combat.Binding == _combatSessionBinding && combat.Scene == _combatSessionScene &&
        combat.Binding.InputMode is "flat" or "simulator" && combat.ExecutionFault is null &&
        combat.LookingEnabled && combat.FightingEnabled && combat.CollisionResident &&
        Finite(combat.Camera) && Finite(combat.Forward) && Finite(combat.Muzzle) && Finite(combat.MuzzleForward) &&
        combat.Forward.LengthSquared() >= .9f && combat.MuzzleForward.LengthSquared() >= .9f &&
        float.IsFinite(combat.HitPoints) && float.IsFinite(combat.MaximumHitPoints) && combat.MaximumHitPoints > 0 &&
        combat.HitPoints > Math.Max(1, combat.MaximumHitPoints * .15f) &&
        _combat is not null && _combat.SourceOwnersValid(combat);

    private bool StopAtCombatLimit(BotCombatObservation combat, bool accountedPhysics = false)
    {
        if (_mode != "combat" || !HasCombatLimits || !CombatLimitSourceReady(combat) ||
            combat.Sample < _combatPolicySample || _combat?.CanApplyLimit(combat, accountedPhysics) != true)
            return false;
        // A physics sample is not an immutable snapshot: actor physics can change
        // health before process observes the same sample. Only time accrual is deduplicated.
        _combatPolicySample = combat.Sample;
        var reason = _minimumCombatHitPoints is { } floor && combat.HitPoints <= floor ? "minimum-hit-points" :
            _maximumCombatSimulationSeconds is { } limit && _combatSimulationSeconds >= limit ? "simulation-time" : null;
        if (reason is null) return false;
        _combatPolicyStop = new(combat.Sample, combat.HitPoints, _combatSimulationSeconds, reason);
        Fail("Explicit combat limit reached: " + reason + ".", "safety-stop");
        return true;
    }

    private void PublishCombatFeedback()
    {
        if (_combatFeedbackPublished || _combat?.Feedback is not { } feedback) return;
        _combatFeedbackPublished = true;
        _combatSkills.Record(feedback);
        try { _persistSkills?.Invoke(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or System.Text.Json.JsonException)
        { throw new IOException("Bot skill evidence persistence failed: " + error.Message, error); }
    }

    internal void ObserveCombat() => _combatObservation = _observe(CombatObservationIdentity).Combat;

    private void ReleaseBotControls(bool protect)
    {
        _input(new(false, 0, 0) { Pause = protect && _combatObservation?.CanPause == true }, false);
    }

    private void ReleaseTerminalInput(bool protect)
    {
        if (_terminalInputReleased) return;
        _terminalInputReleased = true;
        ReleaseBotControls(protect);
    }
}
