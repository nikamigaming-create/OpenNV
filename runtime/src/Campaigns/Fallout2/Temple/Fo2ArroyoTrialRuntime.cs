using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.Campaigns.Fallout1;
using OpenNV.Runtime.Campaigns.Classic;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArroyoTrialProgressState(
    string RouteSha256,
    string Stage,
    int GlobalVariable10,
    int CameronLocalVariable12,
    int CameronLocalVariable13,
    int CameronMapVariable20,
    int CameronDialogueSelections,
    int CameronTile,
    bool CameronVisible,
    bool CameronDoorOpened,
    bool CameronDoorUnlocked,
    ClassicDoorState CameronDoorPlaybackState,
    int KlintGateTile,
    bool KlintAlive,
    bool VillageRouteCompleted,
    int VillageCurrentTile,
    bool VillageFirstActionApplied)
{
    internal const string InitialStage = "approach-cameron";
    internal const string NegotiatedStage = "cameron-negotiated";
    internal const string ReturnedStage = "returned-to-temple-exit";
    internal const string GateMovedStage = "acklint-map-enter-gate-moved";
    internal const string VillageArrivalStage = "arvillag-arrived";
    internal const string VillageFirstActionStage = "arvillag-first-legal-action";

    internal static Fo2ArroyoTrialProgressState Initial(
        Fo2ArroyoTrialRouteContract contract) => new(
            contract.Sha256,
            InitialStage,
            0,
            0,
            0,
            0,
            0,
            contract.Cameron.Tile,
            true,
            false,
            false,
            ClassicDoorSession.Closed(contract.Cameron.ReleaseDoorPresentation),
            contract.KlintGate.SourceTile,
            true,
            false,
            -1,
            false);

    internal void Validate(Fo2ArroyoTrialRouteContract contract)
    {
        if (RouteSha256 != contract.Sha256 || !KlintAlive ||
            CameronDialogueSelections is < 0 ||
            CameronDialogueSelections > contract.Cameron.TaggedSpeechBranch.SelectedMessageIds.Count)
            throw new InvalidOperationException("Fallout 2 saved trial identity drifted.");
        var initial = Stage == InitialStage &&
            GlobalVariable10 == 0 && CameronLocalVariable12 == 0 &&
            CameronLocalVariable13 == 0 && CameronMapVariable20 == 0 &&
            CameronDialogueSelections <
                contract.Cameron.TaggedSpeechBranch.SelectedMessageIds.Count &&
            CameronTile == contract.Cameron.Tile &&
            CameronVisible && !CameronDoorOpened && !CameronDoorUnlocked &&
            CameronDoorPlaybackState ==
                ClassicDoorSession.Closed(contract.Cameron.ReleaseDoorPresentation) &&
            KlintGateTile == contract.KlintGate.SourceTile && !VillageRouteCompleted;
        var negotiated = Stage == NegotiatedStage || Stage == ReturnedStage ||
            Stage == GateMovedStage || Stage == VillageArrivalStage ||
            Stage == VillageFirstActionStage;
        var negotiatedState = negotiated &&
            GlobalVariable10 == contract.Cameron.TaggedSpeechBranch.GlobalVariable10 &&
            CameronLocalVariable12 == contract.Cameron.TaggedSpeechBranch.LocalVariable12 &&
            CameronLocalVariable13 == contract.Cameron.TaggedSpeechBranch.LocalVariable13 &&
            CameronMapVariable20 == contract.Cameron.TaggedSpeechBranch.MapVariable20 &&
            CameronDialogueSelections ==
                contract.Cameron.TaggedSpeechBranch.SelectedMessageIds.Count &&
            CameronTile == contract.Cameron.ReleaseActorTiles[^1] && !CameronVisible &&
            CameronDoorOpened == contract.Cameron.ReleaseDoorOpened &&
            CameronDoorUnlocked == contract.Cameron.ReleaseDoorUnlocked &&
            CameronDoorPlaybackState.Open;
        var gateState = Stage == GateMovedStage || Stage == VillageArrivalStage ||
                Stage == VillageFirstActionStage
            ? KlintGateTile == contract.KlintGate.DestinationTile
            : KlintGateTile == contract.KlintGate.SourceTile;
        var villageState = Stage switch
        {
            InitialStage or NegotiatedStage or ReturnedStage or GateMovedStage =>
                !VillageRouteCompleted && VillageCurrentTile == -1 &&
                !VillageFirstActionApplied,
            VillageArrivalStage => VillageRouteCompleted &&
                VillageCurrentTile == contract.VillageArrival.ArrivalTile &&
                !VillageFirstActionApplied,
            VillageFirstActionStage => VillageRouteCompleted &&
                VillageCurrentTile == contract.VillageArrival.FirstActionToTile &&
                VillageFirstActionApplied,
            _ => false,
        };
        if (!(initial || negotiatedState && gateState && villageState))
            throw new InvalidOperationException(
                "Fallout 2 saved trial state is incomplete for its declared stage.");
    }
}

