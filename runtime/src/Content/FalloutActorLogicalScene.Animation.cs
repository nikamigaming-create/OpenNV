using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutAnimationRootFlagStore(FalloutReferenceModelDataInvocation Invocation,
    Guid ModelFactory, Guid Lifetime, long Changed, uint Before, uint After);

internal sealed partial class FalloutActorLogicalScene
{
    private FalloutLoadedAnimationManagerState? _loadedAnimationManager;
    internal bool HasLoadedAnimationPoseOwner => _loadedAnimationManager is not null;

    internal void BindLoadedAnimationPoseOwner(FalloutLoadedAnimationManagerState manager)
    {
        RequireIdle();
        manager.RequireModelBinding(this);
        if (_loadedAnimationManager is not null)
            throw new InvalidOperationException("Logical model already has its actual loaded animation manager.");
        _loadedAnimationManager = manager;
    }

    internal void RequireCurrentPoseProjection() => RequireIdle(allowFailure: true);

    internal void PublishAnimationSample(FalloutNifSceneKey key, FalloutNifAnimationSample sample)
    {
        RequireIdle(); var node = _objects.GetValueOrDefault(key) ?? throw new InvalidDataException("Animation sample selected a foreign source node.");
        var local = node.Local;
        PublishLocalPose(key, new(sample.Translation ?? local.Translation,
            sample.Rotation is { } rotation ? FalloutNifSceneMath.Rotation(rotation) : local.RotationRowMajor,
            sample.Scale ?? local.Scale));
    }
    // This store mutates the same source node used by bounds, pose and save.
    internal FalloutAnimationRootFlagStore ClearAnimationBindFlag(FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(); requireActual(invocation);
        var before = Root.Flags; Root.Flags &= ~FalloutLoadedAnimationSource.ClearRootFlag;
        var result = new FalloutAnimationRootFlagStore(invocation, Factory, Lifetime, Next(), before, Root.Flags);
        requireActual(invocation); return result;
    }
}
