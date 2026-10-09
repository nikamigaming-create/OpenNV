namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    private readonly Guid _sourceSceneFactory = Guid.NewGuid();
    internal Guid SourceSceneFactory => _sourceSceneFactory;
    internal bool IsFirstPersonSourceRole => _first;
}
