using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

// An actual resident actor's source door leg. The shared door/XTEL owner still
// performs activation; this owner supplies approach, retirement and commit state.
internal sealed class NativeActorPackagePortal(CharacterBody3D body, RuntimeNativeActorCombat process,
    FalloutPluginStack records, FalloutReferenceWorld world, FalloutReferenceInstance state, float unitsToMetres,
    Func<FalloutPluginRecord> package, Func<FalloutFormKey> targetCell, Func<bool> active,
    Func<bool> playbackReady, Action<string> failed)
{
    private const string Pending = "Actor package portal activation is pending its source event result.";
    internal FalloutFormKey? Door { get; private set; }
    private FalloutFormKey? _targetRegion;
    private (Vector3 Authored, Vector3 Floor)? _destination;
    internal bool Requested { get; private set; }
    internal bool Transferred { get; private set; }
    internal string? Wait { get; private set; }

    internal bool Wants(FalloutFormKey door) => !Transferred && active() && Door == door && !process.OwnsPose;

    internal bool Advance(FalloutFormKey cell, bool running, bool sneaking, double delta, FalloutTravelProgress? travel = null)
    {
        if (Transferred || Requested) return true;
        if (!active()) throw new NotSupportedException("Actor portal approach has no accepted active package.");
        Wait = null;
        var region = world.TravelRegion(cell);
        if (world.TravelRegion(world.Placement(state.Reference).Cell) == region)
        { Door = null; _destination = null; _targetRegion = null; Wait = null; return false; }
        if (Door is null || _targetRegion != region || !world.CanUseTravelPortal(state.Reference, Door.Value))
        {
            var selected = world.TravelPortal(state.Reference, cell) ??
                throw new NotSupportedException("Actor package target has no admitted source portal route.");
            _targetRegion = region;
            if (Door != selected) _destination = null;
            Door = selected;
        }
        var door = Door ?? throw new InvalidOperationException("Actor portal route did not retain its source door.");
        var location = world.Placement(door).Position;
        var authored = body.GetParent<Node3D>().ToGlobal(new Vector3(location[0], location[2], -location[1]) * unitsToMetres);
        if (_destination is not { } previous || !previous.Authored.IsEqualApprox(authored))
            _destination = (authored, process.ProjectPackagePortalDestination(authored, door, world.TravelNavigation(world.Placement(door).Cell)));
        var destination = _destination.Value.Floor;
        var distance = process.PackageActivationRange * .75f;
        process.AdvancePackageMotion(package(), destination, distance, running, delta, requireArrivalHeight: true, sneaking: sneaking);
        if (!active()) throw new NotSupportedException("Actor portal package changed while publishing its motion.");
        if (travel is not null) process.SetTravelProgress(travel);
        state.ProcedureCaptureBlocker = null;
        if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(destination) > distance + .1f &&
            !process.PackagePortalContactInReach(door)) return true;
        if (!playbackReady()) { Wait = "source-playback-awaiting-ordinary-retirement"; return true; }
        Wait = null;
        Requested = process.RequestPackagePortal(door);
        if (Requested) state.ProcedureCaptureBlocker = Pending;
        return true;
    }

    internal void Commit(FalloutFormKey door)
    {
        if (!Wants(door) || !Requested || !body.IsOnFloor() || !playbackReady() || state.ProcedureCaptureBlocker != Pending)
            throw new NotSupportedException("Actor package portal has no ready native approach and retirement owner.");
        var source = package();
        var type = FalloutPackageData.Read(source).Procedure;
        if (type is not (1 or 6 or 7)) throw new InvalidDataException("Portal source has no admitted actor procedure.");
        if (!world.CanUseTravelPortal(state.Reference, door) || world.TravelPortal(state.Reference, targetCell()) != door)
            throw new NotSupportedException("Actor package door no longer matches its current winning target route.");
        var placement = world.TravelPortalArrival(door, out var destinationDoor);
        state.ProcedureCaptureBlocker = null;
        FalloutReferenceSnapshot captured;
        try { captured = state.Capture(); }
        finally { state.ProcedureCaptureBlocker = Pending; }
        if (captured.PackageAssignment is not { Done: false } assignment || assignment.Package != source.FormKey ||
            captured.PackageMotion is not { } motion || motion.Package != source.FormKey || captured.Engagement is not null ||
            captured.HitReaction is not null || captured.PackageIdle is not null || captured.FurnitureContinuation is not null ||
            captured.DialogueContinuation is not null || captured.ObjectAnimations is not null)
            throw new NotSupportedException("Actor package portal cannot retire an independent actor continuation.");
        FalloutTravelProgress? travel = null;
        FalloutAccompanyPortalArrival? receipt = null;
        if (type == 6)
        {
            if (motion.Travel is not { Complete: false } progress || motion.AccompanyPortal is not null)
                throw new InvalidDataException("Travel portal lacks its unfinished procedure continuation.");
            FalloutTravelPackage.Read(source).Validate(records, world, state.Reference, progress);
            travel = progress with { PortalArrival = new(door, destinationDoor) };
            travel.Validate();
        }
        else
        {
            _ = type == 7 ? FalloutFollowPackage.ReadAccompany(source) : FalloutFollowPackage.Read(source);
            if (motion.Travel is not null) throw new InvalidDataException("Follower portal retained another Travel procedure.");
            receipt = new(door, destinationDoor); receipt.Validate();
        }
        var position = GamebryoCoordinate.ConvertVector(new(placement.Position[0], placement.Position[1], placement.Position[2])) * unitsToMetres;
        var rotation = GamebryoCoordinate.ConvertReferenceEuler(new(placement.RotationRadians[0], placement.RotationRadians[1],
            placement.RotationRadians[2]), 1).GetRotationQuaternion();
        world.SetPlacement(state.Reference, placement);
        state.PackageMotion = motion with
        {
            Position = [position.X, position.Y, position.Z],
            Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W],
            AccompanyPortal = receipt,
            Travel = travel
        };
        state.PackageAssignment = assignment;
        state.ProcedureCaptureBlocker = null;
        Transferred = true;
        process.StopPackageMotion();
    }

    internal void Finish(FalloutFormKey door, string? error)
    {
        if (Transferred || !Requested || Door != door) return;
        failed(error ?? "Source door did not commit its pending actor package transition.");
    }

    internal void Retire()
    {
        if (Requested && state.ProcedureCaptureBlocker == Pending) state.ProcedureCaptureBlocker = null;
    }
}