internal sealed class Fo2ArroyoTrialRuntime
{
    private readonly Fo2ArroyoTrialRouteContract _contract;
    private readonly Fo2ArroyoCavesPresentationCatalog _catalog;
    private readonly Fo2ArroyoCavesPlayerBody _player;
    private readonly Node3D _worldParent;
    private readonly Dictionary<int, Node3D> _elevationRoots;
    private readonly Dictionary<int, HashSet<int>> _admittedTiles;
    private int _approachIndex;
    private int _returnIndex;
    private int _dialogueIndex;
    private int _villageIndex;
    private readonly ClassicDoorSession _cameronDoor;
    private ClassicDoorPlayback? _cameronDoorPlayback;

    private Fo2ArroyoTrialRuntime(
        Fo2ArroyoTrialRouteContract contract,
        Fo2ArroyoCavesPresentationCatalog catalog,
        Fo2ArroyoCavesSceneCoverage scene,
        Fo2ArroyoCavesPlayerBody player,
        Node3D worldParent,
        Fo2ArroyoTrialProgressState state)
    {
        _contract = contract;
        _catalog = catalog;
        _player = player;
        _worldParent = worldParent;
        _elevationRoots = new Dictionary<int, Node3D> { [0] = scene.Root };
        _admittedTiles = Enumerable.Range(0, 3).ToDictionary(
            elevation => elevation,
            elevation => contract.ApproachCameron.Steps
                .Concat(contract.ReturnToTemple.Steps)
                .Where(row => row.Elevation == elevation)
                .Select(row => row.Tile)
                .ToHashSet());
        State = state;
        State.Validate(contract);
        _cameronDoor = new ClassicDoorSession(
            contract.Cameron.ReleaseDoorPresentation,
            state.CameronDoorPlaybackState);
        _dialogueIndex = state.CameronDialogueSelections;
        _player.PersistenceBoundaryReached += OnPlayerPersistenceBoundary;
        if (state.Stage != Fo2ArroyoTrialProgressState.InitialStage)
            BindRestoredCameronDoorPlayback();
    }

    internal event Action? StateChanged;
    internal Fo2ArroyoTrialProgressState State { get; private set; }
    internal Fo2ArroyoTrialRouteContract Contract => _contract;
    internal int ApproachIndex => _approachIndex;
    internal int ReturnIndex => _returnIndex;
    internal int VillageIndex => _villageIndex;

    internal static Fo2ArroyoTrialRuntime Build(
        Fo2ArroyoTrialRouteContract contract,
        Fo2ArroyoCavesPresentationCatalog catalog,
        Fo2ArroyoCavesSceneCoverage scene,
        Fo2ArroyoCavesPlayerBody player,
        Node3D worldParent,
        Fo2ArroyoTrialProgressState? restored = null)
    {
        if (contract.SourceProfileId != catalog.SourceProfileId ||
            contract.ArroyoSourceSha256 != catalog.SourceManifestSha256 ||
            player.CurrentMapIndex != Fo2ArroyoCavesPresentationCatalog.MapIndex)
            throw new InvalidOperationException(
                "Fallout 2 trial runtime does not match the live ARCAVES source.");
        return new Fo2ArroyoTrialRuntime(
            contract,
            catalog,
            scene,
            player,
            worldParent,
            restored ?? Fo2ArroyoTrialProgressState.Initial(contract));
    }

    internal void TraverseApproach()
    {
        RequireStage(Fo2ArroyoTrialProgressState.InitialStage);
        ApplyPath(_contract.ApproachCameron, ref _approachIndex);
        if (Fo1HexMath.Distance(_player.CurrentTile, _contract.Cameron.Tile) != 1 ||
            _player.CurrentElevation != _contract.Cameron.Elevation)
            throw new InvalidOperationException(
                "Fallout 2 Cameron approach did not finish on an adjacent source hex.");
    }

