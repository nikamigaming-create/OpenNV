using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc : INativeActorPortal
{
    CharacterBody3D INativeActorPortal.PortalBody => this;
    RuntimeNativeActorCombat INativeActorPortal.PortalCombat => Combat!;
    bool INativeActorPortal.PortalTransferred => MarkerPortalTransferred || _followPortal?.Transferred == true;
    bool INativeActorPortal.WantsPortal(FalloutFormKey door) => _followPackage is not null ? _followPortal?.Wants(door) == true : WantsTravelPortal(door);
    void INativeActorPortal.CommitPortal(FalloutFormKey door)
    {
        if (_followPackage is not null) FollowPortal.Commit(door); else CommitTravelPortal(door);
    }
    void INativeActorPortal.FinishPortalAttempt(FalloutFormKey door, string? error)
    {
        if (_followPackage is not null) _followPortal?.Finish(door, error); else FinishTravelPortalAttempt(door, error);
    }
    void INativeActorPortal.RetirePortalBindings() => RetireSourceBindings();
    private bool _markerPortalRequested;
    private string? _markerPortalRetirementWait;
    private const string PendingMarkerPortalCaptureBlocker = "Travel portal activation is pending its source event result.";
    internal bool MarkerPortalTransferred { get; private set; }
    internal bool WantsTravelPortal(FalloutFormKey door) => !MarkerPortalTransferred && _aiError is null &&
        _nativeMarkerTravelProgress is { Complete: false, PortalDoor: { } target } && target == door &&
        _nativeMarkerTravel?.Form == CurrentPackage && Combat?.OwnsPose == false;

    internal float PortalActivationRange => (Combat ?? throw new NotSupportedException("NPC portal has no native interaction owner."))
        .PackageActivationRange;

    private float MarkerTravelRadius(FalloutTravelProgress progress) => progress.PortalDoor is not null
        ? PortalActivationRange * .75f : Math.Max(_nativeMarkerTravel!.Radius * Skeleton.UnitsToMetres, .25f);

    internal static bool PortalOverlayRetired(bool animated, FalloutIdleAnimationPlayback? playback,
        bool responseActive, int objects, string? error)
    {
        if (error is not null) throw new NotSupportedException("NPC portal cannot retire a failed source animation: " + error);
        if (animated)
        {
            if (playback is null || playback.Endless)
                throw new NotSupportedException("NPC portal cannot carry an endless or unowned independent IDLE.");
            // Even after the clock completes, its ordinary FinishIdle owner
            // must release the animation and ANIO before the body can retire.
            return false;
        }
        if (playback is not null || responseActive || objects != 0)
            throw new NotSupportedException("NPC portal retains an independent response or animated-object owner.");
        return true;
    }

    private bool PortalPlaybackReady()
    {
        _markerPortalRetirementWait = null;
        if (!PortalOverlayRetired(_animation is not null, _idlePlayback, _responseIdleActive,
            _animationObjects.Count, AnimationError ?? _packageIdleError))
        {
            _markerPortalRetirementWait = "finite-source-idle-awaiting-ordinary-retirement";
            return false;
        }
        if (NodeTraversal.SelfAndDescendants<NativeOwnedAnimationSoundPlayer>(this).All(sound => sound.CanRetireTransientEffect)) return true;
        _markerPortalRetirementWait = "source-audio-awaiting-retirement";
        return false;
    }

    internal void CommitTravelPortal(FalloutFormKey door)
    {
        if (!WantsTravelPortal(door) || !_markerPortalRequested || !IsOnFloor() || !PortalPlaybackReady())
            throw new NotSupportedException("NPC portal activation has no ready native Travel approach.");
        var world = _aiWorld ?? throw new NotSupportedException("NPC portal has no shared reference owner.");
        var records = _aiStack ?? throw new NotSupportedException("NPC portal has no winning package source owner.");
        var actor = Appearance.Reference!.Value;
        var state = world.Get(actor);
        if (state.ProcedureCaptureBlocker != PendingMarkerPortalCaptureBlocker)
            throw new NotSupportedException("NPC portal request no longer owns the pending procedure continuation.");
        var source = _nativeMarkerTravel ?? throw new InvalidDataException("Portal has no retained source destination owner.");
        var progress = _nativeMarkerTravelProgress!;
        if (!world.CanUseTravelPortal(actor, door) || world.TravelPortal(actor, source.Start(records, world, actor).Cell) != door)
            throw new NotSupportedException("NPC portal activation no longer matches its current winning Travel route.");
        var placement = world.TravelPortalArrival(door, out var destination);
        state.ProcedureCaptureBlocker = null;
        FalloutReferenceSnapshot captured;
        try { captured = state.Capture(); }
        finally { state.ProcedureCaptureBlocker = PendingMarkerPortalCaptureBlocker; }
        if (captured.PackageAssignment is not { Done: false } assignment || assignment.Package != source.Form ||
            captured.PackageMotion is not { } motion || motion.Package != source.Form || captured.Engagement is not null ||
            captured.HitReaction is not null || captured.FurnitureContinuation is not null || captured.DialogueContinuation is not null)
            throw new NotSupportedException("NPC portal cannot retire an unfinished independent actor continuation.");
        var next = progress with
        {
            NavigationSha256 = null,
            RouteTarget = null,
            RouteWaypoints = null,
            RouteCursor = 0,
            RouteFailure = null,
            RouteCell = null,
            PortalDoor = null,
            PortalArrival = new(door, destination)
        };
        next.Validate();
        var position = GamebryoCoordinate.ConvertVector(new(placement.Position[0], placement.Position[1], placement.Position[2])) * Skeleton.UnitsToMetres;
        var rotation = GamebryoCoordinate.ConvertReferenceEuler(new(placement.RotationRadians[0],
            placement.RotationRadians[1], placement.RotationRadians[2]), 1).GetRotationQuaternion();
        // Every refusal precedes the authoritative placement mutation. The
        // saved receipt is a completed portal transition, not final arrival.
        world.SetPlacement(actor, placement);
        state.PackageMotion = motion with
        {
            Position = [position.X, position.Y, position.Z],
            Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W],
            Travel = next
        };
        state.PackageAssignment = assignment;
        state.ProcedureCaptureBlocker = null;
        _nativeMarkerTravelProgress = next;
        MarkerPortalTransferred = true;
        Combat!.StopPackageMotion();
        GD.Print($"OPENNV_NATIVE_NPC_PORTAL_COMMIT actor={actor} package={source.Form} door={door} destination={destination} cell={placement.Cell} finalArrival=false");
    }

    internal void FinishTravelPortalAttempt(FalloutFormKey door, string? error)
    {
        if (MarkerPortalTransferred || !_markerPortalRequested || _nativeMarkerTravelProgress?.PortalDoor != door) return;
        _aiError = error ?? "Source door activation did not commit the NPC's pending Travel transition.";
        _failedPackage = _aiPackage?.FormKey;
        BlockSelectionCapture(_aiError);
        GD.PushError($"OPENNV_NATIVE_NPC_PORTAL_DIVERGENCE actor={Appearance.Reference} door={door}: {_aiError}");
    }
}
