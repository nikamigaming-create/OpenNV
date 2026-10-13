using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerControlInhibition(byte First, byte Second, byte Third, byte Fourth)
{
    internal static FalloutPlayerControlInhibition Constructed => new(0, 0, 0, 0);
}
internal sealed record FalloutPlayerControlMouseRead(uint Axis, int Value, ulong Sample, long Revision);
internal sealed record FalloutPlayerControlAxes(int X, int Y, int Wheel);
internal enum FalloutPlayerMovementControlStep
{
    SourceMode, Inhibition, InputOwner, CacheClear,
    DeviceSelection, MouseAxes, Configuration, PlayerFlags, Wheel, WorldFov, Yaw, Pitch, CameraMode, MotionWriters, Returned
}
internal sealed record FalloutPlayerMovementControlCall(Guid Main, long MainOrdinal, long Entered, long Changed,
    uint ArgumentBits, ushort MoverWord, byte FirstArgument, byte LastArgument,
    FalloutPlayerMovementControlStep Step, Guid? InputFactory, ulong? InputWindow,
    FalloutInputProvider? InputProvider, int CacheStores, IReadOnlyList<FalloutPlayerControlMouseRead> MouseReads,
    bool Returned, string? Failure, Guid Process = default,
    FalloutPlayerControlConfigurationSnapshot? Configuration = null,
    FalloutPlayerControlFieldsSnapshot? FieldsRead = null, FalloutPlayerControlAxes? Axes = null,
    FalloutPlayerControlWorldFovRead? WorldFov = null, uint? FovScaleBits = null,
    FalloutPlayerControlYawRead? Yaw = null, uint? YawBits = null,
    FalloutPlayerControlPitchRead? Pitch = null, uint? PitchBits = null, bool? Result = null);
internal sealed record FalloutPlayerMovementControlSnapshot(string Schema, FalloutPlayerMovementControlSource Source,
    string Stack, Guid CapturedProcess, Guid Factory, long Sequence, uint Mode,
    FalloutPlayerControlInhibition Inhibition, byte JoystickAvailable, byte JoystickSuppressed,
    uint FirstCacheWord, uint SecondCacheWord, long CacheStores,
    FalloutPlayerMovementControlCall? Call, string? Failure, bool Retired,
    FalloutPlayerControlConfigurationSnapshot? Configuration = null,
    FalloutPlayerControlFieldsSnapshot? Fields = null, byte CameraMode = 0,
    uint YawCache = 0, uint PitchCache = 0, long OrientationStores = 0, byte ControllerCameraMode = 0);

// C# owns this source call independently of Actor's physical utility, camera
// presentation, keyboard control bindings, process flags and thread epilogue.
// Zero motion still requires real configuration, Player flag and world/reference
// getters before the source Boolean child can return. Later Player services
// remain separate owners even after a complete returned control child.
// The scene constructs this live owner at validated input publication, after
// loading-worker preparation. Its source thread remains fixed for its lifetime.
internal sealed partial class FalloutPlayerMovementControlState
{
    internal const string Schema = "opennv-Player-movement-control/v2";
    internal const string ConfigurationBoundary = "source-Player-movement-control-invert-Y-configuration-unbound";
    internal const string WorldFovBoundary = "source-Player-movement-control-current-world-projection-FOV-unbound";
    internal const string WheelBoundary = "source-Player-movement-control-wheel-view-services-unbound";
    internal const string MotionBoundary = "source-Player-movement-control-reference-orientation-writers-unbound";
    private readonly FalloutPlayerMovementControlSource _source;
    private readonly string _stack;
    private readonly Guid _process, _factory = Guid.NewGuid();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private long _sequence, _cacheStores, _orientationStores, _reentry;
    private uint _mode;
    private FalloutPlayerControlInhibition _inhibition = FalloutPlayerControlInhibition.Constructed;
    private byte _joystickAvailable, _joystickSuppressed;
    private uint _firstCacheWord, _secondCacheWord;
    private uint _yawCache, _pitchCache;
    private byte _cameraMode, _controllerCameraMode;
    private readonly FalloutPlayerControlConfigurationSnapshot? _historicalConfiguration;
    private FalloutPlayerControlConfiguration? _configuration;
    private IFalloutPlayerControlOrientationOwner? _orientation;
    internal FalloutPlayerControlFieldsState Fields { get; }
    private FalloutPlayerMovementControlCall? _call;
    private string? _failure;
    private bool _writing, _retired, _currentInvocationEntered;

