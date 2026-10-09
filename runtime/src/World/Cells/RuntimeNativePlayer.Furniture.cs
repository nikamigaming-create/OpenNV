using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private RuntimeNativeNpc? _furnitureBody;
    private FalloutFurnitureSeat? _furnitureSeat;
    private FalloutFormKey? _furnitureReference;
    private FalloutReferenceWorld? _furnitureWorld;
    private FalloutFormKey? _furnitureReservationActor;
    private readonly Dictionary<int, PlayerFurnitureClip> _furnitureClips = [];
    private PlayerFurnitureClip? _furnitureClip;
    private Transform3D _occupied, _approach;
    private NativeFurnitureRootMotion? _furnitureMotion;
    private double _furnitureSeconds;
    private double _furniturePhaseSeconds;
    private int _furniturePhase;
    private float _furnitureLookYaw;
    private string? _furnitureError;
    private readonly Dictionary<FalloutFormKey, CellNavigationGraph> _furnitureNavigation = [];
    private CellNavigationGraph? _furnitureGraph;
    private Vector3[]? _furniturePath;
    private int _furnitureWaypoint;
    private readonly FalloutSoundRandomState _furnitureRandom = new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
    internal FalloutFormKey? CurrentFurniture => _furniturePhase >= 2 ? _furnitureReference : null;
    internal bool FurnitureActive => _furniturePhase != 0;
    internal int SittingState
    {
        get
        {
            PhysicalPlayer.RequireHealthy();
            if (_furnitureError is not null) throw new NotSupportedException($"Player furniture continuation failed: {_furnitureError}");
            return _furnitureSeat?.Kind == FalloutPlayerFurnitureKind.Sleeping || _furniturePhase == 1 ? 0 : _furniturePhase;
        }
    }
    internal Transform3D FurnitureApproach => _furniturePhase != 0 ? _approach : throw new InvalidOperationException("No furniture approach is active.");
    internal bool RequestFurnitureExit()
    {
        if (_furniturePhase != 3 || _furnitureError is not null) return false;
        if (PhysicalPlayer.Sleeping) return false;
        PhysicalPlayer.Execute("request-player-furniture-exit", () => StartFurniturePhase(4));
        return true;
    }
    internal object FurnitureState => new
    {
        reference = _furnitureReference?.ToString(),
        marker = _furnitureSeat?.Index,
        phase = _furniturePhase switch { 0 => "none", 1 => "approaching", 2 => "entering", 3 => "occupied", 4 => "exiting", _ => "invalid" },
        source = _furnitureClip?.Identity,
        seconds = _furnitureSeconds,
        error = _furnitureError,
        approach = _furniturePhase == 0 ? null : new[] { _approach.Origin.X, _approach.Origin.Y, _approach.Origin.Z },
        waypoints = _furniturePath?.Length ?? 0,
        waypoint = _furnitureWaypoint,
        camera = "owned-first-person-idle; third-person-transition-unbound",
    };

    private sealed record PlayerFurnitureClip(PlayerPhysicalClip Source, FalloutNifAnimatedNodePath? Camera)
    {
        internal FalloutNifControllerSequence Sequence => Source.Animation.Sequence;
        internal RuntimeNativeNifAnimation Animation => Source.Animation;
        internal FalloutFurnitureClipSnapshot Identity => Source.Identity;
        internal Vector3 Start => Source.Start;
        internal Vector3 End => Source.End;
    }

    internal void ActivateFurniture(FalloutPluginStack records, FalloutQuestState quests,
        FalloutPlacedReference reference, Transform3D placement, FalloutFormKey cell, FalloutReferenceWorld? world = null)
    {
        if (_furniturePhase != 0 || _furnitureError is not null)
            throw new InvalidOperationException("Player already owns a furniture interaction or failure.");
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned furniture source is absent.");
        var furniture = records.GetEffective(reference.Base);
        var path = "meshes/" + FalloutDialogueTopic.Text(furniture.ReadSubrecords().Single(field => field.Signature == "MODL").Data.Span).Replace('\\', '/');
        PhysicalPlayer.RequireHealthy();
        if (PhysicalPlayer.HasPhysicalMotion || world is null || records != _physicalRecords || quests != _physicalQuests || world != _physicalWorld ||
            !world.IsEnabled(reference.FormKey) || world.Get(reference.FormKey).Base != reference.Base || world.Placement(reference.FormKey).Cell != cell)
            throw new InvalidDataException("Furniture activation differs from the actual player's enabled authoritative source world.");
        if (FalloutFurnitureSource.ReadKind(furniture) == FalloutPlayerFurnitureKind.Sleeping)
        {
            // The original player bed branch opens its real sleep menu. It
            // does not dispatch the separate actor chair/lying animation path
            // or consume its IDLE selection RNG/text-key history.
            (OpenSourceSleepMenu ?? throw new NotSupportedException("Source bed activation has no actual sleep/wait menu factory."))
                (FalloutSleepWaitBed.Read(records, reference, Read(path)).Request);
            return;
        }
        if (_thirdPersonMode && _xr is null)
            throw new NotSupportedException("Player furniture requires its actual third-person camera transition owner.");
        var nif = Read(path);
        var seat = FalloutFurnitureSource.ReadPlayer(records, furniture, nif);
        var body = _thirdPerson?.Actor ?? throw new NotSupportedException("Player furniture requires its actual current source body.");
        if (!body.IsInsideTree()) throw new NotSupportedException("Player furniture source body is detached.");
        _furniturePlacement = placement;
        _furnitureSourcePlacement = world.Placement(reference.FormKey);
        _furnitureSourceCell = cell;
        _furnitureReferenceHash = PhysicalRecordHash(records.GetEffective(reference.FormKey));
        _furnitureBaseHash = PhysicalRecordHash(furniture); _furnitureModel = path; _furnitureModelHash = nif.Sha256;
        PhysicalPlayer.Execute("activate-player-source-furniture", () =>
        {
            try
            {
                var tree = new FalloutFurnitureIdleTree(records, body.Appearance.SkeletonPath);
                _furnitureBody = body;
                foreach (var state in new[] { 2, 1, 4 })
                {
                    var firstPerson = false;
                    float Evaluate(FalloutCondition condition) => condition.Function switch
                    {
                        49 => seat.Kind == FalloutPlayerFurnitureKind.Sleeping ? state : 0,
                        159 => seat.Kind == FalloutPlayerFurnitureKind.Sitting ? state : 0,
                        143 when seat.Kind == FalloutPlayerFurnitureKind.Sitting => state == 4 ? 21 : 48,
                        160 => seat.MarkerId,
                        163 => condition.FormArgument1 == furniture.FormKey ? 1 : 0,
                        70 => condition.Argument1 == (body.Appearance.Female ? 1u : 0u) ? 1 : 0,
                        71 => (world ?? throw new NotSupportedException("Player furniture faction predicate has no reference-world owner."))
                            .ActorFactions(records.RuntimeFormKey(0x14)).GetValueOrDefault(condition.FormArgument1, (sbyte)-1) >= 0 ? 1 : 0,
                        73 => (world ?? throw new NotSupportedException("Player furniture faction predicate has no reference-world owner."))
                            .ActorFactions(records.RuntimeFormKey(0x14)).GetValueOrDefault(condition.FormArgument1, (sbyte)-1),
                        69 => condition.FormArgument1 == body.Appearance.Race ? 1 : 0,
                        365 => FalloutRaceProperties.IsChild(records.GetEffective(body.Appearance.Race)) ? 1 : 0,
                        77 => _furnitureRandom.NextBounded(100),
                        72 => condition.FormArgument1 == body.Appearance.Npc ? 1 : 0,
                        58 or 59 or 79 or 420 or 421 or 546 => quests.Evaluate(condition),
                        63 => Activity.Attacked ? 1 : 0,
                        91 => Activity.Alerted ? 1 : 0,
                        101 => Activity.WeaponDrawn ? 1 : 0,
                        289 => (world ?? throw new NotSupportedException("Player furniture combat predicate has no reference-world owner."))
                            .PlayerInCombat() ? 1 : 0,
                        182 => body.Appearance.EquippedArmor.Contains(condition.FormArgument1) ? 1 : 0,
                        247 => 0, // This furniture activation has no acquired/used item.
                        392 => firstPerson ? 1 : 0,
                        _ => EvaluatePlayerPhysicalCondition(condition),
                    };
                    var idle = FalloutActorIdleSource.Resolve(records, tree.Select(Evaluate));
                    if (idle.Objects.Count != 0) throw new NotSupportedException("Player furniture ANIO has no animation object owner.");
                    var clip = ReadPlayerPhysicalClip(records.GetEffective(idle.Form), state == 1, body.Skeleton,
                        state == 1 ? null : sample => GlobalTransform = _furnitureMotion!.Sample(Translation(sample)));
                    if (state != 1 && clip.Root is null)
                        throw new NotSupportedException("Player furniture transition has no source accumulation channel.");
                    FalloutNifAnimatedNodePath? camera = null;
                    if (state == 1)
                    {
                        firstPerson = true;
                        var cameraIdle = FalloutActorIdleSource.Resolve(records, tree.Select(Evaluate));
                        if (cameraIdle.Form != idle.Form)
                        {
                            var actualFirst = _firstPerson?.Skeleton ?? throw new NotSupportedException("Furniture camera has no actual first-person source skeleton.");
                            var cameraNif = Read(cameraIdle.AnimationPath);
                            camera = new(actualFirst.Source, cameraNif, "Camera1st");
                            _furnitureCameraSource = new(cameraIdle.Form, PhysicalRecordHash(records.GetEffective(cameraIdle.Form)), cameraIdle.AnimationPath, cameraNif.Sha256);
                            _furnitureCameraSkeletonHash = actualFirst.Source.Sha256;
                        }
                    }
                    _furnitureClips.Add(state, new(clip, camera));
                }
                _ = body.Skeleton.BoneIndex("Bip01 Head");
                var offset = seat.Marker.Offset;
                _occupied = GamebryoPackagePlacement.FromFurnitureMarker(reference.FormKey.ToString(), placement,
                    GamebryoCoordinate.ConvertVector(new(offset.X, offset.Y, offset.Z)) * UnitsToMeters,
                    new Quaternion(Vector3.Up, -seat.Marker.Orientation / 1000f),
                    GamebryoCoordinate.ConvertVector(new(seat.PlacementOffset[0], seat.PlacementOffset[1], seat.PlacementOffset[2])) * UnitsToMeters,
                    new Quaternion(Vector3.Up, -seat.HeadingDelta), Vector3.One).SourceTransform;
                var entry = _furnitureClips[2];
                _furnitureMotion = NativeFurnitureRootMotion.Enter(_occupied, seat.HeadingDelta, entry.End);
                _approach = _furnitureMotion.Sample(entry.Start);
                if (!_furnitureNavigation.TryGetValue(cell, out _furnitureGraph))
                    _furnitureNavigation.Add(cell, _furnitureGraph = CellNavigationGraph.LoadOwned(records, cell));
                _furniturePath = null; _furnitureWaypoint = 0;
                if (world is not null && !world.ReserveFurnitureSeat(reference.FormKey, seat.Index, records.RuntimeFormKey(0x14)))
                    throw new InvalidOperationException("Player furniture source seat is reserved by another actor.");
                _furnitureWorld = world;
                _furnitureReservationActor = records.RuntimeFormKey(0x14);
                // Resource/clock preparation precedes publication of the interaction.
                _furnitureSeat = seat; _furnitureReference = reference.FormKey; _furniturePhase = 1;
                _furnitureLookYaw = 0;
                PhysicalPlayer.CommitFurniture(seat.Kind, FalloutPlayerFurniturePhase.Approaching);
                CancelWeaponAction(); Velocity = Vector3.Zero; Activity.SetMovement(false, false);
                GD.Print($"OPENNV_NATIVE_PLAYER_FURNITURE_BEGIN reference={reference.FormKey} marker={seat.Index} source=owned-furn-idle-kf camera=first-person-only parity=unverified");
            }
            catch
            {
                if (_furniturePhase == 0)
                {
                    ReleaseFurnitureReservation();
                    _furnitureBody = null; _furnitureClips.Clear(); _furnitureSeat = null; _furnitureReference = null; _furnitureMotion = null;
                }
                throw;
            }

        });

        FalloutNifFile Read(string resource) => content.TryRead(resource, null, out var bytes, out _)
            ? FalloutNifFile.Read(bytes) : throw new FileNotFoundException("Furniture resource is absent.", resource);
    }

    private Vector3 Translation(FalloutNifAnimationSample sample)
    {
        if (sample.Rotation is { } rotation && (rotation.X != 0 || rotation.Y != 0 || rotation.Z != 0 || MathF.Abs(rotation.W) != 1) ||
            sample.Scale is { } scale && scale != 1 || sample.Translation is not { } translation)
            throw new NotSupportedException("Furniture accumulation requires an authored translation-only root.");
        return GamebryoCoordinate.ConvertVector(new(translation.X, translation.Y, translation.Z)) * UnitsToMeters;
    }

    private void StartFurniturePhase(int phase)
    {
        _furniturePhase = phase; _furnitureSeconds = 0; _furniturePhaseSeconds = 0;
        PhysicalPlayer.CommitFurniture(_furnitureSeat!.Kind, (FalloutPlayerFurniturePhase)phase);
        _furnitureClip = _furnitureClips[phase == 3 ? 1 : phase];
        _furniturePhysicalClock = new(_furnitureClip.Identity, 0, true);
        _furnitureMotion = phase == 2 ? NativeFurnitureRootMotion.Enter(_occupied, _furnitureSeat!.HeadingDelta, _furnitureClip.End) :
            phase == 4 ? NativeFurnitureRootMotion.Exit(_occupied, _furnitureSeat!.HeadingDelta, _furnitureClip.Start) : null;
        if (phase == 3) GlobalTransform = _occupied;
        _furnitureBody!.Skeleton.Node.SetBonePose(_furnitureBody.Skeleton.BoneIndex(_furnitureClip.Sequence.TargetName), Transform3D.Identity);
        _furnitureClip.Animation.ApplySourceTime(_furnitureClip.Sequence.StartTime);
        _furnitureBody.Visible = true;
        PublishPlayerPhysicalView();
        Velocity = Vector3.Zero;
        DispatchFurnitureKeys(0);
        PublishFurnitureCamera();
        if (phase == 3 && _furnitureSeat.Kind == FalloutPlayerFurnitureKind.Sleeping && !_bedPublicationAttempted)
        {
            _bedPublicationAttempted = true;
            (SourceBedOccupied ?? throw new NotSupportedException("Player bed has no actual source sleep-menu/time consumer."))(_furnitureReference!.Value);
        }
    }

    private void AdvanceFurniture(double delta)
    {
        if (_furnitureError is not null || _playerPhysical?.Failure is not null || GetTree().Paused || !CanProcess()) return;
        try
        {
            PhysicalPlayer.Execute("advance-player-source-furniture", () =>
            {
                if (!_physicalWorld!.IsEnabled(_furnitureReference!.Value))
                    throw new NotSupportedException("Disabled player furniture needs its actual interruption/retirement owner.");
                if (_furniturePhase == 1)
                {
                    if (_furniturePath is null)
                    {
                        Vector3 Source(Vector3 point) => new Vector3(point.X, -point.Z, point.Y) / UnitsToMeters;
                        _furniturePath = [.. _furnitureGraph!.FindPath(Source(GlobalPosition), Source(_approach.Origin))
                        .Select(point => GamebryoCoordinate.ConvertVector(point) * UnitsToMeters), _approach.Origin];
                    }
                    while (_furnitureWaypoint < _furniturePath.Length &&
                        GlobalPosition.DistanceTo(_furniturePath[_furnitureWaypoint]) <= SafeMargin * 2) ++_furnitureWaypoint;
                    if (_furnitureWaypoint == _furniturePath.Length)
                    {
                        GlobalTransform = _approach;
                        StartFurniturePhase(2);
                        return;
                    }
                    var target = _furniturePath[_furnitureWaypoint];
                    var distance = GlobalPosition.DistanceTo(target);
                    var step = _configuration.Player.MoveSpeedMetersPerSecond * (float)delta;
                    var motion = target - GlobalPosition;
                    if (distance > step) motion = motion.Normalized() * step;
                    MovePhysicalPlayer(motion);
                    return;
                }
                while (delta > 0)
                {
                    var clip = _furnitureClip!;
                    var duration = (double)(clip.Sequence.StopTime - clip.Sequence.StartTime) / clip.Sequence.Frequency;
                    var consumed = Math.Min(delta, Math.Max(0, duration - _furnitureSeconds));
                    var nextPhase = _furniturePhaseSeconds + consumed;
                    DispatchFurnitureKeys(nextPhase);
                    _furnitureSeconds += consumed; delta -= consumed;
                    _furniturePhaseSeconds = nextPhase;
                    clip.Animation.ApplySourceTime(MathF.Min(clip.Sequence.StopTime, clip.Sequence.StartTime + (float)(_furnitureSeconds * clip.Sequence.Frequency)));
                    _furniturePhysicalClock = _furniturePhysicalClock! with
                    {
                        Seconds = nextPhase,
                        StartPending = false,
                        AttemptedKeyOrdinal = null,
                        AttemptedKeyCycle = null
                    };
                    PublishFurnitureCamera();
                    if (_furnitureSeconds < duration) return;
                    if (_furniturePhase == 4) { ReleaseFurniture(); return; }
                    if (_furniturePhase == 2) StartFurniturePhase(3);
                    else _furnitureSeconds = 0;
                }
            });
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        {
            _furnitureError = error.Message;
            GD.PushError($"OPENNV_NATIVE_PLAYER_FURNITURE_DIVERGENCE reference={_furnitureReference}: {error.Message}");
        }
    }

    private void PublishFurnitureCamera()
    {
        if (_furnitureClip?.Camera is { } camera)
        {
            var sequence = camera.Sequence;
            var time = sequence.StartTime + (float)(_furniturePhaseSeconds * sequence.Frequency % (sequence.StopTime - sequence.StartTime));
            ApplyCameraPath(camera, time, _furnitureLookYaw);
            return;
        }
        var skeleton = _furnitureBody!.Skeleton;
        var head = GlobalTransform.AffineInverse() * skeleton.Node.GlobalTransform * skeleton.Node.GetBoneGlobalPose(skeleton.BoneIndex("Bip01 Head"));
        ApplySourceCamera(new(new Basis(Vector3.Up, _furnitureLookYaw), head.Origin));
    }

    private void ReleaseFurniture()
    {
        GlobalBasis = _occupied.Basis;
        ReleaseFurnitureReservation();
        PhysicalPlayer.CommitFurniture(_furnitureSeat!.Kind, FalloutPlayerFurniturePhase.None);
        RetirePlayerPhysicalView();
        _furnitureBody = null; _furnitureClip = null; _furniturePhysicalClock = null; _bedPublicationAttempted = false;
        _furnitureCameraSource = null; _furnitureCameraSkeletonHash = null;
        _furnitureClips.Clear(); _furnitureSeat = null; _furnitureReference = null; _furniturePhase = 0;
        ReleaseSourceCamera();
    }

    private void ReleaseFurnitureReservation()
    {
        if (_furnitureWorld is { } world && _furnitureReference is { } reference && _furnitureSeat is { } seat)
            world.ReleaseFurnitureSeat(reference, seat.Index, _furnitureReservationActor ?? throw new InvalidOperationException("Player furniture reservation has no player identity."));
        _furnitureWorld = null;
        _furnitureReservationActor = null;
    }

}
