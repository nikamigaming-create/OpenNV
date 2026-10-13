using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerControlFlagEffectSnapshot(Guid Factory, Guid Observation,
    FalloutPlayerControlHudSnapshot Hud, FalloutPlayerFightingDisableCall? Fighting,
    FalloutPlayerPointOfViewCall? PointOfView = null)
{
    internal static void Validate(FalloutPlayerControlFlagEffectSnapshot saved, FalloutPlayerMovementControlSource source,
        string stack, Guid process, Guid observation, Guid factory)
    {
        if (saved.Factory != factory || factory == Guid.Empty || saved.Observation != observation || observation == Guid.Empty ||
            saved.Hud is null || saved.Hud.Process != process)
            throw new InvalidDataException("Player flag effects lost their actual observation, source process or consumer factory.");
        FalloutPlayerControlHudState.Validate(saved.Hud, FalloutPlayerControlHudSource.Reviewed(source), stack);
        if (saved.Fighting is { } call && (call.Process.Process == Guid.Empty || call.Process.Stack != stack ||
            call.Process.ExecutableSha256 != source.Main.EngineSha256 || !call.Process.Actor.EnginePlayer ||
            call.Process.GameplayOwnership == Guid.Empty || call.Process.Epoch < 1 || call.Stores != 0 ||
            !call.Returned || !call.GetterReturned || call.Drawn != false || call.Failure is not null ||
            ((call.MoverFlags & 0x800) != 0) != (call.ControlMode is not null) || call.ControlMode == 4))
            throw new InvalidDataException("Player flag fighting prefix invented a source process getter or mutation.");
        if (saved.PointOfView is { } view)
        {
            FalloutStandalonePlayerSceneState.ValidatePointOfViewCall(view);
            if (view.Observation != observation || view.Process != process)
                throw new InvalidDataException("Player flag POV prefix changed its exact actual source command/process.");
        }
    }
}

internal interface IFalloutPlayerControlFightingOwner
{
    FalloutPlayerMovementControlSource Source { get; }
    string Stack { get; }
    Guid Process { get; }
    Guid Factory { get; }
    void RequireCurrent();
    FalloutPlayerFightingDisableCall DisableFighting();
}

internal sealed class FalloutPlayerControlFightingOwner : IFalloutPlayerControlFightingOwner
{
    public FalloutPlayerMovementControlSource Source { get; }
    public string Stack { get; }
    public Guid Process { get; }
    public Guid Factory { get; } = Guid.NewGuid();
    private readonly FalloutActorProcessCommonState _common;
    private readonly FalloutFormKey _actor;
    private readonly long _epoch;
    private readonly Func<uint> _mover, _mode;
    private readonly Action _requireCurrent;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Guid _commonProcess, _gameplayOwner;
    private readonly FalloutDetectionProcessLevel _level;
    internal FalloutPlayerControlFightingOwner(FalloutPlayerMovementControlSource source, string stack, Guid process,
        FalloutActorProcessCommonState common, FalloutFormKey actor, long epoch, Func<uint> currentMoverFlags,
        Func<uint> currentControlMode, Action requireCurrentActorProcess)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(common); ArgumentNullException.ThrowIfNull(currentMoverFlags);
        ArgumentNullException.ThrowIfNull(currentControlMode); ArgumentNullException.ThrowIfNull(requireCurrentActorProcess);
        if (process == Guid.Empty || epoch < 1 || string.IsNullOrWhiteSpace(stack))
            throw new InvalidDataException("Player fighting service omitted its actual source process epoch.");
        Source = source; Stack = stack; Process = process; _common = common; _actor = actor; _epoch = epoch;
        _mover = currentMoverFlags; _mode = currentControlMode; _requireCurrent = requireCurrentActorProcess;
        _requireCurrent();
        var current = _common.ReadSceneQueries(_actor, _epoch);
        ValidateProcess(current); _commonProcess = current.Process; _gameplayOwner = current.GameplayOwnership; _level = current.Level;
    }
    public void RequireCurrent()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player fighting service changed its actual source thread.");
        _requireCurrent();
        var current = _common.ReadSceneQueries(_actor, _epoch); ValidateProcess(current);
        if (current.Process != _commonProcess || current.GameplayOwnership != _gameplayOwner || current.Level != _level)
            throw new InvalidOperationException("Player fighting changed its current common-process or gameplay lease.");
    }
    public FalloutPlayerFightingDisableCall DisableFighting()
    {
        RequireCurrent(); var flags = _mover(); RequireCurrent();
        uint? mode = (flags & 0x800) != 0 ? _mode() : null; RequireCurrent();
        var result = _common.DisablePlayerFighting(_actor, _epoch, flags, mode); RequireCurrent();
        if (result.Process.Process != _commonProcess || result.Process.Stack != Stack || result.Process.ExecutableSha256 != Source.Main.EngineSha256 ||
            result.Process.Actor.Reference != _actor || !result.Process.Actor.EnginePlayer || result.Process.Epoch != _epoch ||
            !result.Returned || result.Failure is not null)
            throw new InvalidDataException("Player fighting getter changed its actual Actor/process or returned source prefix.");
        return result;
    }
    private void ValidateProcess(FalloutProcessSceneQuery current)
    {
        if (current.Process == Guid.Empty || current.Stack != Stack || current.ExecutableSha256 != Source.Main.EngineSha256 ||
            current.Actor.Reference != _actor || !current.Actor.EnginePlayer || current.Epoch != _epoch || current.GameplayOwnership == Guid.Empty)
            throw new InvalidDataException("Fighting owner did not bind the genuine current Actor/common-process lease.");
    }
}

