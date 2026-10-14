using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private readonly FalloutActorMoverState _actorMover;
    // This is a view into the actual shared logical mover, not another set of
    // constructed fields beside the package-assignment owner.
    private FalloutPlayerMoverFields _moverFields => _actorMover.ReadPlayerFields();
    internal FalloutActorMoverState ActorMover => _actorMover;

    internal void BindActorMoverPackageReset(FalloutCombatActorIdentity actor,
        Action<FalloutPackageCommandFieldInvocation> requireCurrent,
        Func<FalloutPackageCommandFieldInvocation, FalloutActorMoverResetJoin> requireReturnedPredecessors)
    {
        RequireIdle(); _actorMover.BindPackageReset(actor, requireCurrent, requireReturnedPredecessors);
    }
    internal void ResetActorMoverForPackageAssignment(FalloutPackageCommandFieldInvocation invocation)
    {
        RequireIdle(); _actorMover.ResetForPackageAssignment(invocation);
    }
    private void RequireActorMoverLifetime()
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
    }
}
