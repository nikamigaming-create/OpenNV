using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPlayerModelLoadTailCall(FalloutReferenceModelDataInvocation Load, string Step,
    Guid? Controller, FalloutControllerModelWorldRead? World, FalloutPlayerModelPipBoyRead? PipBoy,
    bool? Toddler, FalloutSourceAnimationKindScope? Scope, bool Returned, string? Failure);

internal sealed partial class FalloutReferenceWorld
{
    private FalloutPlayerModelLoadTailCall? _playerModelLoadTail;
    private bool _playerModelLoadReturned;

    private void CompleteStandalonePlayerModelLoad(FalloutReferenceModelDataInvocation load, FalloutScriptSession session,
        Action<FalloutScriptSession> requireSession)
    {
        RequireActualPlayerModelLoad(load);
        if (_playerModelLoadTail is not null || _playerModelLoadReturned ||
            _playerModelControllerCall is not { Returned: true, Failure: null } helper || helper.Load != load)
            throw new InvalidOperationException("Player model-load tail preceded its actual returned controller helper or replayed.");
        _playerModelLoadTail = new(load, "current-controller-world", null, null, null, null, null, false, null);
        try
        {
            using (var controller = ProcessCommon.BorrowEarlyController(_enginePlayer, ActorProcesses.Read(_enginePlayer).Epoch))
            {
                if (controller is not null)
                {
                    _playerModelLoadTail = _playerModelLoadTail with { Controller = controller.Controller.Identity };
                    var world = controller.Controller.ReadModelLoadWorld(load, RequireActualPlayerModelLoad);
                    _playerModelLoadTail = _playerModelLoadTail with { World = world };
                    if (world.World is not null)
                        throw new NotSupportedException("source-Player-model-load-current-world-model-registration-unowned");
                }
            }
            Step("current-PipBoy-refresh-gate");
            var pipboy = StandaloneInterface.ReadPlayerModelLoadRefresh(load, RequireActualPlayerModelLoad);
            _playerModelLoadTail = _playerModelLoadTail with { PipBoy = pipboy };
            if (pipboy.Refresh) throw new NotSupportedException("source-Player-model-load-active-PipBoy-refresh-child-unowned");
            Step("current-toddler-flag");
            requireSession(session); RequireActualPlayerModelLoad(load);
            var toddler = session.PlayerToddler;
            _playerModelLoadTail = _playerModelLoadTail with { Toddler = toddler };
            requireSession(session); RequireActualPlayerModelLoad(load);
            if (toddler) throw new NotSupportedException("source-Player-model-load-toddler-context-activation-unowned");
            Step("source-thread-kind-restoration");
            var scope = _loadedPlayerAnimation!.ReturnPlayerLoad(load);
            _playerModelLoadTail = _playerModelLoadTail with { Scope = scope };
            Step("returned");
            _playerModelLoadTail = _playerModelLoadTail with { Returned = true };
            _playerModelLoadReturned = true;
        }
        catch (Exception error) { _playerModelLoadTail = _playerModelLoadTail with { Failure = error.Message }; throw; }

        void Step(string step) { RequireActualPlayerModelLoad(load); _playerModelLoadTail = _playerModelLoadTail! with { Step = step }; }
    }
}
