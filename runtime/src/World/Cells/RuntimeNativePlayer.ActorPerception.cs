using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private object? _playerPerceptionRun;
    private IDisposable? _playerPerceptionLease;
    private FalloutReferenceWorld? _playerPerceptionWorld;
    private Func<FalloutFormKey>? _playerPerceptionCell;
    private string? _playerPerceptionOwner;

    internal IDisposable BindPlayerPerception(FalloutReferenceWorld world, Func<FalloutFormKey> cell)
    {
        if (_playerPerceptionWorld is not null) throw new InvalidOperationException("Player perception is already bound.");
        _playerPerceptionWorld = world; _playerPerceptionCell = cell;
        var run = new object(); _playerPerceptionRun = run;
        try { PublishPlayerPerception(); }
        catch { RetirePlayerPerception(); throw; }
        return new PlayerPerceptionScope(this, run);
    }
    private sealed class PlayerPerceptionScope(RuntimeNativePlayer player, object run) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            if (ReferenceEquals(player._playerPerceptionRun, run)) player.RetirePlayerPerception();
            _disposed = true;
        }
    }
    private void PublishPlayerPerception()
    {
        if (_playerPerceptionWorld is null || _playerPerceptionLease is not null || _playerPhysical?.Published != true) return;
        var body = _thirdPerson?.Actor ?? throw new NotSupportedException("Player perception has no actual published player body.");
        var reference = _physicalRecords!.RuntimeFormKey(0x14);
        _playerPerceptionOwner = "actual-engine-player-source-body/" + reference + "/" + body.Skeleton.Source.Sha256;
        BindActualPlayerSourceProcessBody();
        BindActualPlayerTransferAndController();
        _playerPerceptionLease = _playerPerceptionWorld.BindNativePerception(reference, _playerPerceptionOwner, ObservePlayerPerception);
    }
    private FalloutPerceptionNativeObservation ObservePlayerPerception(long lease)
    {
        var body = _thirdPerson?.Actor ?? throw new NotSupportedException("Actual player source body is absent.");
        if (_playerPhysical is not { Published: true, Failure: null } || !IsInsideTree() || IsQueuedForDeletion() ||
            !GodotObject.IsInstanceValid(body) || !body.IsInsideTree() || !GodotObject.IsInstanceValid(body.Skeleton.Node))
            throw new NotSupportedException("Player perception source publication is absent, faulted or retired.");
        ObserveActualPlayerNeutralLifeBoundary();
        var position = GlobalPosition / UnitsToMeters;
        var rotation = GamebryoCoordinate.ReferenceEuler(GlobalBasis);
        return new(_physicalRecords!.RuntimeFormKey(0x14), _playerPerceptionOwner!, lease, _playerPerceptionCell!(),
            [position.X, -position.Z, position.Y], rotation.Z, true, Activity.InCombat,
            _playerPerceptionWorld!.PlayerInCombat(), Activity.Sneaking, Activity.Running, !Velocity.IsZeroApprox(),
            _physicalVitals!().ExactHitPoints <= 0);
    }
    private void RetirePlayerPerceptionBody()
    {
        var failures = new List<Exception>();
        try { _playerPerceptionLease?.Dispose(); _playerPerceptionLease = null; } catch (Exception error) { failures.Add(error); }
        try { RetireActualPlayerTransferAndController(); } catch (Exception error) { failures.Add(error); }
        try { RetireActualPlayerSourceProcessBody(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Player perception/process-body retirement retains actual failures.", failures);
        _playerPerceptionOwner = null;
    }
    private void RetirePlayerPerception()
    {
        RetirePlayerPerceptionBody();
        _playerPerceptionRun = null; _playerPerceptionWorld = null; _playerPerceptionCell = null;
    }
}
