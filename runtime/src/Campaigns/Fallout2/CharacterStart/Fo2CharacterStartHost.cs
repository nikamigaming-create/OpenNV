using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

public sealed partial class Fo2CharacterStartHost : Node3D
{
    private Fo2ArroyoCavesPresentationCatalog _arroyo = null!;
    private Fo2ArroyoPlayerPresentationCatalog _malePresentation = null!;
    private Fo2CharacterStartCatalog _characterStart = null!;

    internal Fo2CharacterPicker Picker { get; private set; } = null!;
    internal Fo2PremadeCharacter? SelectedCharacter { get; private set; }
    internal Fo2ArroyoCavesSceneCoverage? Scene { get; private set; }
    internal Fo2ArroyoCavesPlayerRuntimeCoverage? Runtime { get; private set; }

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
            _arroyo = Fo2ArroyoCavesPresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-arroyo-cache"),
                transition);
            _malePresentation = Fo2ArroyoPlayerPresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-player-cache"),
                _arroyo.SourceProfileId);
            _characterStart = Fo2CharacterStartCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-character-start-cache"),
                _arroyo.SourceProfileId);
            Picker = new Fo2CharacterPicker(_characterStart);
            Picker.CharacterChosen += StartArroyo;
            Picker.BackRequested += () => GetTree().Quit();
            AddChild(Picker);
            GD.Print(
                "OPENNV_FO2_CHARACTER_START_READY premades=3 " +
                "controls=Left/Right+Enter mouse=enabled exit=Escape");
            if (options.TryGetValue("fo2-character-start-proof", out var proofRoot))
                _ = Fo2CharacterStartProof.Run(this, proofRoot);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_CHARACTER_START_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    public override void _UnhandledKeyInput(InputEvent inputEvent)
    {
        if (Runtime is not null &&
            inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            GetTree().Quit();
    }

    internal void StartArroyo(Fo2PremadeCharacter character)
    {
        if (Runtime is not null || !_characterStart.Characters.Contains(character))
            throw new InvalidOperationException(
                "Fallout 2 character start may hand off exactly once.");
        character.Profile.Validate();
        SelectedCharacter = character;
        Picker.Visible = false;
        Picker.SetProcessInput(false);
        var selectedPresentation = _characterStart.PresentationFor(
            character,
            _malePresentation);
        Scene = Fo2ArroyoCavesScene.Build(_arroyo, this);
        Runtime = Fo2ArroyoCavesPlayerRuntime.Build(
            _arroyo,
            Scene,
            _malePresentation,
            selectedPresentation);
        var player = Runtime.Player;
        player.SetMeta("selected_character_id", character.Id);
        player.SetMeta("selected_character_name", character.Profile.Name);
        player.SetMeta("selected_character_sex", character.Profile.Sex);
        player.SetMeta("selected_character_age", character.Profile.Age);
        player.SetMeta("selected_character_special", string.Join(",", character.Profile.Special));
        player.SetMeta("selected_character_tags", string.Join("|", character.Profile.TaggedSkills));
        player.SetMeta("selected_character_traits", string.Join("|", character.Profile.Traits));
        player.SetMeta("selected_gcd_sha256", character.GcdSha256);
        GD.Print(
            $"OPENNV_FO2_CHARACTER_HANDOFF name={character.Profile.Name} " +
            $"sex={character.Profile.Sex} map={Scene.MapIndex} tile={player.ArrivalTile} " +
            $"fid={selectedPresentation.Fid}");
    }

    internal Fo2CharacterStartCatalog CharacterStart => _characterStart;
}
