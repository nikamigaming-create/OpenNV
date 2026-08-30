using Godot;
using OpenNV.Runtime.Campaigns.Fallout3;

if (args.Length != 1)
    throw new InvalidOperationException(
        "Expected one synthetic Fallout 3 stage-10 contract fixture path.");

var fixture = Path.GetFullPath(args[0]);
var productionRejected = false;
try
{
    Fo3Cg00RetailStage10Contract.Load(fixture);
}
catch (InvalidOperationException)
{
    productionRejected = true;
}
if (!productionRejected)
    throw new InvalidOperationException(
        "Production Fallout 3 stage-10 loader accepted synthetic authority.");

var contract = Fo3Cg00RetailStage10Contract.LoadSyntheticFixture(fixture);
if (contract.Participants.Count != 4 ||
    contract.PackageIdleJoins.Count != 4 ||
    contract.Participants["doctor"].ReferenceFormId != "000290a5" ||
    contract.Participants["mother"].Section01Sequence.LastScaledTimeSeconds != 1.0f)
    throw new InvalidOperationException(
        "Synthetic Fallout 3 stage-10 identity did not survive the strict parser.");

var clear = Fo3Cg00RetailStage10Join.MeasureNearPlane(
    [new Vector3(-1.0f, -1.0f, -2.0f), new Vector3(1.0f, 1.0f, -3.0f)],
    Transform3D.Identity,
    nearPlaneMeters: 1.0f);
if (!clear.FullMeshClearsNearPlane || clear.VerticesAtOrBehindNearPlane != 0 ||
    MathF.Abs(clear.MinimumNearPlaneSeparationMeters - 1.0f) > 1.0e-6f)
    throw new InvalidOperationException(
        "Fallout 3 stage-10 full posed-mesh clear telemetry differs.");

var crossing = Fo3Cg00RetailStage10Join.MeasureNearPlane(
    [new Vector3(0.0f, 0.0f, -0.5f), new Vector3(0.0f, 0.0f, -2.0f)],
    Transform3D.Identity,
    nearPlaneMeters: 1.0f);
if (crossing.FullMeshClearsNearPlane || crossing.VerticesAtOrBehindNearPlane != 1 ||
    MathF.Abs(crossing.MinimumNearPlaneSeparationMeters + 0.5f) > 1.0e-6f)
    throw new InvalidOperationException(
        "Fallout 3 stage-10 full posed-mesh near crossing was not rejected.");

Console.WriteLine(
    $"FO3_CG00_STAGE10_CONTRACT_PROBE_PASS contract={contract.ContractSha256} " +
    $"roles={contract.Participants.Count} clearVertices=2 crossingVertices=2");
