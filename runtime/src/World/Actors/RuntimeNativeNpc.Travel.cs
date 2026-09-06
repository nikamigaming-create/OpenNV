using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private CellNavigationGraph? _navigation;
    private Vector3[] _travelPath = [];
    private int _travelCursor;
    private bool _travelActive;
    private Transform3D _travelDestination;
    private double _baseElapsedSeconds;
    private float _travelPublishedDistance;
    private float _travelCycleDistance;
    private float _travelRootStart;
    private FalloutFormKey? _travelPackage;
    private FalloutFormKey? _travelTarget;
    private string? _travelPurpose;

    private object TravelState => new
    {
        active = _travelActive,
        package = _travelPackage?.ToString(),
        reference = _travelTarget?.ToString(),
        purpose = _travelPurpose,
        waypoints = _travelPath.Length,
        cursor = _travelCursor,
        target = new[] { _travelDestination.Origin.X, _travelDestination.Origin.Y, _travelDestination.Origin.Z },
        rootCycleDistance = _travelCycleDistance,
        source = "winning-navm-and-kf-accumulation",
        unbound = new[] { "dynamic-obstacle-avoidance", "turn-blending", "retail-path-costs" },
    };

    private void StartTravel(FalloutPluginRecord package, FalloutPlacedReference target, Transform3D? furnitureApproach = null)
    {
        if (furnitureApproach is null && !FalloutNewVegasBuiltinForms.IsInternalStatic(_aiCell!.BaseObjects[target.Base].Signature,
            _aiStack!.RuntimeFormId(target.Base)))
            throw new NotSupportedException($"PACK {package.FormKey} requires its non-marker interaction owner.");
        _navigation ??= CellNavigationGraph.LoadOwned(_aiStack!, _aiCell!.Cell.FormKey);
        var units = Skeleton.UnitsToMetres;
        var sourceStart = new Vector3(Position.X, -Position.Z, Position.Y) / units;
        var destination = furnitureApproach is { } approach
            ? new Vector3(approach.Origin.X, -approach.Origin.Z, approach.Origin.Y) / units
            : new Vector3(target.Position[0], target.Position[1], target.Position[2]);
        _travelPath = _navigation.FindPath(sourceStart, destination).Select(value => GamebryoCoordinate.ConvertVector(value) * units).ToArray();
        if (_travelPath.Length == 0) throw new InvalidDataException("Owned NAVM returned no travel corridor.");
        if (furnitureApproach is { } entry && _travelPath[^1] != entry.Origin)
            _travelPath = [.. _travelPath, entry.Origin];
        _travelDestination = furnitureApproach ?? new(_referenceTransform!(target).Basis.Orthonormalized().Scaled(Scale), _travelPath[^1]);
        _travelPackage = package.FormKey;
        _travelTarget = target.FormKey;
        _travelPurpose = furnitureApproach is null ? "reference-marker" : "furniture-approach";
        _travelCursor = 0;
        _travelPublishedDistance = 0;
        _travelActive = true;
        // This locomotion owner publishes the ordinary walking group.
        Activity.SetMovement(running: false, sneaking: false);
        PlayLocomotion(true);
        GD.Print($"OPENNV_NATIVE_PACKAGE_TRAVEL reference={Appearance.Reference} package={package.FormKey} target={target.FormKey} " +
            $"navmeshes={_navigation.NavMeshes} waypoints={_travelPath.Length} distancePerCycle={_travelCycleDistance:R} parity=unmeasured");
    }

    private void PlayLocomotion(bool moving)
    {
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
                AdvanceTravel((distance - _travelPublishedDistance) * Skeleton.UnitsToMetres);
                _travelPublishedDistance = distance;
            };
        }
        _baseAnimation = new(nif, sequence, Skeleton, accumulationRoot: root);
        Skeleton.Node.SetBonePose(Skeleton.BoneIndex(sequence.TargetName), Transform3D.Identity);
        _baseAnimationSeconds = sequence.StartTime;
        _baseElapsedSeconds = 0;
        _baseAnimation.ApplySourceTime(sequence.StartTime);
        SetMeta("opennv_base_animation_source", identity);
    }

    private void AdvanceTravel(float distance)
    {
        if (!_travelActive) return;
        if (!float.IsFinite(distance) || distance < -0.00001f) throw new InvalidDataException("Locomotion accumulation moved backwards.");
        distance = Math.Max(0, distance);
        while (_travelCursor < _travelPath.Length)
        {
            var offset = _travelPath[_travelCursor] - Position;
            var length = offset.Length();
            if (length > 0)
            {
                var horizontal = new Vector3(offset.X, 0, offset.Z);
                if (horizontal.LengthSquared() > 0)
                    Basis = Basis.LookingAt(horizontal.Normalized(), Vector3.Up).Scaled(Scale);
            }
            if (length > distance)
            {
                Position += offset / length * distance;
                return;
            }
            Position = _travelPath[_travelCursor++];
            distance -= length;
        }
        _travelActive = false;
        Transform = _travelDestination;
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
