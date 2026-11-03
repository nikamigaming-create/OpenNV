using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private sealed record VatsView(FalloutFormKey Reference, Vector3 Focus,
        FalloutVatsSelectionCamera Settings, FalloutVatsCameraTransition Yaw, FalloutVatsCameraTransition Pitch,
        FalloutVatsCameraTransition WorldFov, FalloutVatsCameraTransition FirstFov)
    {
        internal bool Complete => Yaw.Complete && Pitch.Complete && WorldFov.Complete && FirstFov.Complete;
    }

    private IReadOnlyList<FalloutFormKey> _vatsActorChoices = [];
    private FalloutFormKey? _vatsSelectedActor;
    private VatsView? _vatsView;
    private Transform3D? _vatsCompletedCameraTransform;
    private float? _vatsWorldFovBefore, _vatsFirstFovBefore;
    private bool _vatsMeasurementStarted;

    private void SelectVatsActor(FalloutFormKey reference)
    {
        if (_vats?.CurrentPhase != FalloutVatsSession.Phase.Selecting || !_vatsActorChoices.Contains(reference))
            throw new InvalidOperationException("VATS selected actor has no admitted source owner.");
        InvalidateVatsSelectedMeasurement();
        _vatsSelectedActor = reference;
        if (_xr is null) _vatsView = CreateVatsView(reference);
        // XR continues to use its actually tracked view. This flat source-camera
        // owner does not rotate the headset or certify stereo fragment queries.
        _vatsMenu?.Refresh();
    }

    private void InvalidateVatsSelectedMeasurement()
    {
        ++_vatsGeneration;
        _vatsCompletedCameraTransform = null;
        _vatsVisibility?.Dispose(); _vatsVisibility = null;
        _vatsTargets = null; _vatsTargetMeasurements.Clear();
        _vatsPreparing = true; _vatsMeasurementStarted = false;
        _vatsPreparationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _vatsPreparationCandidates = null; _vatsPreparationMeasured = 0; _vatsPreparationSkipped = 0;
        _vatsPreparationLastSamples = null;
    }

    private VatsView CreateVatsView(FalloutFormKey reference)
    {
        if (!_vatsActors.TryGetValue(reference, out var actor) || !ActorAvailable(actor))
            throw new InvalidOperationException("VATS selected-view actor retired.");
        var settings = FalloutVatsSelectionCamera.Read(_presentationRecords!);
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("VATS camera has no owned source.");
        var bound = actor.Node switch
        {
            RuntimeNativeNpc npc => npc.CurrentWorldBound(content),
            RuntimeNativeCreature creature => creature.CurrentWorldBound(content),
            _ => throw new InvalidOperationException("VATS camera lost its actual actor model.")
        };
        var points = new List<System.Numerics.Vector3>();
        System.Numerics.Vector3? head = null;
        foreach (var part in _hitReferences!.BodyParts(reference).Parts.OrderBy(value => value.Type))
        {
            if (part.Type >= 15 || string.IsNullOrEmpty(part.Target)) continue;
            if (!actor.Skeleton.TryBoneIndex(part.Target, out var bone)) continue; // Native optional BPNT lookup contributes only actual resolved nodes.
            var point = (actor.Skeleton.Node.GlobalTransform * actor.Skeleton.Node.GetBoneGlobalPose(bone)).Origin;
            var source = new System.Numerics.Vector3(point.X, point.Y, point.Z);
            points.Add(source); if (part.Type == 1) head = source;
        }
        if (_thirdPerson is null || !IsInstanceValid(_thirdPerson.Skeleton.Node))
            throw new NotSupportedException("VATS head-distance focus requires the player's actual reference 3D.");
        var player = _thirdPerson.Skeleton.Node.GlobalPosition;
        var focus = FalloutVatsSelectionCamera.Focus(new(player.X, player.Y, player.Z), head, points, UnitsToMeters);
        var target = new Vector3(focus.X, focus.Y, focus.Z);
        var center = new Vector3(bound.Center.X, bound.Center.Y, bound.Center.Z);
        var desired = settings.Fov(bound.Radius, _camera.GlobalPosition.DistanceTo(center), UnitsToMeters);
        return CreateVatsViewAngles(reference, target, settings, settings.ZoomSeconds,
            new(ReferenceFov(_camera), desired.World, settings.ZoomSeconds),
            new(_firstPersonCamera is null ? desired.FirstPerson : ReferenceFov(_firstPersonCamera), desired.FirstPerson, settings.ZoomSeconds));
    }

    private VatsView CreateVatsViewAngles(FalloutFormKey reference, Vector3 focus, FalloutVatsSelectionCamera settings,
        float duration, FalloutVatsCameraTransition worldFov, FalloutVatsCameraTransition firstFov)
    {
        // Flat third-person presentation orbits the player eye. Its pullback
        // changes when the player turns, so the old camera position cannot be
        // the angular pivot of the new view.
        var pivot = _thirdPersonMode
            ? _sourceAudioFramePublished ? _sourceFirstPersonAudioFrame.Origin :
                throw new InvalidOperationException("VATS has no published player-eye frame.")
            : _camera.GlobalPosition;
        var delta = focus - pivot;
        if (GetParent() is Node3D parent) delta = parent.GlobalBasis.Orthonormalized().Inverse() * delta;
        var angles = FalloutVatsSelectionCamera.Angles(new(delta.X, -delta.Z, delta.Y), UnitsToMeters);
        var yaw = -angles.Yaw;
        var pitch = -angles.Pitch;
        var currentYaw = Rotation.Y;
        var difference = yaw - currentYaw;
        while (difference > MathF.PI) difference -= 2 * MathF.PI;
        while (difference < -MathF.PI) difference += 2 * MathF.PI;
        return new(reference, focus, settings, new(currentYaw, currentYaw + difference, duration),
            new(_pitchRadians, pitch, duration), worldFov, firstFov);
    }

    private static float ReferenceFov(Camera3D camera) => new FalloutCameraProjection(camera.Fov, 1).ReferenceHorizontalFovDegrees;

    private void AdvanceVatsSelectionView(float delta)
    {
        var session = _vats;
        if (session?.CurrentPhase != FalloutVatsSession.Phase.Selecting || _vatsMenu is null || _vatsSelectedActor is null) return;
        if (!_vatsActors.TryGetValue(_vatsSelectedActor.Value, out var actor) || !ActorAvailable(actor))
        { RetireVats("selected-target-retired"); return; }
        if (_vatsView is { } view && (!view.Complete || !_vatsMeasurementStarted))
        {
            var yaw = view.Yaw.Advance(delta); _pitchRadians = view.Pitch.Advance(delta);
            Rotation = new(Rotation.X, yaw, Rotation.Z);
            PublishThirdPersonCamera();
            _camera.Fov = FalloutCameraProjection.FromReferenceFov(view.WorldFov.Advance(delta), _camera.Near / UnitsToMeters).VerticalFovDegrees;
            var firstFov = view.FirstFov.Advance(delta);
            if (_firstPersonCamera is { } first)
                first.Fov = FalloutCameraProjection.FromReferenceFov(firstFov, first.Near / UnitsToMeters).VerticalFovDegrees;
            if (!view.Complete) return;
            // Resolve the final camera orientation from its actual endpoint.
            // Orbit collision and configured eye offsets can move that endpoint
            // while yaw/pitch are being published. Sampling and menu projection
            // must use the same completed view centered on the source focus.
            var direction = view.Focus - _camera.GlobalPosition;
            if (!direction.IsFinite() || direction.IsZeroApprox())
                throw new InvalidDataException("VATS completed camera has no valid target direction.");
            var up = MathF.Abs(direction.Normalized().Dot(Vector3.Up)) < .999f ? Vector3.Up : Vector3.Right;
            _camera.LookAt(view.Focus, up);
            _vatsCompletedCameraTransform = _camera.GlobalTransform;
        }
        if (_vatsMeasurementStarted) return;
        _vatsMeasurementStarted = true;
        _ = PrepareVatsTargets(session, _vatsGeneration);
    }

    private void SelectVatsPartView(FalloutVatsSession.Target target)
    {
        if (_xr is not null || _vatsPreparing || _vatsView is not { Complete: true } view || view.Reference != target.Reference) return;
        if (!TryVatsAimPoint(target.Reference, target.Part, out var point)) return;
        var screen = _camera.UnprojectPosition(point);
        var height = _camera.GetViewport().GetVisibleRect().Size.Y;
        var y = screen.Y / height;
        var margin = 2 * view.Settings.LimbMargin;
        if (!float.IsFinite(margin) || !float.IsFinite(y)) throw new InvalidDataException("VATS limb camera projection is invalid.");
        if (y >= margin && y <= 1 - margin) return;
        var focus = new Vector3(view.Focus.X, point.Y, view.Focus.Z);
        InvalidateVatsSelectedMeasurement();
        _vatsView = CreateVatsViewAngles(target.Reference, focus, view.Settings, view.Settings.LimbSeconds,
            view.WorldFov, view.FirstFov);
        _vatsMenu?.Refresh();
    }

    private void RestoreVatsSelectionView()
    {
        _vatsView = null; _vatsCompletedCameraTransform = null; _vatsSelectedActor = null; _vatsMeasurementStarted = false;
        if (_vatsWorldFovBefore is { } world && IsInstanceValid(_camera)) _camera.Fov = world;
        if (_vatsFirstFovBefore is { } first && _firstPersonCamera is { } camera && IsInstanceValid(camera)) camera.Fov = first;
        _vatsWorldFovBefore = null; _vatsFirstFovBefore = null;
    }
}
