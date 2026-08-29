using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

public sealed partial class Fo2CharacterStartHost : Node3D
{
    private Fo2ArroyoCavesPresentationCatalog _arroyo = null!;
    private Fo2ArroyoPlayerPresentationCatalog _malePresentation = null!;
    private Fo2CharacterStartCatalog _characterStart = null!;
    private string _savePath = "";
    private bool _persistenceEnabled;

    internal Fo2CharacterPicker Picker { get; private set; } = null!;
    internal Fo2CharacterSelection? SelectedCharacter { get; private set; }
    internal Fo2ArroyoCavesSceneCoverage? Scene { get; private set; }
    internal Fo2ArroyoCavesPlayerRuntimeCoverage? Runtime { get; private set; }
    internal Fo2CharacterStartSaveState? CurrentSave { get; private set; }
    internal bool RestoredFromSave { get; private set; }
    internal string SavePath => _savePath;

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
            _persistenceEnabled =
                options.ContainsKey("fo2-save") ||
                !options.ContainsKey("fo2-character-start-proof");
            _savePath = options.TryGetValue("fo2-save", out var configuredSavePath)
                ? configuredSavePath
                : Fo2CharacterStartSaveState.DefaultPath;
            Picker = new Fo2CharacterPicker(_characterStart);
            Picker.CharacterChosen += StartArroyo;
            Picker.BackRequested += () => GetTree().Quit();
            AddChild(Picker);
            if (_persistenceEnabled && Fo2CharacterStartSaveState.Exists(_savePath))
            {
                var runtimeProfile = Fo2ArroyoPlayerProfile.Load(_arroyo);
                var state = Fo2CharacterStartSaveState.Load(
                    _savePath,
                    _characterStart,
                    _arroyo,
                    runtimeProfile);
                StartArroyo(state.Character, state);
                RestoredFromSave = true;
                CurrentSave = state;
            }
            GD.Print(
                $"OPENNV_FO2_CHARACTER_START_READY premades=3 restored={RestoredFromSave} " +
                "controls=Left/Right+Enter mouse=Take/Modify/Create/Back exit=Escape+save");
            if (options.TryGetValue("fo2-character-start-proof", out var proofRoot))
                _ = Fo2CharacterStartProof.Run(this, proofRoot);
            else if (options.TryGetValue("fo2-character-save-write-proof", out var writeRoot))
                _ = Fo2CharacterStartPersistenceProof.RunWrite(this, writeRoot);
            else if (options.TryGetValue(
                    "fo2-character-save-restore-proof",
                    out var restoreRoot))
                _ = Fo2CharacterStartPersistenceProof.RunRestore(this, restoreRoot);
            else if (options.TryGetValue(
                    "fo2-custom-character-write-proof",
                    out var customWriteRoot))
                _ = Fo2CustomCharacterPersistenceProof.RunWrite(
                    this,
                    customWriteRoot,
                    Fo2ArroyoCavesProofOptions.Require(options, "fo2-custom-character-sex"));
            else if (options.TryGetValue(
                    "fo2-custom-character-restore-proof",
                    out var customRestoreRoot))
                _ = Fo2CustomCharacterPersistenceProof.RunRestore(
                    this,
                    customRestoreRoot,
                    Fo2ArroyoCavesProofOptions.Require(options, "fo2-custom-character-sex"));
            else if (options.TryGetValue(
                    "fo2-walk-animation-write-proof",
                    out var walkWriteRoot))
                _ = Fo2WalkAnimationProof.RunWrite(
                    this,
                    walkWriteRoot,
                    Fo2ArroyoCavesProofOptions.Require(options, "fo2-walk-animation-sex"));
            else if (options.TryGetValue(
                    "fo2-walk-animation-restore-proof",
                    out var walkRestoreRoot))
                _ = Fo2WalkAnimationProof.RunRestore(
                    this,
                    walkRestoreRoot,
                    Fo2ArroyoCavesProofOptions.Require(options, "fo2-walk-animation-sex"));
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
        {
            if (_persistenceEnabled)
                PersistCurrentState();
            GetTree().Quit();
        }
    }

    internal void StartArroyo(Fo2CharacterSelection character) => StartArroyo(character, null);

    private void StartArroyo(
        Fo2CharacterSelection character,
        Fo2CharacterStartSaveState? restoredState)
    {
        if (Runtime is not null)
            throw new InvalidOperationException(
                "Fallout 2 character start may hand off exactly once.");
        character.Validate(_characterStart);
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
        if (restoredState is not null)
            player.Restore(
                restoredState.CurrentTile,
                restoredState.Position,
                restoredState.Rotation);
        player.SetMeta("selected_character_id", character.Id);
        player.SetMeta("selected_character_name", character.Profile.Name);
        player.SetMeta("selected_character_sex", character.Profile.Sex);
        player.SetMeta("selected_character_age", character.Profile.Age);
        player.SetMeta("selected_character_special", string.Join(",", character.Profile.Special));
        player.SetMeta("selected_character_tags", string.Join("|", character.Profile.TaggedSkills));
        player.SetMeta("selected_character_traits", string.Join("|", character.Profile.Traits));
        player.SetMeta("selected_character_mode", character.Mode);
        player.SetMeta("selected_character_source_id", character.Source.Id);
        player.SetMeta("selected_gcd_sha256", character.GcdSha256);
        if (_persistenceEnabled)
            player.PersistenceBoundaryReached += () => PersistCurrentState();
        if (_persistenceEnabled && restoredState is null)
            PersistCurrentState();
        GD.Print(
            $"OPENNV_FO2_CHARACTER_HANDOFF mode={character.Mode} name={character.Profile.Name} " +
            $"sex={character.Profile.Sex} map={Scene.MapIndex} tile={player.CurrentTile} " +
            $"fid={selectedPresentation.Fid} restored={restoredState is not null}");
    }

    internal Fo2CharacterStartSaveState PersistCurrentState()
    {
        if (!_persistenceEnabled || Runtime is null || SelectedCharacter is null)
            return CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 persistence is unavailable before character handoff.");
        CurrentSave = Fo2CharacterStartSaveState.Capture(
                _savePath,
                _characterStart,
                _arroyo,
                Runtime,
                SelectedCharacter)
            .Write();
        return CurrentSave;
    }

    internal Fo2CharacterStartCatalog CharacterStart => _characterStart;
}
