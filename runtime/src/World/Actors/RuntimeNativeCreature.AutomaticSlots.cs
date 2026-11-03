using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private byte _automaticExecutionSlot = 7;
    private byte AutomaticExecutionSlot => _automaticExecutionSlot;

    internal bool AutomaticMovementReplacementPending => AmbientIdleActive && AutomaticExecutionSlot == 1 &&
        (_scriptIdleSequenceState != 3 || _scriptIdleDeactivationIncoming is null);
    internal bool AutomaticMovementReplacementReady => _scriptIdleSequenceState is 1 or 3;
    internal FalloutPackageAnimationSource AutomaticMovementSource =>
        new(_aiState!.CreatureScriptIdle!.Animation.Resource, _aiState.CreatureScriptIdle.Animation.Sha256);

    internal void BeginAutomaticMovementReplacement(NativeActorCombatAnimation incoming)
    {
        if (!AutomaticMovementReplacementPending || !AutomaticMovementReplacementReady)
            throw new InvalidOperationException("Automatic movement slot is not ready for source replacement.");
        // A naturally deactivating sequence still owns this slot. Native
        // replacement restarts its fade using both actual source clips.
        _scriptIdleDeactivation = null;
        _ambientIdleInterrupted = true;
        BeginScriptIdleDeactivation(incoming);
        if (_scriptIdleDeactivation is { DurationSeconds: <= 0.01f } instant)
        {
            instant.Advance(instant.DurationSeconds);
            CompleteScriptIdle();
        }
        if (_aiState!.CreatureScriptIdle is { } saved)
            _aiState.CreatureScriptIdle = saved with { ScriptPhase = CaptureScriptIdlePhase() };
    }
}
