namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    internal Guid SourceSceneFactory => Actor.SourceModel.Factory;
    internal bool IsFirstPersonSourceRole => _first;
}