    internal string SelectTaggedSpeechOption(
        int messageId,
        Fo2CharacterSelection character)
    {
        RequireStage(Fo2ArroyoTrialProgressState.InitialStage);
        var branch = _contract.Cameron.TaggedSpeechBranch;
        if (Fo2TempleConfrontationRuntime.EffectiveIntelligence(character) <
                branch.MinimumIntelligence ||
            !character.Profile.TaggedSkills.Contains(
                branch.RequiredTaggedSkill,
                StringComparer.Ordinal) ||
            _dialogueIndex >= branch.SelectedMessageIds.Count ||
            messageId != branch.SelectedMessageIds[_dialogueIndex] ||
            Fo1HexMath.Distance(_player.CurrentTile, _contract.Cameron.Tile) != 1)
            throw new InvalidOperationException(
                "Fallout 2 Cameron reply is outside the exact tagged-Speech branch.");
        _dialogueIndex++;
        if (_dialogueIndex == branch.SelectedMessageIds.Count)
            CompleteNegotiation();
        else
        {
            State = State with { CameronDialogueSelections = _dialogueIndex };
            StateChanged?.Invoke();
        }
        return branch.Messages[messageId];
    }

    internal void TraverseReturn()
    {
        RequireStage(Fo2ArroyoTrialProgressState.NegotiatedStage);
        ApplyPath(_contract.ReturnToTemple, ref _returnIndex);
        State = State with { Stage = Fo2ArroyoTrialProgressState.ReturnedStage };
        State.Validate(_contract);
        StateChanged?.Invoke();
    }

    internal void ApplyKlintMapEnter(
        Fo2TempleSceneCoverage scene,
        Fo2ArroyoCavesPlayerBody player)
    {
        if (State.Stage != Fo2ArroyoTrialProgressState.ReturnedStage &&
            State.Stage != Fo2ArroyoTrialProgressState.GateMovedStage &&
            State.Stage != Fo2ArroyoTrialProgressState.VillageArrivalStage &&
            State.Stage != Fo2ArroyoTrialProgressState.VillageFirstActionStage)
            throw new InvalidOperationException(
                "Fallout 2 ACKlint map_enter ran before Cameron completed the trial.");
        var gate = NodeTraversal.Descendants<Sprite3D>(scene.Root).SingleOrDefault(row =>
            row.HasMeta("map_serial") &&
            row.GetMeta("map_serial").AsInt32() == _contract.KlintGate.GateSerial) ??
            throw new InvalidOperationException("Fallout 2 Klint gate sprite is absent.");
        if (gate.GetMeta("source_pid").AsString() != _contract.KlintGate.GatePid ||
            gate.GetMeta("map_tile").AsInt32() != _contract.KlintGate.SourceTile)
            throw new InvalidOperationException("Fallout 2 Klint gate source identity drifted.");
        gate.Position = Fo1HexMath.Center(_contract.KlintGate.DestinationTile);
        gate.SetMeta("map_tile", _contract.KlintGate.DestinationTile);
        gate.SetMeta("acklint_map_enter_applied", true);
        gate.SetMeta("required_global_10", _contract.KlintGate.RequiredGlobalVariable10);
        var klint = NodeTraversal.Descendants<Sprite3D>(scene.Root).SingleOrDefault(row =>
            row.HasMeta("map_serial") &&
            row.GetMeta("map_serial").AsInt32() == _contract.KlintGate.ActorSerial) ??
            throw new InvalidOperationException("Fallout 2 Klint actor is absent.");
        if (!klint.Visible || klint.GetMeta("source_script_index").AsInt32() !=
                _contract.KlintGate.ActorScriptIndex)
            throw new InvalidOperationException(
                "Fallout 2 ACKlint map_enter may not remove or replace Klint.");
        player.AdmitPostTrialTempleRoute(
            _contract.Village.Path.Steps.Select(row => row.Tile).ToArray(),
            _contract.KlintGate.PostMoveWalkMaskSha256);
        _villageIndex = 0;
        State = State with
        {
            Stage = State.Stage is Fo2ArroyoTrialProgressState.VillageArrivalStage or
                Fo2ArroyoTrialProgressState.VillageFirstActionStage
                ? State.Stage
                : Fo2ArroyoTrialProgressState.GateMovedStage,
            KlintGateTile = _contract.KlintGate.DestinationTile,
        };
        State.Validate(_contract);
        StateChanged?.Invoke();
    }

    internal void TraverseVillageRoute()
    {
        RequireStage(Fo2ArroyoTrialProgressState.GateMovedStage);
        var steps = _contract.Village.Path.Steps;
        if (_player.CurrentTile != steps[0].Tile)
            throw new InvalidOperationException(
                "Fallout 2 village path did not begin at the owned Temple entry.");
        for (_villageIndex = 1; _villageIndex < steps.Count; _villageIndex++)
            if (!_player.TryPostTrialTempleStep(steps[_villageIndex].Tile))
                throw new InvalidOperationException(
                    $"Fallout 2 village source step was rejected: {steps[_villageIndex].Tile}");
    }

