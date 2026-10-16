using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private readonly HashSet<RuntimeNativePlayerActor> _firstModelLeases = [];
    private readonly Dictionary<RuntimeNativePlayerActor, FalloutReferenceModelDataNode> _firstModelNodes = [];

    private FalloutPlayerReturnedModel ConstructFirstModelNode(RuntimeNativePlayerActor body, FalloutReferenceWorld world)
    {
        if (!body.IsFirstPersonSourceRole || body.Error is not null)
            throw new NotSupportedException("Player first-model factory did not return its actual source role.");
        var scene = world.CampaignStandalonePlayerScene;
        var graph = body.BindLogicalScene(scene.CurrentPlayerOpacity?.PreparedColdScene ?? scene.PreparedPlayerOpacityRestore?.FirstPerson);
        _firstModelLeases.Add(body);
        var node = new FalloutReferenceModelDataNode(new(world.CampaignStandalonePlayerScene.Process,
            body.SourceSceneFactory, body.GetInstanceId()), graph, RequireCurrent, () => { }, () =>
            {
                RequireLifetime();
                body.RetireLogicalScene();
                _firstModelLeases.Remove(body); _firstModelNodes.Remove(body);
            }, RequireLifetime);
        _firstModelNodes.Add(body, node);
        return new(node, body.Actor.SourceModel, RequireLifetime);

        void RequireCurrent()
        {
            RequireLifetime();
            if (IsQueuedForDeletion() || body.IsQueuedForDeletion() || body.Error is not null)
                throw new InvalidOperationException("Player first-model factory is unavailable for gameplay.");
        }
        void RequireLifetime()
        {
            if (!ReferenceEquals(_loadedModelWorld, world) || !_firstModelLeases.Contains(body) ||
                !GodotObject.IsInstanceValid(this) || !GodotObject.IsInstanceValid(body) ||
                !body.IsFirstPersonSourceRole || !ReferenceEquals(body.LogicalScene, graph) || body.SourceSceneFactory != graph.Factory)
                throw new InvalidOperationException("Player first-model field lost its actual returned logical/native factory.");
        }
    }

    private void CleanupUnpublishedFirstModel(RuntimeNativePlayerActor body)
    {
        if (_loadedModelWorld?.RetainsStandaloneFirstModel(body.SourceSceneFactory) == true)
        {
            if (body.GetParent() is null) AddChild(body);
            return;
        }
        if (_firstModelNodes.TryGetValue(body, out var node))
        {
            try
            {
                if (_loadedModelWorld is { } world && !world.CleanupUnpublishedStandaloneFirstModel(node))
                {
                    if (body.GetParent() is null) AddChild(body);
                    return;
                }
            }
            catch
            {
                if (body.GetParent() is null) AddChild(body);
                throw;
            }
        }
        _firstModelLeases.Remove(body); _firstModelNodes.Remove(body);
        NativeNifSceneRetirement.Drain("Unadopted Player first factory", [body.RetireLogicalScene, body.Free]);
    }
}
