using Godot;
using System.Text.Json;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

public sealed partial class Fo2CharacterStartHost : Node3D
{
    private const string RetailRandomContractResource =
        "res://config/classic-retail-random-fo2-1.02-v1.json";
    private const string ClassicIntTimeContractResource =
        "res://config/classic-int-time-v1.json";
    private Fo2ArroyoCavesPresentationCatalog _arroyo = null!;
    private Fo2TemplePresentationCatalog _temple = null!;
    private Fo2TempleTransitionCatalog _transition = null!;
    private Fo2ArroyoPlayerPresentationCatalog _malePresentation = null!;
    private Fo2CharacterStartCatalog _characterStart = null!;
    private Fo2HumanoidDonorContract _humanoidDonor = null!;
    private OpeningManifest? _characterReflectron;
    private Fo2ArroyoTrialRouteContract? _trialRoute;
    private Fo2ArvillagPresentationCatalog? _village;
    private string? _openingProofRoot;
    private string? _arrivalVisualProofRoot;
    private string? _villageArrivalCaptureRoot;
    private string _savePath = "";
    private ClassicRetailRandomContract _retailRandomContract = null!;
    private ClassicIntTimerContract _classicIntTimerContract = null!;
    private ClassicRetailRandomLifecycleState _retailRandomLifecycle = null!;
    private bool _persistenceEnabled;
    private IReadOnlyList<Fo2AdjacentMapSession> _adjacentSessions =
        Array.Empty<Fo2AdjacentMapSession>();
    private Fo2AdjacentMapSession? _activeAdjacentSession;

    internal Fo2CharacterPicker Picker { get; private set; } = null!;
    internal Fo2CharacterSelection? SelectedCharacter { get; private set; }
    internal Fo2ArroyoCavesSceneCoverage? Scene { get; private set; }
    internal Fo2TempleSceneCoverage? TempleScene { get; private set; }
    internal Fo2ArvillagSceneCoverage? VillageScene { get; private set; }
    internal Fo2MapSceneBuildCoverage? AdjacentScene { get; private set; }
    internal Fo2ArroyoClassicGameplayHud? VillageHud { get; private set; }
    internal Fo2TempleConfrontationRuntime? TempleConfrontation { get; private set; }
    internal Fo2TempleTransitionRuntime? TempleExitRuntime { get; private set; }
    internal Fo2ArroyoCavesPlayerRuntimeCoverage? Runtime { get; private set; }
    internal Fo2ArroyoTrialRuntime? TrialRuntime { get; private set; }
    internal Fo2CharacterStartSaveState? CurrentSave { get; private set; }
    internal bool RestoredFromSave { get; private set; }
    internal Fo2ArroyoExitTransition? LastTransition { get; private set; }
    internal Fo2OpeningTailHandoff? OpeningHandoff { get; private set; }
    internal Task? OpeningHandoffTask { get; private set; }
    internal string SavePath => _savePath;
    internal ClassicRetailRandomLifecycleState RetailRandomLifecycle =>
        _retailRandomLifecycle;
    internal Fo2ArroyoPlayerPresentationSource MalePlayerPresentation =>
        _malePresentation.Source;

