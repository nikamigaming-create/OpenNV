using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

public sealed partial class Fo2CharacterStartHost : Node3D
{
    private Fo2ArroyoCavesPresentationCatalog _arroyo = null!;
    private Fo2TemplePresentationCatalog _temple = null!;
    private Fo2TempleTransitionCatalog _transition = null!;
    private Fo2ArroyoPlayerPresentationCatalog _malePresentation = null!;
    private Fo2CharacterStartCatalog _characterStart = null!;
    private string? _openingProofRoot;
    private string _savePath = "";
    private bool _persistenceEnabled;

    internal Fo2CharacterPicker Picker { get; private set; } = null!;
    internal Fo2CharacterSelection? SelectedCharacter { get; private set; }
    internal Fo2ArroyoCavesSceneCoverage? Scene { get; private set; }
    internal Fo2TempleSceneCoverage? TempleScene { get; private set; }
    internal Fo2TempleConfrontationRuntime? TempleConfrontation { get; private set; }
    internal Fo2ArroyoCavesPlayerRuntimeCoverage? Runtime { get; private set; }
    internal Fo2CharacterStartSaveState? CurrentSave { get; private set; }
    internal bool RestoredFromSave { get; private set; }
    internal Fo2ArroyoExitTransition? LastTransition { get; private set; }
    internal Fo2OpeningTailHandoff? OpeningHandoff { get; private set; }
    internal Task? OpeningHandoffTask { get; private set; }
    internal string SavePath => _savePath;