    internal Fo2TempleAppliedTransition ApplyVillageExit(
        Fo2TempleTransitionRuntime transition,
        Fo2ArvillagSceneCoverage destination)
    {
        RequireStage(Fo2ArroyoTrialProgressState.GateMovedStage);
        if (_villageIndex != _contract.Village.Path.Steps.Count ||
            _player.CurrentTile != _contract.Village.SourceTile ||
            !transition.TryApplyPostTrial(_contract.Village))
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG exit did not match the admitted post-gate route.");
        var applied = transition.Applied ?? throw new InvalidOperationException(
            "Fallout 2 ARVILLAG transition did not publish its owned destination.");
        State = State with
        {
            Stage = Fo2ArroyoTrialProgressState.VillageArrivalStage,
            VillageRouteCompleted = true,
            VillageCurrentTile = _contract.VillageArrival.ArrivalTile,
        };
        State.Validate(_contract);
        _player.EnterVillage(destination, _contract.VillageArrival);
        StateChanged?.Invoke();
        return applied;
    }

    internal void ApplyVillageFirstLegalAction()
    {
        RequireStage(Fo2ArroyoTrialProgressState.VillageArrivalStage);
        _player.ApplyVillageFirstAction(_contract.VillageArrival);
        State = State with
        {
            Stage = Fo2ArroyoTrialProgressState.VillageFirstActionStage,
            VillageCurrentTile = _contract.VillageArrival.FirstActionToTile,
            VillageFirstActionApplied = true,
        };
        State.Validate(_contract);
        StateChanged?.Invoke();
    }

    private void OnPlayerPersistenceBoundary()
    {
        if (State.Stage != Fo2ArroyoTrialProgressState.VillageArrivalStage ||
            _player.CurrentMapIndex != _contract.VillageArrival.MapIndex ||
            _player.CurrentTile != _contract.VillageArrival.FirstActionToTile)
            return;
        _player.ConfirmVillageFirstActionFromLiveMovement(_contract.VillageArrival);
        State = State with
        {
            Stage = Fo2ArroyoTrialProgressState.VillageFirstActionStage,
            VillageCurrentTile = _contract.VillageArrival.FirstActionToTile,
            VillageFirstActionApplied = true,
        };
        State.Validate(_contract);
        StateChanged?.Invoke();
    }

    private void ApplyPath(Fo2TrialRoutePath path, ref int index)
    {
        if (_player.CurrentMapIndex != Fo2ArroyoCavesPresentationCatalog.MapIndex ||
            _player.CurrentElevation != path.Steps[0].Elevation ||
            _player.CurrentTile != path.Steps[0].Tile)
            throw new InvalidOperationException(
                "Fallout 2 trial route did not begin at its exact source state.");
        for (index = 1; index < path.Steps.Count; index++)
        {
            var step = path.Steps[index];
            var root = ElevationRoot(step.Elevation, step.Tile, step.Rotation);
            SetActiveElevation(step.Elevation);
            _player.ApplyArroyoTrialStep(
                step,
                root,
                _admittedTiles[step.Elevation],
                _contract.WalkMaskSha256[step.Elevation]);
        }
    }

    private Node3D ElevationRoot(int elevation, int arrivalTile, int rotation)
    {
        if (_elevationRoots.TryGetValue(elevation, out var root))
            return root;
        var scene = Fo2MapSceneBuilder.Build(
            _worldParent,
            Fo2ArroyoCavesPresentationCatalog.MapIndex,
            "ARCAVES.MAP",
            _catalog.MapSha256,
            elevation,
            arrivalTile,
            rotation,
            Fo2ArroyoCavesPresentationCatalog.DefaultFloorTileId,
            _catalog.TileEntriesByElevation[elevation],
            _catalog.Artifacts,
            _catalog.TileBindings,
            _catalog.ObjectPlacements);
        root = scene.Root;
        root.Name = $"FO2_MAP_003_ARCAVES_SOURCE_ELEVATION_{elevation}";
        root.SetMeta("trial_route_sha256", _contract.Sha256);
        root.SetMeta("trial_route_bounded_transport", true);
        _elevationRoots.Add(elevation, root);
        return root;
    }

    private void SetActiveElevation(int elevation)
    {
        foreach (var row in _elevationRoots)
            row.Value.Visible = row.Key == elevation;
    }