    public override void _Ready()
    {
        try
        {
            var runtimeConfiguration = RuntimeConfiguration.Load();
            _retailRandomContract = LoadRetailRandomContract();
            _classicIntTimerContract = LoadClassicIntTimerContract();
            _retailRandomLifecycle =
                ClassicRetailRandomLifecycle.InitializeFromExactBuildClock(
                    _retailRandomContract);
            GetWindow().Size = new Vector2I(
                runtimeConfiguration.Capture.ExpectedWidthPixels,
                runtimeConfiguration.Capture.ExpectedHeightPixels);
            var options = Fo2ArroyoCavesProofOptions.Parse(OS.GetCmdlineUserArgs());
            _openingProofRoot = options.TryGetValue(
                "fo2-opening-handoff-proof",
                out var openingProofRoot)
                ? openingProofRoot
                : null;
            _arrivalVisualProofRoot = options.TryGetValue(
                "fo2-arrival-visual-proof",
                out var arrivalVisualProofRoot)
                ? arrivalVisualProofRoot
                : null;
            _villageArrivalCaptureRoot = options.TryGetValue(
                "fo2-arvillag-arrival-capture",
                out var villageArrivalCaptureRoot)
                ? villageArrivalCaptureRoot
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
            _humanoidDonor = Fo2HumanoidDonorContract.RequireFromOptions(options);
            _characterReflectron = options.TryGetValue(
                "character-reflectron-opening-manifest",
                out var reflectronOpeningManifest)
                ? OpeningManifest.Load(
                    reflectronOpeningManifest,
                    runtimeConfiguration)
                : null;
            _trialRoute = options.TryGetValue("fo2-trial-route", out var trialRoutePath)
                ? Fo2ArroyoTrialRouteContract.Load(trialRoutePath, _arroyo, _transition)
                : null;
            _village = _trialRoute is not null
                ? Fo2ArvillagPresentationCatalog.Load(
                    Fo2ArroyoCavesProofOptions.Require(options, "fo2-arvillag-cache"),
                    _trialRoute)
                : null;
            var hasAdjacentJoins = options.TryGetValue(
                "classic-adjacent-map-catalog", out var joinsPath);
            var adjacentCachePaths = new List<string>();
            if (options.TryGetValue("fo2-adjacent-map-cache", out var adjacentCachePath))
                adjacentCachePaths.Add(adjacentCachePath);
            if (options.TryGetValue("fo2-adjacent-map-caches", out var adjacentCacheList))
                adjacentCachePaths.AddRange(adjacentCacheList.Split(
                    Path.PathSeparator,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var hasAdjacentCache = adjacentCachePaths.Count > 0;
            if (hasAdjacentJoins || hasAdjacentCache)
            {
                if (!hasAdjacentJoins || !hasAdjacentCache)
                    throw new InvalidOperationException(
                        "Fallout 2 adjacent play requires its join catalog and map cache.");
                var joins = ClassicAdjacentMapCatalog.Load(joinsPath!);
                _adjacentSessions = adjacentCachePaths
                    .Select(path => new Fo2AdjacentMapSession(
                        joins, Fo2AdjacentMapPresentation.Load(path)))
                    .ToArray();
                if (_adjacentSessions.Select(row => row.DestinationMapIndex)
                    .Distinct().Count() != _adjacentSessions.Count)
                    throw new InvalidOperationException(
                        "Fallout 2 adjacent cache set duplicates a destination MAP.");
            }
            _persistenceEnabled =
                options.ContainsKey("fo2-save") ||
                (!options.ContainsKey("fo2-character-start-proof") &&
                    _arrivalVisualProofRoot is null);
            _savePath = options.TryGetValue("fo2-save", out var configuredSavePath)
                ? configuredSavePath
                : Fo2CharacterStartSaveState.DefaultPath;
            Picker = new Fo2CharacterPicker(
                _characterStart,
                _humanoidDonor,
                _characterReflectron);
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
                    _transition,
                    runtimeProfile,
                    _trialRoute);
                StartArroyo(state.Character, state);
                RestoredFromSave = true;
                CurrentSave = state;
            }
            GD.Print(
                $"OPENNV_FO2_CHARACTER_START_READY premades=3 restored={RestoredFromSave} " +
                "controls=Left/Right+Enter+V mouse=Take/Portrait/Create/Back/PortraitToggle " +
                "exit=Escape+save");
            if (options.TryGetValue("fo2-character-video", out var videoCharacter))
                _ = Fo2CharacterGenerationVideo.Run(this, videoCharacter);
            else if (options.TryGetValue("fo2-character-start-proof", out var proofRoot))
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
                    "fo2-picker-temple-combat-write-proof",
                    out var integratedCombatWriteRoot))
                _ = Fo2TempleConfrontationProof.RunIntegratedWrite(
                    this,
                    integratedCombatWriteRoot);
            else if (options.TryGetValue(
                    "fo2-picker-temple-combat-restore-proof",
                    out var integratedCombatRestoreRoot))
                _ = Fo2TempleConfrontationProof.RunIntegratedRestore(
                    this,
                    integratedCombatRestoreRoot);
            else if (options.TryGetValue(
                    "fo2-arroyo-trial-write-proof",
                    out var trialWriteRoot))
                _ = Fo2ArroyoTrialRouteProof.RunWrite(this, trialWriteRoot);
            else if (options.TryGetValue(
                    "fo2-arroyo-trial-restore-proof",
                    out var trialRestoreRoot))
                _ = Fo2ArroyoTrialRouteProof.RunRestore(this, trialRestoreRoot);
            else if (_openingProofRoot is not null)
                _ = Fo2OpeningHandoffProof.Run(this, _openingProofRoot);
            else if (_arrivalVisualProofRoot is not null)
                _ = Fo2ArrivalVisualProof.Run(this, _arrivalVisualProofRoot);
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
            {
                if (AdjacentScene is null)
                    PersistCurrentState();
                else
                    WriteAdjacentState();
            }
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
        _retailRandomLifecycle = restoredState is null
            ? ClassicRetailRandomLifecycle.ResetForNewGame(
                _retailRandomLifecycle, _retailRandomContract)
            : ClassicRetailRandomLifecycle.ResetForLoad(
                _retailRandomLifecycle, _retailRandomContract);
        SelectedCharacter = character;
        Picker.Visible = false;
        Picker.SetProcessInput(false);
        var selectedPresentation = _characterStart.PresentationFor(
            character,
            _malePresentation);
        _retailRandomLifecycle = ClassicRetailRandomLifecycle.RequireSourceCall(
            _retailRandomLifecycle,
            _retailRandomContract,
            "arroyo-map-load",
            "exact-build-engine-script-interleaving-before-source-map-enter-random");
        Scene = Fo2ArroyoCavesScene.Build(_arroyo, this);
        Runtime = Fo2ArroyoCavesPlayerRuntime.Build(
            _arroyo,
            Scene,
            _malePresentation,
            selectedPresentation,
            character,
            _humanoidDonor);
        var player = Runtime.Player;
        if (_trialRoute is not null)
            TrialRuntime = Fo2ArroyoTrialRuntime.Build(
                _trialRoute,
                _arroyo,
                Scene,
                player,
                this,
                _retailRandomContract,
                _classicIntTimerContract,
                () => _retailRandomLifecycle,
                CommitRetailRandomLifecycle,
                restoredState?.TrialProgress);
        if (restoredState is not null)
        {
            if (restoredState.MapIndex is Fo2TemplePresentationCatalog.MapIndex or 4)
            {
                var exit = restoredState.LastTransition ?? throw new InvalidOperationException(
                    "Fallout 2 Temple save has no source-authored transition identity.");
                player.Restore(
                    exit.SourceTile,
                    Fo1HexMath.Center(exit.SourceTile) +
                        Vector3.Up * Runtime.Profile.SpawnCenterHeightMeters,
                    exit.TargetRotation);
                EnterTemple(exit, restoredState.TempleConfrontation);
                if (restoredState.MapIndex == 4)
                {
                    var route = _trialRoute ?? throw new InvalidOperationException(
                        "Fallout 2 ARVILLAG save has no owned arrival route.");
                    (TempleExitRuntime ?? throw new InvalidOperationException(
                        "Fallout 2 ARVILLAG save has no Temple exit runtime."))
                        .RestorePostTrialApplied(
                            restoredState.TempleExitTransition ??
                                throw new InvalidOperationException(
                                    "Fallout 2 ARVILLAG save has no applied Temple exit."),
                            route.Village);
                    var village = BuildVillageScene();
                    player.EnterVillage(village, route.VillageArrival);
                    ActivateVillagePresentation();
                }
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
            if (_adjacentSessions.Count > 0 && File.Exists(AdjacentSavePath))
                RestoreAdjacentState(player);
            if (restoredState.MapIndex != 4 &&
                restoredState.TempleExitTransition is not null)
            {
                var transition = TempleExitRuntime ?? throw new InvalidOperationException(
                    "Fallout 2 restored Temple exit has no transition runtime.");
                if (restoredState.TrialProgress is not null)
                    transition.RestorePostTrialApplied(
                        restoredState.TempleExitTransition,
                        (_trialRoute ?? throw new InvalidOperationException(
                            "Fallout 2 restored trial save has no active route contract."))
                            .Village);
                else
                    transition.RestoreApplied(restoredState.TempleExitTransition);
                player.SetControlsEnabled(false);
            }
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

    private static ClassicRetailRandomContract LoadRetailRandomContract()
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(RetailRandomContractResource);
        if (bytes.Length == 0)
            throw new FileNotFoundException(
                "Fallout 2 retail random contract is missing.",
                RetailRandomContractResource);
        using var document = JsonDocument.Parse(bytes);
        return ClassicRetailRandomContract.Parse(document.RootElement);
    }

    private static ClassicIntTimerContract LoadClassicIntTimerContract()
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(ClassicIntTimeContractResource);
        if (bytes.Length == 0)
            throw new FileNotFoundException(
                "Classic INT time contract is missing.",
                ClassicIntTimeContractResource);
        using var document = JsonDocument.Parse(bytes);
        return ClassicIntTimerContract.Parse(document.RootElement);
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
            await handoff.Play(contract, _arroyo, scene, runtime, proofRoot);
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
        if (_adjacentSessions.Count > 0 && AdjacentScene is null &&
            player.CurrentMapIndex == Fo2ArvillagPresentationCatalog.MapIndex)
        {
            var committed = _adjacentSessions[0].TryCommit(player);
            if (committed is not null)
            {
                _activeAdjacentSession = _adjacentSessions.SingleOrDefault(
                    row => row.CanActivate(committed.Join.Destination)) ??
                    throw new InvalidOperationException(
                        "Fallout 2 adjacent destination has no decoded cache.");
                PersistCurrentState();
                AdjacentScene = _activeAdjacentSession.Activate(
                    this, player, committed.Join.Destination);
                if (VillageScene is not null)
                    VillageScene.Root.Visible = false;
                WriteAdjacentState();
                return;
            }
        }
        if (_activeAdjacentSession is not null && AdjacentScene is not null)
        {
            var committed = _activeAdjacentSession.TryCommit(player);
            if (committed is not null && committed.Join.Destination.MapIndex ==
                    Fo2ArvillagPresentationCatalog.MapIndex)
            {
                var village = VillageScene ?? throw new InvalidOperationException(
                    "Fallout 2 adjacent return has no ARVILLAG presentation.");
                var walkable = VillageWalkable();
                player.EnterAdjacentMap(
                    village.Root,
                    committed.Join.Destination,
                    walkable,
                    _village!.WalkMaskSha256,
                    _village.ManifestSha256);
                village.Root.Visible = true;
                AdjacentScene.Root.QueueFree();
                AdjacentScene = null;
                _activeAdjacentSession = null;
                if (File.Exists(AdjacentSavePath))
                    File.Delete(AdjacentSavePath);
                PersistCurrentState();
                return;
            }
        }
        if (AdjacentScene is not null)
        {
            WriteAdjacentState();
            return;
        }
        PersistCurrentState();
    }

