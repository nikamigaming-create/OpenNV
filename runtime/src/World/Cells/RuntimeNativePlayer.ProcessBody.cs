using Godot;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private IDisposable? _playerSourceProcessBodyLease;
    private void BindActualPlayerSourceProcessBody()
    {
        if (_playerSourceProcessBodyLease is not null) throw new InvalidOperationException("Player source process body is already leased.");
        var world = _playerPerceptionWorld ?? throw new NotSupportedException("Player process body has no real shared world.");
        var body = _thirdPerson?.Actor ?? throw new NotSupportedException("Player process body has no actual third-person source skeleton.");
        var reference = _physicalRecords!.RuntimeFormKey(0x14);
        _playerSourceProcessBodyLease = world.BindActualNativeProcessBody(reference, body.Appearance.SkeletonPath,
            body.Skeleton.Source, BodyParts(), _playerPerceptionOwner!, () =>
                _playerPhysical is { Published: true, Failure: null } && IsInsideTree() && !IsQueuedForDeletion() &&
                GodotObject.IsInstanceValid(body) && body.IsInsideTree() && !body.IsQueuedForDeletion() &&
                GodotObject.IsInstanceValid(body.Skeleton.Node) && body.Skeleton.Node.IsInsideTree() && !body.Skeleton.Node.IsQueuedForDeletion());
    }
    private void ObserveActualPlayerNeutralLifeBoundary()
    {
        if (_physicalVitals!().ExactHitPoints <= 0 || PhysicalPlayer.SleepingState != 0 || PhysicalPlayer.KnockedState != 0)
            _playerPerceptionWorld!.RetainActualUnownedLifeTransition(_physicalRecords!.RuntimeFormKey(0x14),
                "actual-current-Player-physical-life-transition-original-neutral-code-unbound");
        // These independent physical values detect an uninspected transition.
        // They never manufacture or map an original Actor life-state code.
    }
    private void RetireActualPlayerSourceProcessBody()
    {
        var lease = _playerSourceProcessBodyLease;
        lease?.Dispose(); _playerSourceProcessBodyLease = null;
    }
}
