using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private Transform3D _furnitureOccupied;
    private bool _furnitureApproaching;
    private bool _furnitureInitialPlacement;
    private Transform3D _furnitureApproach;
    private Vector3? _furnitureApproachFloor;
    private FurnitureClip? _furnitureEntry;
    private FalloutFormKey? _reservedFurniture;
    private FurnitureClip? _furnitureClip;
    private string? _furnitureModel, _furnitureModelHash;

    private sealed record FurnitureClip(FalloutNifFile Nif, FalloutNifControllerSequence Sequence, string Identity,
        string Path, FalloutFormKey Idle, string IdleHash);

    private void BeginFurniturePackage(FalloutPluginRecord package, FalloutPlacedReference reference,
        FalloutPluginRecord furniture, bool initializing)
    {
        if (!TryBeginFurniturePackage(package, reference, furniture, initializing))
            throw new NotSupportedException("Furniture has no unreserved source seat.");
    }

    private bool TryBeginFurniturePackage(FalloutPluginRecord package, FalloutPlacedReference reference,
        FalloutPluginRecord furniture, bool initializing)
    {
        var path = _aiCell!.BaseObjects[reference.Base].ModelPath ?? throw new InvalidDataException("Furniture has no model.");
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned files are absent.");
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Furniture model is absent.", path);
        var nif = FalloutNifFile.Read(bytes);
        var seats = FalloutFurnitureSource.ReadSeats(_aiStack!, furniture, nif);
        var candidates = seats.Select(value => (Seat: value, Occupied: Occupied(value)))
            .OrderBy(value => value.Occupied.Origin.DistanceSquaredTo(Position));
        var chosen = candidates.FirstOrDefault(value => _aiWorld?.ReserveFurnitureSeat(reference.FormKey,
            value.Seat.Index, Appearance.Reference!.Value) ?? seats.Count == 1);
        if (chosen.Seat is not { } seat) return false;
        _reservedFurniture = reference.FormKey;
        _furnitureModel = path; _furnitureModelHash = nif.Sha256;
        if (_aiReferenceState is { } state) state.ProcedureCaptureBlocker = FindFurnitureCaptureBlocker;
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
            return true;
        }
        _furnitureEntry = ReadFurnitureClip(2);
        var (start, end) = FurnitureRootEndpoints(_furnitureEntry);
        var motion = NativeFurnitureRootMotion.Enter(_furnitureOccupied, seat.HeadingDelta, end);
        _furnitureApproaching = true;
        _furnitureApproach = motion.Sample(start);
        _furnitureApproachFloor = null;
        _sitting = 0;
        _travelProgress?.Cancel();
        _travelTarget = reference.FormKey;
        _travelPackage = package.FormKey;
        _travelPurpose = "furniture-approach";
        _travelDestination = _furnitureApproach;
        PlayLocomotion(false);
        return true;
    }

    private void AdvanceFurnitureApproach(double delta)
    {
        if (Combat is null || _aiError is not null || Combat.OwnsPose || !Combat.PackageMovementReady ||
            _conversationTarget is not null) return;
        try
        {
            var parent = GetParent<Node3D>();
            var authored = parent.ToGlobal(_furnitureApproach.Origin);
            var destination = _furnitureApproachFloor ??= Combat.ProjectPackageDestination(authored);
            if (destination.DistanceTo(authored) > .15f)
                throw new NotSupportedException("Furniture entry root has no matching supported native floor.");
            Combat.AdvancePackageMotion(_aiPackage!, destination, .08f, _findFurniture?.Running == true, delta, requireArrivalHeight: true);
            if (!IsOnFloor() || GlobalPosition.DistanceTo(destination) > .1f) return;
            var direction = -(parent.GlobalBasis * _furnitureApproach.Basis.Z).Normalized();
            Combat.FacePackageDirection(direction, delta);
            if (GlobalBasis.Orthonormalized().Z.AngleTo(-direction) > .04f) return;
            Combat.StopPackageMotion();
            Transform = _furnitureApproach;
            _furnitureApproaching = false;
            _furnitureReference = _reservedFurniture;
            _sitting = 2;
            StartFurnitureAnimation();
            GD.Print($"OPENNV_NATIVE_FURNITURE_ENTRY reference={Appearance.Reference} target={_furnitureReference} sourceIndex={_seat!.Index} owner=native-capsule-arrival");
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            Combat.StopPackageMotion(); _aiError = error.Message; _failedPackage = _aiPackage?.FormKey;
            BlockSelectionCapture($"Furniture approach continuation is unbound: {error.Message}");
            GD.PushError($"OPENNV_NATIVE_FURNITURE_APPROACH_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
    }

    private FurnitureClip ReadFurnitureClip(int sitting)
    {
        var idle = _furnitureIdles!.Select(condition => condition.Function switch
        {
            159 => sitting,
            143 when sitting == 2 => throw new NotSupportedException("Furniture entry condition needs its native script-visible procedure code."),
            _ => EvaluateAiCondition(condition),
        });
        return ReadFurnitureClip(idle, sitting);
    }

    private FurnitureClip ReadFurnitureClip(FalloutPluginRecord idle, int sitting)
    {
        var source = FalloutActorIdleSource.Resolve(_aiStack!, idle);
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
        return new(nif, sequence, identity, source.AnimationPath, idle.FormKey,
            OpenNV.Runtime.World.Cells.FalloutActorFurnitureContinuation.RecordHash(idle));
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

    private void StartFurnitureAnimation(FurnitureClip? retained = null)
    {
        var clip = retained ?? (_sitting == 2 ? _furnitureEntry ?? throw new InvalidOperationException("Furniture entry source is absent.")
            : ReadFurnitureClip(_sitting));
        _furnitureClip = clip;
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
        BindBaseClock(clip.Nif, clip.Path, ambient: _sitting == 1);
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
        if (!_sandboxFurnitureAction) _packageEvents!.Complete();
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
        _baseClock.Restore(new(_baseResource, _baseHash, remaining, false));
        _baseAnimationSeconds = loop.StartTime + (float)(remaining * loop.Frequency % (loop.StopTime - loop.StartTime));
        _baseAnimation.ApplySourceTime(_baseAnimationSeconds);
        _aiQuestRevision = -1;
    }

    private void CompleteFurnitureExit()
    {
        Basis = _furnitureOccupied.Basis;
        ClearFurniture();
        if (CompleteSandboxFurnitureExit()) return;
        _packageEvents!.Change(null);
        _aiPackage = null;
        _findFurniture = null;
        _packageIdleSource = null;
        _packageIdles = null;
        ClearDialoguePackage();
        _baseAnimation = null;
        _aiQuestRevision = -1;
        _pendingPackage = null;
        PlayLocomotion(moving: false);
        AdvanceAi();
    }

    private void ClearFurniture(bool retire = true)
    {
        if (retire && _reservedFurniture is { } furniture && _seat is { } seat)
            _aiWorld?.ReleaseFurnitureSeat(furniture, seat.Index, Appearance.Reference!.Value);
        if (retire && _aiReferenceState is { } state)
        {
            state.FurnitureContinuation = null;
            if (state.ProcedureCaptureBlocker == FindFurnitureCaptureBlocker) state.ProcedureCaptureBlocker = null;
        }
        _reservedFurniture = null;
        _seat = null;
        _furnitureReference = null;
        _furnitureEntry = null;
        _furnitureClip = null;
        _furnitureModel = null; _furnitureModelHash = null;
        _furnitureApproaching = false;
        _furnitureApproachFloor = null;
        _furnitureInitialPlacement = false;
        _sitting = 0;
    }

    private bool RetainFurniturePackage(FalloutPluginRecord selected)
    {
        var source = FalloutScriptPackage.Read(selected);
        FalloutFindFurniturePackage? find = null;
        if (source.Procedure == 0)
        {
            find = FalloutFindFurniturePackage.Read(selected);
            if (!find.Candidates(_aiStack!, _aiWorld!, Appearance.Reference!.Value, _aiCell!)
                .Any(reference => reference.FormKey == _furnitureReference)) return false;
        }
        else if (source.Procedure != 6 || source.LocationType != 0 || source.LocationRadius != 0 ||
            source.LocationReference != _furnitureReference) return false;
        if (ScriptPackageAssignmentPending || _packageEvents!.Active?.Form != selected.FormKey)
        {
            _packageEvents!.Change(null);
            if (_bindingScriptPackageRevision != ScriptPackageRevision || _requestedSelection is not null)
            { _aiPollRemaining = 0; return true; }
        }
        // A source package can enable seated conversation after occupation.
        // Its change/end events do not require leaving and re-entering the
        // same seat, and its existing animation keeps its observed phase.
        CancelIdle();
        ClearDialoguePackage();
        _aiPackage = selected; _packageIdleSource = source;
        _findFurniture = find;
        _packageIdles = new(source, _idleReplays, idle => _idleConditions!.AllPass(idle, EvaluateAiCondition));
        _packageEvents!.Change(source);
        _packageEvents.Complete();
        return true;
    }

    public override void _ExitTree()
    {
        RetainFollowMotion();
        RetainSandboxCollection();
        RetireSandboxNativeActionOnExit();
        RetainHeadTracking();
        RetainPendingSelection();
        RetainBindingFailure();
        RetainSelectionFailure();
        RetainDialogueContinuation();
        var retainedFurniture = RetainFurnitureContinuation();
        ClearFurniture(retire: !retainedFurniture);
        if (_aiReferenceState is { } furnitureState && ReferenceEquals(furnitureState.QuerySitting, _sittingQuery))
            furnitureState.QuerySitting = null;
        if (_aiReferenceState is { } state && ReferenceEquals(state.QueryCurrentPackage, _currentPackageQuery))
        {
            if (_packageEvents is not null) _aiWorld?.UnloadedPackages?.Retain(Appearance.Reference!.Value, _packageEvents);
            state.QueryCurrentPackage = null;
            if (ReferenceEquals(state.CapturePackageAssignment, _packageAssignmentCapture)) state.CapturePackageAssignment = null;
        }
    }
}
