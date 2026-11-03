using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private string? _fatigueKnockoutError;

    private bool AdvanceFatigueKnockout()
    {
        try
        {
            if (_state.FatigueKnockout is { } retained)
            {
                if (retained.Phase == FalloutFatigueKnockoutPhase.Falling && _ragdoll is { Settled: true })
                    _state.FatigueKnockout = new(FalloutFatigueKnockoutPhase.Unconscious);
                return false;
            }
            if (!_state.ActorValues.TryGetValue("fatigue", out var fatigue) || fatigue.Current >= 0 ||
                Dead || _state.KnockedDown || _state.EssentialKnockout is not null ||
                !FalloutActorKnockdownSource.Allows(_records, _state.Base, _state.Templates)) return false;
            // The source animation-data pending reload group is the additional
            // reload gate; neither queued nor playing reload may be interrupted.
            if (_state.Engagement?.Action == "reload") return false;
            _world.RequireFatigueKnockoutEffects(_state.Reference, _context!.Level(), _context.Globals);
            _ = _world.Health(_state.Reference);
            // Admit the declared source get-up leaves before retiring pose.
            PrepareRecovery();
            PrepareDeath();
            _state.KnockedDown = true;
            BeginKnockdown();
            _state.FatigueKnockout = new(_ragdoll is { Settled: true }
                ? FalloutFatigueKnockoutPhase.Unconscious : FalloutFatigueKnockoutPhase.Falling);
            _fatigueKnockoutError = null;
            return false;
        }
        catch (Exception error)
        {
            _fatigueKnockoutError = error.Message;
            ReportHitReactionError(error);
            return true;
        }
    }

    private bool FatigueRecoveryHeld
    {
        get
        {
            if (_state.Unconscious) return true;
            if ((_state.FatigueKnockout is not null || _state.ActorValues.ContainsKey("fatigue")) &&
                _world.ActorValue(_state.Reference, "fatigue") < 0) return true;
            _world.RequireFatigueKnockoutEffects(_state.Reference, _context!.Level(), _context.Globals);
            return false;
        }
    }
}
