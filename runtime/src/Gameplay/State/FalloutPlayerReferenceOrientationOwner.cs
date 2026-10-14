using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

// Current raw reference fields are independent of rendered Euler angles and
// the Player's separate retained yaw offset. Placement and raw setters enter
// this same owner. A failed change callback keeps its preceding source stores.
internal sealed partial class FalloutPlayerReferenceOrientationOwner : IFalloutPlayerControlOrientationOwner
{
    internal const string Schema = "opennv-source-Player-reference-orientation/v2";
    internal const string ChangeBoundary = "source-reference-rotation-change-mask-two-consumer-unowned";
    private readonly FalloutPlayerWorldOrientationSource _source;
    private readonly FalloutWorldProjectionPublication _projection;
    private readonly FalloutPlayerControlFieldsState _fields;
    private readonly Func<bool> _living;
    private readonly Action<Guid, long> _requireInvocation;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private long _sequence, _stores, _reentry;
    private uint _x, _y, _z;
    private uint? _playerFov;
    private FalloutIniValue? _playerFovSetting;
    private Guid? _playerFovInvocation;
    private bool _playerFovReturned, _writing, _retired;
    private FalloutReferenceOrientationPlacement? _placement;
    private FalloutReferenceOrientationCall? _call;
    private string? _failure;
    private readonly List<FalloutPlayerReferenceOrientationPublication> _publications = [];
    internal FalloutPlayerReferenceOrientationBinding Binding { get; }
    public FalloutPlayerMovementControlSource Source => _source.Controls;
    public string Stack { get; }
    public Guid Process { get; }
    public Guid Factory { get; } = Guid.NewGuid();

    internal FalloutPlayerReferenceOrientationOwner(FalloutPlayerWorldOrientationSource source, string stack, Guid process,
        FalloutPlayerReferenceOrientationBinding binding, FalloutWorldProjectionPublication projection,
        FalloutPlayerControlFieldsState fields, Func<bool> living, Action<Guid, long> requireInvocation,
        FalloutPlayerReferenceOrientationSnapshot? saved = null)
    {
        source.Validate(); binding.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(projection); ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(living); ArgumentNullException.ThrowIfNull(requireInvocation);
        var currentFields = fields.Capture();
        projection.RequireCurrent();
        if (process == Guid.Empty || !binding.IsCharacter || projection.Source != source || projection.Stack != stack ||
            currentFields.Source != source.Controls.Identity || currentFields.Stack != stack || currentFields.Process != process)
            throw new InvalidDataException("Player reference constructor has foreign current control fields/process.");
        _source = source; Stack = stack; Process = process; Binding = binding; _projection = projection;
        _fields = fields; _living = living; _requireInvocation = requireInvocation;
        // The inherited reference constructor explicitly stores positive zero
        // to all three independent fields. Cold construction does not replay it.
        if (saved is null)
        {
            _x = 0; _y = 0; _z = 0;
            _publications.Add(new(Process, Factory));
            return;
        }
        Validate(saved);
        if (saved.Source != source || saved.Stack != stack || saved.Binding != binding || saved.CapturedProcess == process || saved.Retired)
            throw new InvalidDataException("Cold reference orientation changed its exact source binding or reused retirement/process.");
        projection.RequireColdReferenceProjection(saved.ProjectionWorld, saved.ProjectionRootFactory);
        _publications.AddRange(saved.Publications!);
        _publications.Add(new(Process, Factory, saved.CapturedProcess, saved.Factory));
        _sequence = saved.Sequence; _stores = saved.Stores; _x = saved.XBits; _y = saved.YBits; _z = saved.ZBits;
        _playerFov = saved.PlayerFovBits; _playerFovSetting = saved.PlayerFovSetting;
        _playerFovInvocation = saved.PlayerFovInvocation; _playerFovReturned = saved.PlayerFovReturned;
        _placement = saved.Placement; _call = saved.Call; _failure = saved.Failure;
    }

