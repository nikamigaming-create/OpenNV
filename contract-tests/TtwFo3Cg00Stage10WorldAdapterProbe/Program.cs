using Godot;
using OpenNV.Runtime.Campaigns.TTW;

if (args.Length != 2)
    throw new InvalidOperationException(
        "Expected synthetic TTW and standalone Fallout 3 stage-10 fixtures.");

var ttwFixture = Path.GetFullPath(args[0]);
var standaloneFixture = Path.GetFullPath(args[1]);
var productionRejectedSynthetic = Rejects(() =>
    TtwFo3Cg00RetailStage10Contract.Load(ttwFixture));
var ttwRejectedStandalone = Rejects(() =>
    TtwFo3Cg00RetailStage10Contract.LoadSyntheticFixture(standaloneFixture));
if (!productionRejectedSynthetic || !ttwRejectedStandalone)
    throw new InvalidOperationException(
        "TTW world adapter evidence boundary accepted synthetic production or standalone FO3.");

var contract = TtwFo3Cg00RetailStage10Contract.LoadSyntheticFixture(ttwFixture);
const string projectionSha =
    "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
const string artifactSha =
    "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
var projectionPath = Path.GetFullPath("D:\\synthetic\\ttw-fo3-projection.json");
var origin = new Vector3(10.0f, 2.0f, 3.0f);
var participants = contract.Participants.ToDictionary(
    pair => pair.Key,
    pair => new TtwFo3Cg00Stage10MaterializedParticipantIdentity(
        pair.Key,
        pair.Value.FormKey,
        pair.Value.RuntimeFormId,
        pair.Value.PackageFormKey,
        pair.Value.PackageRuntimeFormId,
        pair.Value.IdleFormKey,
        pair.Value.IdleRuntimeFormId,
        pair.Value.SequenceName,
        $"meshes\\characters\\_male\\idleanims\\{pair.Key}.kf",
        artifactSha,
        Path.GetFullPath($"D:\\synthetic\\actors\\{pair.Key}.json"),
        artifactSha),
    StringComparer.Ordinal);
var identity = new TtwFo3Cg00Stage10MaterializedSceneIdentity(
    TtwFo3Cg00Stage10WorldAdapter.MaterializedSourceAuthority,
    contract.SourceProfilePath,
    contract.SourceProfileSha256,
    contract.SourceNamespacePath,
    contract.SourceNamespaceSha256,
    contract.OpeningProfilePath,
    contract.OpeningProfileSha256,
    projectionPath,
    projectionSha,
    contract.PluginStackId,
    contract.SaveCompatibilityId,
    "Fallout3.esm:028138",
    "06028138",
    origin,
    Path.GetFullPath("D:\\synthetic\\ttw-birth-presentation.json"),
    artifactSha,
    EffectiveMemberClosureMaterialized: true,
    StandaloneFallout3ArtifactsAccepted: false,
    StandaloneNewVegasArtifactsAccepted: false,
    participants);

var plan = TtwFo3Cg00Stage10WorldAdapter.BuildSyntheticPlanForTests(
    contract,
    projectionPath,
    projectionSha,
    identity);
if (plan.InteractiveLaunchReady ||
    plan.InteractiveLaunchBlocker !=
        TtwFo3Cg00Stage10WorldAdapter.InteractiveLaunchBlocker ||
    plan.Participants.Count != 4 ||
    plan.Participants["father"].PackageRuntimeFormId != "0006b245" ||
    plan.Participants["doctor"].IdleRuntimeFormId != "00068ab1" ||
    plan.Participants["mother"].SequenceCycleType != 2 ||
    MathF.Abs(plan.Participants["mother"].SequencePhaseSeconds - 4.0f) > 1.0e-6f)
    throw new InvalidOperationException(
        "TTW stage-10 adapter lost participant identity or launcher fail-closed state.");

// Source father root [30,1,0] minus the TTW scene origin [10,2,3] is
// [20,-1,-3] Gamebryo, which maps globally to [20,-3,1] Godot game units.
var expectedFather = new Vector3(20.0f, -3.0f, 1.0f);
if (!plan.Participants["father"].LocalRenderedTransform.Origin.IsEqualApprox(
        expectedFather) ||
    !plan.LocalCameraTransform.Origin.IsEqualApprox(new Vector3(-10.0f, -3.0f, 2.0f)) ||
    !plan.LocalCamera1stTransform.Origin.IsEqualApprox(
        new Vector3(-10.0f, -3.0f, 2.0f)))
    throw new InvalidOperationException(
        "TTW stage-10 observed world-to-cell transform join differs.");

var standaloneIdentity = identity with { StandaloneFallout3ArtifactsAccepted = true };
if (!Rejects(() => TtwFo3Cg00Stage10WorldAdapter.BuildSyntheticPlanForTests(
        contract,
        projectionPath,
        projectionSha,
        standaloneIdentity)))
    throw new InvalidOperationException(
        "TTW stage-10 adapter accepted standalone Fallout 3 artifacts.");

var missingMother = participants
    .Where(pair => pair.Key != "mother")
    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
if (!Rejects(() => TtwFo3Cg00Stage10WorldAdapter.BuildSyntheticPlanForTests(
        contract,
        projectionPath,
        projectionSha,
        identity with { Participants = missingMother })))
    throw new InvalidOperationException(
        "TTW stage-10 adapter synthesized an absent participant.");

var wrongDoctor = participants.ToDictionary(pair => pair.Key, pair => pair.Value,
    StringComparer.Ordinal);
wrongDoctor["doctor"] = wrongDoctor["doctor"] with
{
    RuntimeFormId = contract.Participants["father"].RuntimeFormId,
};
if (!Rejects(() => TtwFo3Cg00Stage10WorldAdapter.BuildSyntheticPlanForTests(
        contract,
        projectionPath,
        projectionSha,
        identity with { Participants = wrongDoctor })))
    throw new InvalidOperationException(
        "TTW stage-10 adapter accepted a mismatched runtime FormID.");

var offCenterCamera = contract with
{
    ActiveCamera = contract.ActiveCamera with { LeftGameUnits = -4.0 },
};
if (!Rejects(() => TtwFo3Cg00Stage10WorldAdapter.BuildSyntheticPlanForTests(
        offCenterCamera,
        projectionPath,
        projectionSha,
        identity)))
    throw new InvalidOperationException(
        "TTW stage-10 adapter accepted an off-center projection Camera3D cannot publish.");

Console.WriteLine(
    $"TTW_FO3_CG00_STAGE10_WORLD_ADAPTER_PROBE_PASS " +
    $"contract={plan.ContractSha256} roles={plan.Participants.Count} " +
    $"fatherLocal={plan.Participants["father"].LocalRenderedTransform.Origin} " +
    "launchReady=0 standaloneRejected=1 missingParticipantRejected=1 " +
    "offCenterProjectionRejected=1");

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