internal sealed class FalloutPlayerControlFlagConsumers : IFalloutPlayerControlFlagConsumers
{
    public FalloutPlayerMovementControlSource Source { get; }
    public string Stack { get; }
    public Guid Process { get; }
    public Guid Factory { get; } = Guid.NewGuid();
    private readonly Guid _observation;
    private readonly FalloutPlayerControlFieldsState _fields;
    private readonly FalloutPlayerControlHudState _hud;
    private readonly IFalloutPlayerControlFightingOwner? _fighting;
    private readonly FalloutStandalonePlayerSceneState? _pointOfView;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    internal FalloutPlayerFightingDisableCall? FightingCall { get; private set; }
    internal FalloutPlayerControlFlagConsumers(FalloutPlayerMovementControlSource source, string stack, Guid process,
        Guid observation, FalloutPlayerControlFieldsState fields, FalloutPlayerControlHudState hud,
        IFalloutPlayerControlFightingOwner? fighting = null, FalloutStandalonePlayerSceneState? pointOfView = null)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(fields); ArgumentNullException.ThrowIfNull(hud);
        if (process == Guid.Empty || observation == Guid.Empty || hud.Source.Controls != source || hud.Process != process || hud.Stack != stack ||
            fighting is not null && (fighting.Source != source || fighting.Stack != stack || fighting.Process != process || fighting.Factory == Guid.Empty) ||
            pointOfView is not null && (pointOfView.Source.Main != source.Main || pointOfView.Stack != stack || pointOfView.Process != process ||
                !ReferenceEquals(pointOfView.ControlFields, fields)))
            throw new InvalidDataException("Player flag service selected foreign source HUD/process fields or observation.");
        Source = source; Stack = stack; Process = process; _observation = observation; _fields = fields; _hud = hud; _fighting = fighting;
        _pointOfView = pointOfView;
    }
    public void RequireCurrent()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player flags changed the source service thread.");
        _ = _fields.ReadForEffects(_observation);
    }
    public void SetInputType(int type)
    {
        RequireCurrent(); _hud.Update(_observation, type, () => _fields.ReadForEffects(_observation)); RequireCurrent();
    }
    public void DisableFighting()
    {
        RequireCurrent(); var owner = _fighting ?? throw new NotSupportedException("source-Player-fighting-current-process-field-unbound");
        owner.RequireCurrent(); FightingCall = owner.DisableFighting(); owner.RequireCurrent(); RequireCurrent();
    }
    public void DisablePointOfView()
    {
        RequireCurrent();
        var owner = _pointOfView ?? throw new NotSupportedException("source-Player-POV-current-child-and-shared-camera-writers-unbound");
        owner.DisableControlPointOfView(_observation); RequireCurrent();
    }
    public void ClearSneaking()
    { RequireCurrent(); throw new NotSupportedException("source-Player-current-mover-sneak-store-and-animation-consumers-unbound"); }
    public FalloutPlayerControlFlagEffectSnapshot CaptureEffects() => new(Factory, _observation, _hud.Capture(), FightingCall,
        _pointOfView?.CaptureControlPointOfViewCall(_observation));
}
