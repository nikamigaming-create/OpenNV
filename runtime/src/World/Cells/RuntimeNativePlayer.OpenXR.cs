using Godot;
using OpenNV.Runtime.Presentation.OpenXR;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private NativeXrRig? _xr;
    private NativeXrPointer? _xrPointer;
    private NativeXrHandContact? _xrLeftContact, _xrRightContact;
    private NativeXrWeaponSupport? _xrSupport;
    private readonly NativeXrBodyHeading _xrBodyHeading = new();
    private string? _xrContactError;
    private Vector3 _aimHit;
    internal Func<Node, NativeXrTarget?>? ResolveXrTarget { get; set; }
    internal Actors.RuntimeNativePlayerActor? XrPresentation => _xr is null || _presentationError is not null ? null : _firstPerson;
    internal event Action? PresentationChanged;
    internal object? XrState => _xr is null ? null : new
    {
        tracking = _xr.State,
        hands = _firstPerson?.XrHandState,
        contact = new
        {
            left = _xrLeftContact?.State,
            right = _xrRightContact?.State,
            support = _xrSupport?.State,
            error = _xrContactError ?? _firstPerson?.XrContactPoseError ?? _thirdPerson?.XrContactPoseError
        },
        pointerTarget = _xrPointer?.TargetName
    };
    internal void PublishXrHands(double delta)
    {
        if (_xr is null || _firstPerson is null) return;
        var right = _xr.RightGrip; var left = _xr.LeftGrip;
        var pointing = _xr.WorldPointer || _xr.PointAtPipBoy is not null || _modalInput;
        _firstPerson.SetXrInteraction(pointing);
        var weaponHeld = _weaponHandling!.Drawn && _firstPerson.Weapon is not null && !pointing;
        var heading = _xrBodyHeading.Advance(_xr.Camera.GlobalTransform, new Vector2(Velocity.X, Velocity.Z).LengthSquared() > .01f, delta);
        _firstPerson.SetTrackedBodyFrame(heading, GlobalPosition);
        _thirdPerson?.SetTrackedBodyFrame(heading, GlobalPosition);
        _thirdPerson!.PrepareTrackedBody(_xr.Camera.GlobalTransform);
        _firstPerson.AdoptTrackedTorso(_thirdPerson);
        var leftPose = left.GlobalTransform; var rightPose = right.GlobalTransform;
        var leftTracked = left.GetHasTrackingData(); var rightTracked = right.GetHasTrackingData();
        Basis? aim = _xr.RightAim.GetHasTrackingData() ? _xr.RightAim.GlobalBasis : null;
        var primary = new Transform3D(aim ?? rightPose.Basis, rightPose.Origin);
        var supportAction = _weaponAction is null ||
            !_weaponAction.StartsWith("reload", StringComparison.Ordinal) && _weaponAction is not ("equip" or "unequip");
        var supportReachable = _xrSupport is null || _firstPerson.XrLeftArm.CanReach(
            _firstPerson.XrLeftArm.HandTarget(_xrSupport.SocketPose(primary)));
        if (_xrSupport is not null && aim is not null)
            aim = _xrSupport.Advance(delta, primary, leftPose,
                weaponHeld && leftTracked && rightTracked && !pointing && supportAction && supportReachable);
        else _xrSupport?.Release();
        var rightTarget = _firstPerson.XrRightArm.ConstrainHandTarget(_firstPerson.XrRightArm.HandTarget(rightPose, weaponHeld ? aim : null));
        _xrRightContact?.SetWeaponEnabled(weaponHeld);
        var rightContact = _xrRightContact?.Resolve(rightTarget, rightTracked,
            CanOccupyPosition?.Invoke(rightTarget.Origin) != false, delta);
        if (rightContact is { } resolved)
        {
            primary = _firstPerson.XrRightArm.GripFromHand(resolved, weaponHeld && aim is not null);
            if (_xrSupport is not null)
            {
                if (!_firstPerson.XrLeftArm.CanReach(_firstPerson.XrLeftArm.HandTarget(_xrSupport.SocketPose(primary)))) _xrSupport.Release();
                leftPose = _xrSupport.SupportPose(primary, leftPose);
            }
        }
        var leftTarget = _firstPerson.XrLeftArm.ConstrainHandTarget(_firstPerson.XrLeftArm.HandTarget(leftPose));
        var leftContact = _xrLeftContact?.Resolve(leftTarget, leftTracked,
            CanOccupyPosition?.Invoke(leftTarget.Origin) != false, delta);
        if (_xrSupport is { Engaged: true } && (leftContact?.Origin.DistanceTo(leftTarget.Origin) > .03f ||
            _xrLeftContact?.Valid != true || _xrRightContact?.Valid != true)) _xrSupport.Release();
        _firstPerson.PoseTrackedArms(delta, _xr.Camera.GlobalTransform, leftPose, rightPose,
            left.GetHasTrackingData(), right.GetHasTrackingData(), left.GetFloat(NativeXrActions.PipBoy), right.GetFloat(NativeXrActions.Activate),
            left.GetFloat(NativeXrActions.FingerTrigger), right.GetFloat(NativeXrActions.Fire), left.IsButtonPressed(NativeXrActions.ThumbTouch),
            right.IsButtonPressed(NativeXrActions.ThumbTouch), weaponHeld, aim, leftContact, rightContact, _xrSupport is { Engaged: true }, bodyPrepared: true);
        if (_thirdPerson is not null)
        {
            _thirdPerson.SetXrInteraction(pointing);
            _thirdPerson.PoseTrackedArms(delta, _xr.Camera.GlobalTransform, leftPose, rightPose,
                left.GetHasTrackingData(), right.GetHasTrackingData(), left.GetFloat(NativeXrActions.PipBoy), right.GetFloat(NativeXrActions.Activate),
                left.GetFloat(NativeXrActions.FingerTrigger), right.GetFloat(NativeXrActions.Fire), left.IsButtonPressed(NativeXrActions.ThumbTouch),
                right.IsButtonPressed(NativeXrActions.ThumbTouch), weaponHeld, aim, leftContact, rightContact, _xrSupport is { Engaged: true }, bodyPrepared: true);
        }
    }
    private void BindXrContacts()
    {
        ReleaseXrContacts(); _xrContactError = null;
        if (_xr is null || _firstPerson is null || _thirdPerson is null) return;
        try
        {
            _xrLeftContact = new(GetWorld3D().Space, SelfQueryBodies, CollisionMask);
            _xrRightContact = new(GetWorld3D().Space, SelfQueryBodies, CollisionMask);
            _firstPerson.BindHandContact(_xrLeftContact, _thirdPerson, true);
            _firstPerson.BindHandContact(_xrRightContact, _thirdPerson, false);
            _xrSupport = _firstPerson.XrSupportGrip is { } socket ? new(socket) : null;
        }
        catch (Exception error)
        {
            ReleaseXrContacts(); _xrContactError = error.Message;
            GD.PushError("OPENNV_XR_CONTACT_UNBOUND " + error.Message);
        }
    }
    private void ReleaseXrContacts()
    {
        _xrLeftContact?.Dispose(); _xrRightContact?.Dispose();
        _xrLeftContact = _xrRightContact = null; _xrSupport = null;
    }
    public override void _ExitTree() => ReleaseXrContacts();
    internal void AttachXr(NativeXrRig rig)
    {
        if (_xr is not null) throw new InvalidOperationException("Player already owns an XR adapter.");
        _camera.Current = false; _camera.QueueFree();
        _xr = rig; rig.Attach(this); _camera = rig.Camera;
        ProcessMode = ProcessModeEnum.Always;
        _thirdPersonMode = false;
        _xrPointer = new(); AddChild(_xrPointer);
        GD.Print("OPENNV_NATIVE_XR_PLAYER_READY state=shared-native-gameplay camera=tracked-head controllers=left_hand,right_hand");
    }
    internal void XrActivate()
    {
        if (_modalInput || !_activationEnabled || _furniturePhase != 0 || AimedObject() is not { } collider) return;
        var target = ResolveXrTarget?.Invoke(collider);
        // Comfort pickup reach is an OpenNV option. Doors, conversations and
        // furniture still require the ordinary source activation distance.
        if (target?.RemotePickup != true && _xr!.RightAim.GlobalPosition.DistanceTo(_aimHit) > _configuration.Player.ActivationDistanceMeters) return;
        ActivateReference?.Invoke(collider);
    }
    internal void XrReload(bool pressed)
    {
        if (_modalInput || _furniturePhase != 0) return;
        using var input = new InputEventKey { PhysicalKeycode = Key.R, Pressed = pressed };
        WeaponInput(input);
    }
    internal void XrFire()
    {
        if (_modalInput || _furniturePhase != 0) return;
        using var input = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
        WeaponInput(input);
    }
    private void PublishXrPointer()
    {
        if (_xr is null) return;
        var collider = _xr.WorldPointer ? AimedObject() : null;
        var target = collider is null ? null : ResolveXrTarget?.Invoke(collider);
        if (target?.RemotePickup != true && _xr.RightAim.GlobalPosition.DistanceTo(_aimHit) > _configuration.Player.ActivationDistanceMeters)
            target = null;
        _xrPointer!.Publish(_xr.WorldPointer, _xr.RightAim.GlobalTransform,
            _xr.WorldPointer ? _xr.RightAim.GlobalPosition.DistanceTo(_aimHit) : 0, target);
    }
}
