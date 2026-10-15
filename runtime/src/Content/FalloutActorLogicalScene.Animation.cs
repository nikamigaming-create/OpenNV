using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutAnimationRootFlagStore(FalloutReferenceModelDataInvocation Invocation,
    Guid ModelFactory, Guid Lifetime, long Changed, uint Before, uint After);

internal sealed partial class FalloutActorLogicalScene
{
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
