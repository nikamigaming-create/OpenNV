using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

public sealed partial class Fo2ArroyoCavesPlayProofHost : Node3D
{
    public override void _Ready()
    {
        try
        {
            var options = Fo2ArroyoCavesProofOptions.Parse(OS.GetCmdlineUserArgs());
            var temple = Fo2TemplePresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-temple-cache"));
            var transition = Fo2TempleTransitionCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-temple-transitions"),
                temple);
            var catalog = Fo2ArroyoCavesPresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-arroyo-cache"),
                transition);
            var playerPresentation = Fo2ArroyoPlayerPresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-player-cache"),
                catalog.SourceProfileId);
            var characterStart = Fo2CharacterStartCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-character-start-cache"),
                catalog.SourceProfileId);
            var selectedCharacter = Fo2CharacterSelection.FromPremade(
                characterStart.Characters.First(character =>
                    character.Profile.Sex == "Male"));
            var selectedPresentation = characterStart.PresentationFor(
                selectedCharacter,
                playerPresentation);
            var humanoidDonor = Fo2HumanoidDonorContract.RequireFromOptions(options);
            var scene = Fo2ArroyoCavesScene.Build(catalog, this);
            var runtime = Fo2ArroyoCavesPlayerRuntime.Build(
                catalog,
                scene,
                playerPresentation,
                selectedPresentation,
                selectedCharacter,
                humanoidDonor);
            _ = Fo2ArroyoCavesPlayProof.Run(
                this,
                catalog,
                scene,
                runtime,
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-arroyo-player-proof"));
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_ARROYO_PLAYER_FAIL {exception}");
            GetTree().Quit(1);
        }
    }
}
