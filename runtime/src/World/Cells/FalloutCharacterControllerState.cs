using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCharacterControllerState : IFalloutPlayerTransferController
{
    internal const string Schema = "opennv-source-character-controller/v1";
    private readonly FalloutCharacterControllerFactory _factory;
    private IFalloutCharacterControllerBody? _body;
    private uint _scalarBits;
    private long _sequence, _reentry;
    private FalloutCharacterControllerFieldStore? _lastStore;
    private readonly List<FalloutCharacterControllerHandoff> _handoffs = [];
    private string? _failureType, _error;
    private bool _busy, _retired;
    public Guid Identity { get; } = Guid.NewGuid();
    public Guid SourceProcess => _factory.Process;
    public long ProcessEpoch => _factory.ProcessEpoch;
    public string Owner => _factory.Owner;
    internal FalloutCharacterControllerFactory Factory => _factory;
    internal string? SaveBlocker => _busy ? "source-character-controller-field-consumer-entered" :
        _error is not null ? "source-character-controller:" + _error : _retired ? "source-character-controller-retired" :
        _body is null ? "source-character-controller-current-body-not-rebound" : null;
    internal object State => new
    {
        identity = Identity,
        factory = _factory,
        scalarBits = _scalarBits,
        sequence = _sequence,
        lastStore = _lastStore,
        handoffs = _handoffs.ToArray(),
        error = _error,
        nativeBound = _body is not null,
        retired = _retired,
        position = "original-controller-proxy-offset-and-transform-producer-unowned",
        blocker = SaveBlocker
    };

    internal FalloutCharacterControllerState(IFalloutCharacterControllerBody body, FalloutCharacterControllerSnapshot? restore = null)
    {
        ArgumentNullException.ThrowIfNull(body); RequireFactory(body.Factory);
        _factory = body.Factory; _body = body;
        body.VerifyCurrent();
        // Actual selected controller constructor uses a positive Float32 zero.
        // Restoring a saved value is a subsequent owned current-process write.
        _scalarBits = 0;
        if (restore is not null) Restore(restore);
    }

    internal void Rebind(IFalloutCharacterControllerBody body)
    {
        Require(); ArgumentNullException.ThrowIfNull(body); RequireFactory(body.Factory);
        var candidate = body.Factory;
        if (_body is not null || candidate.Source != _factory.Source || candidate.Stack != _factory.Stack ||
            candidate.Process != _factory.Process || candidate.ProcessEpoch != _factory.ProcessEpoch || candidate.Level != _factory.Level ||
            candidate.Actor != _factory.Actor || candidate.Owner != _factory.Owner ||
            !FalloutActorProcessCommonState.BodyEquivalent(candidate.Body, _factory.Body))
            throw new InvalidDataException("Character controller rebind changed its current source process/body factory.");
        body.VerifyCurrent(); _body = body;
    }

    internal void Detach(IFalloutCharacterControllerBody body)
    {
        if (_busy) { _reentry = checked(_reentry + 1); throw new InvalidOperationException("Character controller body retired inside its source field consumer."); }
        if (!ReferenceEquals(_body, body)) throw new InvalidOperationException("Character controller retirement has a foreign physical owner.");
        _body = null;
    }

    public void StoreScalar(FalloutMainPlayerCellInvocation invocation, Guid request, uint bits)
    {
        Require(); invocation.Require(FalloutMainPlayerCellStep.PendingSceneScalar);
        if (invocation.Main.Process != SourceProcess || invocation.Owner.MainPlayerCellSource != _factory.Source.Pending.Player || request == Guid.Empty)
            throw new InvalidDataException("Character controller scalar writer has a foreign actual Main/request epoch.");
        _busy = true; var faults = _reentry;
        try
        {
            RequireBody().VerifyCurrent(); invocation.Require(FalloutMainPlayerCellStep.PendingSceneScalar);
            if (_reentry != faults) throw new InvalidOperationException("Controller body swallowed a source field reentry refusal.");
            _scalarBits = bits;
            _lastStore = new(invocation.Main.Identity, request, bits, Next(), "actual-source-pending-PositionZ-controller-field-store",
                SourceProcess, Identity, ProcessEpoch);
        }
        catch (Exception failure) { Retain(failure); throw; }
        finally { _busy = false; }
    }

    public uint ReadScalarBits()
    {
        Require(); var faults = _reentry; _busy = true;
        try
        {
            RequireBody().VerifyCurrent();
            if (_reentry != faults) throw new InvalidOperationException("Controller getter swallowed a real owner reentry refusal.");
            return _scalarBits;
        }
        catch (Exception failure) { Retain(failure); throw; }
        finally { _busy = false; }
    }

    internal uint ReadHeightDeltaBits()
    {
        Require(); var faults = _reentry; _busy = true;
        try
        {
            var body = RequireBody(); body.VerifyCurrent();
            // The original getter returns positive zero for either signed
            // zero scalar. A nonzero value needs the real proxy-space read;
            // Actor GlobalPosition is not that missing source producer.
            if (BitConverter.UInt32BitsToSingle(_scalarBits) == 0f) return 0;
            var position = body.ControllerPositionZ().Require();
            if (_reentry != faults) throw new InvalidOperationException("Controller height producer swallowed a source reentry refusal.");
            return HeightDeltaBits(_scalarBits, position);
        }
        catch (Exception failure) { Retain(failure); throw; }
        finally { _busy = false; }
    }

    internal static uint HeightDeltaBits(uint scalar, uint position)
    {
        var height = BitConverter.UInt32BitsToSingle(scalar);
        if (height == 0f) return 0;
        return BitConverter.SingleToUInt32Bits((float)((double)height - BitConverter.UInt32BitsToSingle(position)));
    }
    private IFalloutCharacterControllerBody RequireBody() => _body ??
        throw new NotSupportedException("Character controller has no current actual physical/source body lease.");
    private void Require()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy) { _reentry = checked(_reentry + 1); throw new InvalidOperationException("Character controller reentered its live source consumer."); }
        if (_error is not null) throw new InvalidOperationException("Character controller retains its entered failure: " + _error);
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private void Retain(Exception failure)
    {
        _failureType ??= failure.GetType().FullName ?? failure.GetType().Name;
        _error ??= string.IsNullOrWhiteSpace(failure.Message) ? failure.GetType().Name : failure.Message;
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_busy || _body is not null) throw new InvalidOperationException("Character controller provider must outlive its actual entered/native body consumers.");
        _retired = true;
    }
}
