using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutPlayerPhysicalAnimation? _furniturePhysicalClock;
    private Transform3D _furniturePlacement;
    private FalloutReferencePlacement? _furnitureSourcePlacement;
    private FalloutFormKey _furnitureSourceCell;
    private string? _furnitureReferenceHash, _furnitureBaseHash, _furnitureModel, _furnitureModelHash;
    private FalloutFurnitureClipSnapshot? _furnitureCameraSource;
    private string? _furnitureCameraSkeletonHash;
    private bool _bedPublicationAttempted;
    private static string PhysicalRecordHash(FalloutPluginRecord source) => Convert.ToHexString(SHA256.HashData(source.ReadData()));

    private void DispatchFurnitureKeys(double next)
    {
        var clock = _furniturePhysicalClock!;
        foreach (var key in _furnitureClip!.Source.Events.Crossed(clock.Seconds, next, clock.StartPending))
        {
            _furniturePhysicalClock = clock with { AttemptedKeyOrdinal = key.SourceOrdinal, AttemptedKeyCycle = key.Cycle };
            DispatchPhysicalKey(key);
        }
        _furniturePhysicalClock = clock with { StartPending = false, AttemptedKeyOrdinal = null, AttemptedKeyCycle = null };
    }

    private FalloutPlayerFurnitureSnapshot? CapturePlayerFurniture()
    {
        if (_furniturePhase == 0) return null;
        if (_furnitureWorld?.OwnsFurnitureSeat(_furnitureReference!.Value, _furnitureSeat!.Index,
            _furnitureReservationActor!.Value) != true)
            throw new InvalidOperationException("Player furniture has no actual retained source reservation.");
        var result = new FalloutPlayerFurnitureSnapshot(_furnitureReference!.Value, _furnitureReferenceHash!,
            _furnitureSourceCell, _furnitureBaseHash!, _furnitureModel!, _furnitureModelHash!, _furnitureSeat!,
            _furnitureSeat!.Kind, (FalloutPlayerFurniturePhase)_furniturePhase,
            PhysicalPose(_occupied), PhysicalPose(_approach), PhysicalPose(GlobalTransform),
            _furniturePath?.Select(point => new[] { point.X, point.Y, point.Z }).ToArray(), _furnitureWaypoint,
            _furniturePhysicalClock?.Seconds ?? 0, _furnitureLookYaw, _furniturePhysicalClock,
            _furnitureClips.ToDictionary(pair => pair.Key, pair => pair.Value.Identity), PhysicalPose(_furniturePlacement),
            _furnitureSourcePlacement!, _furnitureCameraSource, _furnitureCameraSkeletonHash, _bedPublicationAttempted);
        result.Validate(); return result;
    }

    private void RestorePlayerFurniture(FalloutPlayerFurnitureSnapshot saved)
    {
        saved.Validate(); var records = _physicalRecords!; var world = _physicalWorld!;
        if (saved.Kind == FalloutPlayerFurnitureKind.Sleeping)
            throw new NotSupportedException("Cold player bed requires its admitted original player activation and sleep/wait consumer.");
        var original = records.GetEffective(saved.Reference); var instance = world.Get(saved.Reference);
        var furniture = records.GetEffective(instance.Base); var placement = world.Placement(saved.Reference);
        if (original.Signature != "REFR" || furniture.Signature != "FURN" || instance.Base != saved.Seat.Furniture ||
            !world.IsEnabled(saved.Reference) || PhysicalRecordHash(original) != saved.ReferenceSha256 ||
            PhysicalRecordHash(furniture) != saved.FurnitureSha256 || placement.Cell != saved.ReferencePlacement.Cell ||
            !placement.Position.SequenceEqual(saved.ReferencePlacement.Position) ||
            !placement.RotationRadians.SequenceEqual(saved.ReferencePlacement.RotationRadians))
            throw new InvalidDataException("Cold player furniture differs from its enabled source/reference continuation.");
        var nativePlacement = (_physicalFurniturePlacement ?? throw new NotSupportedException(
            "Cold player furniture requires its actual published native reference placement."))(saved.Reference);
        if (!PhysicalPose(nativePlacement).SequenceEqual(saved.Placement))
            throw new InvalidDataException("Cold player furniture native placement differs from the authoritative saved instance.");
        var model = "meshes/" + FalloutDialogueTopic.Text(furniture.ReadSubrecords().Single(field => field.Signature == "MODL").Data.Span).Replace('\\', '/');
        var content = RuntimeLiveContentSource.Current ?? throw new NotSupportedException("Cold physical source is absent.");
        FalloutNifFile Read(string resource) => content.TryRead(resource, null, out var bytes, out _)
            ? FalloutNifFile.Read(bytes) : throw new FileNotFoundException("Cold physical source resource is absent.", resource);
        var nif = Read(model); var seat = FalloutFurnitureSource.ReadPlayer(records, furniture, nif);
        if (model != saved.Model || !nif.Sha256.Equals(saved.ModelSha256, StringComparison.OrdinalIgnoreCase) ||
            seat.Furniture != saved.Seat.Furniture || seat.Index != saved.Seat.Index || seat.MarkerId != saved.Seat.MarkerId ||
            seat.Kind != saved.Kind || seat.Marker != saved.Seat.Marker || seat.HeadingDelta != saved.Seat.HeadingDelta ||
            !seat.PlacementOffset.SequenceEqual(saved.Seat.PlacementOffset))
            throw new InvalidDataException("Cold player furniture marker/model/placement declaration changed.");
        _furnitureBody = _thirdPerson!.Actor;
        foreach (var (phase, identity) in saved.Clips)
        {
            var source = records.GetEffective(identity.Idle);
            var clip = ReadPlayerPhysicalClip(source, phase == 1, _furnitureBody.Skeleton,
                phase == 1 ? null : sample => GlobalTransform = _furnitureMotion!.Sample(Translation(sample)));
            if (clip.Identity != identity || phase != 1 && clip.Root is null)
                throw new InvalidDataException("Cold furniture source IDLE/KF identity or accumulation changed.");
            _furnitureClips.Add(phase, new(clip, null));
        }
        if (saved.Camera is { } camera)
        {
            var actual = _firstPerson?.Skeleton ?? throw new NotSupportedException("Cold furniture has no actual first-person skeleton.");
            var idle = records.GetEffective(camera.Idle); var source = FalloutActorIdleSource.Resolve(records, idle);
            var cameraNif = Read(source.AnimationPath);
            if (source.Objects.Count != 0 || PhysicalRecordHash(idle) != camera.IdleSha256 || source.AnimationPath != camera.Resource ||
                !cameraNif.Sha256.Equals(camera.Sha256, StringComparison.OrdinalIgnoreCase) || actual.Source.Sha256 != saved.CameraSkeletonSha256)
                throw new InvalidDataException("Cold player furniture camera source changed.");
            _furnitureClips[1] = _furnitureClips[1] with { Camera = new(actual.Source, cameraNif, "Camera1st") };
        }
        _furniturePlacement = nativePlacement; _furnitureSourcePlacement = placement; _furnitureSourceCell = saved.Cell;
        _furnitureReferenceHash = saved.ReferenceSha256; _furnitureBaseHash = saved.FurnitureSha256;
        _furnitureModel = model; _furnitureModelHash = nif.Sha256;
        _furnitureSeat = seat; _furnitureReference = saved.Reference;
        var offset = seat.Marker.Offset;
        var occupied = GamebryoPackagePlacement.FromFurnitureMarker(saved.Reference.ToString(), nativePlacement,
            GamebryoCoordinate.ConvertVector(new(offset.X, offset.Y, offset.Z)) * UnitsToMeters,
            new Quaternion(Vector3.Up, -seat.Marker.Orientation / 1000f),
            GamebryoCoordinate.ConvertVector(new(seat.PlacementOffset[0], seat.PlacementOffset[1], seat.PlacementOffset[2])) * UnitsToMeters,
            new Quaternion(Vector3.Up, -seat.HeadingDelta), Vector3.One).SourceTransform;
        var approach = NativeFurnitureRootMotion.Enter(occupied, seat.HeadingDelta, _furnitureClips[2].End).Sample(_furnitureClips[2].Start);
        if (!PhysicalPose(occupied).SequenceEqual(saved.Occupied) || !PhysicalPose(approach).SequenceEqual(saved.Approach))
            throw new InvalidDataException("Cold furniture approach/occupied frames changed.");
        if (!world.ReserveFurnitureSeat(saved.Reference, seat.Index, records.RuntimeFormKey(0x14)))
            throw new InvalidOperationException("Cold player furniture reservation belongs to another actual actor.");
        _furnitureWorld = world; _furnitureReservationActor = records.RuntimeFormKey(0x14);
        _occupied = occupied; _approach = approach; GlobalTransform = PhysicalPose(saved.Pose);
        _furniturePhase = (int)saved.Phase; _furniturePhysicalClock = saved.Animation;
        _furnitureCameraSource = saved.Camera; _furnitureCameraSkeletonHash = saved.CameraSkeletonSha256;
        _bedPublicationAttempted = saved.BedPublicationAttempted; _furnitureLookYaw = saved.LookYaw;
        _furniturePath = saved.Path?.Select(point => new Vector3(point[0], point[1], point[2])).ToArray(); _furnitureWaypoint = saved.Waypoint;
        if (!_furnitureNavigation.TryGetValue(saved.Cell, out _furnitureGraph))
            _furnitureNavigation.Add(saved.Cell, _furnitureGraph = CellNavigationGraph.LoadOwned(records, saved.Cell));
        if (_furniturePhase != 1)
        {
            PublishPlayerPhysicalView();
            var phase = _furniturePhase == 3 ? 1 : _furniturePhase;
            _furnitureClip = _furnitureClips[phase]; var clock = saved.Animation!;
            if (!(_furnitureClip.Source.Loop || clock.Seconds < _furnitureClip.Source.Duration ||
                clock.Seconds == _furnitureClip.Source.Duration && PhysicalPlayer.Failure is not null) || saved.PhaseSeconds != clock.Seconds)
                throw new InvalidDataException("Cold player furniture selected clock is outside its actual source phase.");
            _furniturePhaseSeconds = clock.Seconds;
            _furnitureSeconds = _furnitureClip.Source.Loop ? clock.Seconds % _furnitureClip.Source.Duration : clock.Seconds;
            _furnitureMotion = _furniturePhase == 2 ? NativeFurnitureRootMotion.Enter(_occupied, seat.HeadingDelta, _furnitureClip.End) :
                _furniturePhase == 4 ? NativeFurnitureRootMotion.Exit(_occupied, seat.HeadingDelta, _furnitureClip.Start) : null;
            // Complete saved local components already published above. Seeking
            // is unnecessary and could replace a faulted operation's prefix.
            PublishFurnitureCamera();
        }
        // No tree selection, random roll, key dispatch or sleep-menu callback
        // occurs during cold publication of the genuine selected continuation.
    }
}
