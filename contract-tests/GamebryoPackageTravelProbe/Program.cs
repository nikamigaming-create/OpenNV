using Godot;
using OpenNV.Runtime.World.Actors;

var targetTransform = new Transform3D(
    new Basis(Vector3.Up, 1.25f),
    new Vector3(10.0f, 2.0f, 0.0f));
var target = new SourcePackagePlacement("nearReference", "00000020", targetTransform);
var travel = GamebryoPackageTravel.Start(
    "00000010",
    target,
    Transform3D.Identity,
    [new Vector3(5.0f, 0.0f, 0.0f)],
    5.0f,
    GamebryoPackageTravel.ExactArrivalToleranceCellUnits);
if (travel.Advance(0.5) || !travel.Transform.Origin.IsEqualApprox(
        new Vector3(2.5f, 0.0f, 0.0f)))
    throw new InvalidOperationException("Source package travel first step differs.");
if (travel.Advance(0.5) || !travel.Transform.Origin.IsEqualApprox(
        new Vector3(5.0f, 0.0f, 0.0f)))
    throw new InvalidOperationException("Source package travel waypoint differs.");
if (!travel.Advance(2.0) || !travel.Arrived ||
    !travel.Transform.IsEqualApprox(targetTransform))
    throw new InvalidOperationException("Source package arrival transform differs.");
var state = travel.CaptureState();
if (!state.Arrived || state.PackageFormId != "00000010" ||
    state.TargetFormId != target.TargetFormId)
    throw new InvalidOperationException("Source package travel state was not persisted.");

var moveTo = GamebryoPackageTravel.ArriveAtSourceTarget(
    "00000030",
    target,
    Transform3D.Identity,
    GamebryoPackageTravel.ExactArrivalToleranceCellUnits);
if (!moveTo.Arrived || !moveTo.Transform.IsEqualApprox(targetTransform))
    throw new InvalidOperationException("Source MoveTo package arrival differs.");

if (!Rejects(() => GamebryoPackageTravel.Start(
        "00000010", target, Transform3D.Identity, [], 0.0f, 0.0f)) ||
    !Rejects(() => GamebryoPackageTravel.Start(
        "00000010", target, Transform3D.Identity, [], 1.0f, -1.0f)) ||
    !Rejects(() => travel.Advance(double.NaN)))
    throw new InvalidOperationException("Invalid package travel did not fail closed.");

Console.WriteLine("Gamebryo package travel probe passed.");

static bool Rejects(Action action)
{
    try
    {
        action();
        return false;
    }
    catch (InvalidOperationException)
    {
        return true;
    }
}
