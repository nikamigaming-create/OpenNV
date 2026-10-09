using System.Runtime.InteropServices;
using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Ordinary native delivery adapts only the current callback and OS key query.
// The shared Main owner mints its actual invoked source segment and ordinal;
// Engine.GetProcessFrames is never itself a source completion receipt.
internal sealed partial class RuntimeNativeSourceMainScriptCaller : Node
{
    private readonly FalloutReferenceWorld _world;
    private readonly Action<Exception> _failed;
    private IDisposable? _callerLease, _fadeLease;
    private bool _started, _retired;
    private Exception? _failure, _retirementFailure;
    internal object State => new { source = _world.CampaignMainScriptCallerState, started = _started,
        retired = _retired, failure = _failure?.ToString(), retirementFailure = _retirementFailure?.ToString() };
    private RuntimeNativeSourceMainScriptCaller(FalloutReferenceWorld world, Action<Exception> failed)
    {
        _world = world; _failed = failed; Name = "SourceMainScriptCaller";
        ProcessMode = ProcessModeEnum.Disabled;
        // Existing shared clock is int.MinValue. This orders the real current
        // Godot adaptation before ordinary driver consumers; it does not claim
        // the rest of the original Main frame has acquired a producer.
        ProcessPriority = int.MinValue + 1;
    }
    internal static RuntimeNativeSourceMainScriptCaller Attach(Node parent, FalloutReferenceWorld world,
        FalloutInterfaceFade actualFade, Action<Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(failed); ArgumentNullException.ThrowIfNull(actualFade);
        if (!OperatingSystem.IsWindows() || !parent.IsInsideTree() || parent.IsQueuedForDeletion() || !world.CampaignSharedScriptRuntimeConfigured)
            throw new NotSupportedException("Original Main caller needs its actual selected Windows/native/source construction lifetime.");
        var source = world.CampaignMainScriptSource; source.Validate();
        var result = new RuntimeNativeSourceMainScriptCaller(world, failed);
        try
        {
            parent.AddChild(result);
            if (!result.IsInsideTree() || result.GetParent() != parent) throw new InvalidOperationException("Native Main caller was not attached to its actual campaign.");
            result._fadeLease = world.BindCampaignScriptFrameFade(actualFade);
            result._callerLease = world.BindCampaignMainScriptCaller(new FalloutCampaignMainScriptConsumers(source, ReadOsKeyHighBit),
                "actual-native-Main-adapter/" + result.GetInstanceId());
            return result;
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            try { result.Retire(); } catch (Exception cleanup) { failures.Add(cleanup); }
            try { result.Free(); } catch (Exception cleanup) { failures.Add(cleanup); }
            if (failures.Count != 1) throw new AggregateException("Main native attachment and retirement failed.", failures);
            throw;
        }
    }
    internal void Start()
    {
        if (_retired || _started || !IsInsideTree() || IsQueuedForDeletion() || _callerLease is null || _fadeLease is null || _failure is not null)
            throw new InvalidOperationException("Main native callback cannot start before actual source/fade consumers exist.");
        _started = true; ProcessMode = ProcessModeEnum.Always;
    }
    public override void _Process(double delta)
    {
        if (!_started || _retired || _failure is not null) return;
        try
        {
            if (!double.IsFinite(delta) || delta < 0 || !float.IsFinite((float)delta)) throw new InvalidDataException("Main native adapter delta is not finite Float32.");
            _world.ExecuteCampaignMainScriptCaller(Engine.GetProcessFrames(), (float)delta);
        }
        catch (Exception failure)
        {
            _failure ??= failure; ProcessMode = ProcessModeEnum.Disabled;
            try { _failed(failure); }
            catch (Exception publication) { _failure = new AggregateException("Main source failure and error publication both failed.", _failure, publication); }
            GD.PushError("OPENNV_SOURCE_MAIN_CALLER_REFUSED " + _failure);
        }
    }
    internal void Retire()
    {
        _retired = true; ProcessMode = ProcessModeEnum.Disabled;
        var failures = new List<Exception>();
        if (_retirementFailure is { } prior) failures.Add(prior);
        // Native delivery closes before the source fade. A refused entered
        // release keeps its actual lease for a later safe cleanup attempt;
        // the first retirement failure remains visible even after cleanup.
        try { _callerLease?.Dispose(); _callerLease = null; } catch (Exception error) { failures.Add(error); }
        try { _fadeLease?.Dispose(); _fadeLease = null; } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0)
        {
            _retirementFailure = failures.Count == 1 ? failures[0] : new AggregateException("Main native/source lifetime retirement failed.", failures);
            throw _retirementFailure;
        }
    }
    public override void _ExitTree()
    {
        try { Retire(); }
        catch (Exception failure) { _failure ??= failure; _failed(failure); }
    }
    private static bool ReadOsKeyHighBit(int virtualKey)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Original Main OS key producer requires Windows.");
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }
    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);
}
