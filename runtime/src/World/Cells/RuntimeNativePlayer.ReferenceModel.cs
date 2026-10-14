using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutReferenceWorld? _referenceModelWorld;
    private readonly HashSet<RuntimeNativePlayerActor> _referenceModelLeases = [];

    internal void BindStandaloneReferenceModelLoad(FalloutReferenceWorld world)
    {
        if (!world.HasStandalonePlayerScene) return;
        if (_referenceModelWorld is not null || _thirdPerson is not null)
            throw new InvalidOperationException("Reference model load must bind before its actual third-person factory.");
        _referenceModelWorld = world;
    }

    private RuntimeNativePlayerActor ConstructReferenceModel(Func<RuntimeNativePlayerActor> construct)
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
                return new(new(world.CampaignStandalonePlayerScene.Process, body.SourceSceneFactory, body.GetInstanceId()),
                    logical, RequireCurrent, () =>
                    {
                        RequireCurrent();
                        if (!_referenceModelLeases.Remove(body))
                            throw new InvalidOperationException("Reference data released another actual model lease.");
                    });

                void RequireCurrent()
                {
                    if (!ReferenceEquals(_referenceModelWorld, world) || !_referenceModelLeases.Contains(body) ||
                        !GodotObject.IsInstanceValid(this) || IsQueuedForDeletion() ||
                        !GodotObject.IsInstanceValid(body) || body.IsQueuedForDeletion() || body.Error is not null ||
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
                var node = new FalloutSourceSceneNode(world.CampaignStandalonePlayerScene.Process,
                    returned.SourceSceneFactory, returned.GetInstanceId());
                if (world.RetainsReturnedReferenceModel(node))
                {
                    // A committed source field owns this actual failed factory
                    // prefix. Keep it in the native lifetime tree for orderly
                    // retirement even though the caller did not adopt it.
                    if (returned.GetParent() is null) AddChild(returned);
                }
                else
                {
                    _referenceModelLeases.Remove(returned);
                    NativeNifSceneRetirement.Drain("Unadopted Player reference factory",
                        [returned.RetireLogicalScene, returned.Free]);
                }
            }
            throw;
        }
    }

    private void ReleaseStandaloneReferenceModel()
    {
        if (_referenceModelWorld is not { } world) return;
        world.ReleaseStandaloneReferenceModel();
        if (_referenceModelLeases.Count != 0)
            throw new InvalidOperationException("Reference data retained a native model after its source release.");
        _referenceModelWorld = null;
    }
}
