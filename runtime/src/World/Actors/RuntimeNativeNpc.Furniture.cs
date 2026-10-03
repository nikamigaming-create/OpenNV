using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private Transform3D _furnitureOccupied;
    private bool _furnitureApproaching;
    private bool _furnitureInitialPlacement;
    private FurnitureClip? _furnitureEntry;
    private FalloutFormKey? _reservedFurniture;

    private sealed record FurnitureClip(FalloutNifFile Nif, FalloutNifControllerSequence Sequence, string Identity);

    private void BeginFurniturePackage(FalloutPluginRecord package, FalloutPlacedReference reference,
        FalloutPluginRecord furniture, bool initializing)
    {
        var path = _aiCell!.BaseObjects[reference.Base].ModelPath ?? throw new InvalidDataException("Furniture has no model.");
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned files are absent.");
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Furniture model is absent.", path);
        var seats = FalloutFurnitureSource.ReadSeats(_aiStack!, furniture, FalloutNifFile.Read(bytes));
        var candidates = seats.Select(value => (Seat: value, Occupied: Occupied(value)))
            .OrderBy(value => value.Occupied.Origin.DistanceSquaredTo(Position));
        var chosen = candidates.FirstOrDefault(value => _aiWorld?.ReserveFurnitureSeat(reference.FormKey,
            value.Seat.Index, Appearance.Reference!.Value) ?? seats.Count == 1);
        var seat = chosen.Seat ?? throw new NotSupportedException("Furniture has no unreserved source seat.");
        _reservedFurniture = reference.FormKey;
        _furnitureIdles ??= new(_aiStack!, Appearance.SkeletonPath);
        _seat = seat;
        _furnitureOccupied = chosen.Occupied;

        Transform3D Occupied(FalloutFurnitureSeat value)
        {
            var offset = value.Marker.Offset;
            var units = Skeleton.UnitsToMetres;
            return GamebryoPackagePlacement.FromFurnitureMarker(reference.FormKey.ToString(), _referenceTransform!(reference),
            GamebryoCoordinate.ConvertVector(new(offset.X, offset.Y, offset.Z)) * units,
            new Quaternion(Vector3.Up, -value.Marker.Orientation / 1000.0f),
            GamebryoCoordinate.ConvertVector(new(value.PlacementOffset[0], value.PlacementOffset[1], value.PlacementOffset[2])) * units,
            new Quaternion(Vector3.Up, -value.HeadingDelta), Scale).SourceTransform;
        }
        _aiPackage = package;
        _furnitureInitialPlacement = initializing;
        _packageEvents!.Change(_packageIdleSource);
        // Initial process binding retains the existing source placement. A
        // later package must physically approach and enter before it is done.
        if (initializing)
        {
            Transform = _furnitureOccupied;
            _furnitureReference = reference.FormKey;
            OccupyFurniture();
            return;
        }
        _furnitureEntry = ReadFurnitureClip(2);
        var (start, end) = FurnitureRootEndpoints(_furnitureEntry);
        var motion = NativeFurnitureRootMotion.Enter(_furnitureOccupied, seat.HeadingDelta, end);
        _furnitureApproaching = true;
        _sitting = 0;
        StartTravel(package, reference, motion.Sample(start));
        CompletePendingTravel();
    }

    private FurnitureClip ReadFurnitureClip(int sitting)
    {
        var source = FalloutActorIdleSource.Resolve(_aiStack!, _furnitureIdles!.Select(condition => condition.Function switch
        {
            159 => sitting,
            143 when sitting == 2 => throw new NotSupportedException("Furniture entry condition needs its native script-visible procedure code."),
            _ => EvaluateAiCondition(condition),
        }));
        if (source.Objects.Count != 0) throw new NotSupportedException("Furniture base ANIO requires object ownership.");
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned files are absent.");
        if (!content.TryRead(source.AnimationPath, null, out var bytes, out var identity))
            throw new FileNotFoundException("Furniture animation is absent.", source.AnimationPath);
        var nif = FalloutNifFile.Read(bytes);
        var sequences = nif.Roots.Select(nif.ReadObject).OfType<FalloutNifControllerSequence>().ToArray();
        if (sequences.Length != 1) throw new NotSupportedException("Furniture KF requires one sequence.");
        var sequence = sequences[0];
        if (sequence.Frequency <= 0 || sequence.StopTime <= sequence.StartTime ||
            sequence.CycleType != (sitting is 2 or 4 ? 2 : 0))
            throw new NotSupportedException("Furniture procedure has an unsupported source clock.");
        return new(nif, sequence, identity);
    }

    private (Vector3 Start, Vector3 End) FurnitureRootEndpoints(FurnitureClip clip)
    {
        var root = clip.Sequence.ControlledBlocks.SingleOrDefault(link =>
            link.NodeName == clip.Sequence.TargetName && link.ControllerType == "NiTransformController")
            ?? throw new NotSupportedException("Furniture transition has no authored accumulation channel.");
        var sampler = new FalloutNifAnimationSampler(clip.Nif, root.Interpolator);
        var start = sampler.Sample(clip.Sequence.StartTime);
        var end = sampler.Sample(clip.Sequence.StopTime);
        RequireTranslationOnlyRoot(start); RequireTranslationOnlyRoot(end);
        return (SourceTranslation(start), SourceTranslation(end));
    }

    private void StartFurnitureAnimation()
    {
        var clip = _sitting == 2 ? _furnitureEntry ?? throw new InvalidOperationException("Furniture entry source is absent.")
            : ReadFurnitureClip(_sitting);
        Action<FalloutNifAnimationSample>? rootOwner = null;
        if (_sitting is 2 or 4)
        {
            var (start, end) = FurnitureRootEndpoints(clip);
            var motion = _sitting == 2
                ? NativeFurnitureRootMotion.Enter(_furnitureOccupied, _seat!.HeadingDelta, end)
                : NativeFurnitureRootMotion.Exit(_furnitureOccupied, _seat!.HeadingDelta, start);
            rootOwner = sample =>
            {
                RequireTranslationOnlyRoot(sample);
                Transform = motion.Sample(SourceTranslation(sample));
            };
        }
        else if (clip.Sequence.ControlledBlocks.Any(link => link.NodeName == clip.Sequence.TargetName))
            throw new NotSupportedException("Furniture base accumulation requires root-motion extraction.");
        var animation = new RuntimeNativeNifAnimation(clip.Nif, clip.Sequence, Skeleton, accumulationRoot: rootOwner);
        Skeleton.Node.SetBonePose(Skeleton.BoneIndex(animation.Sequence.TargetName), Transform3D.Identity);
        animation.ApplySourceTime(animation.Sequence.StartTime);
        _baseAnimation = animation;
        _baseAnimationSeconds = animation.Sequence.StartTime;
        _baseElapsedSeconds = 0;
        SetMeta("opennv_base_animation_source", clip.Identity);
    }

    private void CompleteTravel()
    {
        if (_furnitureApproaching)
        {
            _furnitureApproaching = false;
            _furnitureReference = _travelTarget;
            _sitting = 2;
            StartFurnitureAnimation();
            return;
        }
        PlayLocomotion(false);
        if (_dialoguePackage is null) _packageEvents!.Complete();
    }

    private void OccupyFurniture()
    {
        _sitting = 1;
        StartFurnitureAnimation();
        _furnitureEntry = null;
        _packageEvents!.Complete();
        GD.Print($"OPENNV_NATIVE_FURNITURE_OCCUPIED reference={Appearance.Reference} package={_aiPackage!.FormKey} " +
            $"target={_furnitureReference} marker={_seat!.MarkerId} sourceIndex={_seat.Index} " +
            $"initialPlacement={_furnitureInitialPlacement} animation={_baseAnimation!.Sequence.Name} parity=unmeasured");
    }

    private void CompleteFurnitureEntry()
    {
        var previous = _baseAnimation!.Sequence;
        var remaining = Math.Max(0, _baseElapsedSeconds - (previous.StopTime - previous.StartTime) / previous.Frequency);
        Transform = _furnitureOccupied;
        OccupyFurniture();
        var loop = _baseAnimation!.Sequence;
        _baseElapsedSeconds = remaining;
        _baseAnimationSeconds = loop.StartTime + (float)(remaining * loop.Frequency % (loop.StopTime - loop.StartTime));
        _baseAnimation.ApplySourceTime(_baseAnimationSeconds);
        _aiQuestRevision = -1;
    }

    private void CompleteFurnitureExit()
    {
        Basis = _furnitureOccupied.Basis;
        ClearFurniture();
        _packageEvents!.Change(null);
        _aiPackage = null;
        _baseAnimation = null;
        _aiQuestRevision = -1;
        _pendingPackage = null;
        PlayLocomotion(moving: false);
        AdvanceAi();
    }

    private void ClearFurniture()
    {
        if (_reservedFurniture is { } furniture && _seat is { } seat)
            _aiWorld?.ReleaseFurnitureSeat(furniture, seat.Index, Appearance.Reference!.Value);
        _reservedFurniture = null;
        _seat = null;
        _furnitureReference = null;
        _furnitureEntry = null;
        _furnitureApproaching = false;
        _furnitureInitialPlacement = false;
        _sitting = 0;
    }

    private bool RetainFurniturePackage(FalloutPluginRecord selected)
    {
        var source = FalloutScriptPackage.Read(selected);
        if (source.Procedure != 6 || source.LocationType != 0 || source.LocationRadius != 0 ||
            source.LocationReference != _furnitureReference) return false;
        // A source package can enable seated conversation after occupation.
        // Its change/end events do not require leaving and re-entering the
        // same seat, and its existing animation keeps its observed phase.
        CancelIdle();
        _aiPackage = selected; _packageIdleSource = source;
        _packageIdles = new(source, _idleReplays, idle => _idleConditions!.AllPass(idle, EvaluateAiCondition));
        _packageEvents!.Change(source);
        _packageEvents.Complete();
        return true;
    }

    public override void _ExitTree()
    {
        ClearFurniture();
        if (_aiReferenceState is { } state && ReferenceEquals(state.QueryCurrentPackage, _currentPackageQuery))
        {
            if (_packageEvents is not null) _aiWorld?.UnloadedPackages?.Retain(Appearance.Reference!.Value, _packageEvents);
            state.QueryCurrentPackage = null;
            if (ReferenceEquals(state.CapturePackageAssignment, _packageAssignmentCapture)) state.CapturePackageAssignment = null;
        }
    }
}
