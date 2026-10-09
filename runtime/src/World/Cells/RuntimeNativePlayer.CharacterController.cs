using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private IDisposable? _sourcePlayerTransferPlacementLease, _sourcePlayerControllerLease;
    private SourcePlayerCharacterControllerBody? _sourcePlayerControllerBody;
    private int? _sourcePlayerTransferThread;

    private void BindActualPlayerTransferAndController()
    {
        var world = _playerPerceptionWorld ?? throw new InvalidOperationException("Current Player fields have no real source world.");
        if (_sourcePlayerTransferPlacementLease is not null || _sourcePlayerControllerLease is not null)
            throw new InvalidOperationException("Current Player transfer/controller already owns a real body lease.");
        _sourcePlayerTransferThread = System.Environment.CurrentManagedThreadId;
        _sourcePlayerTransferPlacementLease = world.BindCurrentPlayerTransferPlacement(CaptureRawPlayerTransferPlacement, UnitsToMeters);
        if (!world.CampaignMainPlayerCellConstructed) return; // Distinct selected source family remains unowned.
        var body = new SourcePlayerCharacterControllerBody(this, world, world.ReadSourcePlayerCharacterControllerFactory());
        _sourcePlayerControllerBody = body;
        _sourcePlayerControllerLease = world.BindCurrentPlayerCharacterController(body);
    }

    private FalloutReferencePlacement CaptureRawPlayerTransferPlacement()
    {
        if (_sourcePlayerTransferThread != System.Environment.CurrentManagedThreadId ||
            _playerSourceProcessBodyLease is null || _playerPhysical is not { Published: true, Failure: null } ||
            !IsInsideTree() || IsQueuedForDeletion() || _playerPerceptionCell is null)
            throw new NotSupportedException("Player raw request cannot borrow an absent/retired physical source body.");
        var position = GlobalPosition / UnitsToMeters;
        var rotation = GamebryoCoordinate.ReferenceEuler(GlobalBasis);
        var result = new FalloutReferencePlacement(_playerPerceptionCell(), [position.X, -position.Z, position.Y],
            [rotation.X, rotation.Y, rotation.Z]);
        result.Validate(); return result;
    }

    internal IFalloutPlayerTransferController RequireSourcePendingController(FalloutMainPlayerCellInvocation invocation)
    {
        var body = _sourcePlayerControllerBody ?? throw new NotSupportedException("Actual Player source character controller has no published physical factory.");
        body.VerifyCurrent();
        return (_playerPerceptionWorld ?? throw new InvalidOperationException("Player controller lost its actual world."))
            .CampaignPlayerPendingConsumers.RequireCharacterController(invocation);
    }

    private sealed class SourcePlayerCharacterControllerBody(RuntimeNativePlayer player, FalloutReferenceWorld world,
        FalloutCharacterControllerFactory factory) : IFalloutCharacterControllerBody
    {
        private readonly int _thread = System.Environment.CurrentManagedThreadId;
        private readonly ulong _body = player.GetInstanceId();
        public FalloutCharacterControllerFactory Factory => factory;
        public string Owner => "actual-native-Player-character-body/" + _body;
        public void VerifyCurrent()
        {
            var actor = player._thirdPerson?.Actor;
            if (System.Environment.CurrentManagedThreadId != _thread || !GodotObject.IsInstanceValid(player) ||
                player.GetInstanceId() != _body || !player.IsInsideTree() || player.IsQueuedForDeletion() ||
                !ReferenceEquals(player._playerPerceptionWorld, world) || player._playerSourceProcessBodyLease is null ||
                player._playerPhysical is not { Published: true, Failure: null } || actor is null ||
                !GodotObject.IsInstanceValid(actor) || !actor.IsInsideTree() || actor.IsQueuedForDeletion())
                throw new NotSupportedException("Character controller lost its genuine current native body/thread/source lifetime.");
            world.RequireSourcePlayerCharacterControllerFactory(factory);
        }
        public FalloutActorProcessFact<uint> ControllerPositionZ()
        {
            VerifyCurrent();
            return new(null, "source-character-controller-proxy-transform-offset-and-position-conversion-unowned");
        }
    }

    private void RetireActualPlayerTransferAndController()
    {
        var failures = new List<Exception>();
        try { _sourcePlayerControllerLease?.Dispose(); _sourcePlayerControllerLease = null; _sourcePlayerControllerBody = null; }
        catch (Exception error) { failures.Add(error); }
        try { _sourcePlayerTransferPlacementLease?.Dispose(); _sourcePlayerTransferPlacementLease = null; _sourcePlayerTransferThread = null; }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Player raw/controller retirement retains every independent live lease failure.", failures);
    }
}
