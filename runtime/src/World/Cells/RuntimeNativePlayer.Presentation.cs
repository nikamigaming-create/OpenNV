using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutPluginStack? _presentationRecords;
    private FalloutPlayerInventory? _presentationInventory;
    private Func<FalloutNpcAppearance>? _appearance;
    private Func<long>? _appearanceRevision;
    private Func<FalloutScriptSession>? _playerPolicy;
    private long _presentationAppearanceRevision = -1;
    private Func<Color>? _presentationAmbient;
    private RuntimeNativePlayerActor? _firstPerson, _thirdPerson;
    private SubViewport? _firstPersonView;
    private Camera3D? _firstPersonCamera;
    private TextureRect? _firstPersonPixels;
    private uint[]? _presentationEquipment;
    private long _presentationInventoryRevision = -1;
    private string? _presentationError;
    private bool _thirdPersonMode, _aiming;
    private FalloutWeaponSight? _weaponSight;
    private bool WeaponAiming => _aiming && _weaponHandling is { Drawn: true } && FalloutWeaponSight.CanAim(_firstPerson?.Weapon);
    private readonly Godot.Collections.Array<Rid> _selfQueryBodies = [];
    private float _thirdPersonDistance = 2;
    internal object PresentationState => new
    {
        thirdPerson = _thirdPersonMode,
        zoomMeters = _thirdPersonDistance,
        appearanceRevision = _presentationAppearanceRevision,
        sourceScale = _sourceScale,
        toddler = _playerPolicy?.Invoke().PlayerToddler ?? false,
        aiming = WeaponAiming,
        sight = _weaponSight is null ? null : new
        {
            horizontalDegrees = _weaponSight.HorizontalDegrees,
            targetDegrees = _weaponSight.TargetDegrees,
            transitionSeconds = _weaponSight.TransitionSeconds,
            worldVerticalDegrees = _camera.Fov,
            firstPersonVerticalDegrees = _firstPersonCamera?.Fov,
            projection = _xr is null ? "flat-source-weapon" : "OpenXR-runtime",
            retailTransition = "unverified"
        },
        first = _firstPerson?.State,
        third = _thirdPerson?.State,
        error = _presentationError,
        weaponHandling = WeaponHandlingState,
        limbMovement = LimbMovementState,
        xr = XrState
    };

    internal void ConfigurePresentation(FalloutPluginStack records, FalloutPlayerInventory inventory,
        Func<FalloutNpcAppearance> appearance, Func<Color> ambient, FalloutWeaponHandlingSnapshot? handling = null,
        Func<long>? appearanceRevision = null, Func<FalloutScriptSession>? playerPolicy = null)
    {
        _presentationRecords = records; _presentationInventory = inventory; _appearance = appearance; _presentationAmbient = ambient;
        _appearanceRevision = appearanceRevision;
        _playerPolicy = playerPolicy;
        if (playerPolicy is not null) ApplySourceScale(playerPolicy().PlayerScale);
        _weaponHandling = new(inventory);
        if (handling is not null) _weaponHandling.Restore(handling, key => FalloutWeaponPresentation.Read(records, key));
    }

    private bool PresentationInput(InputEvent input)
    {
        if (_modalInput || !_movementEnabled || _furniturePhase != 0 || _sourceCamera is not null) return false;
        if (WeaponInput(input)) return true;
        if ((_inputControls is not null ? input.IsActionPressed(InputSystem.RuntimeNativeInputControls.Action(13), allowEcho: false)
            : input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F }) && GetMeta("opennv_source_pointofview_enabled", true).AsBool())
        { _thirdPersonMode = !_thirdPersonMode; PublishThirdPersonCamera(); return true; }
        if (_inputControls is not null && (input.IsActionPressed(InputSystem.RuntimeNativeInputControls.Action(6), allowEcho: false) ||
            input.IsActionReleased(InputSystem.RuntimeNativeInputControls.Action(6))))
        { _aiming = input.IsActionPressed(InputSystem.RuntimeNativeInputControls.Action(6), allowEcho: false); return true; }
        if (input is InputEventMouseButton mouse)
        {
            if (_inputControls is null && mouse.ButtonIndex == MouseButton.Right) { _aiming = mouse.Pressed; return true; }
            if (mouse.Pressed && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && GetMeta("opennv_source_pointofview_enabled", true).AsBool())
            {
                if (mouse.ButtonIndex == MouseButton.WheelDown)
                { _thirdPersonDistance = _thirdPersonMode ? Math.Min(5, _thirdPersonDistance + .25f) : .5f; _thirdPersonMode = true; }
                else if (_thirdPersonMode)
                { _thirdPersonDistance -= .25f; if (_thirdPersonDistance < .5f) _thirdPersonMode = false; }
                PublishThirdPersonCamera(); return true;
            }
        }
        return false;
    }

    private void PublishThirdPersonCamera()
    {
        PublishSelfCameraVisibility();
        if (_xr is not null) return;
        if (_sourceCamera is not null || _furniturePhase != 0) return;
        if (!_thirdPersonMode && _firstPerson is { Toddler: true })
        {
            _camera.Transform = _firstPerson.SourceCamera * new Transform3D(new Basis(Vector3.Right, _pitchRadians), Vector3.Zero);
            return;
        }
        var head = Vector3.Up * _configuration.Player.SpawnCenterHeightMeters + _configuration.Player.DesktopCameraOffsetMeters.Vector3();
        var basis = new Basis(Vector3.Right, _pitchRadians);
        var offset = _thirdPersonMode ? basis.Z * _thirdPersonDistance : Vector3.Zero;
        if (_thirdPersonMode && IsInsideTree())
        {
            var query = PhysicsRayQueryParameters3D.Create(ToGlobal(head), ToGlobal(head + offset), CollisionMask);
            query.Exclude = SelfQueryBodies;
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.TryGetValue("position", out var value))
                offset = offset.Normalized() * Math.Max(0, ToLocal(value.AsVector3()).DistanceTo(head) - .1f);
        }
        _camera.Transform = new(basis, head + offset);
    }

    public override void _Process(double delta)
    {
        if (!GetTree().Paused) AdvancePendingProjectileImpacts((float)delta);
        if (_presentationRecords is null) return;
        var active = !_modalInput && _sourceCamera is null && _furniturePhase == 0 && _movementEnabled;
        if (_firstPersonPixels is not null) _firstPersonPixels.Visible = _presentationError is null && active && !_thirdPersonMode && (_firstPerson?.Weapon is null || _weaponHandling!.Drawn || _weaponAction is not null);
        if (_thirdPerson is not null)
        {
            _thirdPerson.Visible = _presentationError is null && (_xr is not null || _sourceCamera is null && _furniturePhase == 0);
            _thirdPerson.SetViewPolicy(_xr is not null || _thirdPersonMode, true);
        }
        if (_xr is not null && _firstPerson is not null) _firstPerson.Visible = _presentationError is null;
        var equipment = _presentationEquipment is not null && _presentationInventoryRevision == _presentationInventory!.Revision
            ? _presentationEquipment : _presentationInventory!.Equipped.ToArray();
        _presentationInventoryRevision = _presentationInventory.Revision;
        var appearanceRevision = _appearanceRevision?.Invoke() ?? 0;
        if (_presentationEquipment is null || !equipment.SequenceEqual(_presentationEquipment) || appearanceRevision != _presentationAppearanceRevision)
        {
            var changing = _presentationEquipment is not null;
            _presentationEquipment = equipment; _presentationError = null; CancelWeaponAction(); _pendingShotCount = 0; _shot = null;
            _presentationAppearanceRevision = appearanceRevision;
            try { RebuildPresentation(equipment); }
            catch (Exception error)
            {
                _presentationError = error.Message;
                if (_firstPerson is not null) _firstPerson.Visible = false;
                if (_thirdPerson is not null) _thirdPerson.Visible = false;
                if (_firstPersonPixels is not null) _firstPersonPixels.Visible = false;
                PresentationChanged?.Invoke();
                GD.PushError("OPENNV_PLAYER_PRESENTATION_UNBOUND " + error.Message);
            }
            if (changing && _presentationError is null && _weaponHandling!.Drawn) RequestWeaponAction("equip");
        }
        if (_playerPolicy is not null && _presentationError is null)
        {
            var policy = _playerPolicy();
            ApplySourceScale(policy.PlayerScale);
            _firstPerson?.SetToddler(policy.PlayerToddler);
        }
        if (!active && _xr is null && !(_firstPerson is { Toddler: true } && !_modalInput && _sourceCamera is null && _furniturePhase == 0))
        {
            PublishThirdPersonCamera();
            return;
        }
        if (_presentationError is not null) return;
        var simulationDelta = GetTree().Paused ? 0 : delta;
        AdvanceWeaponHandling(simulationDelta);
        var movement = GlobalBasis.Inverse() * Velocity;
        _firstPerson?.Advance(simulationDelta, movement, IsOnFloor(), WeaponAiming);
        _thirdPerson?.Advance(simulationDelta, movement, IsOnFloor(), WeaponAiming);
        try { PublishXrHands(delta); }
        catch (Exception error)
        {
            _presentationError = error.Message;
            GD.PushError("OPENNV_TRACKED_BODY_UNBOUND " + error.Message);
            return;
        }
        if (_firstPersonCamera is not null && _firstPerson is not null)
        {
            _firstPersonCamera.Transform = _firstPerson.SourceCamera;
            _firstPersonView!.Size = (Vector2I)GetViewport().GetVisibleRect().Size;
        }
        PublishWeaponSight(simulationDelta);
        if (_presentationError is not null) return;
        PublishThirdPersonCamera();
        PublishPendingShot();
    }

    private void PublishWeaponSight(double seconds)
    {
        if (_xr is not null) return;
        try
        {
            _weaponSight ??= new(_presentationRecords!, new FalloutCameraProjection(_camera.Fov, _camera.Near / UnitsToMeters).ReferenceHorizontalFovDegrees);
            var horizontal = _weaponSight.Advance(seconds, _firstPerson?.Weapon, WeaponAiming);
            _camera.Fov = FalloutCameraProjection.FromReferenceFov(horizontal, _camera.Near / UnitsToMeters).VerticalFovDegrees;
        }
        catch (Exception error)
        {
            _presentationError = error.Message;
            GD.PushError("OPENNV_WEAPON_SIGHT_UNBOUND " + error.Message);
        }
    }

    private void RebuildPresentation(uint[] equipped)
    {
        var records = _presentationRecords!; var content = RuntimeLiveContentSource.Current!;
        var appearance = _appearance!();
        var weapon = equipped.Select(records.RuntimeFormKey).Where(key => records.GetEffective(key).Signature == "WEAP")
            .Select(key => (FalloutFormKey?)key).SingleOrDefault();
        var ambient = _presentationAmbient!();
        RuntimeNativePlayerActor? first = null, third = null;
        try
        {
            first = new(records, content, appearance, weapon, true, UnitsToMeters, ambient);
            third = new(records, content, appearance, weapon, false, UnitsToMeters, ambient);
            if (first.Error is not null || third.Error is not null) throw new NotSupportedException(first.Error ?? third.Error);
            third.ConfigureBodyContacts(CollisionLayer);
            third.SetMeta("opennv_reference_form_key", records.RuntimeFormKey(0x14).ToString());
            first.SetViewPolicy(true, false);
            third.SetViewPolicy(_xr is null && _thirdPersonMode, true);
            if (_firstPersonView is null && _xr is null)
            {
                var settings = FalloutInstallationSettings.Read(content);
                var projection = FalloutCameraProjection.FromReferenceFov(settings.Number("Display", "fDefault1stPersonFOV"),
                    settings.Number("Display", "fNearDistance"));
                _firstPersonView = new SubViewport
                {
                    Name = "FirstPersonView",
                    OwnWorld3D = true,
                    TransparentBg = true,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                    Size = new(1280, 720)
                };
                AddChild(_firstPersonView);
                _firstPersonCamera = new Camera3D
                {
                    Name = "FirstPersonCamera",
                    Current = true,
                    Fov = projection.VerticalFovDegrees,
                    Near = projection.NearGameUnits * UnitsToMeters,
                    Far = 10,
                    KeepAspect = Camera3D.KeepAspectEnum.Height
                };
                _firstPersonCamera.CullMask = FalloutNpcAppearanceSelfView.CameraMask(_firstPersonCamera.CullMask, true);
                _firstPersonView.AddChild(_firstPersonCamera);
                var layer = new CanvasLayer { Name = "FirstPersonLayer", Layer = 1 }; AddChild(layer);
                _firstPersonPixels = new TextureRect
                {
                    Name = "FirstPersonPixels",
                    Texture = _firstPersonView.GetTexture(),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                layer.AddChild(_firstPersonPixels); _firstPersonPixels.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            }
            if (_firstPerson is not null) _firstPerson.Visible = false;
            if (_thirdPerson is not null)
            {
                _thirdPerson.Visible = false;
                foreach (var contact in _thirdPerson.BodyContacts) contact.CollisionLayer = 0;
            }
            _firstPerson?.QueueFree(); _thirdPerson?.QueueFree();
            _firstPerson = first; _thirdPerson = third;
            first = third = null;
            _firstPerson.SetDrawn(_weaponHandling!.Drawn); _thirdPerson?.SetDrawn(_weaponHandling.Drawn);
            if (_xr is null) _firstPersonView!.AddChild(_firstPerson);
            else
            {
                AddChild(_firstPerson);
                var trackedActor = _firstPerson;
                // BoneAttachment3D updates before this later subscription.
                // Input therefore samples the device that this pose renders.
                trackedActor.Skeleton.Node.SkeletonUpdated += () =>
                {
                    if (_firstPerson == trackedActor) _xr.PublishWristInput();
                };
            }
            if (_thirdPerson is not null) AddChild(_thirdPerson);
            _selfQueryBodies.Clear(); _selfQueryBodies.Add(GetRid());
            foreach (var contact in _thirdPerson!.BodyContacts) _selfQueryBodies.Add(contact.GetRid());
            if (_xr is not null)
            {
                _firstPerson.EnableTrackedArms();
                _thirdPerson.EnableTrackedArms(_firstPerson);
                _thirdPerson.ShowTrackedBody(_firstPerson);
                BindXrContacts();
            }
            PresentationChanged?.Invoke();
            Activity.SetWeaponDrawn(weapon is not null && _weaponHandling.Drawn);
        }
        finally { first?.Free(); third?.Free(); }
    }

    private Godot.Collections.Array<Rid> SelfQueryBodies
    {
        get
        {
            if (_selfQueryBodies.Count == 0) _selfQueryBodies.Add(GetRid());
            return _selfQueryBodies;
        }
    }
}