    internal FalloutPlayerMovementControlState(FalloutPlayerMovementControlSource source, string stack,
        Guid process, FalloutPlayerMovementControlSnapshot? saved = null, FalloutPlayerControlFieldsState? fields = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty) throw new ArgumentException("Player movement/control needs its actual Main process.", nameof(process));
        _source = source; _stack = stack; _process = process;
        Fields = fields ?? new(source, stack, process, saved?.Fields);
        var currentFields = Fields.Capture();
        if (currentFields.Source != source.Identity || currentFields.Stack != stack || currentFields.Process != process)
            throw new InvalidOperationException("Player movement/control selected foreign source Player fields.");
        if (saved is null) return;
        Validate(saved);
        if (saved.Source != source || saved.Stack != stack || saved.CapturedProcess == process || saved.Retired)
            throw new InvalidDataException("Cold Player movement/control changed source/stack/process or resurrected retirement.");
        _sequence = saved.Sequence; _mode = saved.Mode; _inhibition = saved.Inhibition;
        _joystickAvailable = saved.JoystickAvailable; _joystickSuppressed = saved.JoystickSuppressed;
        _firstCacheWord = saved.FirstCacheWord; _secondCacheWord = saved.SecondCacheWord;
        _cacheStores = saved.CacheStores; _call = saved.Call; _failure = saved.Failure;
        _cameraMode = saved.CameraMode; _yawCache = saved.YawCache; _pitchCache = saved.PitchCache;
        _orientationStores = saved.OrientationStores;
        _controllerCameraMode = saved.ControllerCameraMode;
        _historicalConfiguration = saved.Configuration;
    }

    internal void BindConfiguration(FalloutNumericIniSettings settings)
    {
        RequireOwner();
        if (_configuration is not null || _currentInvocationEntered)
            throw new InvalidOperationException("Player controls changed configuration after actual entry/publication.");
        var current = new FalloutPlayerControlConfiguration(_source, _stack, settings);
        if (_historicalConfiguration is { } previous && previous != current.Fields)
            throw new InvalidDataException("Cold Player controls changed their exact configured declarations or layered values.");
        _configuration = current;
    }
    internal void BindOrientation(IFalloutPlayerControlOrientationOwner owner)
    {
        RequireOwner(); ArgumentNullException.ThrowIfNull(owner);
        if (_orientation is not null || _currentInvocationEntered)
            throw new InvalidOperationException("Player controls changed orientation ownership after actual entry/publication.");
        owner.RequireCurrent(); RequireOrientationIdentity(owner); _orientation = owner;
    }

    internal bool ExecutePrefix(Guid main, long ordinal, uint sceneArgument, ushort moverWord,
        FalloutDirectInputState input, byte firstArgument = 0, byte lastArgument = 0)
    {
        RequireOwner(); ArgumentNullException.ThrowIfNull(input);
        if (_failure is { } failure) throw new NotSupportedException(failure);
        if (main == Guid.Empty || ordinal < 1 || _call is { Returned: false } ||
            _call is { } previous && (main == previous.Main || previous.Process == _process && ordinal <= previous.MainOrdinal) ||
            (moverWord & ~FalloutPlayerMovementControlSource.MoverMask) != 0 || firstArgument != 0 || lastArgument != 0)
            throw new InvalidOperationException("Player movement/control changed or replayed its actual Main child and source arguments.");
        // Restored calls are immutable history. Fresh dependencies bind until
        // this process enters its first call; failure closes that phase too.
        _currentInvocationEntered = true;
        _writing = true; var fault = _reentry;
        _call = new(main, ordinal, Next(), _sequence, sceneArgument, moverWord, firstArgument, lastArgument,
            FalloutPlayerMovementControlStep.SourceMode, null, null, null, 0, [], false, null, _process);
        try
        {
            if (_mode != 0) throw new NotSupportedException("source-Player-movement-control-nonzero-mode-producer-unbound");
            Step(FalloutPlayerMovementControlStep.Inhibition);
            if (_inhibition != FalloutPlayerControlInhibition.Constructed)
                throw new NotSupportedException("source-Player-movement-control-inhibition-writer-unbound");
            Step(FalloutPlayerMovementControlStep.InputOwner);
            var current = input.ReadManagerOwner();
            if (!ReferenceEquals(current, input) || current.Source != _stack)
                throw new InvalidOperationException("Player movement/control selected a foreign current input owner.");
            if (Fields.HasActualInput) Fields.RequireActualInput(current);
            _call = _call with
            {
                InputFactory = current.Factory,
                InputWindow = current.Window,
                InputProvider = current.Provider,
                Changed = Next()
            };
            Step(FalloutPlayerMovementControlStep.CacheClear);
            _firstCacheWord = 0; _cacheStores = checked(_cacheStores + 1);
            _call = _call with { CacheStores = 1, Changed = Next() };
            _secondCacheWord = 0; _cacheStores = checked(_cacheStores + 1);
            _call = _call with { CacheStores = 2, Changed = Next() };
            Step(FalloutPlayerMovementControlStep.DeviceSelection);
            if (_joystickAvailable != 0 || _joystickSuppressed != 0)
                throw new NotSupportedException("source-Player-movement-control-joystick-selection-writer-unbound");
            Step(FalloutPlayerMovementControlStep.MouseAxes);
            var reads = new List<FalloutPlayerControlMouseRead>(3);
            for (uint axis = 1; axis <= 3; ++axis)
            {
                var value = current.ReadRelativeMouseAxis(axis);
                reads.Add(new(axis, value, current.Sample, current.Revision));
                _call = _call with { MouseReads = reads.ToArray(), Changed = Next() };
            }
            Step(FalloutPlayerMovementControlStep.Configuration);
            var configuration = _configuration ?? throw new NotSupportedException(ConfigurationBoundary);
            var inverted = configuration.ReadInvertY();
            _call = _call with { Configuration = configuration.Fields, Changed = Next() };
            Step(FalloutPlayerMovementControlStep.PlayerFlags);
            var word = Fields.ReadDisabledWord();
            _call = _call with { FieldsRead = Fields.Capture(), Changed = Next() };
            var x = reads[0].Value; var y = inverted ? unchecked(-reads[1].Value) : reads[1].Value;
            var wheel = reads[2].Value;
            if ((word & FalloutPlayerControlFieldsState.LookingDisabled) != 0) x = y = wheel = 0;
            var axes = new FalloutPlayerControlAxes(x, y, wheel);
            _call = _call with { Axes = axes, Changed = Next() };
            Step(FalloutPlayerMovementControlStep.Wheel);
            if (wheel != 0) throw new NotSupportedException(WheelBoundary);
            Step(FalloutPlayerMovementControlStep.WorldFov);
            var orientation = _orientation ?? throw new NotSupportedException(WorldFovBoundary);
            RequireCurrent();
            var fov = orientation.ReadCurrentWorldFov(main, ordinal); Check(); RequireRead(fov.Lease);
            var scale = FalloutPlayerControlOrientation.WorldFovScale(configuration.ReadDefaultWorldFov(), fov.Bits);
            _call = _call with { WorldFov = fov, FovScaleBits = BitConverter.SingleToUInt32Bits(scale), Changed = Next() };
            Step(FalloutPlayerMovementControlStep.Yaw); RequireCurrent();
            var yaw = orientation.ReadCurrentReferenceYaw(main, ordinal); Check(); RequireRead(yaw.Lease);
            var yawBits = FalloutPlayerControlOrientation.Yaw(yaw, Fields.ReadYawOffset());
            _yawCache = yawBits; _orientationStores = checked(_orientationStores + 1);
            _call = _call with { Yaw = yaw, YawBits = yawBits, Changed = Next() };
            Step(FalloutPlayerMovementControlStep.Pitch); RequireCurrent();
            var pitch = orientation.ReadCurrentReferencePitch(main, ordinal); Check(); RequireRead(pitch.Lease);
            _ = FalloutPlayerControlOrientation.Finite(pitch.Bits);
            _pitchCache = pitch.Bits; _orientationStores = checked(_orientationStores + 1);
            _call = _call with { Pitch = pitch, PitchBits = pitch.Bits, Changed = Next() };
            Step(FalloutPlayerMovementControlStep.CameraMode);
            if (_cameraMode != 0 || _controllerCameraMode != 0)
                throw new NotSupportedException("source-Player-movement-control-alternate-camera-context-producer-unbound");
            Step(FalloutPlayerMovementControlStep.MotionWriters);
            if (axes.X != 0 || axes.Y != 0) throw new NotSupportedException(MotionBoundary);
            Step(FalloutPlayerMovementControlStep.Returned);
            _call = _call with { Returned = true, Result = false, Changed = Next() }; return false;

            void RequireCurrent() { orientation.RequireCurrent(main, ordinal); Check(); RequireOrientationIdentity(orientation); }
            void RequireRead(FalloutPlayerControlReadLease lease)
            {
                lease.Validate();
                if (lease.Source != _source.Identity || lease.Stack != _stack || lease.Process != _process ||
                    lease.Factory != orientation.Factory || lease.Main != main || lease.MainOrdinal != ordinal)
                    throw new InvalidOperationException("Player controls borrowed a foreign current world/reference getter.");
            }
        }
        catch (Exception error)
        {
            _failure ??= error.Message;
            _call = _call with { Failure = _failure, Changed = Next() }; throw;
        }
        finally { _writing = false; }
        void Step(FalloutPlayerMovementControlStep step) => _call = _call! with { Step = step, Changed = Next() };
        void Check()
        {
            if (_thread != Environment.CurrentManagedThreadId || _reentry != fault ||
                _call?.FieldsRead is { } fields && Fields.Capture().Sequence != fields.Sequence)
                throw new InvalidOperationException("Player control getter swallowed reentry or changed source thread/fields.");
        }
    }

    internal FalloutPlayerMovementControlSnapshot Capture()
    {
        RequireOwner(allowRetired: true);
        var saved = new FalloutPlayerMovementControlSnapshot(Schema, _source, _stack, _process, _factory,
            _sequence, _mode, _inhibition, _joystickAvailable, _joystickSuppressed, _firstCacheWord,
            _secondCacheWord, _cacheStores, _call, _failure, _retired, _configuration?.Fields ?? _historicalConfiguration,
            Fields.Capture(), _cameraMode, _yawCache, _pitchCache, _orientationStores, _controllerCameraMode);
        Validate(saved); return saved;
    }

    internal void Retire() { RequireOwner(allowRetired: true); _retired = true; }

    private long Next() => _sequence = checked(_sequence + 1);
    private void RequireOrientationIdentity(IFalloutPlayerControlOrientationOwner owner)
    {
        if (owner.Source != _source || owner.Stack != _stack || owner.Process != _process || owner.Factory == Guid.Empty)
            throw new InvalidOperationException("Player controls selected a foreign world/reference field owner.");
    }
    private void RequireOwner(bool allowRetired = false)
    {
        if (!allowRetired) ObjectDisposedException.ThrowIf(_retired, this);
        if (_writing || _thread != Environment.CurrentManagedThreadId)
        {
            _reentry = checked(_reentry + 1);
            throw new InvalidOperationException("Player movement/control changed its source thread or reentered its actual call.");
        }
    }
}
