using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerControlFlagStep { Stored, InputType, Fighting, PointOfView, Sneaking, Returned }
internal sealed record FalloutPlayerControlFlagCall(Guid Observation, string Owner, long Entered, long Changed,
    bool Enable, IReadOnlyList<bool> Arguments, byte Mask, ushort Before, ushort After, int Stores,
    int InputType, FalloutPlayerControlFlagStep Step, Guid? EffectsFactory, bool Returned, string? Failure,
    FalloutPlayerControlFlagEffectSnapshot? Effects = null, string? EffectCaptureFailure = null,
    Guid CapturedProcess = default, Guid FieldFactory = default);
internal sealed record FalloutPlayerControlFieldsColdHandoff(Guid PreviousProcess, Guid CurrentProcess,
    Guid PreviousFactory, Guid CurrentFactory);
internal sealed record FalloutPlayerControlFieldsSnapshot(string Source, string Stack, Guid Process, Guid Factory,
    long Sequence, ushort DisabledWord, uint YawOffsetBits, long FlagStores, long YawStores,
    FalloutPlayerControlFlagCall? Call, string? Failure, bool WordAvailable = true,
    IReadOnlyList<FalloutPlayerControlCommandObservation>? Observations = null,
    FalloutPlayerControlFieldsColdHandoff? ColdHandoff = null);

internal interface IFalloutPlayerControlFlagConsumers
{
    FalloutPlayerMovementControlSource Source { get; }
    string Stack { get; }
    Guid Process { get; }
    Guid Factory { get; }
    void RequireCurrent();
    void SetInputType(int type);
    void DisableFighting();
    void DisablePointOfView();
    void ClearSneaking();
    FalloutPlayerControlFlagEffectSnapshot? CaptureEffects() => null;
}

// The constructor owns a zero word and a separate zero Float32 yaw offset.
// Script add/remove masks enter the original full-byte writer: the stores
// happen before mandatory input and optional Actor/view/mover services. A
// failed service retains those stores without claiming a returned command.
internal sealed partial class FalloutPlayerControlFieldsState
{
    internal const byte LookingDisabled = 2;
    internal const string InputTypeBoundary = "source-Player-control-flag-input-type-service-unbound";
    internal const string ObservedWriterBoundary = "source-Player-control-observed-command-flag-writer-unbound";
    private readonly FalloutPlayerMovementControlSource _source;
    private readonly string _stack;
    private readonly Guid _process, _factory = Guid.NewGuid();
    private int _thread = Environment.CurrentManagedThreadId;
    private Guid? _inputFactory;
    private ushort _word;
    private uint _yaw;
    private long _sequence, _stores, _yawStores, _reentry;
    private FalloutPlayerControlFlagCall? _call;
    private string? _failure;
    private bool _writing;
    private readonly FalloutPlayerControlFieldsColdHandoff? _cold;
    private readonly List<FalloutPlayerControlCommandObservation> _observations = [];
    private readonly HashSet<Guid> _observedIds = [];

