using OpenNV.Runtime.Campaigns.TTW;

if (args.Length != 2)
    throw new InvalidOperationException(
        "Expected TTW and standalone synthetic stage-10 contract fixture paths.");

var fixture = Path.GetFullPath(args[0]);
var standaloneFixture = Path.GetFullPath(args[1]);
var productionRejected = false;
try
{
    TtwFo3Cg00RetailStage10Contract.Load(fixture);
}
catch (InvalidOperationException)
{
    productionRejected = true;
}
if (!productionRejected)
    throw new InvalidOperationException(
        "TTW production stage-10 loader accepted synthetic authority.");

var standaloneRejected = false;
try
{
    TtwFo3Cg00RetailStage10Contract.LoadSyntheticFixture(standaloneFixture);
}
catch (InvalidOperationException)
{
    standaloneRejected = true;
}
if (!standaloneRejected)
    throw new InvalidOperationException(
        "TTW stage-10 loader accepted a relabeled standalone Fallout3.exe contract.");

var contract = TtwFo3Cg00RetailStage10Contract.LoadSyntheticFixture(fixture);
if (contract.Participants.Count != 4 ||
    contract.TargetExecutableSha256 !=
        "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" ||
    contract.Participants["doctor"].FormKey != "Fallout3.esm:0290a5" ||
    contract.Participants["doctor"].RuntimeFormId != "060290a5" ||
    contract.Participants["father"].PackageFormKey != "FalloutNV.esm:06b245" ||
    contract.Participants["mother"].SequenceName != "SpecialIdle_CG00MomSection01" ||
    Math.Abs(contract.Participants["mother"].NearPlaneSeparationGameUnits - 45.0) >
        1.0e-6)
    throw new InvalidOperationException(
        "Synthetic TTW stage-10 identity did not survive the strict parser.");

Console.WriteLine(
    $"TTW_FO3_CG00_STAGE10_CONTRACT_PROBE_PASS contract={contract.ContractSha256} " +
    $"roles={contract.Participants.Count} target={contract.TargetExecutableSha256}");
