using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutReferenceWorld? _referenceModelWorld;
    private FalloutReferenceWorld? _loadedModelWorld;
    private readonly HashSet<RuntimeNativePlayerActor> _referenceModelLeases = [];

    internal void BindStandaloneReferenceModelLoad(FalloutReferenceWorld world)
    {
        if (!world.HasStandalonePlayerScene) return;
        if (_referenceModelWorld is not null || _loadedModelWorld is not null || _thirdPerson is not null)
            throw new InvalidOperationException("Reference model load must bind before its actual third-person factory.");
        _referenceModelWorld = world;
        _loadedModelWorld = world;
    }

    private RuntimeNativePlayerActor ConstructReferenceModel(Func<RuntimeNativePlayerActor> construct,
        Action<FalloutPlayerReturnedModel>? constructed = null)
    {
        if (_referenceModelWorld is not { } world) return construct();
        RuntimeNativePlayerActor? returned = null;
        try
        {
            world.LoadStandaloneReferenceModel(saved =>
            {
                returned = construct();
                if (returned.IsFirstPersonSourceRole || returned.Error is not null)
                    throw new NotSupportedException("Reference model factory did not return its actual third-person source role.");
                var body = returned;
                var logical = body.BindLogicalScene(saved);
                _referenceModelLeases.Add(body);
                var node = new FalloutReferenceModelDataNode(new(world.CampaignStandalonePlayerScene.Process, body.SourceSceneFactory, body.GetInstanceId()),
                    logical, RequireCurrent, () => { }, () =>
                    {
                        RequireBindingLifetime();
                        body.RetireLogicalScene();
                        if (!_referenceModelLeases.Remove(body))
                            throw new InvalidOperationException("Reference data released another actual model lease.");
                    }, RequireBindingLifetime);
                constructed?.Invoke(new(node, body.Actor.SourceModel, RequireBindingLifetime));
                return node;

                void RequireCurrent()
                {
                    RequireBindingLifetime();
                    if (IsQueuedForDeletion() || body.IsQueuedForDeletion() || body.Error is not null)
                        throw new InvalidOperationException("Reference model is unavailable for gameplay.");
                }
                void RequireBindingLifetime()
                {
                    if (!ReferenceEquals(_referenceModelWorld, world) || !_referenceModelLeases.Contains(body) ||
                        !GodotObject.IsInstanceValid(this) || !GodotObject.IsInstanceValid(body) ||
                        body.IsFirstPersonSourceRole || !ReferenceEquals(body.LogicalScene, logical) ||
                        body.SourceSceneFactory != logical.Factory)
                        throw new InvalidOperationException("Reference data lost its actual returned logical/native model.");
                }
            });
            return returned ?? throw new InvalidOperationException("Reference model loader omitted its returned factory child.");
        }
        catch
        {
            if (returned is not null)
            {
                CleanupUnpublishedReferenceModel(returned);
            }
            throw;
        }
    }

    private void CleanupUnpublishedReferenceModel(RuntimeNativePlayerActor model)
    {
        if (_referenceModelWorld is not { } world) { model.Free(); return; }
        var node = new FalloutSourceSceneNode(world.CampaignStandalonePlayerScene.Process,
            model.SourceSceneFactory, model.GetInstanceId());
        world.CleanupUnpublishedReferenceModel(node,
            () => { if (model.GetParent() is null) AddChild(model); },
            () =>
            {
                _referenceModelLeases.Remove(model);
                NativeNifSceneRetirement.Drain("Unadopted Player reference factory",
                    [model.RetireLogicalScene, model.Free]);
            });
    }

    private void ReleaseStandaloneReferenceModel()
    {
        if (_referenceModelWorld is not { } world) return;
        world.ReleaseStandaloneReferenceModel();
        if (_referenceModelLeases.Count != 0)
            throw new InvalidOperationException("Reference data retained a native model after its source release.");
        _referenceModelWorld = null;
    }

    private void ReleaseStandaloneLoadedPlayerModels()
    {
        if (_loadedModelWorld is not { } world) return;
        world.RetireStandalonePlayerLoadedModels();
        if (_firstModelLeases.Count != 0 || _firstModelNodes.Count != 0)
            throw new InvalidOperationException("Player loaded-model retirement retains an actual first-model binding.");
        _loadedModelWorld = null;
    }
}
