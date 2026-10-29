using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private readonly FalloutSandboxIdleSelection _sandboxIdleSelection = new();
    private FalloutFormKey? _sandboxMarker;
    private FalloutIdleCollection? _sandboxMarkerSource;
    private Vector3? _sandboxMarkerFloor;
    private FalloutSandboxMarkerPhase _sandboxMarkerPhase;
    private int? _sandboxMarkerNativePhase;
    private PackageSelection? _sandboxMarkerPendingSelection;
    private Func<bool>? _sandboxMarkerReady;
    private Func<FalloutActorSandboxMarkerContinuation?>? _sandboxMarkerCapture;

    private void AdvanceSandboxMarker(FalloutPluginRecord package, FalloutSandboxPackage source,
        FalloutSandboxActivityProgress activity, double delta)
    {
        if (_requestedSelection is not null) return;
        var target = activity.Target ?? throw new InvalidDataException("Sandbox marker activity has no selected reference.");
        if (_sandboxMarker is null)
        {
            if (!_aiWorld!.ReserveIdleMarker(target, 0, Appearance.Reference!.Value, _aiWorld.IdleMarkerOccupied)) return;
            _sandboxMarker = target;
            _sandboxMarkerSource = FalloutIdleCollection.Read(_aiStack!.GetEffective(_aiWorld.Get(target).Base));
            _sandboxMarkerPhase = FalloutSandboxMarkerPhase.Approach;
            _sandboxMarkerNativePhase = null;
            _aiReferenceState!.ProcedureCaptureBlocker = FalloutActorSandboxMarkerContinuation.CaptureBlocker;
        }
        if (_sandboxMarker != target) throw new InvalidOperationException("Sandbox marker changed its accepted target.");
        if (_sandboxMarkerPhase is FalloutSandboxMarkerPhase.Dispatch or FalloutSandboxMarkerPhase.Playback)
            _sandboxMarkerNativePhase = ObserveSandboxMarkerNativePhase(_sandboxMarkerNativePhase, Combat!.PackageMoving || Combat.PackageRoutePending,
                _sandboxIdleSelection.SelectedIdle is not null);
        if (_sandboxMarkerPhase == FalloutSandboxMarkerPhase.Retiring)
        {
            if (!_idleRetirementPending && IdleRequestsReady) CompleteSandboxMarkerPackageExit();
            return;
        }
        var reference = _aiWorld!.PlacedSource(target).Reference;
        var local = _referenceTransform!(reference);
        var parent = GetParent<Node3D>();
        var authored = parent.ToGlobal(local.Origin);
        var difference = GlobalPosition - authored;
        var tolerance = 10 * Skeleton.UnitsToMetres;
        if (_sandboxMarkerPhase == FalloutSandboxMarkerPhase.Approach)
        {
            if (difference.X * difference.X + difference.Z * difference.Z > tolerance * tolerance ||
                Math.Abs(difference.Y) > 100 * Skeleton.UnitsToMetres)
            {
                var destination = _sandboxMarkerFloor ??= Combat!.ProjectPackageDestination(authored);
                Combat!.AdvancePackageMotion(package, destination, tolerance, source.Running, delta,
                    source.WeaponDrawn, requireArrivalHeight: false);
                return;
            }
            var heading = -(parent.GlobalBasis * local.Basis.Z).Normalized();
            var facing = -GlobalBasis.Z.Normalized();
            var yaw = Mathf.Wrap(Mathf.Atan2(facing.X, facing.Z) - Mathf.Atan2(heading.X, heading.Z), -Mathf.Pi, Mathf.Pi);
            if (Math.Abs(yaw) > .1f) { Combat!.FacePackageDirection(heading, delta); return; }
            Combat!.StopPackageMotion();
            Combat.AdvancePackageMotion(package, GlobalPosition, 0, false, 0, source.WeaponDrawn);
            PlayLocomotion(false);
            _sandboxMarkerPhase = FalloutSandboxMarkerPhase.Readiness;
        }
        ObserveSandboxFurniturePose(activity);
        if (_sandboxMarkerPhase == FalloutSandboxMarkerPhase.Playback)
        {
            if (!IdleRequestsReady) return;
            _sandboxMarkerPhase = FalloutSandboxMarkerPhase.Readiness;
        }
        if (_sandboxMarkerPhase == FalloutSandboxMarkerPhase.Readiness)
        {
            if (!IdleRequestsReady) return;
            // Readiness raises deferred process work. Retire the old request
            // before promoting the next activity's queued request.
            if (_currentIdleRequest is { Sequence: not null, SequenceState: not 3 }) RequestIdleRetirement();
            // Readiness admits native phase two. The next update observes any
            // process-selected IDLE before filtering this marker's candidates;
            // a process with no prior selection still reports public0.
            _sandboxMarkerNativePhase ??= 2;
            _sandboxMarkerPhase = FalloutSandboxMarkerPhase.Dispatch;
            return;
        }
        if (!IdleRequestsReady || _idleRetirementPending) return;
        var declaration = _sandboxMarkerSource!;
        var eligible = declaration.Idles.Where(idle => _idleReplays.CanSelect(idle) &&
            _idleConditions!.AllPass(idle, EvaluateAiCondition)).ToArray();
        if (_sandboxIdleSelection.TrySelect(declaration.IdleFlags, eligible, _aiRandom.NextUInt32,
            idle =>
            {
                _aiReferenceState!.SandboxIdleSelection = _sandboxIdleSelection.Capture();
                PlayIdle(_aiStack!, idle, "sandbox-marker");
            }))
        {
            _aiReferenceState!.SandboxIdleSelection = _sandboxIdleSelection.Capture();
            _sandboxMarkerPhase = FalloutSandboxMarkerPhase.Playback;
            GD.Print($"OPENNV_NATIVE_SANDBOX_MARKER_START reference={Appearance.Reference} marker={target} " +
                $"idle={_sandboxIdleSelection.SelectedIdle} owner=source-idlm-request parity=unmeasured");
        }
    }

    internal static int? ObserveSandboxMarkerNativePhase(int? phase, bool movementPending, bool hasSelectedIdle) => phase switch
    {
        // Source observation precedes this update's marker handler. The
        // process-selected IDLE can survive a marker change; the new handler's
        // selection still cannot promote its own eligibility evaluation.
        2 => !movementPending && hasSelectedIdle ? 3 : 2,
        3 => 3,
        // An earlier continuation may own a valid request without having
        // recorded this separate native phase. Keep it unknown until the next
        // real update observes the same source transition; never assume two.
        null => !movementPending && hasSelectedIdle ? 3 : null,
        _ => throw new InvalidDataException("Sandbox marker observation has no admitted native phase.")
    };

    private bool ReleaseSandboxMarkerForReselection()
    {
        if (_sandboxMarker is null) return true;
        if (!IdleRequestsReady) return false;
        if (_sandboxMarkerPhase == FalloutSandboxMarkerPhase.Retiring)
        {
            if (_idleRetirementPending) return false;
            if (_sandboxMarkerPendingSelection is not null) { CompleteSandboxMarkerPackageExit(); return false; }
            ClearSandboxMarker(); return true;
        }
        if (_currentIdleRequest is null) { ClearSandboxMarker(); return true; }
        if (!_idleRetirementPending)
        {
            RequestIdleRetirement();
            _sandboxMarkerPhase = FalloutSandboxMarkerPhase.Retiring;
            _sandboxMarkerNativePhase = null;
            return false;
        }
        return false;
    }

    private bool BeginSandboxMarkerPackageExit(PackageSelection selected)
    {
        if (_sandboxMarker is null) return false;
        _sandboxMarkerPendingSelection = selected;
        _sandboxMarkerPhase = FalloutSandboxMarkerPhase.Retiring;
        _sandboxMarkerNativePhase = null;
        RequestIdleRetirement();
        return true;
    }

    private void CompleteSandboxMarkerPackageExit()
    {
        var accepted = _sandboxMarkerPendingSelection;
        ClearSandboxMarker();
        if (accepted is null) return;
        _requestedSelection = accepted;
        _aiQuestRevision = -1;
    }

    private void ClearSandboxMarker(bool release = true, bool clearRetained = true)
    {
        if (release && _sandboxMarker is { } marker)
            _aiWorld?.ReleaseIdleMarker(marker, 0, Appearance.Reference!.Value);
        _sandboxMarker = null; _sandboxMarkerSource = null; _sandboxMarkerFloor = null;
        _sandboxMarkerNativePhase = null;
        _sandboxMarkerPendingSelection = null;
        if (clearRetained && _aiReferenceState is { } state)
        {
            state.SandboxMarker = null;
            if (state.ProcedureCaptureBlocker == FalloutActorSandboxMarkerContinuation.CaptureBlocker) state.ProcedureCaptureBlocker = null;
        }
    }

    private void InterruptSandboxMarker()
    {
        if (_sandboxMarker is null) return;
        _requestedSelection ??= _sandboxMarkerPendingSelection;
        RequestIdleRetirement();
        ClearSandboxMarker();
    }

    private bool CanCaptureSandboxMarker() => _sandboxMarker is not null && !_bindingInitialBase &&
        _aiError is null && _requestedSelection is null && CanCaptureIdleRequestContinuation() &&
        _packageEvents is { Active: not null, Done: false, Error: null } &&
        _aiReferenceState?.ProcedureCaptureBlocker == FalloutActorSandboxMarkerContinuation.CaptureBlocker;

    private FalloutActorSandboxMarkerContinuation? CaptureSandboxMarker()
    {
        if (_sandboxMarker is not { } marker) return null;
        if (!CanCaptureSandboxMarker()) throw new NotSupportedException("Sandbox marker still has an unowned request or source selection.");
        var lifecycle = _packageEvents!;
        return new(FalloutActorPackageAssignment.Capture(_aiStack!, lifecycle)!, lifecycle.Revision,
            lifecycle.LastEvent, lifecycle.LastPackage, lifecycle.LastPackage is { } last
                ? FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(last)) : null,
            _aiRandom.State, Math.Max(0, _aiPollRemaining), _aiScheduleTime, _selectedSourcePackage,
            Activity.Capture(), _blink?.Capture(), marker,
            _sandboxMarkerSource!.Form, FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(_sandboxMarkerSource.Form)),
            _sandboxMarkerPhase, _sandboxMarkerFloor is { } floor ? [floor.X, floor.Y, floor.Z] : null,
            _sandboxMarkerPendingSelection is not null, _sandboxMarkerPendingSelection?.Record?.FormKey,
            _sandboxMarkerPendingSelection?.Forced == true, _sandboxMarkerNativePhase);
    }

    private void BindSandboxMarkerCapture()
    {
        if (_aiReferenceState is not { } state) return;
        state.CanCaptureSandboxMarker = _sandboxMarkerReady = CanCaptureSandboxMarker;
        state.CaptureSandboxMarker = _sandboxMarkerCapture = CaptureSandboxMarker;
        if (state.SandboxIdleSelection is { } selection) _sandboxIdleSelection.Restore(selection);
    }

    private void RestoreSandboxMarker(FalloutActorSandboxMarkerContinuation saved)
    {
        saved.Validate(_aiStack!, _aiWorld!, _aiReferenceState!);
        if (!_aiWorld!.OwnsIdleMarker(saved.Marker, 0, Appearance.Reference!.Value))
            throw new InvalidDataException("Saved Sandbox marker has no matching reservation.");
        var package = _aiStack!.GetEffective(saved.Package);
        _packageIdleSource = FalloutScriptPackage.Read(package);
        _sandboxPackage = FalloutSandboxPackage.Read(package); _sandboxWander = null; _aiPackage = package; _packageIdles = null;
        if (_packageEvents!.Active is null) saved.Assignment.Bind(_aiStack, _packageEvents);
        else if (_packageEvents.Active.Form != saved.Package || _packageEvents.Done)
            throw new InvalidDataException("Restored marker disagrees with its retained package lifecycle.");
        _packageEvents.RestoreHistory(saved.EventRevision, saved.LastEvent, saved.LastPackage);
        _selectedSourcePackage = saved.SelectedPackage; _sourceSelectionKnown = true;
        _aiRandom.Restore(saved.RandomState); _aiPollRemaining = saved.PollRemaining; _aiScheduleTime = saved.ScheduleTime;
        Activity.Restore(saved.Activity);
        if (saved.Blink is { } blink) (_blink ?? throw new InvalidDataException("Saved marker has no source blink owner.")).Restore(blink);
        _aiQuestRevision = _questState!.ProgressRevision; _aiActivityRevision = Activity.Revision;
        _sandboxMarker = saved.Marker; _sandboxMarkerSource = FalloutIdleCollection.Read(_aiStack!.GetEffective(saved.Source));
        _sandboxMarkerPhase = saved.Phase;
        _sandboxMarkerNativePhase = saved.NativePhase;
        _sandboxMarkerFloor = saved.Floor is { } floor ? new Vector3(floor[0], floor[1], floor[2]) : null;
        var pending = saved.PendingPackage is { } key ? _aiStack.GetEffective(key) : null;
        _sandboxMarkerPendingSelection = saved.PendingSelectionAccepted
            ? new(pending, pending is null ? null : FalloutScriptPackage.Read(pending), saved.PendingSelectionForced) : null;
        _aiReferenceState!.ProcedureCaptureBlocker = FalloutActorSandboxMarkerContinuation.CaptureBlocker;
    }

    private bool RetainSandboxMarkerContinuation()
    {
        if (_aiReferenceState is not { } state || _sandboxMarker is null ||
            !ReferenceEquals(state.CaptureSandboxMarker, _sandboxMarkerCapture)) return false;
        var saved = CaptureSandboxMarker() ?? throw new InvalidDataException("Marker lost its continuation during retirement.");
        state.SandboxMarker = saved.Copy(); state.PackageAssignment = saved.Assignment;
        return true;
    }
}
