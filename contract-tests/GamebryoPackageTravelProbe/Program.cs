using Godot;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using System.Text.Json;

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
var resumed = GamebryoPackageTravel.Restore(travel.CaptureState(), target);
if (resumed.Advance(0.5) || !resumed.Transform.Origin.IsEqualApprox(
        new Vector3(5.0f, 0.0f, 0.0f)))
    throw new InvalidOperationException("Restored source package travel step differs.");
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
var savedFacing = new Transform3D(
    new Basis(Vector3.Up, -0.5f),
    targetTransform.Origin);
var settled = GamebryoPackageTravel.RestoreSettledAtSourceTarget(
    "00000030",
    target,
    savedFacing,
    GamebryoPackageTravel.ExactArrivalToleranceCellUnits);
if (!settled.Arrived || !settled.Transform.IsEqualApprox(savedFacing))
    throw new InvalidOperationException("Saved source package rest transform differs.");
var resumedSettled = GamebryoPackageTravel.Restore(settled.CaptureState(), target);
if (!resumedSettled.Arrived || !resumedSettled.Transform.IsEqualApprox(savedFacing))
    throw new InvalidOperationException("Restored source package rest transform differs.");
var facingBasis = GamebryoActorFacing.ModelFrontBasis(Vector3.Right, Vector3.Up);
if (facingBasis.Z.Normalized().Dot(Vector3.Right) < 0.999f)
    throw new InvalidOperationException(
        "Source actor model front faces backward along package travel.");
using var savedPackageDocument = JsonDocument.Parse(
    """
    {
      "PackageFormId": "00000030",
      "AnimationLogicalPath": "meshes\\characters\\_male\\idle.kf",
      "AnimationPositionSeconds": 1.25,
      "Arrived": true
    }
    """);
var savedPackage = OpeningGuidePackageState.Parse(savedPackageDocument.RootElement);
savedPackage?.Validate();
if (savedPackage is not { AnimationPositionSeconds: 1.25, Arrived: true })
    throw new InvalidOperationException("Saved source package rest phase differs.");

if (!Rejects(() => GamebryoPackageTravel.Start(
        "00000010", target, Transform3D.Identity, [], 0.0f, 0.0f)) ||
    !Rejects(() => GamebryoPackageTravel.Start(
        "00000010", target, Transform3D.Identity, [], 1.0f, -1.0f)) ||
    !Rejects(() => GamebryoPackageTravel.Restore(
        resumed.CaptureState() with { TargetFormId = "00000021" },
        target)) ||
    !Rejects(() => GamebryoPackageTravel.RestoreSettledAtSourceTarget(
        "00000030",
        target,
        new Transform3D(savedFacing.Basis, Vector3.Zero),
        GamebryoPackageTravel.ExactArrivalToleranceCellUnits)) ||
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
