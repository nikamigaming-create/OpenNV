using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private Transform3D _furnitureOccupied;
    private bool _furnitureApproaching;
    private bool _furnitureInitialPlacement;
    private bool _furnitureSandboxActivity;
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
        FalloutPluginRecord furniture, bool initializing, bool sandboxActivity = false)
    {
        if (_aiWorld is { } world)
        {
            var placement = world.Placement(reference.FormKey);
            reference = reference with { Cell = placement.Cell, Position = placement.Position, RotationRadians = placement.RotationRadians };
        }
        var path = (_aiWorld?.PlacedSource(reference.FormKey).BaseObject ?? _aiCell!.BaseObjects[reference.Base]).ModelPath
            ?? throw new InvalidDataException("Furniture has no model.");
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned files are absent.");
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Furniture model is absent.", path);
        var nif = FalloutNifFile.Read(bytes);
        var seats = FalloutFurnitureSource.ReadSeats(_aiStack!, furniture, nif);
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
        var candidates = seats.Select(value => (Seat: value, Occupied: Occupied(value)))
            .OrderBy(value => value.Occupied.Origin.DistanceSquaredTo(Position));
        var chosen = candidates.FirstOrDefault(value => _aiWorld?.ReserveFurnitureSeat(reference.FormKey,
            value.Seat.Index, Appearance.Reference!.Value) ?? seats.Count == 1);
        if (chosen.Seat is not { } seat) return false;
        try
        {
            _reservedFurniture = reference.FormKey;
            _furnitureSandboxActivity = sandboxActivity;
            _furnitureModel = path; _furnitureModelHash = nif.Sha256;
            if (_aiReferenceState is { } state) state.ProcedureCaptureBlocker = FindFurnitureCaptureBlocker;
            _furnitureIdles ??= new(_aiStack!, Appearance.SkeletonPath);
            _seat = seat;
            _furnitureOccupied = chosen.Occupied;

            _aiPackage = package;
            _furnitureInitialPlacement = initializing;
            if (!sandboxActivity) _packageEvents!.Change(_packageIdleSource);
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
        catch
        {
            ClearFurniture();
            throw;
        }
    }

    private void AdvanceFurnitureApproach(double delta)
    {
        if (Combat is null || _aiError is not null || Combat.OwnsPose || !Combat.PackageMovementReady ||
            _conversationTarget is not null) return;
        try
        {
            var parent = GetParent<Node3D>();
            var authored = parent.ToGlobal(_furnitureApproach.Origin);
            if (_furnitureApproachFloor is null)
            {
                // NAVM bounds the source corridor, not the chair's entry root.
                // The shared capsule route walks that prefix before refining
                // the authored reference approach against native collision.
                _furnitureApproachFloor = Combat.ProjectPackageDestination(authored);
                GD.Print($"OPENNV_NATIVE_FURNITURE_APPROACH reference={Appearance.Reference} target={_reservedFurniture} " +
                    $"authored={authored} navm={_furnitureApproachFloor.Value} owner=source-corridor-and-native-capsule arrival=unobserved");
            }
            Combat.AdvancePackageMotion(_aiPackage!, authored, .08f,
                _furnitureSandboxActivity ? _sandboxPackage!.Running : _findFurniture?.Running == true,
                delta, weaponDrawn: _furnitureSandboxActivity && _sandboxPackage!.WeaponDrawn, requireArrivalHeight: true);
            if (!IsOnFloor() || GlobalPosition.DistanceTo(authored) > .1f) return;
            var direction = -(parent.GlobalBasis * _furnitureApproach.Basis.Z).Normalized();
            Combat.FacePackageDirection(direction, delta);
            if (GlobalBasis.Orthonormalized().Z.AngleTo(-direction) > .04f) return;
            Combat.StopPackageMotion();
            var arrival = GlobalPosition;
            Transform = _furnitureApproach;
            _furnitureApproaching = false;
            _furnitureReference = _reservedFurniture;
            _sitting = 2;
            StartFurnitureAnimation();
            GD.Print($"OPENNV_NATIVE_FURNITURE_ENTRY reference={Appearance.Reference} target={_furnitureReference} sourceIndex={_seat!.Index} " +
                $"arrival={arrival} authored={authored} distance={arrival.DistanceTo(authored):R} owner=native-capsule-arrival");
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            Combat.StopPackageMotion(); _aiError = error.Message; _failedPackage = _aiPackage?.FormKey;
            ClearFurniture();
            BlockSelectionCapture($"Furniture approach continuation is unbound: {error.Message}");
            GD.PushError($"OPENNV_NATIVE_FURNITURE_APPROACH_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
    }

    private FurnitureClip ReadFurnitureClip(int sitting)
    {
        var idle = _furnitureIdles!.Select(condition => condition.Function switch
        {
            // Branch requests use the base-idle loading phase (1), while the
            // public occupied query reports phase 3 for the running loop.
            159 => _seat?.Sleeping == true ? 0 : sitting,
            49 => _seat?.Sleeping == true ? sitting : 0,
            // This request owns ordinary physical sitting/sleeping, not a
            // Sandman assassination or a Cannibal corpse action. Those other
            // action contexts still require their own gameplay executor.
            226 or 227 => 0,
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
        if (!_furnitureSandboxActivity && _sleepPackage is null) _packageEvents!.Complete();
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
        if (_furnitureSandboxActivity && _aiReferenceState?.PackageMotion?.Sandbox is { } activity)
            ObserveSandboxFurniturePose(activity);
        _aiQuestRevision = -1;
    }

    private void CompleteFurnitureExit()
    {
        Basis = _furnitureOccupied.Basis;
        var sandboxActivity = _furnitureSandboxActivity;
        var sleep = _sleepPackage is not null;
        var packageExit = _sandboxFurniturePackageExit;
        ClearFurniture();
        if (sleep)
        {
            _requestedSelection = _furniturePendingSelection ??
                throw new InvalidOperationException("Sleep exit lost its accepted package selection.");
            _furniturePendingSelection = null;
            _sleepPackage = null; _sleepProgress = null; _sleepBeds = null;
        }
        if (sandboxActivity && !packageExit)
        {
            // The chair is an activity inside the retained Sandbox package.
            // Releasing its pose cannot complete or replace that package.
            _baseAnimation = null;
            _sandboxWander = null;
            PlayLocomotion(moving: false);
            if (_aiReferenceState?.PackageMotion?.Sandbox is { } activity)
                ObserveSandboxFurniturePose(activity);
            return;
        }
        if (sandboxActivity)
        {
            _requestedSelection = _furniturePendingSelection ??
                throw new InvalidOperationException("Sandbox chair exit lost its accepted parent selection.");
            _furniturePendingSelection = null;
            _sandboxPackage = null;
            _sandboxWander = null;
            _pendingCurrentLocationSandbox = null;
            ClearPendingSandboxStart(clearBlocker: true);
        }
        _packageEvents!.Change(null);
        if (sleep)
        {
            Combat?.RetirePackageMotionNavigation(_aiPackage!.FormKey);
            if (_aiReferenceState?.PackageMotion?.Package == _aiPackage!.FormKey)
                _aiReferenceState.PackageMotion = null;
        }
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
        _furnitureSandboxActivity = false;
        _sandboxFurniturePackageExit = false;
        _sitting = 0;
    }

    private bool RetainFurniturePackage(FalloutPluginRecord selected)
    {
        if (_furnitureSandboxActivity) return false;
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
        // A source package can enable seated conversation after occupation.
        // Its change/end events do not require leaving and re-entering the
        // same seat, and its existing animation keeps its observed phase.
        CancelIdle();
        ClearDialoguePackage();
        if (_sleepPackage is { } sleep)
        {
            _packageEvents!.Change(null);
            Combat?.RetirePackageMotionNavigation(sleep.Form);
            _sleepPackage = null; _sleepProgress = null; _sleepBeds = null;
            // FurnitureContinuation retains the physical seat and clock.
            // The outgoing Sleep movement intent cannot own the new PACK.
            if (_aiReferenceState?.PackageMotion?.Package == sleep.Form)
                _aiReferenceState.PackageMotion = null;
        }
        _aiPackage = selected; _packageIdleSource = source;
        _findFurniture = find;
        _packageIdles = new(source, _idleReplays, idle => _idleConditions!.AllPass(idle, EvaluateAiCondition));
        _packageEvents!.Change(source);
        _packageEvents.Complete();
        return true;
    }

    private bool _sourceBindingsRetired;

    public override void _ExitTree() => RetireSourceBindings();

    internal void RetireSourceBindings()
    {
        if (_sourceBindingsRetired) return;
        _sourceBindingsRetired = true;
        var ownsFurniture = _furnitureCapture is not null && _aiReferenceState is { } owner &&
            ReferenceEquals(owner.CaptureFurniture, _furnitureCapture) && ReferenceEquals(owner.CanCaptureFurniture, _furnitureCaptureReady);
        var retainedFurniture = false;
        var retainedMarker = false;
        try
        {
            if (HasDetachedPendingSandboxStart) return;
            if (_sandboxAdmissionStaging is { } staging)
            {
                if (!staging.Retire(RetainLiveSources)) ReleaseDetachedSandboxAdmission();
            }
            else RetainLiveSources();
        }
        finally
        {
            ReleaseSourceBindings();
            ClearFurniture(retire: ownsFurniture && !retainedFurniture);
            ClearSandboxMarker(release: !retainedMarker, clearRetained: !retainedMarker);
        }

        void RetainLiveSources()
        {
            RetainHeadTracking();
            RetainPendingSelection();
            RetainPendingSandboxStart();
            RetainBindingFailure();
            RetainSandboxAdmissionFailure();
            RetainSelectionFailure();
            RetainDialogueContinuation();
            retainedFurniture = RetainFurnitureContinuation();
            retainedMarker = RetainSandboxMarkerContinuation();
            RetainIdleRequestContinuation();
            if (_aiReferenceState is { } state && ReferenceEquals(state.QueryCurrentPackage, _currentPackageQuery) && _packageEvents is not null)
                _aiWorld?.UnloadedPackages?.Retain(Appearance.Reference!.Value, _packageEvents);
        }
    }
}