    private string AdjacentSavePath => _savePath + ".adjacent-map.json";

    private IReadOnlySet<int> VillageWalkable()
    {
        var village = _village ?? throw new InvalidOperationException(
            "Fallout 2 ARVILLAG source presentation is unavailable.");
        var noBlock = Fo2TempleTopologyProfile.Load(_temple).ObjectNoBlockFlag;
        var blocked = village.ObjectPlacements
            .Where(row => row.Tile >= 0 && row.Blocking(noBlock))
            .Select(row => row.Tile)
            .ToHashSet();
        var walkable = Enumerable.Range(0, Fo1HexMath.Width * Fo1HexMath.Height)
            .Where(tile =>
                (village.TileEntries[Fo1HexMath.FloorIndex(tile)] &
                    Fo2MapSceneBuilder.FloorIdMask) !=
                    Fo2ArvillagPresentationCatalog.DefaultFloorTileId &&
                !blocked.Contains(tile))
            .ToHashSet();
        if (walkable.Count != village.WalkableHexes)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG runtime walk topology drifted.");
        return walkable;
    }

    private void WriteAdjacentState()
    {
        var player = Runtime?.Player ?? throw new InvalidOperationException(
            "Fallout 2 adjacent save has no player.");
        var session = _activeAdjacentSession ?? throw new InvalidOperationException(
            "Fallout 2 adjacent save has no session.");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "opennv-fo2-adjacent-map-save/v1",
            joinCatalogSha256 = session.JoinCatalogSha256,
            destinationCacheSha256 = session.DestinationCacheSha256,
            mapIndex = player.CurrentMapIndex,
            mapSha256 = player.CurrentMapSha256,
            elevation = player.CurrentElevation,
            tile = player.CurrentTile,
            rotation = player.Presentation.Direction,
        });
        var temporary = AdjacentSavePath + ".tmp";
        File.WriteAllBytes(temporary, payload);
        File.Move(temporary, AdjacentSavePath, true);
    }

    private void RestoreAdjacentState(Fo2ArroyoCavesPlayerBody player)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(AdjacentSavePath));
        var state = document.RootElement;
        if (state.GetProperty("schema").GetString() != "opennv-fo2-adjacent-map-save/v1")
            throw new InvalidOperationException("Fallout 2 adjacent save provenance drifted.");
        var endpoint = new ClassicMapEndpoint(
            state.GetProperty("mapIndex").GetInt32(),
            null,
            state.GetProperty("mapSha256").GetString()!,
            state.GetProperty("tile").GetInt32(),
            state.GetProperty("elevation").GetInt32(),
            state.GetProperty("rotation").GetInt32());
        _activeAdjacentSession = _adjacentSessions.SingleOrDefault(row =>
            row.CanActivate(endpoint) &&
            state.GetProperty("joinCatalogSha256").GetString() == row.JoinCatalogSha256 &&
            state.GetProperty("destinationCacheSha256").GetString() ==
                row.DestinationCacheSha256) ?? throw new InvalidOperationException(
                    "Fallout 2 adjacent save provenance drifted.");
        AdjacentScene = _activeAdjacentSession.Activate(this, player, endpoint);
        player.Restore(
            endpoint.Tile,
            Fo1HexMath.Center(endpoint.Tile) +
                Vector3.Up * (Runtime?.Profile.SpawnCenterHeightMeters ??
                    throw new InvalidOperationException(
                        "Fallout 2 adjacent restore has no player profile.")),
            endpoint.Rotation!.Value);
        if (VillageScene is not null)
            VillageScene.Root.Visible = false;
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
        TempleExitRuntime = Fo2TempleTransitionRuntime.Build(
            TempleScene.Root,
            _transition,
            TempleScene.Topology.Movement);
        if (TrialRuntime?.State.Stage is
                Fo2ArroyoTrialProgressState.ReturnedStage or
                Fo2ArroyoTrialProgressState.GateMovedStage or
                Fo2ArroyoTrialProgressState.VillageArrivalStage or
                Fo2ArroyoTrialProgressState.VillageFirstActionStage)
            TrialRuntime.ApplyKlintMapEnter(_temple, TempleScene, Runtime.Player);
        TempleConfrontation = Fo2TempleConfrontationRuntime.Build(
            _temple,
            TempleScene,
            Runtime.Player,
            SelectedCharacter ?? throw new InvalidOperationException(
                "Fallout 2 Temple confrontation has no selected character."),
            _characterStart.Inventory,
            TempleExitRuntime,
            _retailRandomContract,
            () => _retailRandomLifecycle,
            CommitRetailRandomLifecycle,
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

    private void CommitRetailRandomLifecycle(
        ClassicRetailRandomLifecycleState source,
        ClassicRetailRandomLifecycleState result)
    {
        if (_retailRandomLifecycle != source)
            throw new InvalidOperationException(
                "Fallout 2 retail random lifecycle changed during INT dispatch.");
        result.Validate(_retailRandomContract);
        _retailRandomLifecycle = result;
    }

    internal void EnterTempleAfterTrial()
    {
        if (TrialRuntime?.State.Stage != Fo2ArroyoTrialProgressState.ReturnedStage ||
            Runtime?.Player.CurrentTile != _arroyo.LiveExit.SourceTile ||
            Runtime.Player.CurrentElevation != _arroyo.LiveExit.SourceElevation)
            throw new InvalidOperationException(
                "Fallout 2 Temple entry requires the completed Cameron return route.");
        EnterTemple(_arroyo.LiveExit, null);
    }

    internal Fo2TempleAppliedTransition EnterVillageAfterTrial()
    {
        var runtime = TrialRuntime ?? throw new InvalidOperationException(
            "Fallout 2 village entry has no active trial runtime.");
        var transition = TempleExitRuntime ?? throw new InvalidOperationException(
            "Fallout 2 village entry has no active Temple transition runtime.");
        var village = BuildVillageScene();
        var applied = runtime.ApplyVillageExit(transition, village);
        ActivateVillagePresentation();
        return applied;
    }

    private Fo2ArvillagSceneCoverage BuildVillageScene()
    {
        if (VillageScene is not null)
            return VillageScene;
        if (_village is null || _trialRoute is null ||
            _village.SourceProfileId != _arroyo.SourceProfileId ||
            _village.MapSha256 != _trialRoute.VillageArrival.MapSha256)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG owned presentation cache is unavailable.");
        var presentationProfile = Scene?.Molded3D.Profile ??
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG has no versioned classic 3D presentation profile.");
        VillageScene = Fo2ArvillagScene.Build(_village, presentationProfile, this);
        VillageScene.Root.Visible = false;
        return VillageScene;
    }

    private void ActivateVillagePresentation()
    {
        if (VillageScene is null || Runtime is null || SelectedCharacter is null ||
            Runtime.Player.GetParent() != VillageScene.Root)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG presentation cannot be activated.");
        VillageScene.Root.Visible = true;
        if (TempleScene is not null)
            TempleScene.Root.Visible = false;
        if (TempleConfrontation is not null)
            TempleConfrontation.Visible = false;
        VillageHud = Fo2ArroyoClassicGameplayHud.Build(VillageScene.Root, _arroyo);
        VillageHud.BindCharacter(SelectedCharacter);
        GD.Print(
            $"OPENNV_FO2_ARVILLAG_PRESENTATION_READY map=4 tile={Runtime.Player.CurrentTile} " +
            $"relief={VillageScene.ReliefPlacements} roofs=cutaway:{VillageScene.SourceRoofPatches}");
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
                TempleConfrontation?.State,
                TempleExitRuntime?.Applied,
                _transition,
                TrialRuntime?.State,
                _trialRoute)
            .Write();
        return CurrentSave;
    }

    internal Fo2CharacterStartCatalog CharacterStart => _characterStart;
    internal Fo2ArroyoCavesPresentationCatalog Arroyo => _arroyo;
    internal Fo2TemplePresentationCatalog Temple => _temple;
    internal Fo2TempleTransitionCatalog TempleTransition => _transition;
    internal Fo2ArroyoTrialRouteContract TrialRoute => _trialRoute ??
        throw new InvalidOperationException("Fallout 2 trial route was not configured.");
    internal Fo2ArvillagPresentationCatalog Village => _village ??
        throw new InvalidOperationException("Fallout 2 ARVILLAG cache was not configured.");
    internal string? VillageArrivalCaptureRoot => _villageArrivalCaptureRoot;
}
