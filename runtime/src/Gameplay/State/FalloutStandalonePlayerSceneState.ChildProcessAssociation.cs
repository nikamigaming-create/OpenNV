namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private static bool ChildBelongsToParentProcessLineage(FalloutStandalonePlayerSceneSnapshot parent, Guid process)
    {
        // The parent validates this complete ordered constructor/process chain
        // through its retained POV fields. Several cold captures can precede
        // child rebinding, so historical children need not be the last parent.
        // A nonnull handoff alone never authorizes an unrelated process.
        return process != Guid.Empty && parent.PointOfView?.Lifetimes is { } lifetimes &&
            lifetimes.Any(lifetime => lifetime.Process == process);
    }
}
