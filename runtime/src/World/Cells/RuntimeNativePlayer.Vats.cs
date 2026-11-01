using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutPlayerVitals? _vatsVitals;
    private FalloutVatsSession? _vats;
    private readonly FalloutVatsPublications _vatsPublications = new();
    private NativeVatsMenu? _vatsMenu;
    private CanvasLayer? _vatsLayer;
    private Action? _releaseVatsInput;
    private Func<FalloutFormKey, FalloutCondition, float>? _vatsTargetCondition;
    private bool _vatsWasPaused, _vatsRequestedAttack, _retiringVats;
    private Input.MouseModeEnum _vatsPreviousMouse;
    private string? _vatsError, _vatsLastReason;
    private FalloutActorProjectileLaunch? _playerProjectileLaunch;
    private NativeVatsVisibilitySamples? _vatsVisibility;
    private IReadOnlyList<FalloutVatsSession.Target>? _vatsTargets;
    private long _vatsGeneration;
    private long _vatsPreparationStarted;
    private double _vatsPreparationElapsed;
    private int? _vatsPreparationCandidates;
    private int _vatsPreparationMeasured, _vatsPreparationSkipped;
    private object? _vatsPreparationLastSamples;
    private bool _vatsPreparing, _vatsOwnsPause, _vatsGameplayFault;
    private bool _vatsPreviousAiming;
    internal bool VatsExecuting => _vats?.CurrentPhase == FalloutVatsSession.Phase.Executing;
    internal bool VatsMenuActive => _vatsMenu is not null;
    internal string? VatsSaveBlocker => _vatsGameplayFault ? "vats-fault" :
        _vatsPublications.SaveBlocker ?? (PendingProjectileImpactCount != 0 ? "projectile-impact-continuation" :
        VatsMenuActive || VatsExecuting ? "vats-active" : null);
    internal FalloutActorProjectileLaunch? CaptureProjectileLaunch() => _playerProjectileLaunch;
    internal object VatsState => new
    {
        active = VatsMenuActive,
        preparing = _vatsPreparing,
        preparation = new
        {
            candidateParts = _vatsPreparationCandidates,
            completedPartMeasurements = _vatsPreparationMeasured,
            skippedCandidateParts = _vatsPreparationSkipped,
            elapsedMilliseconds = _vatsPreparing && _vatsPreparationStarted != 0
                ? System.Diagnostics.Stopwatch.GetElapsedTime(_vatsPreparationStarted).TotalMilliseconds : _vatsPreparationElapsed,
            samples = _vatsVisibility?.State ?? _vatsPreparationLastSamples
        },
        phase = _vats?.CurrentPhase.ToString(),
        error = _vatsError ?? _vats?.Error,
        reason = _vatsLastReason,
        attacks = _vats?.Queue,
        completed = _vats?.CompletedAttacks,
        pendingPublications = _vatsPublications.Count,
        impacts = _vats?.Impacts,
        availableAP = _vatsVitals?.State.ExactActionPoints,
        baseAP = _vatsVitals?.BaseActionPoints,
        source = "source-control16;source-WEAP-GMST;BPTD-BPNT-current-pose;shared-weapon-ammo-projectile-events",
        unverified = "matched-fragment-query;native-package-autoHit;process-wobble-mutation-producer;cinematic-camera;multi-projectile-melee-grenade-special-targets;physical-XR"
    };

    internal void ConfigureVats(FalloutPlayerVitals vitals, Func<FalloutFormKey, FalloutCondition, float> targetCondition,
        FalloutActorProjectileLaunch? projectileLaunch)
    {
        if (_vatsVitals is not null) throw new InvalidOperationException("VATS already has an authoritative vitals owner.");
        _vatsVitals = vitals; _vatsTargetCondition = targetCondition;
        // A missing owner starts a new legitimate process; no prior history is reconstructed.
        _playerProjectileLaunch = projectileLaunch ?? FalloutActorProjectileLaunch.NewProcess();
        _playerProjectileLaunch?.Validate();
        var clock = new RuntimeNativeVatsClock(AdvanceVats, () => RetireVats("player-retired"))
        { Name = "VatsGameplayClock", ProcessMode = ProcessModeEnum.Always, ProcessPriority = 10 };
        AddChild(clock);
    }

    private bool VatsInput(InputEvent input)
    {
        if (VatsExecuting)
        {
            if (input.IsActionPressed(_configuration.Player.DesktopInput.Cancel.Action, allowEcho: false)) RetireVats("player-cancelled");
            return true;
        }
        if (_inputControls is null || !input.IsActionPressed(InputSystem.RuntimeNativeInputControls.Action(16), allowEcho: false)) return false;
        if (!_modalInput) OpenVats();
        return true;
    }

    internal void XrVats()
    {
        if (VatsMenuActive || VatsExecuting) RetireVats("player-cancelled");
        else if (!_modalInput) OpenVats();
    }

    private void OpenVats()
    {
        if (_vatsVitals is null || _presentationRecords is null || _presentationInventory is null || _hitReferences is null)
            throw new InvalidOperationException("VATS has no shared player/world source owner.");
        if (_vatsMenu is not null || _modalInput || !SourceControls.Fighting || !_movementEnabled || _sourceCamera is not null ||
            _furniturePhase != 0 || IsDefeated?.Invoke() == true || GetTree().Paused) return;
        if (_weaponAction is not null || _pendingShotCount != 0) { _vatsLastReason = "weapon-action-in-progress"; return; }
        CanvasLayer? layer = null;
        NativeVatsMenu? menu = null;
        try
        {
            _ = RequireVatsWeapon();
            if (_vatsGameplayFault) throw new InvalidOperationException("VATS retained a gameplay publication failure.");
            if (_playerProjectileLaunch is null) throw new NotSupportedException("Player projectile process history is unknown.");
            var session = new FalloutVatsSession(_vatsVitals);
            _vats = session; _vatsError = null; _vatsLastReason = null;
            DiscoverVatsActors();
            _vatsTargets = null; _vatsPreparing = true;
            _vatsPreparationStarted = System.Diagnostics.Stopwatch.GetTimestamp(); _vatsPreparationElapsed = 0;
            _vatsPreparationCandidates = null; _vatsPreparationMeasured = 0; _vatsPreparationSkipped = 0;
            _vatsPreparationLastSamples = null;
            menu = new(session, VatsTargets, () => _vatsPreparing, QueueVatsTarget, BeginVatsExecution,
                () => { if (ReferenceEquals(_vats, session)) RetireVats("player-cancelled"); }, ProjectVatsTarget, _presentationRecords,
                () => _vatsVitals!.State, () => AmmunitionHud, () => SourceControls, error =>
                {
                    if (!ReferenceEquals(_vats, session)) return;
                    _vatsError = error.Message; RetireVats("presentation-failed");
                    GD.PushError("OPENNV_NATIVE_VATS_PRESENTATION_DIVERGENCE: " + error.Message);
                });
            layer = new CanvasLayer { Name = "NativeVatsLayer", Layer = 99, ProcessMode = ProcessModeEnum.Always };
            _vatsMenu = menu; _vatsLayer = layer;
            _vatsWasPaused = GetTree().Paused; _vatsPreviousMouse = Input.MouseMode;
            _vatsPreviousAiming = _aiming;
            _releaseVatsInput = AcquireModalInput();
            GetTree().Paused = true;
            _vatsOwnsPause = true;
            if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Visible;
            AddChild(layer); layer.AddChild(menu);
            _ = PrepareVatsTargets(_vats, checked(++_vatsGeneration));
            GD.Print("OPENNV_NATIVE_VATS_OPEN source=control16 owner=shared-queue;source-anatomy;weapon-instance");
        }
        catch (Exception error)
        {
            _vatsError = error.Message;
            RetireVats("admission-failed");
            if (menu is not null && IsInstanceValid(menu) && menu.GetParent() is null) menu.Free();
            if (layer is not null && IsInstanceValid(layer) && !layer.IsInsideTree()) layer.Free();
            GD.PushError("OPENNV_NATIVE_VATS_DIVERGENCE: " + error.Message);
        }
    }

    private FalloutWeaponPresentation RequireVatsWeapon()
    {
        var weapon = _firstPerson?.Weapon ?? throw new NotSupportedException("Unarmed VATS requires its native unarmed attack owner.");
        if (weapon.WeaponAnimationType is < 3 or > 9 || weapon.IsMine || weapon.IsThrownWeapon)
            throw new NotSupportedException("This VATS weapon requires its separate melee, grenade or special-attack owner.");
        if (_weaponHandling is null || !_weaponHandling.Drawn) throw new InvalidOperationException("Draw your equipped weapon before entering VATS.");
        if (_presentationInventory!.SelectedWeapon(weapon.Form).Extra.Instance is not { } instance || instance == Guid.Empty)
            throw new InvalidOperationException("VATS equipped weapon has no selected physical instance.");
        return weapon;
    }

    private void QueueVatsTarget(FalloutVatsSession.Target target)
    {
        var weapon = RequireVatsWeapon();
        var current = VatsTargets().SingleOrDefault(value => value.Reference == target.Reference && value.Part == target.Part) ??
            throw new InvalidOperationException("That VATS target is no longer available.");
        var always = _presentationRecords!.IniSettings.Get("bVatsAlwaysHit:Combat");
        if (always is not (0 or 1)) throw new NotSupportedException("VATS always-hit setting is unbound.");
        _vats!.Add(weapon.Form, _presentationInventory!.SelectedWeapon(weapon.Form).Extra.Instance!.Value, current,
            _weaponHandling!.NextShotRandomUInt32, always == 1, FalloutVatsActions.OrdinaryRanged(weapon.WeaponAnimationType));
    }

    private void BeginVatsExecution()
    {
        if (_vats?.CurrentPhase != FalloutVatsSession.Phase.Selecting || _vatsPreparing) throw new InvalidOperationException("VATS targets are not ready.");
        var weapon = RequireVatsWeapon();
        var instance = _presentationInventory!.SelectedWeapon(weapon.Form).Extra.Instance;
        if (_vats.Queue.Any(attack => attack.Weapon != weapon.Form || attack.WeaponInstance != instance))
            throw new InvalidOperationException("The selected weapon instance changed after queueing.");
        _vats.Execute();
        var release = _releaseVatsInput; _releaseVatsInput = null;
        release?.Invoke(); GetTree().Paused = _vatsWasPaused; _vatsOwnsPause = false;
        if (DisplayServer.GetName() != "headless") Input.MouseMode = _vatsPreviousMouse;
        _weaponTriggerHeld = false; _automaticFireStopped = false; _vatsRequestedAttack = false; _aiming = true;
        GD.Print($"OPENNV_NATIVE_VATS_EXECUTE attacks={_vats.Queue.Count} reservedAP={_vats.ReservedActionPoints:R}");
    }

    private void AdvanceVats(double delta)
    {
        if (_vatsVitals is null) return;
        try
        {
            if (!GetTree().Paused && _playerProjectileLaunch is { } process && _presentationRecords is { } processRecords)
                _playerProjectileLaunch = process.Advance(delta, processRecords.IniSettings.Float("fAimChaseLookingMult:Combat"),
                    processRecords.IniSettings.Float("fAnimationMult:General"));
            if (VatsMenuActive && (IsDefeated?.Invoke() == true || _xr is { } tracked &&
                (!tracked.Camera.Current || !tracked.RightGrip.GetHasTrackingData() || !tracked.RightAim.GetHasTrackingData())))
            { RetireVats("player-or-tracking-lost"); return; }
            if (!VatsExecuting)
            {
                if (!GetTree().Paused && !VatsMenuActive && _vatsVitals.State.ExactHitPoints > 0)
                {
                    if (_vatsVitals.State.ExactActionPoints >= _vatsVitals.State.MaximumActionPoints) return;
                    var records = _presentationRecords ?? throw new InvalidOperationException("AP regeneration has no source records.");
                    var rate = FalloutGameSettingFloats.Read(records, "fActionPointsRestoreRate");
                    var restoredFraction = FalloutPlayerPerkNumericEffects.ApplyContext(39, rate * (float)delta,
                        _combatPerks?.Invoke() ?? [], VatsPerkCondition);
                    _vatsVitals.RestoreActionPoints(restoredFraction * _vatsVitals.BaseActionPoints);
                }
                return;
            }
            if (IsDefeated?.Invoke() == true || _modalInput || !SourceControls.Fighting || _furniturePhase != 0 || _sourceCamera is not null)
            { RetireVats("player-interrupted"); return; }
            if (_xr is { } xr && (!xr.Camera.Current || !xr.RightGrip.GetHasTrackingData() || !xr.RightAim.GetHasTrackingData()))
            { RetireVats("tracking-lost"); return; }
            if (_vatsRequestedAttack)
            {
                if (_weaponAction is not null || _pendingShotCount != 0 || PendingProjectileImpactCount != 0 ||
                    _vats!.PendingPublications != 0) return;
                _vats!.CompleteAttack(_vats.CurrentShotCommitted);
                _vatsRequestedAttack = false; _vatsMenu?.Refresh();
                if (_vats.CurrentPhase == FalloutVatsSession.Phase.Completed) { RetireVats("queue-complete"); return; }
            }
            if (_vats?.CurrentAttack is not { } attack) { RetireVats("queue-complete"); return; }
            var weapon = RequireVatsWeapon();
            if (weapon.Form != attack.Weapon || _presentationInventory!.SelectedWeapon(weapon.Form).Extra.Instance != attack.WeaponInstance)
            { RetireVats("weapon-instance-changed"); return; }
            if (!TryVatsPoint(attack.Target.Reference, attack.Target.Part, out var targetPoint, out var visible) || !visible)
            {
                _vatsLastReason = "target-unavailable"; _vats.CompleteAttack(false); _vatsMenu?.Refresh(); return;
            }
            if (!_weaponHandling!.CanFire(weapon) || _vatsVitals.State.ExactActionPoints < attack.Target.ActionPointCost)
            { RetireVats("ammo-or-AP-unavailable"); return; }
            if (_xr is null)
            {
                var direction = (targetPoint - _camera.GlobalPosition).Normalized();
                GlobalRotation = new(GlobalRotation.X, MathF.Atan2(-direction.X, -direction.Z), GlobalRotation.Z);
                _pitchRadians = Math.Clamp(MathF.Asin(direction.Y), -_configuration.Player.VerticalLookLimitRadians, _configuration.Player.VerticalLookLimitRadians);
                _camera.Rotation = new(_pitchRadians, 0, 0);
            }
            _vatsRequestedAttack = true;
            RequestWeaponFire();
            if (_weaponAction is null) { RetireVats("weapon-action-refused"); return; }
            _vatsMenu?.Refresh();
        }
        catch (Exception error)
        {
            _vatsError = error.Message; _vats?.Fail(error);
            _vatsGameplayFault = true;
            RetireVats("execution-failed"); GD.PushError("OPENNV_NATIVE_VATS_DIVERGENCE: " + error.Message);
        }
    }

    private bool VatsBurstActive => VatsExecuting && _vatsRequestedAttack;
    private FalloutVatsValueContext? CurrentVatsValueContext()
    {
        if (_vats?.CurrentPhase == FalloutVatsSession.Phase.Faulted)
            throw new InvalidOperationException("VATS action has a retained publication fault: " + _vats.Error);
        if (_vats?.CurrentAttack is not { } attack) return null;
        return new(attack.Weapon, attack.Target.Reference, attack.Target.Base, attack.Target.PartActorValue,
            attack.Action, attack.Success);
    }
    private Vector3? VatsAimPoint()
    {
        if (!VatsBurstActive || _vats?.CurrentAttack is not { } attack) return null;
        if (_presentationInventory!.SelectedWeapon(attack.Weapon).Extra.Instance != attack.WeaponInstance)
            throw new InvalidOperationException("Queued VATS weapon instance was replaced before discharge.");
        if (!TryVatsPoint(attack.Target.Reference, attack.Target.Part, out var point, out _))
            throw new InvalidOperationException("Queued VATS target disappeared before discharge.");
        if (!_vats.CurrentShotCommitted && _vatsVitals!.State.ExactActionPoints < attack.Target.ActionPointCost)
            throw new InvalidOperationException("Queued VATS AP became unavailable before discharge.");
        if (attack.Success) return point;
        var bound = VatsMissBound(attack.Target.Reference, attack.Target.Part);
        return bound is { Radius: > 0 } source ? new Vector3(source.Center.X, source.Center.Y, source.Center.Z) : point;
    }

    private Vector3 VatsMissDirection(Vector3 from, Vector3 corrected)
    {
        if (!VatsBurstActive || _vats?.CurrentAttack is not { Success: false } attack) return corrected;
        var bound = VatsMissBound(attack.Target.Reference, attack.Target.Part);
        var point = VatsAimPoint()!.Value;
        var offsets = FalloutVatsMiss.Angles(_presentationRecords!, (bound?.Radius ?? 0) / UnitsToMeters,
            from.DistanceTo(point) / UnitsToMeters, _weaponHandling!.NextShotRandomUInt32);
        var pitch = -MathF.Asin(Math.Clamp(corrected.Y, -1, 1)) + offsets.Pitch;
        var yaw = MathF.Atan2(corrected.X, -corrected.Z) + offsets.Yaw;
        var horizontal = MathF.Cos(pitch);
        return new(MathF.Sin(yaw) * horizontal, -MathF.Sin(pitch), -MathF.Cos(yaw) * horizontal);
    }
    private void PublishVatsShot()
    {
        if (!VatsBurstActive || _vats?.CurrentAttack is not { } attack || _vats.CurrentShotCommitted) return;
        _vats.CommitShot(attack.Ordinal);
    }

    private void FailVatsFiring(Exception error)
    {
        if (!VatsBurstActive) return;
        _vatsError = error.Message; _vats!.Fail(error); RetireVats("firing-failed");
        _vatsGameplayFault = true;
    }

    private void RetireVats(string reason)
    {
        if (_retiringVats) return;
        _retiringVats = true;
        try
        {
            _vatsLastReason = reason;
            ++_vatsGeneration;
            _vatsVisibility?.Dispose(); _vatsVisibility = null; _vatsPreparing = false;
            if (_vats?.CurrentPhase is FalloutVatsSession.Phase.Selecting or FalloutVatsSession.Phase.Executing) _vats.Cancel();
            _vatsRequestedAttack = false; _weaponTriggerHeld = false; _pendingShotCount = 0;
            if (_weaponAction is not null && _vatsLayer is not null) CancelWeaponAction();
            var layer = _vatsLayer; _vatsLayer = null; _vatsMenu = null;
            if (layer is not null) _aiming = _vatsPreviousAiming;
            var release = _releaseVatsInput; _releaseVatsInput = null;
            try { release?.Invoke(); }
            finally
            {
                if (_vatsOwnsPause && IsInsideTree())
                {
                    GetTree().Paused = _vatsWasPaused;
                    if (DisplayServer.GetName() != "headless") Input.MouseMode = _vatsPreviousMouse;
                }
                _vatsOwnsPause = false;
                if (layer is not null && IsInstanceValid(layer) && !layer.IsQueuedForDeletion()) layer.QueueFree();
                _vatsActors.Clear();
            }
        }
        finally { _retiringVats = false; }
    }

    private float VatsPerkCondition(byte group, FalloutCondition condition)
    {
        if (group == 0) return (_combatCondition ?? throw new InvalidOperationException("VATS player conditions are absent."))(condition);
        var targetContext = _vatsConditionTarget ?? _hitReferences?.VatsConditions.CurrentContext?.Target;
        if (group == 2 && targetContext is { } target)
            return (_vatsTargetCondition ?? throw new InvalidOperationException("VATS target conditions are absent."))(target, condition);
        if (group != 1 || condition.RunOn != 0) throw new NotSupportedException("VATS perk condition context is unbound.");
        if (condition.Function is 224 or 235 or 408)
            return (_combatCondition ?? throw new InvalidOperationException("VATS shared conditions are absent."))(condition);
        var weapon = _hitReferences?.VatsConditions.CurrentContext?.Weapon ?? _firstPerson?.Weapon?.Form;
        if (FalloutPlatformConditions.Evaluate(condition) is { } platform) return platform;
        return condition.Function switch
        {
            72 => weapon == condition.FormArgument1 ? 1 : 0,
            108 when weapon is { } form => FalloutWeaponPresentation.ReadScriptAnimationType(_presentationRecords!.GetEffective(form)),
            _ => throw new NotSupportedException($"VATS weapon perk condition {condition.Function} is unbound."),
        };
    }
}

internal sealed partial class RuntimeNativeVatsClock(Action<double> advance, Action retire) : Node
{
    public override void _Process(double delta) => advance(delta);
    public override void _ExitTree() => retire();
}