    private void CompleteNegotiation()
    {
        var branch = _contract.Cameron.TaggedSpeechBranch;
        var root = ElevationRoot(_contract.Cameron.Elevation, _contract.Cameron.Tile, 0);
        var actor = NodeTraversal.Descendants<Sprite3D>(root).SingleOrDefault(row =>
            row.HasMeta("map_serial") &&
            row.GetMeta("map_serial").AsInt32() == _contract.Cameron.Serial) ??
            throw new InvalidOperationException("Fallout 2 Cameron sprite is absent.");
        var door = NodeTraversal.Descendants<Sprite3D>(root).SingleOrDefault(row =>
            row.HasMeta("map_serial") &&
            row.GetMeta("map_serial").AsInt32() == _contract.Cameron.ReleaseDoorSerial) ??
            throw new InvalidOperationException("Fallout 2 Cameron door sprite is absent.");
        foreach (var tile in _contract.Cameron.ReleaseActorTiles)
        {
            actor.Position = Fo1HexMath.Center(tile);
            actor.SetMeta("map_tile", tile);
        }
        actor.Visible = _contract.Cameron.ReleaseFinalVisible;
        actor.SetMeta("actemvil_release_applied", true);
        _cameronDoorPlayback = new ClassicDoorPlayback(
            _cameronDoor,
            door,
            state => ApplyCameronDoorPlaybackState(door, state));
        door.AddChild(_cameronDoorPlayback);
        var doorState = _cameronDoorPlayback.BeginOpening();
        if (doorState.Open != _contract.Cameron.ReleaseDoorOpened)
            throw new InvalidOperationException(
                "Fallout 2 Cameron door source presentation disagrees with decoded release state.");
        door.SetMeta("source_door_unlocked", _contract.Cameron.ReleaseDoorUnlocked);
        State = State with
        {
            Stage = Fo2ArroyoTrialProgressState.NegotiatedStage,
            GlobalVariable10 = branch.GlobalVariable10,
            CameronLocalVariable12 = branch.LocalVariable12,
            CameronLocalVariable13 = branch.LocalVariable13,
            CameronMapVariable20 = branch.MapVariable20,
            CameronDialogueSelections = _dialogueIndex,
            CameronTile = _contract.Cameron.ReleaseActorTiles[^1],
            CameronVisible = _contract.Cameron.ReleaseFinalVisible,
            CameronDoorOpened = doorState.Open,
            CameronDoorUnlocked = _contract.Cameron.ReleaseDoorUnlocked,
            CameronDoorPlaybackState = doorState,
        };
        State.Validate(_contract);
        StateChanged?.Invoke();
    }

    private void BindRestoredCameronDoorPlayback()
    {
        var root = ElevationRoot(_contract.Cameron.Elevation, _contract.Cameron.Tile, 0);
        var door = NodeTraversal.Descendants<Sprite3D>(root).SingleOrDefault(row =>
            row.HasMeta("map_serial") &&
            row.GetMeta("map_serial").AsInt32() == _contract.Cameron.ReleaseDoorSerial) ??
            throw new InvalidOperationException(
                "Fallout 2 restored Cameron door sprite is absent.");
        _cameronDoorPlayback = new ClassicDoorPlayback(
            _cameronDoor,
            door,
            state => ApplyCameronDoorPlaybackState(door, state));
        door.AddChild(_cameronDoorPlayback);
        ApplyCameronDoorPlaybackState(door, _cameronDoor.State);
    }

    private void ApplyCameronDoorPlaybackState(Sprite3D door, ClassicDoorState doorState)
    {
        door.SetMeta("source_door_open", doorState.Open);
        door.SetMeta("source_door_blocked", doorState.Blocked);
        door.SetMeta("source_door_frame", doorState.Frame);
        door.SetMeta(
            "source_door_frames_per_second",
            _contract.Cameron.ReleaseDoorPresentation.StoredFramesPerSecond);
        door.SetMeta("source_door_phase", doorState.Phase);
        if (doorState.LastSoundLogicalPath is { } sound)
            door.SetMeta("source_door_sound", sound);
        if (State.Stage == Fo2ArroyoTrialProgressState.InitialStage)
            return;
        State = State with
        {
            CameronDoorOpened = doorState.Open,
            CameronDoorPlaybackState = doorState,
        };
        State.Validate(_contract);
        StateChanged?.Invoke();
    }

    private void RequireStage(string expected)
    {
        if (State.Stage != expected)
            throw new InvalidOperationException(
                $"Fallout 2 trial stage {State.Stage} cannot perform {expected}.");
    }

}
