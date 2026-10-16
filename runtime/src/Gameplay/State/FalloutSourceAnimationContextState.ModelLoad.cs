using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSourceAnimationContextState
{
    internal (FalloutPlayerModelNamedRead Read, FalloutNifSceneObject? Object) ReadModelLoadNamedObject(
        string name, FalloutActorLogicalScene expected, FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(); requireActual(load); _requireLifetime();
        if (_writer is { } writer) throw new NotSupportedException(writer);
        if (Construction.Invocation != load || LastBind is not { Returned: true } || _root is null ||
            !ReferenceEquals(_root.LogicalScene, expected) || _manager is null)
            throw new InvalidOperationException("Model named lookup requires the same returned live AnimData root/manager.");
        var palette = _manager.ReadModelPalette(name, expected);
        var fallback = palette is null ? expected.ReadPlayerModelNamedObject(name, load, requireActual) : null;
        requireActual(load);
        var result = palette ?? fallback;
        return (new(name, expected.Factory, expected.Lifetime, _manager.Factory, palette?.Key, fallback?.Key, result?.Key), result);
    }
}
