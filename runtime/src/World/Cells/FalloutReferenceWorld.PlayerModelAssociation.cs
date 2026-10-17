using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPlayerModelAssociationCall(FalloutReferenceModelDataInvocation Load, string Step,
    Guid? Controller, FalloutActorProcessFact<int>? ActorMode, FalloutFormKey? Parent, FalloutControllerModelWorldRead? World,
    FalloutPlayerThreadModelCacheRead? ModelCache, FalloutSourceSceneNode? Model,
    IReadOnlyList<FalloutControllerModelFieldRead> BodyReads, bool Returned, string? Failure);

internal sealed partial class FalloutReferenceWorld
{
    private FalloutPlayerModelAssociationCall? _playerModelAssociation;
    private void ContinueStandalonePlayerModelAssociation(FalloutReferenceModelDataInvocation load)
    {
        RequireActualPlayerModelLoad(load);
        if (_playerModelAssociation is not null) throw new InvalidOperationException("Player model association replayed its actual entered child.");
        _playerModelAssociation = new(load, "current-process-controller", null, null, null, null, null, null, [], false, null);
        try
        {
            using var controller = ProcessCommon.BorrowEarlyController(_enginePlayer, ActorProcesses.Read(_enginePlayer).Epoch);
            if (controller is null) { Return(); return; }
            _playerModelAssociation = _playerModelAssociation with { Controller = controller.Controller.Identity };
            Step("actual-Actor-mode");
            _playerModelAssociation = _playerModelAssociation with { ActorMode = ReadStandalonePlayerPhysicalMode() };
            if (_playerModelAssociation.ActorMode is not { Value: not null, Failure: null })
                throw new NotSupportedException("source-controller-current-Actor-mode-field-unavailable");
            Step("current-ParentCELL-world");
            var parent = ReadReferenceModelParent().Cell;
            _playerModelAssociation = _playerModelAssociation with { Parent = parent };
            if (parent is not null) throw new NotSupportedException("source-controller-current-ParentCELL-world-getter-unowned");
            Step("current-proxy-phantom-world");
            var world = controller.Controller.ReadModelLoadWorld(load, RequireActualPlayerModelLoad);
            _playerModelAssociation = _playerModelAssociation with { World = world };
            if (world.World is not null) throw new NotSupportedException("source-controller-old-world-remove-and-position-stores-unowned");
            Step("current-first-body-model-property");
            var first = controller.Controller.ReadModelLoadField(load, FalloutControllerPostProcessField.FirstBody, RequireActualPlayerModelLoad);
            _playerModelAssociation = _playerModelAssociation with { BodyReads = [first] };
            if (first.Value is not null) throw new NotSupportedException("source-controller-current-body-model-property-getter-unowned");
            Step("current-reference-model");
            var cache = CampaignStandalonePlayerScene.ThreadContext.ReadEarlyControllerModelCache(Guid.NewGuid(),
                _sourcePlayerModelData!.Factory, _enginePlayer, () => RequireActualPlayerModelLoad(load));
            _playerModelAssociation = _playerModelAssociation with { ModelCache = cache };
            var model = cache.Matched ? cache.Model : _sourcePlayerModelData.ReadForPlayerLoad(load, RequireActualPlayerModelLoad)?.Node;
            _playerModelAssociation = _playerModelAssociation with { Model = model };
            // Both current body fields are independent. Their constructor nulls
            // skip the source property setters even when the current model exists.
            if (model is not null)
            {
                Step("current-second-body-model-binding");
                var second = controller.Controller.ReadModelLoadField(load, FalloutControllerPostProcessField.SecondBody, RequireActualPlayerModelLoad);
                _playerModelAssociation = _playerModelAssociation with { BodyReads = [first, second] };
                if (second.Value is not null) throw new NotSupportedException("source-controller-second-body-model-property-setter-unowned");
            }
            Step("same-null-world-return");
            Return();
        }
        catch (Exception error) { _playerModelAssociation = _playerModelAssociation with { Failure = error.Message }; throw; }

        void Step(string step) { RequireActualPlayerModelLoad(load); _playerModelAssociation = _playerModelAssociation! with { Step = step }; }
        void Return() { Step("returned"); _playerModelAssociation = _playerModelAssociation! with { Returned = true }; }
    }
}
