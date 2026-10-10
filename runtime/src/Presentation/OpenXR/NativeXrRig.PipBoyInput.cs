using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.OpenXR;

internal sealed partial class NativeXrRig
{
    private FalloutPipBoyHeldInput _pipBoyHeldInput = new();
    private bool _pipBoyInputFailureReported;
    private string? _pipBoySourceAccessFailure;
    private Func<Transform3D, bool, bool, Vector3?>? _pipBoyPointerOwner;
    private long _pipBoyPointerGeneration;
    internal Func<FalloutPipBoyInputBlocker>? PipBoyInputAccess { get; set; }
    internal string? PipBoyInputFailure => _pipBoyHeldInput.Failure ?? _pipBoySourceAccessFailure ?? _pipBoyHeldInput.RetirementFailure;
    internal object PipBoyInputState => new
    {
        blocker = _pipBoyHeldInput.Blocker.ToString(),
        tracked = _pipBoyHeldInput.Tracked,
        held = _pipBoyHeldInput.Held,
        awaitingRelease = _pipBoyHeldInput.AwaitRelease,
        requestedFocus = _pipBoyHeldInput.RequestedFocus,
        requestOrdinal = _pipBoyHeldInput.RequestOrdinal,
        lastRequestReturned = _pipBoyHeldInput.LastRequestReturned,
        retired = _pipBoyHeldInput.Retired,
        failure = _pipBoyHeldInput.Failure,
        sourceAccessFailure = _pipBoySourceAccessFailure,
        retirementFailure = _pipBoyHeldInput.RetirementFailure,
        actualPointerRoute = _pipBoyPointerOwner is not null,
        actualPointerGeneration = _pipBoyPointerGeneration,
        rightInputReady = _rightInputReady,
        snapReady = _snapReady,
    };

    private void SamplePipBoyInput(bool tracked, bool held)
    {
        if (_pipBoyHeldInput.Retired) return;
        try
        {
            var blocker = SetPipBoyHeld is null || PipBoyInputAccess is null
                ? FalloutPipBoyInputBlocker.Unbound : PipBoyInputAccess();
            _pipBoyHeldInput.Observe(tracked, held, blocker, DeliverPipBoyFocus, PointAtPipBoy is not null);
            ObservePipBoyPointerRoute();
        }
        catch (Exception error)
        {
            _pipBoySourceAccessFailure ??= error.ToString();
            if (!_pipBoyInputFailureReported)
            {
                _pipBoyInputFailureReported = true;
                GD.PushError("OPENNV_XR_PIPBOY_INPUT_FAILED " + error);
            }
            try { RetirePipBoyInput(); }
            catch (Exception retirement) { GD.PushError("OPENNV_XR_PIPBOY_INPUT_RETIRE_FAILED " + retirement); }
            // A failed callback cannot leave a callable pointer route. Actual
            // menu state/close failure stays with its original owner.
            PointAtPipBoy = null;
            ObservePipBoyPointerRoute();
        }
    }

    private void DeliverPipBoyFocus(bool focused)
    {
        var request = SetPipBoyHeld ?? throw new NotSupportedException("Wrist focus has no actual Pip-Boy request owner.");
        request(focused);
    }

    private void SampleCurrentWristAccess()
    {
        // This second observation belongs to actual wrist pose publication.
        // Scripts may revoke access after the earlier XR frame input sample.
        SamplePipBoyInput(LeftGrip.GetHasTrackingData(),
            LeftGrip.GetFloat(NativeXrActions.PipBoy) >= _configuration.Xr.ActionThreshold);
    }

    private void EnterPipBoyInput()
    {
        // Reparenting the same genuine rig delivers ExitTree/EnterTree. Its new
        // process-local capture requires a release; an old fault stays refused.
        if (_pipBoyHeldInput.Retired && PipBoyInputFailure is null)
            _pipBoyHeldInput = new();
    }

    private void RetirePipBoyInput()
    {
        try { _pipBoyHeldInput.Retire(DeliverPipBoyFocus, PointAtPipBoy is not null); }
        finally { ObservePipBoyPointerRoute(); }
    }

    private void ObservePipBoyPointerRoute()
    {
        var owner = PointAtPipBoy;
        if (Equals(_pipBoyPointerOwner, owner)) return;
        _pipBoyPointerOwner = owner;
        _pipBoyPointerGeneration = checked(_pipBoyPointerGeneration + 1);
        // A trigger/stick held on a UI capture must not become a new world
        // fire/grab/reload/snap request when the actual pointer route retires.
        _rightInputReady = false; _pointerPressed = false; _snapReady = false;
        WorldPointer = false;
        _player?.XrFire(false);
    }
}
