using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

public sealed partial class Fo2ArroyoCavesInteractiveHost : Node3D
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
            var scene = Fo2ArroyoCavesScene.Build(catalog, this);
            var runtime = Fo2ArroyoCavesPlayerRuntime.Build(
                catalog,
                scene,
                playerPresentation);
            GD.Print(
                $"OPENNV_FO2_ARROYO_INTERACTIVE_READY map={scene.MapIndex} " +
                $"elevation={scene.Elevation} tile={runtime.Player.ArrivalTile} " +
                $"fid={Fo2ArroyoPlayerPresentationCatalog.ExpectedFid} " +
                "controls=WASD exit=Escape");
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_ARROYO_INTERACTIVE_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    public override void _UnhandledKeyInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            GetTree().Quit();
    }
}
