using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private RuntimeNativeNpc? _furnitureBody;
    private FalloutFurnitureSeat? _furnitureSeat;
    private FalloutFormKey? _furnitureReference;
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
    internal Func<RuntimeNativeNpc>? CreateFurnitureBody { get; set; }
    internal FalloutFormKey? CurrentFurniture => _furniturePhase >= 2 ? _furnitureReference : null;
    internal bool FurnitureActive => _furniturePhase != 0;
    internal Transform3D FurnitureApproach => _furniturePhase != 0 ? _approach : throw new InvalidOperationException("No furniture approach is active.");
    internal bool RequestFurnitureExit()
    {
        if (_furniturePhase != 3 || _furnitureError is not null) return false;
        StartFurniturePhase(4);
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

    private sealed record PlayerFurnitureClip(FalloutNifControllerSequence Sequence, RuntimeNativeNifAnimation Animation,
        string Identity, Vector3 Start, Vector3 End, FalloutNifAnimatedNodePath? Camera);

    internal void ActivateFurniture(FalloutPluginStack records, FalloutQuestState quests,
        FalloutPlacedReference reference, Transform3D placement, FalloutFormKey cell)
    {
        if (_furniturePhase != 0 || _furnitureError is not null)
            throw new InvalidOperationException("Player already owns a furniture interaction or failure.");
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned furniture source is absent.");
        var furniture = records.GetEffective(reference.Base);
        var path = "meshes/" + FalloutDialogueTopic.Text(furniture.ReadSubrecords().Single(field => field.Signature == "MODL").Data.Span).Replace('\\', '/');
        var seat = FalloutFurnitureSource.Read(records, furniture, Read(path));
        var body = CreateFurnitureBody?.Invoke() ?? throw new NotSupportedException("Player furniture requires the current owned player body.");
        try
        {
            var tree = new FalloutFurnitureIdleTree(records, body.Appearance.SkeletonPath);
            _furnitureBody = body;
            foreach (var state in new[] { 2, 1, 4 })
            {
                var firstPerson = false;
                float Evaluate(FalloutCondition condition) => condition.Function switch
                {
                    159 => state,
                    160 => seat.MarkerId,
                    163 => condition.FormArgument1 == furniture.FormKey ? 1 : 0,
                    70 => condition.Argument1 == (body.Appearance.Female ? 1u : 0u) ? 1 : 0,
                    69 => condition.FormArgument1 == body.Appearance.Race ? 1 : 0,
                    365 => FalloutRaceProperties.IsChild(records.GetEffective(body.Appearance.Race)) ? 1 : 0,
                    77 => _furnitureRandom.NextBounded(100),
                    72 => condition.FormArgument1 == body.Appearance.Npc ? 1 : 0,
                    58 or 59 or 79 or 420 or 421 or 546 => quests.Evaluate(condition),
                    63 => Activity.Attacked ? 1 : 0,
                    91 => Activity.Alerted ? 1 : 0,
                    101 => Activity.WeaponDrawn ? 1 : 0,
                    182 => body.Appearance.EquippedArmor.Contains(condition.FormArgument1) ? 1 : 0,
                    247 => 0, // This furniture activation has no acquired/used item.
                    392 => firstPerson ? 1 : 0,
                    _ => throw new NotSupportedException($"Player furniture IDLE {condition.Owner.FormKey} condition {condition.Function} is unbound."),
                };
                var idle = FalloutActorIdleSource.Resolve(records, tree.Select(Evaluate));
                if (idle.Objects.Count != 0) throw new NotSupportedException("Player furniture ANIO has no animation object owner.");
                var nif = Read(idle.AnimationPath);
                var sequence = nif.Roots.Select(nif.ReadObject).OfType<FalloutNifControllerSequence>().Single();
                if (!float.IsFinite(sequence.Frequency) || sequence.Frequency <= 0 || sequence.StopTime <= sequence.StartTime ||
                    sequence.CycleType != (state == 1 ? 0 : 2)) throw new NotSupportedException("Player furniture source clock is unsupported.");
                var root = sequence.ControlledBlocks.SingleOrDefault(link => link.NodeName == sequence.TargetName && link.ControllerType == "NiTransformController");
                Vector3 start = default, end = default;
                Action<FalloutNifAnimationSample>? accumulation = null;
                if (state != 1)
                {
                    if (root is null) throw new NotSupportedException("Player furniture transition has no accumulation channel.");
                    var sampler = new FalloutNifAnimationSampler(nif, root.Interpolator);
                    start = Translation(sampler.Sample(sequence.StartTime)); end = Translation(sampler.Sample(sequence.StopTime));
                    accumulation = sample => GlobalTransform = _furnitureMotion!.Sample(Translation(sample));
                }
                else if (root is not null) throw new NotSupportedException("Player furniture loop accumulation is unbound.");
                var animation = new RuntimeNativeNifAnimation(nif, sequence, body.Skeleton, accumulationRoot: accumulation);
                FalloutNifAnimatedNodePath? camera = null;
                if (state == 1)
                {
                    firstPerson = true;
                    var cameraIdle = FalloutActorIdleSource.Resolve(records, tree.Select(Evaluate));
                    if (cameraIdle.Form != idle.Form)
                        camera = new(Read("meshes/characters/_1stperson/skeleton.nif"), Read(cameraIdle.AnimationPath), "Camera1st");
                }
                _furnitureClips.Add(state, new(sequence, animation, idle.AnimationPath, start, end, camera));
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
            // Resource/clock preparation precedes publication of the interaction.
            _furnitureSeat = seat; _furnitureReference = reference.FormKey; _furniturePhase = 1;
            _furnitureLookYaw = 0;
            body.Visible = false;
            body.SetProcess(false);
            AddChild(body);
            GD.Print($"OPENNV_NATIVE_PLAYER_FURNITURE_BEGIN reference={reference.FormKey} marker={seat.Index} source=owned-furn-idle-kf camera=first-person-only parity=unverified");
        }
        catch
        {
            _furnitureBody = null; _furnitureClips.Clear(); _furnitureSeat = null; _furnitureReference = null;
            _furniturePhase = 0; _furnitureMotion = null; body.Free();
            throw;
        }

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
        _furnitureClip = _furnitureClips[phase == 3 ? 1 : phase];
        _furnitureMotion = phase == 2 ? NativeFurnitureRootMotion.Enter(_occupied, _furnitureSeat!.HeadingDelta, _furnitureClip.End) :
            phase == 4 ? NativeFurnitureRootMotion.Exit(_occupied, _furnitureSeat!.HeadingDelta, _furnitureClip.Start) : null;
        if (phase == 3) GlobalTransform = _occupied;
        _furnitureBody!.Skeleton.Node.SetBonePose(_furnitureBody.Skeleton.BoneIndex(_furnitureClip.Sequence.TargetName), Transform3D.Identity);
        _furnitureClip.Animation.ApplySourceTime(_furnitureClip.Sequence.StartTime);
        _furnitureBody.Visible = true;
        // The head is still a real player part, culled from its own first-person camera.
        foreach (var part in _furnitureBody.Parts.Where(part => part.Root.GetMeta("opennv_source_part").AsString() is
            "head" or "hair" or "ears" or "mouth" or "teeth-lower" or "teeth-upper" or "tongue" or "eye-left" or "eye-right"))
            part.Root.Visible = false;
        Velocity = Vector3.Zero;
        PublishFurnitureCamera();
    }

    private void AdvanceFurniture(double delta)
    {
        if (_furnitureError is not null) return;
        try
        {
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
                if (MoveAndCollide(motion) is { } collision)
                {
                    // NAVM points lie on the authored floor. Let the capsule
                    // retain its collision margin while following that surface.
                    if (collision.GetNormal().Dot(UpDirection) >= MathF.Cos(FloorMaxAngle))
                        collision = MoveAndCollide(collision.GetRemainder().Slide(collision.GetNormal()));
                    if (collision is not null)
                        throw new NotSupportedException($"Player furniture NAVM approach was obstructed by {((Node)collision.GetCollider()).GetPath()} " +
                            $"at {collision.GetPosition()} normal={collision.GetNormal()} motion={motion} target={target}; dynamic avoidance is unbound.");
                }
                return;
            }
            while (delta > 0)
            {
                var clip = _furnitureClip!;
                var duration = (double)(clip.Sequence.StopTime - clip.Sequence.StartTime) / clip.Sequence.Frequency;
                var consumed = Math.Min(delta, Math.Max(0, duration - _furnitureSeconds));
                _furnitureSeconds += consumed; delta -= consumed;
                _furniturePhaseSeconds += consumed;
                clip.Animation.ApplySourceTime(MathF.Min(clip.Sequence.StopTime, clip.Sequence.StartTime + (float)(_furnitureSeconds * clip.Sequence.Frequency)));
                PublishFurnitureCamera();
                if (_furnitureSeconds < duration) return;
                if (_furniturePhase == 4) { ReleaseFurniture(); return; }
                if (_furniturePhase == 2) StartFurniturePhase(3);
                else _furnitureSeconds = 0;
            }
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
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
        var head = skeleton.Node.Transform * skeleton.Node.GetBoneGlobalPose(skeleton.BoneIndex("Bip01 Head"));
        ApplySourceCamera(new(new Basis(Vector3.Up, _furnitureLookYaw), head.Origin));
    }

    private void ReleaseFurniture()
    {
        GlobalBasis = _occupied.Basis;
        _furnitureBody!.QueueFree(); _furnitureBody = null; _furnitureClip = null;
        _furnitureClips.Clear(); _furnitureSeat = null; _furnitureReference = null; _furniturePhase = 0;
        ReleaseSourceCamera();
    }
}
