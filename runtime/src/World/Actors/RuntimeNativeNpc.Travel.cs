using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private CellNavigationGraph? _navigation;
    private GamebryoRootMotionTravel? _travelProgress;
    private bool _travelActive => _travelProgress?.Active == true;
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
        rootCycleDistance = _travelCycleDistance,
        source = "winning-navm-and-kf-accumulation",
        unbound = new[] { "dynamic-obstacle-avoidance", "turn-blending", "retail-path-costs" },
    };

    private void StartTravel(FalloutPluginRecord package, FalloutPlacedReference target, Transform3D? furnitureApproach = null,
        int destinationRadiusGameUnits = 0)
    {
        if (furnitureApproach is null && !FalloutNewVegasBuiltinForms.IsInternalStatic(_aiCell!.BaseObjects[target.Base].Signature,
            _aiStack!.RuntimeFormId(target.Base)))
            throw new NotSupportedException($"PACK {package.FormKey} requires its non-marker interaction owner.");
        var destination = furnitureApproach ?? _referenceTransform!(target);
        StartTravelTo(package, target.FormKey, destination,
            furnitureApproach is null ? "reference-marker" : "furniture-approach", furnitureApproach is not null,
            destinationRadiusGameUnits);
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
