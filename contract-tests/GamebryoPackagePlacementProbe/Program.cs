using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

var furniture = GamebryoPackagePlacement.FromFurnitureMarker(
    "00000010",
    Transform3D.Identity,
    new Vector3(2.0f, 3.0f, 4.0f),
    Quaternion.Identity,
    new Vector3(0.5f, 1.0f, 1.5f),
    new Quaternion(Vector3.Up, Mathf.Pi),
    Vector3.One);
var raceData = new byte[36];
System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(raceData.AsSpan(32), 0x105);
if (FalloutRaceProperties.ReadFlags(raceData) != 0x105)
    throw new InvalidOperationException("Race flags lost child/playable or unrelated source bits.");
var invalidRaceRejected = false;
try { FalloutRaceProperties.ReadFlags(raceData.AsSpan(0, 35)); }
catch (InvalidDataException) { invalidRaceRejected = true; }
if (!invalidRaceRejected) throw new InvalidOperationException("A truncated race layout was admitted.");
if (!furniture.SourceTransform.Origin.IsEqualApprox(new Vector3(1.5f, 3.0f, 2.5f)))
    throw new InvalidOperationException("Furniture marker root composition differs.");

var grounded = GamebryoPackagePlacement.AdjustSupportHeight(
    furniture.SourceTransform,
    0.75f);
var rootStart = new Vector3(0.02f, -0.04f, 0.3f);
var rootEnd = new Vector3(-0.03f, 0.09f, -0.6f);
var occupied = new Transform3D(new Basis(Vector3.Up, 0.7f).Scaled(Vector3.One * 1.2f), new(7, 3, -5));
var entryMotion = NativeFurnitureRootMotion.Enter(occupied, 1.1f, rootEnd);
var exitMotion = NativeFurnitureRootMotion.Exit(occupied, 1.1f, rootEnd);
if (entryMotion.Sample(rootStart).Origin.DistanceTo(occupied.Origin) < 0.5f ||
    !entryMotion.Sample(rootEnd).Origin.IsEqualApprox(occupied.Origin) ||
    !exitMotion.Sample(rootEnd).Origin.IsEqualApprox(occupied.Origin) ||
    !exitMotion.Sample(rootStart).IsEqualApprox(entryMotion.Sample(rootStart)))
    throw new InvalidOperationException("Furniture root curve lost its occupied endpoint or reversible approach frame.");
var translated = NativeFurnitureRootMotion.Enter(new(occupied.Basis, occupied.Origin + new Vector3(13, -9, 4)), 1.1f, rootEnd);
if (!(translated.Sample(rootStart).Origin - entryMotion.Sample(rootStart).Origin).IsEqualApprox(new Vector3(13, -9, 4)))
    throw new InvalidOperationException("Furniture accumulation depends on a fitted world location.");
var invalidRootRejected = false;
try { entryMotion.Sample(new Vector3(float.NaN, 0, 0)); }
catch (InvalidDataException) { invalidRootRejected = true; }
if (!invalidRootRejected) throw new InvalidOperationException("Non-finite furniture accumulation was admitted.");
GamebryoPackagePlacement.RequireSupportHeightOnly(
    furniture.SourceTransform,
    grounded);
if (!Mathf.IsEqualApprox(grounded.Origin.Y, 3.75f) ||
    !Mathf.IsEqualApprox(grounded.Origin.X, furniture.SourceTransform.Origin.X) ||
    !Mathf.IsEqualApprox(grounded.Origin.Z, furniture.SourceTransform.Origin.Z) ||
    !grounded.Basis.IsEqualApprox(furniture.SourceTransform.Basis))
    throw new InvalidOperationException("Support-height-only placement differs.");

var marker = GamebryoPackagePlacement.FromPlanarGameReferenceMarker(
    "00000020",
    new Vector3(12.0f, 24.0f, 36.0f),
    new Vector3(0.0f, 0.0f, Mathf.Pi / 2.0f),
    1.0f,
    new Vector3(2.0f, 4.0f, 6.0f));
if (!marker.SourceTransform.Origin.IsEqualApprox(new Vector3(10.0f, 30.0f, -20.0f)))
    throw new InvalidOperationException("Reference marker coordinate conversion differs.");

var transfer = GamebryoPackagePlacement.CalculateRootTransfer(
    furniture.SourceTransform,
    new Vector3(0.0f, 0.0f, 2.0f));
if (!transfer.After.Basis.IsEqualApprox(transfer.Before.Basis) ||
    !transfer.After.Origin.IsEqualApprox(
        transfer.Before.Origin + transfer.AppliedDisplacement))
    throw new InvalidOperationException("Furniture exit root transfer differs.");

var loop = new SourceActorAnimation(
    "meshes\\characters\\loop.kf",
    new string('a', 64),
    "Loop",
    0.0f,
    2.0f,
    ActorAnimationPlayback.LoopCycleType,
    "owned-world-root-authoritative-zero-local-translation");
var exit = loop with
{
    LogicalPath = "meshes\\characters\\exit.kf",
    Sha256 = new string('b', 64),
    SequenceName = "Exit",
    CycleType = ActorAnimationPlayback.ClampCycleType,
    AccumulationRootTranslationDisposition =
        "preserve-hash-bound-owned-clip-root-curve",
};
var furnitureSource = new SourceGamebryoFurniture(
    "00000010",
    14,
    Transform3D.Identity,
    new Vector3(2.0f, 3.0f, 4.0f),
    Quaternion.Identity,
    new Vector3(0.5f, 1.0f, 1.5f),
    new Quaternion(Vector3.Up, Mathf.Pi),
    Vector3.One,
    loop,
    null,
    exit,
    new Vector3(0.0f, 0.0f, 2.0f));
var sessionPlacement = GamebryoFurnitureSession.PlacementFromSource(furnitureSource);
if (!sessionPlacement.SourceTransform.IsEqualApprox(furniture.SourceTransform) ||
    sessionPlacement.TargetFormId != furniture.TargetFormId)
    throw new InvalidOperationException("Furniture session placement differs.");

Console.WriteLine(
    "GAMEBRYO_PACKAGE_PLACEMENT_PROBE_PASS furniture=1 marker=1 supportHeightOnly=1 " +
    "root=1 session=1");
