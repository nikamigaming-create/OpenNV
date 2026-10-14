using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal FalloutPackageCommandAnimationViewRead ReadPackageCommandAnimationView(
        FalloutPackageCommandFieldInvocation invocation,
        Action<FalloutPackageCommandFieldInvocation> requireActualCommand)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(requireActualCommand);
        requireActualCommand(invocation);
        RequireIdle();
        if (!invocation.Actor.EnginePlayer || invocation.Actor.ReferenceSha256 != Source.Main.EngineSha256 ||
            invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Package animation view lost its actual Player, source command or current thread.");
        var view = new FalloutPackageCommandAnimationViewRead(_process, _stack, Source.Main.EngineSha256, _constructor, _view);
        requireActualCommand(invocation);
        RequireIdle();
        return view;
    }
}
