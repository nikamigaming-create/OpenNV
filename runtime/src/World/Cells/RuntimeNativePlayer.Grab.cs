using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private RuntimeNifRigidBody? _grabbedBody;
    private float _grabDistance;
    private Vector3 _grabLocalPoint;
    private Vector3 _grabTarget;
    private FalloutPluginStack? _grabRecords;

    internal object GrabState => new
    {
        body = GodotObject.IsInstanceValid(_grabbedBody) ? _grabbedBody!.GetPath().ToString() : null,
        distance = _grabbedBody is null ? (float?)null : _grabDistance,
        separation = GodotObject.IsInstanceValid(_grabbedBody)
            ? _grabTarget.DistanceTo(_grabbedBody!.ToGlobal(_grabLocalPoint)) : (float?)null,
        owner = "source-dynamic-body;bounded-force-adapter",
    };

    private bool GrabInput(InputEvent inputEvent)
    {
        var action = _configuration.Player.DesktopInput.Grab.Action;
        if (inputEvent.IsActionReleased(action)) { ReleaseGrab(); return true; }
        if (!inputEvent.IsActionPressed(action) || inputEvent.IsEcho()) return false;
        if (_grabbedBody is not null) { ReleaseGrab(); return true; }
        if (!_modalInput && _activationEnabled && _furniturePhase == 0 && _sourceCamera is null && IsDefeated?.Invoke() != true)
            TryGrab();
        return true;
    }

    private bool TryGrab()
    {
        if (ActivationObject() is not RuntimeNifRigidBody { Freeze: false } body ||
            body.IsQueuedForDeletion() || !body.IsVisibleInTree() || !body.CanProcess()) return false;
        var limit = FalloutGameSettingFloats.Read(_grabRecords ??
            throw new InvalidOperationException("Player grab has no source setting owner."),
            Running ? "fGrabMaxWeightRunning" : "fGrabMaxWeightWalking");
        if (!double.IsFinite(limit) || limit < 0) throw new InvalidDataException("Source grab weight limit is invalid.");
        if (body.Mass > limit) return false;
        // Retain the actual picked surface and depth. The body applies a
        // bounded force on its physics clock; collision remains authoritative.
        var hit = _aimHit;
        if (!body.TryBeginGrab(this, hit, _configuration.Pickup.GrabResponseHertz,
            _configuration.Pickup.GrabMaximumAccelerationMetersPerSecondSquared)) return false;
        _grabbedBody = body; _grabLocalPoint = body.ToLocal(hit); _grabTarget = hit;
        _grabDistance = ActivationOrigin.DistanceTo(hit);
        return true;
    }

    private void AdvanceGrab()
    {
        if (_grabbedBody is null) return;
        if (!GodotObject.IsInstanceValid(_grabbedBody) || _grabbedBody.IsQueuedForDeletion() ||
            !_grabbedBody.IsVisibleInTree() || !_grabbedBody.CanProcess() || _grabbedBody.Freeze ||
            _modalInput || !_activationEnabled || _furniturePhase != 0 || _sourceCamera is not null || IsDefeated?.Invoke() == true)
        { ReleaseGrab(); return; }
        var aim = (Node3D?)_xr?.RightAim ?? _camera;
        _grabTarget = aim.GlobalPosition - aim.GlobalBasis.Z * _grabDistance;
        _grabbedBody.SetGrabTarget(this, _grabTarget);
    }

    private void ReleaseGrab()
    {
        if (GodotObject.IsInstanceValid(_grabbedBody)) _grabbedBody!.ReleaseGrab(this);
        _grabbedBody = null;
    }

}