    internal FalloutPlayerControlFieldsState(FalloutPlayerMovementControlSource source, string stack, Guid process,
        FalloutPlayerControlFieldsSnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty) throw new ArgumentException("Player control fields have no actual source process.", nameof(process));
        _source = source; _stack = stack; _process = process;
        if (saved is null) return;
        Validate(saved, source, stack);
        if (saved.Process == process) throw new InvalidDataException("Cold Player control fields reused their previous process.");
        _word = saved.DisabledWord; _yaw = saved.YawOffsetBits; _sequence = saved.Sequence;
        _stores = saved.FlagStores; _yawStores = saved.YawStores; _call = saved.Call; _failure = saved.Failure;
        _observations.AddRange(saved.Observations!);
        _observedIds.UnionWith(saved.Observations!.Select(value => value.Observation));
        _cold = new(saved.Process, _process, saved.Factory, _factory);
    }
    internal ushort ReadDisabledWord()
    {
        RequireOwner(allowFailure: true); RequireCurrentWord();
        if (_failure is { } failure) throw new NotSupportedException(failure);
        return _word;
    }
    internal uint ReadYawOffset() { RequireOwner(); return _yaw; }

    internal void BindActualInput(FalloutDirectInputState input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_writing || _inputFactory is not null)
            throw new InvalidOperationException("Player fields already have an actual input-thread publication or entered writer.");
        var current = input.ReadManagerOwner();
        if (!ReferenceEquals(input, current) || input.Source != _stack || input.Factory == Guid.Empty ||
            !FalloutDirectInputState.ValidProvider(input.Provider, input.Window))
            throw new InvalidOperationException("Player fields cannot transfer to a foreign current input owner.");
        // This explicit product publication transfers the loading constructor
        // to the validated gameplay input thread. Ordinary getters and command
        // writers never acquire a thread from their first caller.
        _thread = Environment.CurrentManagedThreadId; _inputFactory = current.Factory;
    }

    internal void RequireActualInput(FalloutDirectInputState input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var current = input.ReadManagerOwner();
        if (_writing || _thread != Environment.CurrentManagedThreadId || _inputFactory != input.Factory ||
            !ReferenceEquals(input, current) || input.Source != _stack || !FalloutDirectInputState.ValidProvider(input.Provider, input.Window))
            throw new InvalidOperationException("Player controls lost their actual published input/thread handoff.");
    }

    internal bool HasActualInput => _inputFactory is not null;

    internal ushort ReadForEffects(Guid command)
    {
        if (!_writing || _thread != Environment.CurrentManagedThreadId || _call?.Observation != command ||
            _observations.Count != 0 || command == Guid.Empty)
            throw new InvalidOperationException("Player effect getter requires the exact entered source command and current field owner.");
        return _word;
    }

    internal void ApplyCommand(Guid observation, string owner, FalloutPlayerControlCommand command,
        IFalloutPlayerControlFlagConsumers? consumers = null)
    {
        RequireOwner(); ArgumentNullException.ThrowIfNull(command); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        RequireCurrentWord();
        if (observation == Guid.Empty || _call?.Observation == observation) throw new InvalidOperationException("Player control flags replayed their actual command observation.");
        var mask = CommandMask(command); var before = _word;
        var requested = command.Enable ? (byte)((byte)before & ~mask) : (byte)((byte)before | mask);
        _writing = true; var fault = _reentry;
        var entered = Next();
        _word = (ushort)((before & 0xff00) | requested); _stores = checked(_stores + 1);
        var stores = 1;
        if (requested != byte.MaxValue)
        {
            _word = (ushort)((before & 0xff00) | (requested & 0x7f));
            _stores = checked(_stores + 1); stores = 2;
        }
        var type = (_word & 1) != 0 ? 12 : 1;
        _call = new(observation, owner, entered, Next(), command.Enable, command.Arguments.ToArray(), mask, before,
            _word, stores, type, FalloutPlayerControlFlagStep.Stored, null, false, null, CapturedProcess: _process, FieldFactory: _factory);
        try
        {
            Step(FalloutPlayerControlFlagStep.InputType);
            if (consumers is null) throw new NotSupportedException(InputTypeBoundary);
            consumers.RequireCurrent(); Check();
            if (consumers.Source != _source || consumers.Stack != _stack || consumers.Process != _process || consumers.Factory == Guid.Empty)
                throw new InvalidOperationException("Player control flags selected foreign effect services.");
            _call = _call with { EffectsFactory = consumers.Factory, Changed = Next() };
            consumers.SetInputType(type); Check(); consumers.RequireCurrent(); Check(); CaptureEffects();
            if ((_word & 8) != 0) { Step(FalloutPlayerControlFlagStep.Fighting); consumers.DisableFighting(); Check(); CaptureEffects(); }
            if ((_word & 0x10) != 0) { Step(FalloutPlayerControlFlagStep.PointOfView); consumers.DisablePointOfView(); Check(); }
            if ((_word & 0x40) != 0) { Step(FalloutPlayerControlFlagStep.Sneaking); consumers.ClearSneaking(); Check(); }
            consumers.RequireCurrent(); Check(); CaptureEffects(); Step(FalloutPlayerControlFlagStep.Returned);
            _call = _call with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _failure ??= error.Message; _call = _call with { Failure = _failure, Changed = Next() };
            if (_call.EffectsFactory is not null)
                try { CaptureEffects(); }
                catch (Exception captureError) { _call = _call with { EffectCaptureFailure = captureError.Message, Changed = Next() }; }
            throw;
        }
        finally { _writing = false; }
        void Step(FalloutPlayerControlFlagStep step) => _call = _call! with { Step = step, Changed = Next() };
        void CaptureEffects()
        {
            var capture = consumers?.CaptureEffects();
            if (capture is null) return;
            FalloutPlayerControlFlagEffectSnapshot.Validate(capture, _source, _stack, _process, observation, consumers!.Factory);
            _call = _call! with { Effects = capture, Changed = Next() };
        }
        void Check()
        {
            if (_thread != Environment.CurrentManagedThreadId || _reentry != fault)
                throw new InvalidOperationException("Player flag effect swallowed reentry or changed its actual source thread.");
        }
    }

    internal FalloutPlayerControlFieldsSnapshot Capture()
    {
        if (_writing || _inputFactory is not null && _thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Entered or published Player fields cannot be captured from a foreign source thread.");
        var saved = new FalloutPlayerControlFieldsSnapshot(_source.Identity, _stack, _process, _factory,
            _sequence, _word, _yaw, _stores, _yawStores, _call, _failure, _observations.Count == 0, _observations.ToArray(), _cold);
        Validate(saved, _source, _stack); return saved;
    }
    internal static byte CommandMask(FalloutPlayerControlCommand command)
    {
        var selected = command.SelectedStandaloneArguments();
        byte mask = 0;
        ReadOnlySpan<byte> bits = [1, 4, 8, 0x10, 2, 0x20, 0x40];
        for (var index = 0; index < selected.Length; ++index) if (selected[index]) mask |= bits[index];
        return mask;
    }
    internal static void Validate(FalloutPlayerControlFieldsSnapshot saved, FalloutPlayerMovementControlSource source, string stack)
    {
        source.Validate();
        if (saved.Source != source.Identity || saved.Stack != stack || saved.Process == Guid.Empty || saved.Factory == Guid.Empty ||
            saved.Sequence < 0 || saved.FlagStores < 0 || saved.YawOffsetBits != 0 || saved.YawStores != 0 || (saved.DisabledWord & 0xff00) != 0 ||
            saved.Observations is null || saved.WordAvailable != (saved.Observations.Count == 0) ||
            saved.Observations.Select(value => value.Observation).Distinct().Count() != saved.Observations.Count)
            throw new InvalidDataException("Player control fields lost their independent source constructor/store history.");
        foreach (var observation in saved.Observations) observation.Validate();
        if (saved.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.PreviousProcess == cold.CurrentProcess ||
            cold.CurrentProcess != saved.Process || cold.PreviousFactory == Guid.Empty || cold.PreviousFactory == cold.CurrentFactory || cold.CurrentFactory != saved.Factory))
            throw new InvalidDataException("Player control fields lost their explicit cold process/factory handoff.");
        if (saved.Call is not { } call)
        {
            if (saved.Sequence != saved.Observations.Count || saved.DisabledWord != 0 || saved.FlagStores != 0 || saved.Failure is not null)
                throw new InvalidDataException("Unwritten Player controls acquired field mutations or a failure.");
            return;
        }
        var command = new FalloutPlayerControlCommand(call.Enable, call.Arguments);
        var selected = call.Enable ? (byte)((byte)call.Before & ~call.Mask) : (byte)((byte)call.Before | call.Mask);
        var result = (ushort)((call.Before & 0xff00) | (selected == byte.MaxValue ? selected : selected & 0x7f));
        if (call.Observation == Guid.Empty || call.CapturedProcess == Guid.Empty || call.FieldFactory == Guid.Empty ||
            (call.CapturedProcess == saved.Process ? call.FieldFactory != saved.Factory : saved.ColdHandoff is null || call.FieldFactory == saved.Factory) ||
            string.IsNullOrWhiteSpace(call.Owner) || call.Entered < 1 ||
            call.Changed <= call.Entered || call.Changed + saved.Observations.Count != saved.Sequence || call.Mask != CommandMask(command) ||
            call.After != result || saved.DisabledWord != result || call.Stores != (selected == byte.MaxValue ? 1 : 2) ||
            saved.FlagStores < call.Stores || call.InputType != ((result & 1) != 0 ? 12 : 1) || !Enum.IsDefined(call.Step) ||
            call.Returned != (call.Step == FalloutPlayerControlFlagStep.Returned) ||
            call.Returned != (call.Failure is null) || call.Failure != saved.Failure ||
            call.EffectsFactory is null && (call.Step != FalloutPlayerControlFlagStep.InputType || call.Returned) ||
            call.EffectsFactory == Guid.Empty || call.EffectCaptureFailure is not null && (call.Returned || string.IsNullOrWhiteSpace(call.EffectCaptureFailure)))
            throw new InvalidDataException("Player flag writer lost its actual mask/stores, ordered service prefix or complete return.");
        ValidateEffects(call, source, stack, call.CapturedProcess);
    }
    internal static void ValidateEffects(FalloutPlayerControlFlagCall call, FalloutPlayerMovementControlSource source, string stack, Guid process)
    {
        if (call.CapturedProcess != process)
            throw new InvalidDataException("Player flag call changed the process that actually entered its source writer.");
        if (call.Effects is not { } effects) return;
        if (call.EffectsFactory is not { } factory)
            throw new InvalidDataException("Player flag effect snapshot omitted its actual selected consumer factory.");
        FalloutPlayerControlFlagEffectSnapshot.Validate(effects, source, stack, process, call.Observation, factory);
        if (call.Step != FalloutPlayerControlFlagStep.InputType &&
            (effects.Hud.Call is not { Returned: true } hud || hud.Invocation != call.Observation || hud.Requested != call.InputType))
            throw new InvalidDataException("Player flag continuation lost its mandatory returned source HUD call.");
        if (call.Returned && (call.After & 8) != 0 && effects.Fighting is not { Returned: true, GetterReturned: true, Drawn: false, Stores: 0, Failure: null })
            throw new InvalidDataException("Returned source controls omitted their actual independent fighting getter/early return.");
        if (call.Returned && (call.After & 0x10) != 0 && effects.PointOfView is not
            {
                Returned: true, GetterReturned: true,
                FirstPerson: null, DesiredViewStored: not null, SharedScalarStored: not null, Failure: null
            })
            throw new InvalidDataException("Returned source controls omitted their ordered actual POV field/scalar stores.");
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private void RequireOwner(bool allowFailure = false)
    {
        if (_writing || _thread != Environment.CurrentManagedThreadId)
        {
            _reentry = checked(_reentry + 1);
            throw new InvalidOperationException("Player control fields changed their actual thread or reentered a source write.");
        }
        if (!allowFailure && _failure is { } failure) throw new NotSupportedException(failure);
    }
}