    public override void _Ready()
    {
        try
        {
            var options = Fo2ArroyoCavesProofOptions.Parse(OS.GetCmdlineUserArgs());
            _openingProofRoot = options.TryGetValue(
                "fo2-opening-handoff-proof",
                out var openingProofRoot)
                ? openingProofRoot
                : null;
            _temple = Fo2TemplePresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-temple-cache"));
            _transition = Fo2TempleTransitionCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-temple-transitions"),
                _temple);
            _arroyo = Fo2ArroyoCavesPresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-arroyo-cache"),
                _transition);
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
                    _temple,
                    runtimeProfile);
                StartArroyo(state.Character, state);
                RestoredFromSave = true;
                CurrentSave = state;
            }
            GD.Print(
                $"OPENNV_FO2_CHARACTER_START_READY premades=3 restored={RestoredFromSave} " +
                "controls=Left/Right+Enter+V mouse=Take/Modify/Create/Back/PortraitToggle " +
                "exit=Escape+save");
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
            else if (options.TryGetValue(
                    "fo2-exit-transition-write-proof",
                    out var exitWriteRoot))
                _ = Fo2ExitTransitionProof.RunWrite(this, exitWriteRoot);
            else if (options.TryGetValue(
                    "fo2-exit-transition-restore-proof",
                    out var exitRestoreRoot))
                _ = Fo2ExitTransitionProof.RunRestore(this, exitRestoreRoot);
            else if (options.TryGetValue(
                    "fo2-temple-confrontation-write-proof",
                    out var confrontationWriteRoot))
                _ = Fo2TempleConfrontationProof.RunWrite(this, confrontationWriteRoot);
            else if (options.TryGetValue(
                    "fo2-temple-confrontation-restore-proof",
                    out var confrontationRestoreRoot))
                _ = Fo2TempleConfrontationProof.RunRestore(this, confrontationRestoreRoot);
            else if (options.TryGetValue(
                    "fo2-custom-portrait-write-proof",
                    out var portraitWriteRoot))
                Fo2ProceduralPortraitProof.RunWrite(this, portraitWriteRoot);
            else if (options.TryGetValue(
                    "fo2-custom-portrait-restore-proof",
                    out var portraitRestoreRoot))
                Fo2ProceduralPortraitProof.RunRestore(this, portraitRestoreRoot);
            else if (options.TryGetValue(
                    "fo2-custom-portrait-v8-migration-proof",
                    out var portraitMigrationRoot))
                Fo2ProceduralPortraitProof.RunV8MigrationRestore(this, portraitMigrationRoot);
            else if (_openingProofRoot is not null)
                _ = Fo2OpeningHandoffProof.Run(this, _openingProofRoot);
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
            if (OpeningHandoff?.IsPlaying == true)
            {
                OpeningHandoff.RequestSkip();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (TempleConfrontation?.CloseInventoryIfOpen() == true)
            {
                GetViewport().SetInputAsHandled();
                return;
            }
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
        {
            if (restoredState.MapIndex == Fo2TemplePresentationCatalog.MapIndex)
            {
                var exit = restoredState.LastTransition ?? throw new InvalidOperationException(
                    "Fallout 2 Temple save has no source-authored transition identity.");
                player.Restore(
                    exit.SourceTile,
                    Fo1HexMath.Center(exit.SourceTile) +
                        Vector3.Up * Runtime.Profile.SpawnCenterHeightMeters,
                    exit.TargetRotation);
                EnterTemple(exit, restoredState.TempleConfrontation);
            }
            if (restoredState.MapSha256 != player.CurrentMapSha256 ||
                restoredState.WalkMaskSha256 != player.CurrentWalkMaskSha256 ||
                restoredState.Elevation != player.CurrentElevation)
                throw new InvalidOperationException(
                    "Fallout 2 saved active-map identity differs from the loaded source scene.");
            player.Restore(
                restoredState.CurrentTile,
                restoredState.Position,
                restoredState.Rotation);
        }
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
        player.SetMeta("selected_face_shape", character.Appearance.FaceShapeId);
        player.SetMeta("selected_hair_style", character.Appearance.HairStyleId);
        player.SetMeta("selected_skin_tone", character.Appearance.SkinToneId);
        player.SetMeta("selected_hair_color", character.Appearance.HairColorId);
        player.SetMeta("selected_eye_color", character.Appearance.EyeColorId);
        player.SetMeta("selected_brow_style", character.Appearance.BrowStyleId);
        player.SetMeta("selected_nose_style", character.Appearance.NoseStyleId);
        player.SetMeta("selected_mouth_style", character.Appearance.MouthStyleId);
        player.SetMeta(
            "selected_appearance_recipe_sha256",
            character.Appearance.AppearanceRecipeSha256);
        player.SetMeta(
            "selected_generated_portrait_sha256",
            character.Appearance.GeneratedPortraitSha256);
        if (_persistenceEnabled)
            player.PersistenceBoundaryReached += OnPlayerPersistenceBoundary;
        if (_persistenceEnabled && restoredState is null)
            PersistCurrentState();
        if (restoredState is null && _characterStart.OpeningTail is not null)
        {
            OpeningHandoff = new Fo2OpeningTailHandoff
            {
                Name = "FO2_OWNED_ELDER_TAIL_HANDOFF",
            };
            AddChild(OpeningHandoff);
            OpeningHandoffTask = RunOpeningTail(
                OpeningHandoff,
                _characterStart.OpeningTail,
                Scene,
                Runtime,
                _openingProofRoot);
        }
        GD.Print(
            $"OPENNV_FO2_CHARACTER_HANDOFF mode={character.Mode} name={character.Profile.Name} " +
            $"sex={character.Profile.Sex} map={player.CurrentMapIndex} tile={player.CurrentTile} " +
            $"fid={selectedPresentation.Fid} restored={restoredState is not null}");
    }

    private async Task RunOpeningTail(
        Fo2OpeningTailHandoff handoff,
        Fo2OpeningTailContract contract,
        Fo2ArroyoCavesSceneCoverage scene,
        Fo2ArroyoCavesPlayerRuntimeCoverage runtime,
        string? proofRoot)
    {
        try
        {
            await handoff.Play(contract, scene, runtime, proofRoot);
            GD.Print(
                $"OPENNV_FO2_OPENING_HANDOFF_COMPLETE terminal={handoff.FinalPresentedSourceFrame} " +
                $"black={handoff.TerminalBlackPresented} controls={handoff.ControlReleased}");
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_OPENING_HANDOFF_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private void OnPlayerPersistenceBoundary()
    {
        var player = Runtime?.Player ?? throw new InvalidOperationException(
            "Fallout 2 player persistence boundary has no runtime.");
        if (player.CurrentMapIndex == Fo2ArroyoCavesPresentationCatalog.MapIndex &&
            player.CurrentTile == _arroyo.LiveExit.SourceTile)
            EnterTemple(_arroyo.LiveExit, null);
        PersistCurrentState();
    }

    private void EnterTemple(
        Fo2ArroyoExitTransition exit,
        Fo2TempleConfrontationState? restoredConfrontation)
    {
        if (Runtime is null || Scene is null || TempleScene is not null ||
            exit != _arroyo.LiveExit ||
            _temple.SourceProfileId != _arroyo.SourceProfileId ||
            _temple.MapSha256 != exit.TargetMapSha256 ||
            exit.TargetMapIndex != Fo2TemplePresentationCatalog.MapIndex)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo exit cannot resolve its owned Temple destination.");
        var sourceRoot = Scene.Root;
        TempleScene = Fo2TempleScene.Build(_temple, this);
        Runtime.Player.EnterTemple(TempleScene, exit);
        TempleConfrontation = Fo2TempleConfrontationRuntime.Build(
            _temple,
            TempleScene,
            Runtime.Player,
            SelectedCharacter ?? throw new InvalidOperationException(
                "Fallout 2 Temple confrontation has no selected character."),
            _characterStart.Inventory,
            restoredConfrontation);
        if (_persistenceEnabled)
            TempleConfrontation.StateChanged += OnTempleConfrontationStateChanged;
        LastTransition = exit;
        sourceRoot.QueueFree();
        GD.Print(
            $"OPENNV_FO2_EXIT_TRANSITION serial={exit.ExitSerial} " +
            $"source={exit.SourceMapIndex}:{exit.SourceTile} " +
            $"target={exit.TargetMapIndex}:{exit.TargetTile} " +
            $"elevation={exit.TargetElevation} rotation={exit.TargetRotation}");
    }

    private void OnTempleConfrontationStateChanged() => PersistCurrentState();

    internal Fo2CharacterStartSaveState PersistCurrentState()
    {
        if (!_persistenceEnabled || Runtime is null || SelectedCharacter is null)
            return CurrentSave ?? throw new InvalidOperationException(
                "Fallout 2 persistence is unavailable before character handoff.");
        CurrentSave = Fo2CharacterStartSaveState.Capture(
                _savePath,
                _characterStart,
                _arroyo,
                _temple,
                Runtime,
                SelectedCharacter,
                TempleConfrontation?.State)
            .Write();
        return CurrentSave;
    }

    internal Fo2CharacterStartCatalog CharacterStart => _characterStart;
    internal Fo2ArroyoCavesPresentationCatalog Arroyo => _arroyo;
    internal Fo2TemplePresentationCatalog Temple => _temple;
}
