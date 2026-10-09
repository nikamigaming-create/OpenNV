using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private const string MarkerTravelCaptureBlocker = "Marker Travel has started but its native capsule route snapshot is not initialized.";
    private CellNavigationGraph? _navigation;
    private GamebryoRootMotionTravel? _travelProgress;
    private FalloutTravelPackage? _nativeMarkerTravel;
    private FalloutTravelProgress? _nativeMarkerTravelProgress;
    private Vector3? _nativeMarkerTravelDestination;
    private bool _nativeMarkerTravelRestored;
    private bool _travelActive => _travelProgress?.Active == true || _nativeMarkerTravelProgress is { Complete: false };
    private Transform3D _travelDestination;
    private double _baseElapsedSeconds;
    private float _travelPublishedDistance;
    private double _travelPublishedSeconds;
    private float _travelTurnSpeed;
    private float _travelCycleDistance;
    private float _travelRootStart;
    private FalloutFormKey? _travelPackage;
    private FalloutFormKey? _travelTarget;
    private string? _travelPurpose;
    private int _travelLocationRadiusGameUnits;
    private Vector3 _travelLocationPosition;

    private object TravelState => new
    {
        active = _travelActive,
        package = _travelPackage?.ToString(),
        reference = _travelTarget?.ToString(),
        purpose = _travelPurpose,
        waypoints = _travelProgress?.Waypoints ?? 0,
        cursor = _travelProgress?.Cursor ?? 0,
        arrivalPending = _travelProgress?.ArrivalPending == true,
        target = new[] { _travelDestination.Origin.X, _travelDestination.Origin.Y, _travelDestination.Origin.Z },
        location = new[] { _travelLocationPosition.X, _travelLocationPosition.Y, _travelLocationPosition.Z },
        locationRadiusGameUnits = _travelLocationRadiusGameUnits,
        markerTravel = _nativeMarkerTravel is null ? null : new
        {
            package = _nativeMarkerTravel.Form.ToString(),
            progress = _nativeMarkerTravelProgress,
            projectedEndpoint = _nativeMarkerTravelDestination is { } endpoint ? new[] { endpoint.X, endpoint.Y, endpoint.Z } : null,
            restored = _nativeMarkerTravelRestored,
            owner = "source-navm-native-capsule-and-package-kf",
        },
        rootCycleDistance = _travelCycleDistance,
        source = "winning-navm-and-kf-accumulation",
        unbound = new[] { "dynamic-obstacle-avoidance", "turn-blending", "retail-path-costs" },
    };

    private void StartTravel(FalloutPluginRecord package, FalloutPlacedReference target, Transform3D? furnitureApproach = null,
        int destinationRadiusGameUnits = 0)
    {
        if (furnitureApproach is null) RequireTravelMarker(package, target);
        var destination = furnitureApproach ?? _referenceTransform!(target);
        StartTravelTo(package, target.FormKey, destination,
            furnitureApproach is null ? "reference-marker" : "furniture-approach", furnitureApproach is not null,
            destinationRadiusGameUnits);
    }

    private void BeginMarkerTravel(FalloutPluginRecord package, bool initializing)
    {
        var world = _aiWorld ?? throw new NotSupportedException("Marker Travel has no shared reference owner.");
        var source = FalloutTravelPackage.Read(package, ownsIdleCollection: true);
        if (source.LocationType != 0)
            throw new NotSupportedException("Legacy marker Travel requires its reference-location owner.");
        var actor = Appearance.Reference!.Value;
        var state = world.Get(actor);
        var retainedMotion = state.PackageMotion is { } motion && motion.Package == package.FormKey ? motion : null;
        var restored = initializing ? retainedMotion?.Travel : null;
        var retainedAssignment = state.PackageAssignment is { } assignment && assignment.Package == package.FormKey
            ? assignment : null;
        if (initializing && retainedAssignment is { Done: false } && restored is null)
            throw new NotSupportedException("Saved active marker Travel lacks its native route cursor and source animation continuation.");
        if (restored is not null && retainedAssignment is not null && restored.Complete != retainedAssignment.Done)
            throw new InvalidDataException("Saved marker Travel arrival differs from its package event lifecycle.");

        var progress = restored ?? source.Start(_aiStack!, world, actor);
        var completedFromLifecycle = initializing && restored is null && retainedAssignment is { Done: true } &&
            _packageEvents is { Done: true, Active: { Form: var activeForm } } && activeForm == package.FormKey;
        if (completedFromLifecycle)
        {
            if (retainedMotion is null)
            {
                if (source.Radius != 0)
                    throw new NotSupportedException("Saved completed marker Travel region lacks its actual retained endpoint.");
                var marker = _aiCell!.References.SingleOrDefault(value => value.FormKey == source.Reference) ??
                    throw new NotSupportedException("Saved completed marker Travel requires its resident source marker.");
                // Older completed exact-marker procedures retained their
                // lifecycle but no native motion snapshot. Preserve that
                // source-derived cold placement contract without replaying
                // walking, package start, or arrival effects.
                Transform = _referenceTransform!(marker);
            }
            progress = progress with { Complete = true };
        }
        if (restored is not null) source.Validate(_aiStack!, world, actor, restored);
        if (progress.Cell != _aiCell!.Cell.FormKey)
            throw new NotSupportedException("Marker Travel destination requires its other-cell route owner.");
        _navigation = CellNavigationGraph.LoadOwned(_aiStack!, progress.Cell);
        if (restored is not null && (restored.NavigationSha256 is not { } navigationSha256 ||
            !navigationSha256.Equals(_navigation.SourceSha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Saved marker Travel route differs from its winning NAVM source.");
        if (restored is null)
            progress = progress with { NavigationSha256 = _navigation.SourceSha256 };

        _nativeMarkerTravel = source;
        _nativeMarkerTravelProgress = progress;
        _nativeMarkerTravelDestination = null;
        _nativeMarkerTravelRestored = restored is not null;
        _aiPackage = package;
        _travelProgress?.Cancel();
        if (_packageEvents!.Active is null)
        {
            if (initializing && ((retainedAssignment is not null && retainedMotion is not null) || restored is not null))
                _packageEvents!.Restore(_packageIdleSource!, retainedAssignment?.Done ?? restored!.Complete);
            else
                _packageEvents.Change(_packageIdleSource);
        }
        else if (_packageEvents.Active.Form != package.FormKey)
            throw new InvalidOperationException("Marker Travel lifecycle differs from its selected package.");
        else if (initializing && restored is null && !_packageEvents.Done)
            throw new NotSupportedException("Saved active marker Travel lifecycle has no native route continuation.");

        state.ProcedureCaptureBlocker = restored is null && !completedFromLifecycle
            ? MarkerTravelCaptureBlocker : null;
        GD.Print($"OPENNV_NATIVE_MARKER_TRAVEL_READY reference={actor} package={package.FormKey} " +
            $"navigation={_navigation.SourceSha256} restored={restored is not null} complete={progress.Complete}");
    }

    private void AdvanceMarkerTravel(double delta)
    {
        if (_nativeMarkerTravel is not { } source || Combat is null || _aiError is not null || Combat.OwnsPose ||
            !Combat.PackageMovementReady || _conversationTarget is not null) return;
        try
        {
            var progress = _nativeMarkerTravelProgress!;
            var authored = GetParent<Node3D>().ToGlobal(new Vector3(progress.Location[0], progress.Location[2], -progress.Location[1]) * Skeleton.UnitsToMetres);
            var destination = _nativeMarkerTravelDestination ??= Combat.ProjectPackageDestination(authored);
            if (progress.RouteTarget is { } retainedTarget)
            {
                var retained = new Vector3(retainedTarget[0], retainedTarget[1], retainedTarget[2]);
                if (retained.DistanceTo(destination) > Math.Max(source.Radius * Skeleton.UnitsToMetres, .25f) + .1f)
                    throw new InvalidDataException("Saved marker Travel endpoint differs from its projected source destination.");
            }
            var radius = Math.Max(source.Radius * Skeleton.UnitsToMetres, .25f);
            var reached = IsOnFloor() && GlobalPosition.DistanceTo(destination) <= radius + .1f;
            var completedNow = reached && !progress.Complete;
            progress = progress with
            {
                NavigationSha256 = _navigation!.SourceSha256,
                RouteTarget = [destination.X, destination.Y, destination.Z],
            };
            Combat.AdvancePackageMotion(_aiPackage!, progress.Complete ? GlobalPosition : destination,
                progress.Complete ? 0 : radius, source.Running, delta, requireArrivalHeight: true);
            if (Combat.PackageRouteRestorable || Combat.PackageRouteFailure is not null || reached)
                Combat.SetTravelProgress(progress);
            if (completedNow)
            {
                progress = progress with { Complete = true, RouteFailure = null };
                _nativeMarkerTravelProgress = progress;
                Combat.SetTravelProgress(progress);
                _packageEvents!.Complete();
            }
            else _nativeMarkerTravelProgress = Combat.PackageMotion?.Travel ?? progress;

            var state = _aiWorld!.Get(Appearance.Reference!.Value);
            if (progress.Complete || Combat.PackageRouteRestorable || Combat.PackageRouteFailure is not null)
                state.ProcedureCaptureBlocker = null;
            else state.ProcedureCaptureBlocker = MarkerTravelCaptureBlocker;
            _nativeMarkerTravelRestored = true;
            if (progress.Complete)
            {
                // Keep the package KF clock and final physical pose in the same
                // shared snapshot after the source completion event is consumed.
                Combat.AdvancePackageMotion(_aiPackage!, GlobalPosition, 0, source.Running, 0, requireArrivalHeight: true);
                Combat.SetTravelProgress(progress);
            }
            if (completedNow) GD.Print($"OPENNV_NATIVE_MARKER_TRAVEL_ARRIVAL reference={Appearance.Reference} package={source.Form} " +
                $"position={GlobalPosition} routeCursor={progress.RouteCursor} arrivalReplayed=false");
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            Combat.StopPackageMotion(); _aiError = error.Message; _failedPackage = _aiPackage?.FormKey;
            BlockSelectionCapture($"Marker Travel continuation is unbound: {error.Message}");
            GD.PushError($"OPENNV_NATIVE_MARKER_TRAVEL_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }

    private void RequireTravelMarker(FalloutPluginRecord package, FalloutPlacedReference target)
    {
        if (!FalloutNewVegasBuiltinForms.IsInternalStatic(_aiCell!.BaseObjects[target.Base].Signature,
            _aiStack!.RuntimeFormId(target.Base)))
            throw new NotSupportedException($"PACK {package.FormKey} requires its non-marker interaction owner.");
    }

    private void StartTravelTo(FalloutPluginRecord package, FalloutFormKey target, Transform3D destination,
        string purpose, bool exact = false, int destinationRadiusGameUnits = 0)
    {
        if (destinationRadiusGameUnits < 0 || exact && destinationRadiusGameUnits != 0)
            throw new InvalidDataException("Exact actor interaction cannot use a travel location radius.");
        _navigation ??= CellNavigationGraph.LoadOwned(_aiStack!, _aiCell!.Cell.FormKey);
        var units = Skeleton.UnitsToMetres;
        var sourceStart = new Vector3(Position.X, -Position.Z, Position.Y) / units;
        var sourceDestination = new Vector3(destination.Origin.X, -destination.Origin.Z, destination.Origin.Y) / units;
        var path = _navigation.FindPath(sourceStart, sourceDestination, destinationRadiusGameUnits: destinationRadiusGameUnits)
            .Select(value => GamebryoCoordinate.ConvertVector(value) * units).ToArray();
        if (path.Length == 0) throw new InvalidDataException("Owned NAVM returned no travel corridor.");
        if (exact && path[^1] != destination.Origin) path = [.. path, destination.Origin];
        _travelDestination = exact ? destination : new(destination.Basis.Orthonormalized().Scaled(Scale), path[^1]);
        _travelPackage = package.FormKey;
        _travelTarget = target;
        _travelPurpose = purpose;
        _travelLocationRadiusGameUnits = destinationRadiusGameUnits;
        _travelLocationPosition = destination.Origin;
        _travelProgress?.Cancel();
        _travelProgress = new(path);
        _travelPublishedDistance = 0;
        _travelPublishedSeconds = 0;
        _travelTurnSpeed = Mathf.DegToRad(FalloutGameSettingFloats.ReadRetained(_aiStack!,
            "fCharacterDefaultTurningSpeed", nameof(RuntimeNativeNpc)));
        if (!float.IsFinite(_travelTurnSpeed) || _travelTurnSpeed <= 0)
            throw new InvalidDataException("Source travel turning speed is not positive and finite.");
        if (_bindingInitialBase && _packageEvents is { Done: true, Active: { } retained } && retained.Form == package.FormKey)
        {
            if (destinationRadiusGameUnits != 0 || purpose == "furniture-approach")
                throw new NotSupportedException("Saved completed travel region requires its selected controller endpoint owner.");
            // The saved source lifecycle already consumed arrival/results.
            // Its exact marker destination supplies the settled placement;
            // restarting walking would contradict the saved idle clock and
            // replay a completed procedure during cold assembly.
            _travelProgress = GamebryoRootMotionTravel.RestoreCompleted(path);
            Transform = _travelDestination;
            PlayLocomotion(false);
            GD.Print($"OPENNV_NATIVE_PACKAGE_TRAVEL_RESTORE reference={Appearance.Reference} package={package.FormKey} target={target} completed=true arrivalReplayed=false");
            return;
        }
        // This locomotion owner publishes the ordinary walking group.
        Activity.SetMovement(running: false, sneaking: false);
        PlayLocomotion(true);
        GD.Print($"OPENNV_NATIVE_PACKAGE_TRAVEL reference={Appearance.Reference} package={package.FormKey} target={target} " +
            $"navmeshes={_navigation.NavMeshes} waypoints={_travelProgress.Waypoints} locationRadius={destinationRadiusGameUnits} " +
            $"distancePerCycle={_travelCycleDistance:R} parity=unmeasured");
    }

    private void PlayLocomotion(bool moving, FalloutActorAnimationSnapshot? continuation = null)
    {
        if (continuation is not null && (!moving || !_travelActive))
            throw new InvalidOperationException("A retained walking clock requires its still-active travel controller.");
        var directory = Appearance.SkeletonPath[..Appearance.SkeletonPath.LastIndexOf('/')];
        // These are the engine's ordinary movement-group directories. The
        // source NPC sex selects the authored locomotion variant.
        var path = moving ? $"{directory}/locomotion/{(Appearance.Female ? "female" : "male")}/mtforward.kf"
            : $"{directory}/locomotion/mtidle.kf";
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned files are absent.");
        if (!content.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException("Owned locomotion is absent.", path);
        var nif = FalloutNifFile.Read(bytes);
        var sequence = nif.Roots.Select(nif.ReadObject).OfType<FalloutNifControllerSequence>().Single();
        if (sequence.CycleType != 0 || sequence.Frequency <= 0 || sequence.StopTime <= sequence.StartTime)
            throw new NotSupportedException("Locomotion requires a positive source loop.");
        Action<FalloutNifAnimationSample>? root = null;
        if (moving)
        {
            var channel = sequence.ControlledBlocks.Single(link => link.NodeName == sequence.TargetName && link.ControllerType == "NiTransformController");
            var sampler = new FalloutNifAnimationSampler(nif, channel.Interpolator);
            var start = sampler.Sample(sequence.StartTime);
            var end = sampler.Sample(sequence.StopTime);
            RequireTranslationOnlyRoot(start); RequireTranslationOnlyRoot(end);
            _travelRootStart = start.Translation!.Value.Y;
            _travelCycleDistance = end.Translation!.Value.Y - _travelRootStart;
            if (_travelCycleDistance <= 0 || start.Translation.Value.X != end.Translation.Value.X || start.Translation.Value.Z != end.Translation.Value.Z)
                throw new NotSupportedException("Locomotion accumulation requires a forward source displacement.");
            root = sample =>
            {
                RequireTranslationOnlyRoot(sample);
                var cycles = Math.Floor(_baseElapsedSeconds * sequence.Frequency / (sequence.StopTime - sequence.StartTime));
                var distance = (float)(cycles * _travelCycleDistance) + sample.Translation!.Value.Y - _travelRootStart;
                AdvanceTravel((distance - _travelPublishedDistance) * Skeleton.UnitsToMetres,
                    _baseElapsedSeconds - _travelPublishedSeconds);
                _travelPublishedDistance = distance;
                _travelPublishedSeconds = _baseElapsedSeconds;
            };
        }
        _baseAnimation = new(nif, sequence, Skeleton, accumulationRoot: root);
        Skeleton.Node.SetBonePose(Skeleton.BoneIndex(sequence.TargetName), Transform3D.Identity);
        _baseAnimationSeconds = sequence.StartTime;
        _baseElapsedSeconds = 0;
        if (moving && continuation is null) { _travelPublishedDistance = 0; _travelPublishedSeconds = 0; }
        BindBaseClock(nif, path, ambient: !moving, continuation);
        _baseLocomotionMoving = moving;
        if (_animation is null) _baseAnimation.ApplySourceTime(_baseAnimationSeconds);
        else RuntimeNativeNifAnimation.ApplyLayers((_baseAnimation, _baseAnimationSeconds), (_animation, _animationSeconds));
        SetMeta("opennv_base_animation_source", identity);
    }

    private void AdvanceTravel(float distance, double delta)
    {
        if (!_travelActive || _conversationTarget is not null) return;
        if (!double.IsFinite(delta) || delta < 0)
            throw new InvalidDataException("Source travel turning clock moved backwards.");
        var step = _travelProgress!.Advance(Position, distance);
        Position = step.Position;
        if (step.Direction is { } offset)
        {
            var horizontal = new Vector3(offset.X, 0, offset.Z);
            if (horizontal.LengthSquared() > 0)
            {
                var current = Basis.Orthonormalized().GetRotationQuaternion();
                var destination = Basis.LookingAt(horizontal.Normalized(), Vector3.Up).GetRotationQuaternion();
                var angle = current.AngleTo(destination);
                Basis = new Basis(current.Slerp(destination, angle <= .00001f ? 1 :
                    Math.Min(1, _travelTurnSpeed * (float)delta / angle))).Scaled(Scale);
            }
        }
        if (!_travelActive) Transform = _travelDestination;
    }

    private void CompletePendingTravel()
    {
        if (_travelProgress?.TakeArrival() == true) CompleteTravel();
    }

    private Vector3 SourceTranslation(FalloutNifAnimationSample sample)
    {
        if (sample.Translation is not { } value) throw new NotSupportedException("Source accumulation has no translation.");
        return GamebryoCoordinate.ConvertVector(new(value.X, value.Y, value.Z)) * Skeleton.UnitsToMetres;
    }

    private static void RequireTranslationOnlyRoot(FalloutNifAnimationSample sample)
    {
        if (sample.Translation is null || sample.Scale is { } scale && scale != 1 ||
            sample.Rotation is { } rotation && (rotation.W != 1 || rotation.X != 0 || rotation.Y != 0 || rotation.Z != 0))
            throw new NotSupportedException("Source accumulation requires rotation/scale extraction.");
    }
}