    internal void InitializePlayerFov(FalloutNumericIniSettings settings)
    {
        RequireOwner();
        if (_playerFovInvocation is not null) throw new InvalidOperationException("Player FOV constructor was replayed.");
        _playerFovSetting = _source.ReadConstructorFov(settings, Stack);
        _playerFov = BitConverter.SingleToUInt32Bits((float)_playerFovSetting.Number!.Value);
        _playerFovInvocation = Guid.NewGuid(); Next();
        try
        {
            _projection.Current.SetFov(_playerFovInvocation.Value, "actual-source-Player-FOV-constructor",
                FalloutWorldFovIngress.PlayerConstructor, _playerFov.Value, false, null, false, settings);
            _playerFovReturned = true; Next();
        }
        catch (Exception error) { _failure ??= error.Message; Next(); throw; }
    }

    internal void PublishPlacement(Guid invocation, string producer, FalloutReferencePlacementIngress ingress,
        uint x, uint y, uint z)
    {
        RequireOwner(); ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        if (invocation == Guid.Empty || _placement?.Invocation == invocation || !Enum.IsDefined(ingress) ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(x)) || !float.IsFinite(BitConverter.UInt32BitsToSingle(y)) ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(z)))
            throw new InvalidDataException("Reference placement lost its actual captured source rotation payload.");
        _x = x; _y = y; _z = z; _stores = checked(_stores + 3);
        _placement = new(invocation, producer, ingress, Next(), x, y, z);
    }

    internal void SetRawRotation(Guid invocation, string producer, FalloutReferenceRotationAxis axis, uint bits,
        IFalloutReferenceOrientationChangeConsumer? consumer = null)
    {
        RequireOwner(); ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        if (invocation == Guid.Empty || _call?.Invocation == invocation || !Enum.IsDefined(axis))
            throw new InvalidOperationException("Reference rotation setter replayed or lost its actual source ingress.");
        var before = ReadAxis(axis); var entered = Next(); var fault = _reentry; _writing = true;
        Store(axis, bits);
        _call = new(invocation, producer, "raw-reference-setter", entered, Next(), axis, bits, before, bits,
            FalloutReferenceOrientationStep.Stored, null, null, false, null);
        try
        {
            if (axis == FalloutReferenceRotationAxis.Z && Binding.IsCharacter)
            {
                var normalized = FalloutPlayerControlOrientation.NormalizeBits(bits);
                Store(axis, normalized);
                _call = _call with { StoredBits = normalized, Step = FalloutReferenceOrientationStep.Normalized, Changed = Next() };
            }
            _call = _call with { ChangeMask = 2, Step = FalloutReferenceOrientationStep.ChangeCallback, Changed = Next() };
            if (consumer is null) throw new NotSupportedException(ChangeBoundary);
            consumer.RequireCurrent(); Check();
            if (consumer.Source != _source || consumer.Stack != Stack || consumer.Process != Process ||
                consumer.Reference != Binding.Reference || consumer.Factory == Guid.Empty)
                throw new InvalidOperationException("Reference change callback selected another actual source reference/process.");
            _call = _call with { Consumer = consumer.Factory, Changed = Next() };
            consumer.Changed(invocation, 2); Check(); consumer.RequireCurrent(); Check();
            _call = _call with { Step = FalloutReferenceOrientationStep.Returned, Returned = true, Changed = Next() };
        }
        catch (Exception error) { _failure ??= error.Message; _call = _call with { Failure = error.Message, Changed = Next() }; throw; }
        finally { _writing = false; }
        void Check()
        {
            if (Environment.CurrentManagedThreadId != _thread || _reentry != fault || !_living())
                throw new InvalidOperationException("Reference change callback reentered or changed its actual source thread/lifetime.");
        }
    }

    internal void EnterYawDelta(Guid invocation, string producer, uint delta)
    {
        RequireOwner();
        var yaw = FalloutPlayerControlOrientation.YawFields(_z, _y, Binding.IsCharacter, Binding.BaseSignature,
            Binding.BaseFlags, _fields.ReadYawOffset());
        var argument = BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Add(BitConverter.UInt32BitsToSingle(yaw),
            BitConverter.UInt32BitsToSingle(delta)));
        RetainUnownedDelta(invocation, producer, "virtual-yaw-delta", argument,
            "source-Player-yaw-current-action-process-admission-writer-unowned");
    }
    internal void EnterPitchDelta(Guid invocation, string producer, uint delta)
    {
        RequireOwner();
        var argument = BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Add(BitConverter.UInt32BitsToSingle(_x),
            BitConverter.UInt32BitsToSingle(delta)));
        RetainUnownedDelta(invocation, producer, "Player-pitch-delta", argument,
            "source-Player-pitch-current-action-limits-writer-unowned");
    }
    private void RetainUnownedDelta(Guid invocation, string producer, string ingress, uint argument, string failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        if (invocation == Guid.Empty || _call?.Invocation == invocation) throw new InvalidOperationException("Reference motion child was replayed.");
        var entered = Next(); _failure ??= failure;
        _call = new(invocation, producer, ingress, entered, Next(), null, argument, null, null,
            FalloutReferenceOrientationStep.Admission, null, null, false, failure);
        throw new NotSupportedException(failure);
    }

    public void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (Environment.CurrentManagedThreadId != _thread || !_living())
            throw new InvalidOperationException("Player reference orientation lost its actual current process/thread/lifetime.");
        var fields = _fields.Capture();
        if (fields.Process != Process || fields.Source != Source.Identity || fields.Stack != Stack)
            throw new InvalidOperationException("Player reference orientation changed its current independent offset owner.");
    }
    public void RequireCurrent(Guid main, long ordinal) { RequireCurrent(); _requireInvocation(main, ordinal); }
    internal void RequireControlFields(FalloutPlayerControlFieldsState fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        RequireCurrent();
        if (!ReferenceEquals(fields, _fields))
            throw new InvalidOperationException("Player reference orientation borrowed another actual control-field owner.");
    }
    public FalloutPlayerControlWorldFovRead ReadCurrentWorldFov(Guid main, long ordinal)
    {
        RequireCurrent(main, ordinal); return new(Lease(main, ordinal, "actual-current-source-World-projection-field"), _projection.Current.ReadWrittenFov());
    }
    public FalloutPlayerControlYawRead ReadCurrentReferenceYaw(Guid main, long ordinal)
    {
        RequireCurrent(main, ordinal); return new(Lease(main, ordinal, "actual-current-reference-Player-base-yaw-fields"),
            _z, _y, Binding.IsCharacter, Binding.Base, Binding.BaseSignature, Binding.BaseFlags);
    }
    public FalloutPlayerControlPitchRead ReadCurrentReferencePitch(Guid main, long ordinal)
    {
        RequireCurrent(main, ordinal); return new(Lease(main, ordinal, "actual-current-reference-pitch-field"), _x);
    }
    private FalloutPlayerControlReadLease Lease(Guid main, long ordinal, string producer) =>
        new(Source.Identity, Stack, Process, Factory, main, ordinal, _sequence, producer);
    private uint ReadAxis(FalloutReferenceRotationAxis axis) => axis switch
    {
        FalloutReferenceRotationAxis.X => _x,
        FalloutReferenceRotationAxis.Y => _y,
        FalloutReferenceRotationAxis.Z => _z,
        _ => throw new InvalidDataException("Reference rotation axis is invalid."),
    };
    private void Store(FalloutReferenceRotationAxis axis, uint bits)
    {
        switch (axis)
        {
            case FalloutReferenceRotationAxis.X: _x = bits; break;
            case FalloutReferenceRotationAxis.Y: _y = bits; break;
            case FalloutReferenceRotationAxis.Z: _z = bits; break;
            default: throw new InvalidDataException("Reference rotation axis is invalid.");
        }
        _stores = checked(_stores + 1);
    }
    private void RequireOwner(bool allowFailure = false)
    {
        RequireCurrent();
        if (_writing) { _reentry = checked(_reentry + 1); throw new InvalidOperationException("Reference orientation writer reentered."); }
        if (!allowFailure && _failure is { } failure) throw new NotSupportedException(failure);
    }
    private long Next() => _sequence = checked(_sequence + 1);
    internal void Retire() { if (_retired) return; RequireOwner(allowFailure: true); _retired = true; }
}
