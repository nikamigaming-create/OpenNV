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
    private string? _phaseBeforeCombat, _failureKind;

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
            var target = ReactiveCombatSkill.SelectThreat(combat, _mode == "combat" ? _goalReference : null);
            if (target is null)
            {
                if (_mode == "combat")
                { Fail("No eligible resident source threat targets the player; no victory is inferred.", "safety-stop"); return true; }
                return false;
            }
            if (++_combatCount > 3)
            { Fail("Combat takeover reached its three-target goal bound; ordinary review is required.", "bot-policy"); return true; }
            CancelRoute(); _path = null; _steering.Reset();
            _input(default, false);
            _phaseBeforeCombat = _phase;
            _combat = new(_combatSkills); _combat.Start(combat, target); _combatFeedbackPublished = false;
            _motionPosition = observation.Position; _motionlessSeconds = _stalled = 0;
        }
        var step = _combat.Tick(combat, seconds);
        _input(step.Input, false); _phase = "combat-" + _combat.Phase;
        if (!step.Finished) return true;
        PublishCombatFeedback();
        var feedback = _combat.Feedback!;
        if (!feedback.Success && feedback.FailureKind != "cancelled")
        { Fail(feedback.Error ?? "Combat stopped without attributable gameplay receipts.", feedback.FailureKind ?? "bot-policy"); return true; }
        if (_mode == "combat")
        {
            Complete(feedback.Success ? "combat-observed" : "combat-cancelled");
            return true;
        }
        // Keep source goal, range, pending activation and route-door ownership.
        // Reacquire NAVM/capsule segments only after returning to ordinary travel.
        if (_phaseBeforeCombat is "awaiting-interaction" or "awaiting-route-door")
            _phase = _phaseBeforeCombat;
        else Replan(feedback.Success ? "replanning-after-combat" : "combat-suspended");
        return feedback.Success;
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
}
