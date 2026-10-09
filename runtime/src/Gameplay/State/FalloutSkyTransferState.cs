using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// This is the source owner for the reset call, not a completion flag on a
// renderer registration. Entered failures retain every already-written field.
internal sealed partial class FalloutSkyTransferState
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutImageSpaceState _images;
    private readonly string _stack;
    private readonly float _daytimeExtension;
    private long _changed;
    private Guid _process;
    private int? _thread;
    private bool _retired;
    private string? _retirementFailure;
    private string? _modeFailure;
    private string? _clockFailure;
    private FalloutSkyTransferHandoff? _handoff;
    private FalloutSkyResetCall? _last;
    private Exception? _callbackViolation;
    private FalloutSkyChildBinding _cloudBinding = new(FalloutSkyChildDisposition.ConstructorNull, null, null);
    private FalloutSkyChildBinding _precipitationBinding = new(FalloutSkyChildDisposition.ConstructorNull, null, null);
    private IFalloutSkyResetChild? _clouds;
    private IFalloutSkyResetChild? _precipitation;
    private readonly Dictionary<FalloutSourceSkyImageSlot, FalloutSourceSkyImageModifier> _instances = [];
    internal Guid Identity { get; } = Guid.NewGuid();
    internal FalloutSkyTransferDeclaration Source { get; }
    internal uint Flags { get; private set; } = 0x20;
    internal int Mode { get; private set; } = 4;
    internal uint HourBits { get; private set; }
    internal uint BlendBits { get; private set; } = BitConverter.SingleToUInt32Bits(1f);
    internal uint TransitionBits { get; private set; }
    internal FalloutFormKey? Climate { get; private set; }
    internal FalloutFormKey? CurrentWeather { get; private set; }
    internal FalloutFormKey? PreviousWeather { get; private set; }
    internal FalloutFormKey? OverrideWeather { get; private set; }
    internal FalloutFormKey? TargetWeather { get; private set; }
    internal FalloutSkyTimeCaches TimeCaches { get; private set; } = new(0, 0, 0, 0);
    internal FalloutSkyResetCall? LastCall => _last;
    internal string? SaveBlocker => _retirementFailure ?? (_retired ? "source-Sky-retired" :
        _last is { Returned: false } ? "source-Sky-reset-entered-prefix/" + (_last.Error ?? _last.EnteredChild.ToString()) :
        _cloudBinding.Disposition == FalloutSkyChildDisposition.Published && _clouds is null ? "source-Sky-cold-Clouds-republication-pending" :
        _precipitationBinding.Disposition == FalloutSkyChildDisposition.Published && _precipitation is null ? "source-Sky-cold-Precipitation-republication-pending" : null);
    internal object State => new
    {
        source = Source,
        sky = Identity,
        process = _process,
        changed = _changed,
        flags = Flags,
        mode = Mode,
        hourBits = HourBits,
        moonHourStore = _moonHourStore,
        blendBits = BlendBits,
        transitionBits = TransitionBits,
        climate = Climate,
        currentWeather = CurrentWeather,
        previousWeather = PreviousWeather,
        overrideWeather = OverrideWeather,
        targetWeather = TargetWeather,
        times = TimeCaches,
        standaloneBaseTimes = CaptureStandaloneBaseTimes(),
        clouds = _cloudBinding,
        precipitation = _precipitationBinding,
        images = OrderedInstances(),
        lastCall = _last,
        cold = _handoff,
        modeFailure = _modeFailure,
        clockFailure = _clockFailure,
        deferredBy = SaveBlocker,
        pixels = "unverified",
    };
    internal FalloutSkyTransferState(FalloutSkyTransferDeclaration source, FalloutPluginStack records,
        FalloutImageSpaceState images, string stack, float daytimeExtension)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(images);
        if (string.IsNullOrWhiteSpace(stack) || !float.IsFinite(daytimeExtension) || daytimeExtension < 0)
            throw new InvalidDataException("Source Sky omitted its immutable selection or actual GMST producer.");
        Source = source; _records = records; _images = images; _stack = stack; _daytimeExtension = daytimeExtension;
        RequireStandaloneExtensionSource();
        HourBits = source.ConstructorClockBits; _changed = 1;
    }
    internal void BindProcess(Guid process)
    {
        RequireLiving();
        if (process == Guid.Empty || _process != Guid.Empty && _process != process)
            throw new InvalidOperationException("Sky reset belongs to another actual campaign process.");
        _process = process;
    }
    internal void BindPresentationThread()
    {
        RequireLiving(); var thread = System.Environment.CurrentManagedThreadId;
        if (_thread is { } bound && bound != thread) throw new InvalidOperationException("Sky presentation lease belongs to another actual thread.");
        _thread = thread;
    }
    private long Next() => _changed = checked(_changed + 1);
    private void RequireLiving()
    {
        if (_retired || _retirementFailure is not null) throw new ObjectDisposedException(nameof(FalloutSkyTransferState), _retirementFailure);
        Source.Validate();
    }
    private void RequireWriter()
    {
        RequireLiving();
        var thread = System.Environment.CurrentManagedThreadId;
        if (_thread is { } bound && bound != thread) throw new InvalidOperationException("Sky source call left its actual presentation-thread lease.");
        if (_last is { Returned: false })
        {
            var failure = new InvalidOperationException("An entered Sky reset prefix cannot be replayed or overwritten.", new InvalidOperationException(_last.Error));
            _callbackViolation ??= failure; throw failure;
        }
    }
    internal FalloutSkyTransferReturn ResetForTransfer(FalloutMainPlayerCellInvocation invocation,
        FalloutPlayerPendingRequest request, FalloutPlayerTransferPayload payload, Action requireRequest,
        Action<FalloutSkyResetStep> publishFields)
    {
        invocation.Require(FalloutMainPlayerCellStep.PendingDestination);
        BindPresentationThread();
        ArgumentNullException.ThrowIfNull(requireRequest); requireRequest();
        var declared = FalloutSkyTransferDeclaration.Read(payload.FactoryReceipt?.Source ??
            throw new InvalidDataException("Sky transfer has no original raw payload factory receipt."));
        if (declared != Source || payload.TransferArgument == 0 || _process != invocation.Main.Process)
            throw new InvalidDataException("Sky reset changed its real request/source/argument/process association.");
        var context = NewContext(FalloutSkyResetOrigin.PlayerTransfer, invocation.Main.Identity, request.Identity);
        Reset(context, () => { invocation.Require(FalloutMainPlayerCellStep.PendingDestination); requireRequest(); }, publishFields);
        var receipt = new FalloutSkyTransferReturn(Identity, _process, invocation.Main.Identity, request.Identity,
            context.Call, _last!.Changed, Source.Contract);
        RequireReturn(invocation, request, receipt); return receipt;
    }
    private FalloutSkyResetContext NewContext(FalloutSkyResetOrigin origin, Guid? main, Guid? request)
    {
        RequireWriter();
        if (_process == Guid.Empty) throw new InvalidOperationException("Sky source invocation has no actual current process.");
        return new(Identity, _process, Guid.NewGuid(), main, request, Next(), origin, Source.Contract);
    }
    internal void ResetClimate(FalloutFormKey climate, Action<FalloutSkyResetStep> publishFields)
    {
        RequireWriter(); _ = FalloutClimateLighting.Read(_records.GetEffective(climate));
        if (Climate == climate) return;
        Climate = climate;
        if (!Source.IsStandalone) Flags |= 0x3f00;
        Next();
        Reset(NewContext(FalloutSkyResetOrigin.ClimateSelection, null, null), RequireLiving, publishFields);
        Flags |= 0x40; Next();
    }
    private void Reset(FalloutSkyResetContext context, Action require, Action<FalloutSkyResetStep> publishFields)
    {
        ArgumentNullException.ThrowIfNull(require); ArgumentNullException.ThrowIfNull(publishFields);
        var completed = new List<FalloutSkyResetStep>();
        var combinedFlags = SelectedResetFlags(context);
        _last = new(context, Next(), [], null, false, null, null);
        try
        {
            foreach (var step in ResetSteps(Source))
                Step(step, () => ApplySelectedResetStep(step, combinedFlags, context, publishFields));
            require(); _last = _last with { Changed = Next(), EnteredChild = null, Returned = true };
        }
        catch (Exception error)
        {
            _last = _last! with
            {
                Changed = Next(),
                FailureType = error.GetType().FullName,
                Error =
                string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message
            };
            throw;
        }
        void Step(FalloutSkyResetStep step, Action action)
        {
            require(); _last = _last! with { Changed = Next(), EnteredChild = step };
            action();
            if (_callbackViolation is { } violation) throw new InvalidOperationException("Sky child swallowed forbidden source reentry.", violation);
            require(); completed.Add(step);
            _last = _last with { Changed = Next(), Completed = completed.ToArray(), EnteredChild = null };
        }
    }
    internal void RequireReturn(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request,
        FalloutSkyTransferReturn receipt)
    {
        invocation.Require(FalloutMainPlayerCellStep.PendingDestination);
        if (_last is not { Returned: true, FailureType: null, Error: null } call || receipt.Sky != Identity ||
            receipt.Process != _process || receipt.Main != invocation.Main.Identity || receipt.Request != request.Identity ||
            call.Context.Call != receipt.Call || call.Context.Main != receipt.Main || call.Context.Request != receipt.Request ||
            receipt.Returned != call.Changed || receipt.SourceContract != Source.Contract ||
            call.Context.Origin != FalloutSkyResetOrigin.PlayerTransfer || !call.Completed.SequenceEqual(ResetSteps(Source)))
            throw new InvalidDataException("Sky transfer did not return all actual original children for this exact pending request.");
    }
}
