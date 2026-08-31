using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

public sealed partial class Fo2ArroyoArrivalFirstBeatProofHost : Node
{
    public override void _Ready()
    {
        try
        {
            var options = Fo2ArroyoCavesProofOptions.Parse(OS.GetCmdlineUserArgs());
            var temple = Fo2TemplePresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-temple-cache"));
            var transition = Fo2TempleTransitionCatalog.LoadFromPresentationOutput(temple);
            var catalog = Fo2ArroyoCavesPresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-arroyo-cache"),
                transition);
            var playerPresentation = Fo2ArroyoPlayerPresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-player-cache"),
                catalog.SourceProfileId);
            Fo2ArroyoArrivalFirstBeatProof.Run(
                catalog,
                playerPresentation,
                Fo2HumanoidDonorContract.RequireFromOptions(options),
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-arroyo-first-beat-report"));
            GD.Print($"OPENNV_FO2_ARROYO_FIRST_BEAT_PASS arrival={catalog.ArrivalTile}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_ARROYO_FIRST_BEAT_FAIL {exception}");
            GetTree().Quit(1);
        }
    }
}
