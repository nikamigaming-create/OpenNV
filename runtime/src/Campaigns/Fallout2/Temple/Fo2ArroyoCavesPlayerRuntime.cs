using Godot;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArroyoCavesPlayerRuntimeCoverage(
    Fo2ArroyoPlayerProfile Profile,
    Fo2ArroyoPlayerPresentationCatalog PlayerPresentation,
    Fo2ArroyoPlayerPresentationSource SelectedPlayerPresentation,
    string FloorCollisionPath,
    int FloorSupportPatches,
    int FloorCollisionTriangles,
    int ArrivalComponentHexes,
    Fo2ArroyoCavesPlayerBody Player,
    Fo2ArroyoClassicGameplayHud Hud);

internal static class Fo2ArroyoCavesPlayerRuntime
{
    internal static Fo2ArroyoCavesPlayerRuntimeCoverage Build(
        Fo2ArroyoCavesPresentationCatalog catalog,
        Fo2ArroyoCavesSceneCoverage scene,
        Fo2ArroyoPlayerPresentationCatalog playerPresentation,
        Fo2ArroyoPlayerPresentationSource? selectedPlayerPresentation = null,
        Fo2CharacterSelection? selectedCharacter = null,
        Fo2HumanoidDonorContract? humanoidDonor = null)
    {
        var selectedPresentation = selectedPlayerPresentation ?? playerPresentation.Source;
        if (playerPresentation.SourceProfileId != catalog.SourceProfileId)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo player/map source profiles differ.");
        if (selectedPresentation.SourceProfileId != catalog.SourceProfileId)
            throw new InvalidOperationException(
                "Fallout 2 selected character/map source profiles differ.");
        if (selectedCharacter is not null && humanoidDonor is not null &&
            humanoidDonor.ForSex(selectedCharacter.Profile.Sex).OutfitFormId !=
                playerPresentation.Live3DPresentationOutfitFormId)
            throw new InvalidOperationException(
                "Fallout 2 owned humanoid donor does not match the source-role 3D binding.");
        var profile = Fo2ArroyoPlayerProfile.Load(catalog);
        var component = Fo2ArroyoArrivalFirstBeat.RequireArrivalComponent(catalog).ToHashSet();

        var floorIds = catalog.TileEntries
            .Select(entry => (int)(entry & 0x0fff))
            .ToArray();
        var floorPatches = Enumerable.Range(0, floorIds.Length)
            .Where(index => floorIds[index] != Fo2ArroyoCavesPresentationCatalog.DefaultFloorTileId)
            .ToArray();
        if (floorPatches.Length != scene.ConstructedFloorPatches)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo player floor-support coverage drifted.");
        var floorMesh = BuildFloorCollisionMesh(floorPatches);
        var floorShape = floorMesh.CreateTrimeshShape() ??
            throw new InvalidOperationException(
                "Could not build Fallout 2 Arroyo source floor support.");
        if (floorShape is ConcavePolygonShape3D concave)
            concave.BackfaceCollision = true;
        var physicsRoot = new Node3D { Name = "MAP_3_PLAYER_RUNTIME_PHYSICS" };
        scene.Root.AddChild(physicsRoot);
        var floorBody = new StaticBody3D
        {
            Name = "NON_DEFAULT_SOURCE_FLOOR_PATCH_SUPPORT",
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        floorBody.SetMeta("collision_mode", profile.FloorCollisionMode);
        floorBody.SetMeta("source_map_sha256", catalog.MapSha256);
        floorBody.SetMeta("source_walk_mask_sha256", catalog.WalkMaskSha256);
        floorBody.AddChild(new CollisionShape3D
        {
            Name = "SOURCE_FLOOR_PATCH_TRIMESH_COLLISION",
            Shape = floorShape,
        });
        physicsRoot.AddChild(floorBody);

        Fo2ArroyoCavesInput.Configure(profile);
        var player = new Fo2ArroyoCavesPlayerBody();
        scene.Root.AddChild(player);
        player.Configure(
            catalog,
            profile,
            component,
            selectedPresentation,
            scene.SourcePixelsPerMeter,
            scene.Molded3D.Profile,
            selectedCharacter,
            humanoidDonor);
        var hud = Fo2ArroyoClassicGameplayHud.Build(scene.Root, catalog);
        if (selectedCharacter is not null)
            hud.BindCharacter(selectedCharacter);
        player.BlockedMovementChanged += hud.SetBlockedMovement;
        return new Fo2ArroyoCavesPlayerRuntimeCoverage(
            profile,
            playerPresentation,
            selectedPresentation,
            floorBody.GetPath().ToString(),
            floorPatches.Length,
            floorPatches.Length * 2,
            component.Count,
            player,
            hud);
    }

    internal static ArrayMesh BuildFloorCollisionMesh(IReadOnlyCollection<int> floorPatches)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        var halfX = Fo1HexMath.ColumnSpacingMeters;
        var halfZ = Fo1HexMath.FlatToFlatMeters;
        foreach (var index in floorPatches)
        {
            var center = Fo1HexMath.FloorPatchCenter(index);
            var first = center + new Vector3(-halfX, 0.0f, -halfZ);
            var second = center + new Vector3(-halfX, 0.0f, halfZ);
            var third = center + new Vector3(halfX, 0.0f, halfZ);
            var fourth = center + new Vector3(halfX, 0.0f, -halfZ);
            surface.SetNormal(Vector3.Up);
            surface.AddVertex(first);
            surface.SetNormal(Vector3.Up);
            surface.AddVertex(second);
            surface.SetNormal(Vector3.Up);
            surface.AddVertex(third);
            surface.SetNormal(Vector3.Up);
            surface.AddVertex(first);
            surface.SetNormal(Vector3.Up);
            surface.AddVertex(third);
            surface.SetNormal(Vector3.Up);
            surface.AddVertex(fourth);
        }
        surface.Index();
        return surface.Commit() ?? throw new InvalidOperationException(
            "Fallout 2 Arroyo source floor support mesh is empty.");
    }

}

internal sealed partial class Fo2ArroyoCavesPlayerBody : CharacterBody3D
{
    private const float Half = 0.5f;
    private Fo2ArroyoPlayerProfile? _profile;
    private HashSet<int>? _arrivalComponent;
    private Fo2ArroyoPlayerPresentation? _presentation;
    private Fo2CharacterSelection? _selectedCharacter;
    private Fo2ArroyoPlayerPresentationSource? _selectedSourcePresentation;
    private Fo2HumanoidDonorContract? _humanoidDonor;
    private Fo2HumanoidVisual? _villageHumanoid;
    private IReadOnlyDictionary<int, float>? _villageFloorHeightByTile;
    private Vector3 _spawnWorldMeters;
    private bool _controlsEnabled = true;
    private bool _requireNeutralInput;
    private bool _blockedMovement;

    internal event Action<bool>? BlockedMovementChanged;

    internal int ArrivalTile { get; private set; }
    internal int CurrentMapIndex { get; private set; }
    internal int CurrentElevation { get; private set; }
    internal string CurrentMapSha256 { get; private set; } = "";
    internal string CurrentWalkMaskSha256 { get; private set; } = "";
    internal int CurrentTile { get; private set; }
    internal int CompletedTileTransitions { get; private set; }
    internal int RejectedMovementFrames { get; private set; }
    internal int LastRejectedCandidateTile { get; private set; } = -1;
    internal bool ControlsEnabled => _controlsEnabled;
    internal float CameraSizeMeters { get; private set; }
    internal float CameraSourcePixelScale { get; private set; }
    internal float CameraVisibleSourceFrameHeightPixels { get; private set; }
    internal float CameraSourceFrameCropPixels { get; private set; }
    internal float CameraWorldViewportHeightPixels { get; private set; }
    internal string? VillageArrivalFramingMode { get; private set; }
    internal IReadOnlyList<int> VillageArrivalFramingSourceSerials { get; private set; } =
        Array.Empty<int>();
    internal Vector3 VillageArrivalFramingFocusWorldMeters { get; private set; }
    internal event Action? PersistenceBoundaryReached;
    internal Vector3 SpawnWorldMeters => _spawnWorldMeters;
    internal Fo2ArroyoPlayerPresentation Presentation => _presentation ??
        throw new InvalidOperationException("Fallout 2 player presentation is not configured.");
    internal Fo2HumanoidVisual? VillageHumanoid => _villageHumanoid;
    internal float HorizontalDistanceFromSpawn => new Vector2(
        Position.X - _spawnWorldMeters.X,
        Position.Z - _spawnWorldMeters.Z).Length();

    internal void Configure(
        Fo2ArroyoCavesPresentationCatalog catalog,
        Fo2ArroyoPlayerProfile profile,
        HashSet<int> arrivalComponent,
        Fo2ArroyoPlayerPresentationSource playerPresentation,
        float sourcePixelsPerMeter,
        Fo2ArroyoCaves3DProfile presentationProfile,
        Fo2CharacterSelection? selectedCharacter,
        Fo2HumanoidDonorContract? humanoidDonor)
    {
        if (_profile is not null ||
            arrivalComponent.Count != catalog.ArrivalComponentHexes ||
            !arrivalComponent.Contains(catalog.ArrivalTile))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo player was configured more than once or without its arrival component.");
        _profile = profile;
        _selectedCharacter = selectedCharacter;
        _selectedSourcePresentation = playerPresentation;
        _humanoidDonor = humanoidDonor;
        _arrivalComponent = arrivalComponent.ToHashSet();
        ArrivalTile = catalog.ArrivalTile;
        CurrentMapIndex = Fo2ArroyoCavesPresentationCatalog.MapIndex;
        CurrentElevation = Fo2ArroyoCavesPresentationCatalog.Elevation;
        CurrentMapSha256 = catalog.MapSha256;
        CurrentWalkMaskSha256 = catalog.WalkMaskSha256;
        CurrentTile = ArrivalTile;
        _spawnWorldMeters = Fo1HexMath.Center(ArrivalTile) +
            Vector3.Up * profile.SpawnCenterHeightMeters;
        Position = _spawnWorldMeters;
        Name = "MAP_3_ARRIVAL_CHOSEN_ONE_PLAYER_BODY";
        CollisionLayer = 1;
        CollisionMask = 1;
        MotionMode = MotionModeEnum.Grounded;
        UpDirection = Vector3.Up;
        FloorSnapLength = profile.FloorSnapLengthMeters;
        FloorMaxAngle = profile.MaximumFloorAngleRadians;
        SafeMargin = profile.SafeMarginMeters;
        SetMeta("player_runtime_profile_sha256", profile.Sha256);
        SetMeta("source_map_sha256", catalog.MapSha256);
        SetMeta("source_walk_mask_sha256", catalog.WalkMaskSha256);
        SetMeta("current_map_index", CurrentMapIndex);
        SetMeta("current_elevation", CurrentElevation);
        SetMeta("blocked_movement_mode", profile.BlockedMovementMode);
        SetMeta("arrival_tile", ArrivalTile);
        SetMeta("arrival_rotation", catalog.ArrivalRotation);
        SetMeta("character_art_loaded", true);
        SetMeta("character_fid", playerPresentation.Fid);
        SetMeta("character_source_sha256", playerPresentation.SourceSha256);
        SetMeta("character_identity_authority", "owned-fallout2-gcd-pro-fid-frm");
        SetMeta("humanoid_visual_parity", false);
        AddChild(new CollisionShape3D
        {
            Name = "PLAYER_CAPSULE_COLLISION",
            Position = Vector3.Zero,
            Shape = new CapsuleShape3D
            {
                Radius = profile.CapsuleRadiusMeters,
                Height = profile.CapsuleHeightMeters,
            },
        });
        _presentation = new Fo2ArroyoPlayerPresentation(
            playerPresentation,
            sourcePixelsPerMeter,
            profile.SpawnCenterHeightMeters,
            catalog.ArrivalRotation);
        AddChild(_presentation);
        if ((selectedCharacter is null) != (humanoidDonor is null))
            throw new InvalidOperationException(
                "Fallout 2 selected character and owned humanoid donor must be bound together.");
        if (selectedCharacter is not null && humanoidDonor is not null)
        {
            _villageHumanoid = new Fo2HumanoidVisual(
                Fo2HumanoidIdentity.FromSelection(selectedCharacter, playerPresentation),
                humanoidDonor,
                selectedCharacter.Appearance.BodyProportions)
            {
                Name = "MAP3_SELECTED_HASH_BOUND_FULL_BODY_PLAYER",
                Position = Vector3.Down * profile.SpawnCenterHeightMeters,
            };
            AddChild(_villageHumanoid);
            GroundVillageHumanoid(CurrentTile);
            _villageHumanoid.ApplyPresentationLighting(
                presentationProfile,
                profile.CameraFarMeters);
            _villageHumanoid.SetDirection(catalog.ArrivalRotation);
            _villageHumanoid.SetEquipmentState(
                _presentation.SpearEquipped,
                Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedItemFid,
                Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedItemPid,
                Fo2ArroyoPlayerPresentationCatalog.ExpectedWeaponAnimationCode,
                Fo2ArroyoPlayerPresentationCatalog.EquippedGeometryDisposition);
            if (!_villageHumanoid.UsesOwnedDonor ||
                !_villageHumanoid.EquipmentSocketResolved ||
                _villageHumanoid.MeshInstances <= 0 ||
                _villageHumanoid.AuthoredSurfaces <= 0 ||
                _villageHumanoid.LitMaterials <= 0)
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo full-body donor did not load its owned assembly.");
            _presentation.Visible = false;
        }
        var camera = new Camera3D
        {
            Name = "ARROYO_PLAYER_FOLLOW_CAMERA",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Near = profile.CameraNearMeters,
            Far = profile.CameraFarMeters,
            Position = profile.CameraOffsetMeters,
            Current = true,
        };
        AddChild(camera);
        camera.LookAt(
            GlobalPosition + Vector3.Up * profile.CameraLookHeightMeters,
            Vector3.Up);
        var headlessSourceFrame = DisplayServer.GetName() == "headless";
        var viewportPixels = headlessSourceFrame
            ? new Vector2(
                profile.CameraSourceFramePixels.X,
                profile.CameraSourceFramePixels.Y)
            : GetViewport().GetVisibleRect().Size;
        CameraSourcePixelScale = Fo2ArroyoClassicGameplayHud.SourcePixelScale(
            viewportPixels,
            catalog.ClassicHud);
        CameraVisibleSourceFrameHeightPixels =
            viewportPixels.Y / CameraSourcePixelScale;
        CameraSourceFrameCropPixels = profile.CameraSourceFramePixels.Y -
            CameraVisibleSourceFrameHeightPixels;
        CameraWorldViewportHeightPixels = CameraVisibleSourceFrameHeightPixels -
            profile.CameraSourceHudCropHeightPixels;
        var sourceVerticalProjection = MathF.Abs(camera.Basis.Y.Dot(Vector3.Up));
        CameraSizeMeters = viewportPixels.Y * sourceVerticalProjection /
            (sourcePixelsPerMeter * CameraSourcePixelScale);
        if (!float.IsFinite(CameraSizeMeters) || CameraSizeMeters <= 0.0f ||
            CameraSourceFrameCropPixels < 0.0f ||
            CameraWorldViewportHeightPixels <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 2 owned-frame camera composition could not be derived.");
        camera.Size = CameraSizeMeters;
        SetMeta("camera_composition_mode", profile.CameraCompositionMode);
        SetMeta("camera_source_pixel_scale", CameraSourcePixelScale);
        SetMeta("camera_visible_source_frame_height_pixels",
            CameraVisibleSourceFrameHeightPixels);
        SetMeta("camera_source_frame_crop_pixels", CameraSourceFrameCropPixels);
        SetMeta("camera_world_viewport_height_pixels", CameraWorldViewportHeightPixels);
        SetMeta("camera_size_meters", CameraSizeMeters);
        SetMeta("camera_headless_source_frame_contract", headlessSourceFrame);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_profile is null || _arrivalComponent is null)
            return;
        var input = Input.GetVector(
            _profile.MoveLeft.Action,
            _profile.MoveRight.Action,
            _profile.MoveForward.Action,
            _profile.MoveBackward.Action);
        if (!_controlsEnabled)
            input = Vector2.Zero;
        if (_requireNeutralInput)
        {
            if (input.IsZeroApprox())
                _requireNeutralInput = false;
            else
                input = Vector2.Zero;
        }
        var desired = new Vector3(input.X, 0.0f, input.Y);
        if (desired.LengthSquared() > 1.0f)
            desired = desired.Normalized();
        var direction = desired.IsZeroApprox()
            ? (int?)null
            : DirectionForMovement(CurrentTile, desired);
        var previousDirection = Presentation.Direction;
        var before = Position;
        var sourceHexDirection = direction.HasValue
            ? (Fo1HexMath.Center(Fo1HexMath.TileInDirection(CurrentTile, direction.Value)) -
                Fo1HexMath.Center(CurrentTile)).Normalized()
            : Vector3.Zero;
        var horizontal = sourceHexDirection * _profile.SpeedMetersPerSecond;
        var candidatePosition = before + horizontal * (float)delta;
        var candidateTile = Fo1HexMath.NearestTile(new Vector3(
            candidatePosition.X,
            0.0f,
            candidatePosition.Z));
        var blockedMovement = !desired.IsZeroApprox() && !CanOccupy(candidateTile);
        if (blockedMovement)
        {
            horizontal = Vector3.Zero;
            RejectedMovementFrames++;
            LastRejectedCandidateTile = candidateTile;
        }
        var nextBlockedState = blockedMovement ||
            (desired.IsZeroApprox() && _blockedMovement);
        if (_blockedMovement != nextBlockedState)
        {
            _blockedMovement = nextBlockedState;
            BlockedMovementChanged?.Invoke(nextBlockedState);
        }
        var vertical = IsOnFloor()
            ? _profile.GroundVelocityMetersPerSecond
            : Velocity.Y - _profile.GravityMetersPerSecondSquared * (float)delta;
        Velocity = new Vector3(horizontal.X, vertical, horizontal.Z);
        MoveAndSlide();
        var movedTile = Fo1HexMath.NearestTile(new Vector3(Position.X, 0.0f, Position.Z));
        if (!CanOccupy(movedTile))
        {
            Position = new Vector3(before.X, Position.Y, before.Z);
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
            RejectedMovementFrames++;
            LastRejectedCandidateTile = movedTile;
            movedTile = CurrentTile;
        }
        var movedHorizontally = !new Vector2(
            Position.X - before.X,
            Position.Z - before.Z).IsZeroApprox();
        if (movedHorizontally && direction.HasValue)
        {
            Presentation.StartWalking(direction.Value);
            _villageHumanoid?.SetDirection(direction.Value);
            _villageHumanoid?.SetWalking(true);
        }
        else if (direction.HasValue && previousDirection != direction.Value)
        {
            Presentation.SetDirection(direction.Value);
            _villageHumanoid?.SetDirection(direction.Value);
            _villageHumanoid?.SetWalking(false);
        }
        else
        {
            Presentation.StopWalking();
            _villageHumanoid?.SetWalking(false);
        }
        if (direction.HasValue && previousDirection != direction.Value)
            PersistenceBoundaryReached?.Invoke();
        if (movedTile != CurrentTile)
        {
            CurrentTile = movedTile;
            GroundVillageHumanoid(CurrentTile);
            CompletedTileTransitions++;
            SetMeta("current_tile", CurrentTile);
            PersistenceBoundaryReached?.Invoke();
        }
    }

    internal void SetControlsEnabled(bool enabled)
    {
        _controlsEnabled = enabled;
        if (enabled)
            _requireNeutralInput = true;
        Presentation.StopWalking();
        _villageHumanoid?.SetWalking(false);
        SetMeta("opening_controls_enabled", enabled);
    }

    internal void Restore(int tile, Vector3 position, int rotation)
    {
        if (_profile is null || _arrivalComponent is null || _presentation is null ||
            !CanOccupy(tile) ||
            !float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            !float.IsFinite(position.Z) ||
            Fo1HexMath.NearestTile(new Vector3(position.X, 0.0f, position.Z)) != tile ||
            MathF.Abs(position.Y - _profile.SpawnCenterHeightMeters) >
                _profile.FloorSnapLengthMeters ||
            rotation is < 0 or >= Fo1HexMath.DirectionCount)
            throw new InvalidOperationException(
                "Fallout 2 saved player state is outside the admitted active-map runtime.");
        Position = position;
        Velocity = Vector3.Zero;
        CurrentTile = tile;
        _presentation.SetDirection(rotation);
        _villageHumanoid?.SetDirection(rotation);
        GroundVillageHumanoid(CurrentTile);
        SetMeta("current_tile", CurrentTile);
        SetMeta("restored_from_save", true);
    }

    internal bool TryTacticalStep(int destinationTile)
    {
        if (_profile is null || _presentation is null ||
            !CanOccupy(destinationTile) ||
            !Fo1HexMath.Neighbors(CurrentTile).Contains(destinationTile))
            return false;
        var origin = Fo1HexMath.Center(CurrentTile);
        var destination = Fo1HexMath.Center(destinationTile);
        var direction = DirectionForMovement(CurrentTile, destination - origin);
        Position = destination + Vector3.Up * _profile.SpawnCenterHeightMeters;
        Velocity = Vector3.Zero;
        CurrentTile = destinationTile;
        CompletedTileTransitions++;
        _presentation.SetDirection(direction);
        SetMeta("current_tile", CurrentTile);
        SetMeta("last_tactical_step_source_bound", true);
        return true;
    }

    internal void EnterTemple(
        Fo2TempleSceneCoverage destination,
        Fo2ArroyoExitTransition transition)
    {
        if (_profile is null || _arrivalComponent is null || _presentation is null ||
            CurrentMapIndex != transition.SourceMapIndex ||
            CurrentElevation != transition.SourceElevation ||
            CurrentMapSha256 != transition.SourceMapSha256 ||
            CurrentTile != transition.SourceTile ||
            destination.MapSha256 != transition.TargetMapSha256 ||
            destination.EntryElevation != transition.TargetElevation ||
            !destination.Topology.Movement.CanReachFromEntry(transition.TargetTile) ||
            destination.Topology.WalkMaskSha256.Length != 64)
            throw new InvalidOperationException(
                "Fallout 2 source exit cannot enter the admitted Temple destination.");
        _ = ClassicMapJoinOwner.Commit(
            new ClassicMapJoin(
                transition.ExitSerial,
                new ClassicMapEndpoint(
                    transition.SourceMapIndex,
                    null,
                    transition.SourceMapSha256,
                    transition.SourceTile,
                    transition.SourceElevation,
                    null),
                new ClassicMapEndpoint(
                    transition.TargetMapIndex,
                    Path.GetFileName(transition.TargetLogicalPath),
                    transition.TargetMapSha256,
                    transition.TargetTile,
                    transition.TargetElevation,
                    transition.TargetRotation)),
            CurrentMapIndex,
            CurrentMapSha256,
            CurrentTile,
            CurrentElevation);
        Reparent(destination.Root, keepGlobalTransform: false);
        _arrivalComponent = destination.Topology.Movement.ReachableTiles.ToHashSet();
        CurrentMapIndex = transition.TargetMapIndex;
        CurrentElevation = transition.TargetElevation;
        CurrentMapSha256 = transition.TargetMapSha256;
        CurrentWalkMaskSha256 = destination.Topology.WalkMaskSha256;
        ArrivalTile = transition.TargetTile;
        CurrentTile = transition.TargetTile;
        Name = "MAP_126_EXIT_ARRIVAL_CHOSEN_ONE_PLAYER_BODY";
        _spawnWorldMeters = Fo1HexMath.Center(CurrentTile) +
            Vector3.Up * _profile.SpawnCenterHeightMeters;
        Position = _spawnWorldMeters;
        Velocity = Vector3.Zero;
        _presentation.SetDirection(transition.TargetRotation);
        _requireNeutralInput = true;
        SetMeta("source_map_sha256", CurrentMapSha256);
        SetMeta("source_walk_mask_sha256", CurrentWalkMaskSha256);
        SetMeta("current_map_index", CurrentMapIndex);
        SetMeta("current_elevation", CurrentElevation);
        SetMeta("arrival_tile", ArrivalTile);
        SetMeta("arrival_rotation", transition.TargetRotation);
        SetMeta("current_tile", CurrentTile);
        SetMeta("last_exit_serial", transition.ExitSerial);
    }

    internal void ApplyArroyoTrialStep(
        Fo2TrialRouteStep step,
        Node3D elevationRoot,
        IReadOnlySet<int> admittedElevationTiles,
        string walkMaskSha256)
    {
        if (_profile is null || _presentation is null ||
            CurrentMapIndex != Fo2ArroyoCavesPresentationCatalog.MapIndex ||
            step.Elevation is < 0 or > 2 ||
            !admittedElevationTiles.Contains(step.Tile) ||
            walkMaskSha256.Length != 64 ||
            (step.Elevation == CurrentElevation &&
                !Fo1HexMath.Neighbors(CurrentTile).Contains(step.Tile)) ||
            (step.Elevation != CurrentElevation && step.ExitSerial is null))
            throw new InvalidOperationException(
                "Fallout 2 trial step is outside the compiled ARCAVES route.");
        if (GetParent() != elevationRoot)
            Reparent(elevationRoot, keepGlobalTransform: false);
        _arrivalComponent = admittedElevationTiles.ToHashSet();
        CurrentElevation = step.Elevation;
        CurrentWalkMaskSha256 = walkMaskSha256;
        CurrentTile = step.Tile;
        Position = Fo1HexMath.Center(CurrentTile) +
            Vector3.Up * _profile.SpawnCenterHeightMeters;
        Velocity = Vector3.Zero;
        _presentation.SetDirection(step.Rotation);
        CompletedTileTransitions++;
        SetMeta("current_elevation", CurrentElevation);
        SetMeta("source_walk_mask_sha256", CurrentWalkMaskSha256);
        SetMeta("current_tile", CurrentTile);
        SetMeta("last_trial_exit_serial", step.ExitSerial ?? -1);
        SetMeta("last_trial_step_source_bound", true);
    }

    internal void AdmitPostTrialTempleRoute(
        IReadOnlyList<int> route,
        string walkMaskSha256)
    {
        if (_profile is null || _arrivalComponent is null ||
            CurrentMapIndex != Fo2TemplePresentationCatalog.MapIndex ||
            route.Count == 0 || route[0] != CurrentTile ||
            route.Zip(route.Skip(1)).Any(row =>
                !Fo1HexMath.Neighbors(row.First).Contains(row.Second)) ||
            walkMaskSha256.Length != 64)
            throw new InvalidOperationException(
                "Fallout 2 post-trial Temple route is not source-admitted.");
        _arrivalComponent.UnionWith(route);
        CurrentWalkMaskSha256 = walkMaskSha256;
        SetMeta("source_walk_mask_sha256", CurrentWalkMaskSha256);
        SetMeta("post_trial_temple_route_admitted", true);
    }

    internal bool TryPostTrialTempleStep(int destinationTile)
    {
        if (_profile is null || _presentation is null ||
            CurrentMapIndex != Fo2TemplePresentationCatalog.MapIndex ||
            !CanOccupy(destinationTile) ||
            !Fo1HexMath.Neighbors(CurrentTile).Contains(destinationTile))
            return false;
        var origin = Fo1HexMath.Center(CurrentTile);
        var destination = Fo1HexMath.Center(destinationTile);
        Position = destination + Vector3.Up * _profile.SpawnCenterHeightMeters;
        Velocity = Vector3.Zero;
        _presentation.SetDirection(DirectionForMovement(CurrentTile, destination - origin));
        CurrentTile = destinationTile;
        CompletedTileTransitions++;
        SetMeta("current_tile", CurrentTile);
        SetMeta("last_post_trial_step_source_bound", true);
        return true;
    }

    internal void EnterVillage(
        Fo2ArvillagSceneCoverage destination,
        Fo2TrialVillageArrival arrival)
    {
        if (_profile is null || _presentation is null ||
            CurrentMapIndex != Fo2TemplePresentationCatalog.MapIndex ||
            CurrentTile < 0 || arrival.MapIndex != 4 || arrival.MapSha256.Length != 64 ||
            arrival.ArrivalTile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            arrival.Elevation is < 0 or > 2 ||
            arrival.ArrivalRotation is < 0 or >= Fo1HexMath.DirectionCount ||
            arrival.WalkMaskSha256.Length != 64 ||
            arrival.FirstActionFromTile != arrival.ArrivalTile ||
            !arrival.LegalNeighborTiles.Contains(arrival.FirstActionToTile) ||
            destination.MapIndex != arrival.MapIndex ||
            destination.Elevation != arrival.Elevation ||
            destination.MapSha256 != arrival.MapSha256 ||
            destination.ArrivalTile != arrival.ArrivalTile ||
            destination.ArrivalRotation != arrival.ArrivalRotation ||
            destination.WalkMaskSha256 != arrival.WalkMaskSha256 ||
            destination.WalkableHexes != arrival.WalkableHexes ||
            !destination.AdmittedArrivalTiles.SetEquals(
                arrival.LegalNeighborTiles.Append(arrival.ArrivalTile)) ||
            !destination.RoofCutaway || destination.SourceRoofPatches <= 0 ||
            destination.ReliefPlacements <= 0 || destination.HiddenSpriteCards <= 0)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG presentation/arrival identity is invalid.");
        Reparent(destination.Root, keepGlobalTransform: false);
        _arrivalComponent = destination.AdmittedArrivalTiles.ToHashSet();
        _villageFloorHeightByTile = destination.MoldedFloorHeightByTile;
        CurrentMapIndex = arrival.MapIndex;
        CurrentElevation = arrival.Elevation;
        CurrentMapSha256 = arrival.MapSha256;
        CurrentWalkMaskSha256 = arrival.WalkMaskSha256;
        ArrivalTile = arrival.ArrivalTile;
        CurrentTile = arrival.ArrivalTile;
        _spawnWorldMeters = Fo1HexMath.Center(CurrentTile) +
            Vector3.Up * _profile.SpawnCenterHeightMeters;
        Position = _spawnWorldMeters;
        Velocity = Vector3.Zero;
        _presentation.SetDirection(arrival.ArrivalRotation);
        ApplyVillageArrivalFraming(destination);
        if (_selectedCharacter is null || _selectedSourcePresentation is null ||
            _humanoidDonor is null)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG requires one selected hash-bound full-body donor.");
        if (_villageHumanoid is null)
        {
            _villageHumanoid = new Fo2HumanoidVisual(
                Fo2HumanoidIdentity.FromSelection(
                    _selectedCharacter,
                    _selectedSourcePresentation),
                _humanoidDonor);
            AddChild(_villageHumanoid);
        }
        _villageHumanoid.Name = "MAP4_SELECTED_HASH_BOUND_FULL_BODY_PLAYER";
        GroundVillageHumanoid(CurrentTile);
        _villageHumanoid.ApplyPresentationLighting(
            destination.PresentationProfile,
            _profile.CameraFarMeters);
        _villageHumanoid.SetDirection(arrival.ArrivalRotation);
        _villageHumanoid.SetEquipmentState(
            _presentation.SpearEquipped,
            Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedItemFid,
            Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedItemPid,
            Fo2ArroyoPlayerPresentationCatalog.ExpectedWeaponAnimationCode,
            Fo2ArroyoPlayerPresentationCatalog.EquippedGeometryDisposition);
        if (!_villageHumanoid.UsesOwnedDonor ||
            !_villageHumanoid.EquipmentSocketResolved ||
            _villageHumanoid.MeshInstances <= 0 ||
            _villageHumanoid.AuthoredSurfaces <= 0 ||
            _villageHumanoid.LitMaterials <= 0)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG full-body donor did not load its owned assembly.");
        _presentation.Visible = false;
        SetControlsEnabled(true);
        Name = "MAP_4_ARVILLAG_ARRIVAL_CHOSEN_ONE_PLAYER_BODY";
        SetMeta("current_map_index", CurrentMapIndex);
        SetMeta("current_elevation", CurrentElevation);
        SetMeta("source_map_sha256", CurrentMapSha256);
        SetMeta("source_walk_mask_sha256", CurrentWalkMaskSha256);
        SetMeta("arrival_tile", ArrivalTile);
        SetMeta("arrival_rotation", arrival.ArrivalRotation);
        SetMeta("current_tile", CurrentTile);
        SetMeta("destination_presentation_loaded", true);
        SetMeta("destination_cache_manifest_sha256", destination.ManifestSha256);
        SetMeta("destination_relief_placements", destination.ReliefPlacements);
        SetMeta("destination_roof_cutaway", destination.RoofCutaway);
        SetMeta("destination_player_presentation", _villageHumanoid.PresentationMode);
        SetMeta("destination_player_donor_manifest_sha256", _humanoidDonor.ManifestSha256);
        SetMeta("destination_player_source_fid", _selectedSourcePresentation.Fid);
        SetMeta("destination_player_source_frm_sha256",
            _selectedSourcePresentation.SourceSha256);
        SetMeta("owned_map_arrival", true);
    }

    internal void EnterAdjacentMap(
        Node3D destinationRoot,
        ClassicMapEndpoint destination,
        IReadOnlySet<int> walkable,
        string walkMaskSha256,
        string cacheSha256)
    {
        if (_profile is null || _presentation is null ||
            destination.Elevation is not int elevation ||
            destination.Rotation is not int rotation ||
            destination.MapSha256.Length !=
                Fo2TemplePresentationCatalog.Sha256HexCharacters ||
            walkMaskSha256.Length != Fo2TemplePresentationCatalog.Sha256HexCharacters ||
            cacheSha256.Length != Fo2TemplePresentationCatalog.Sha256HexCharacters ||
            !walkable.Contains(destination.Tile))
            throw new InvalidOperationException(
                "Fallout 2 adjacent destination state is incomplete.");
        Reparent(destinationRoot, keepGlobalTransform: false);
        _arrivalComponent = walkable.ToHashSet();
        _villageFloorHeightByTile = null;
        CurrentMapIndex = destination.MapIndex;
        CurrentElevation = elevation;
        CurrentMapSha256 = destination.MapSha256;
        CurrentWalkMaskSha256 = walkMaskSha256;
        ArrivalTile = destination.Tile;
        CurrentTile = destination.Tile;
        _spawnWorldMeters = Fo1HexMath.Center(CurrentTile) +
            Vector3.Up * _profile.SpawnCenterHeightMeters;
        Position = _spawnWorldMeters;
        Velocity = Vector3.Zero;
        _presentation.SetDirection(rotation);
        GroundVillageHumanoid(CurrentTile);
        _villageHumanoid?.SetDirection(rotation);
        SetControlsEnabled(true);
        SetMeta("current_map_index", CurrentMapIndex);
        SetMeta("current_elevation", CurrentElevation);
        SetMeta("source_map_sha256", CurrentMapSha256);
        SetMeta("source_walk_mask_sha256", CurrentWalkMaskSha256);
        SetMeta("arrival_tile", ArrivalTile);
        SetMeta("arrival_rotation", rotation);
        SetMeta("current_tile", CurrentTile);
        SetMeta("destination_cache_manifest_sha256", cacheSha256);
        SetMeta("owned_map_arrival", true);
    }

    private void ApplyVillageArrivalFraming(Fo2ArvillagSceneCoverage destination)
    {
        if (_profile is null)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG arrival framing requires the player profile.");
        var camera = GetNode<Camera3D>("ARROYO_PLAYER_FOLLOW_CAMERA");
        var viewportPixels = GetViewport().GetVisibleRect().Size;
        var visibleWorldFraction = CameraWorldViewportHeightPixels /
            CameraVisibleSourceFrameHeightPixels;
        if (viewportPixels.X <= 0.0f || viewportPixels.Y <= 0.0f ||
            visibleWorldFraction is <= 0.0f or > 1.0f)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG arrival framing viewport is invalid.");
        var rootTransform = destination.Root.GlobalTransform;
        var focus = rootTransform * destination.ArrivalFraming.FocusWorldMeters;
        camera.GlobalPosition = focus + _profile.CameraOffsetMeters;
        camera.LookAt(focus, Vector3.Up);
        var cameraBasis = camera.GlobalTransform.Basis;
        var projected = Fo2ArvillagScene.BoundsCorners(
                destination.ArrivalFraming.RouteAndObjectBoundsMeters)
            .Select(point => rootTransform * point - focus)
            .ToArray();
        var verticalSpan = projected.Max(point => cameraBasis.Y.Dot(point)) -
            projected.Min(point => cameraBasis.Y.Dot(point));
        var horizontalSpan = projected.Max(point => cameraBasis.X.Dot(point)) -
            projected.Min(point => cameraBasis.X.Dot(point));
        var paddingScale = 1.0f + 2.0f *
            destination.ArrivalFraming.PaddingFraction;
        var aspect = viewportPixels.X / viewportPixels.Y;
        CameraSizeMeters = MathF.Max(
            verticalSpan / visibleWorldFraction,
            horizontalSpan / aspect) * paddingScale;
        if (!float.IsFinite(CameraSizeMeters) || CameraSizeMeters <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG source-bound camera size could not be derived.");
        camera.Size = CameraSizeMeters;
        var visibleCenterShift = cameraBasis.Y * CameraSizeMeters *
            (1.0f - visibleWorldFraction) * Half;
        var framedFocus = focus - visibleCenterShift;
        camera.GlobalPosition = framedFocus + _profile.CameraOffsetMeters;
        camera.LookAt(framedFocus, Vector3.Up);
        VillageArrivalFramingMode = destination.ArrivalFraming.Mode;
        VillageArrivalFramingSourceSerials =
            destination.ArrivalFraming.SourceObjectSerials;
        VillageArrivalFramingFocusWorldMeters = focus;
        SetMeta("camera_composition_mode", destination.ArrivalFraming.Mode);
        SetMeta("camera_size_meters", CameraSizeMeters);
        SetMeta("village_arrival_framing_source_serials",
            string.Join(",", VillageArrivalFramingSourceSerials));
        SetMeta("village_arrival_framing_focus_world_meters", focus);
    }

    internal void ApplyVillageFirstAction(Fo2TrialVillageArrival arrival)
    {
        if (_profile is null || _presentation is null ||
            CurrentMapIndex != arrival.MapIndex || CurrentMapSha256 != arrival.MapSha256 ||
            CurrentElevation != arrival.Elevation || CurrentTile != arrival.FirstActionFromTile ||
            arrival.FirstActionToTile !=
                Fo1HexMath.TileInDirection(CurrentTile, arrival.FirstActionRotation) ||
            !CanOccupy(arrival.FirstActionToTile) ||
            !GetMeta("destination_presentation_loaded").AsBool() ||
            _presentation.Visible || _villageHumanoid is null ||
            !_villageHumanoid.Visible || !_villageHumanoid.UsesOwnedDonor ||
            !_controlsEnabled)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG first action differs from the owned walk contract.");
        CurrentTile = arrival.FirstActionToTile;
        Position = Fo1HexMath.Center(CurrentTile) +
            Vector3.Up * _profile.SpawnCenterHeightMeters;
        Velocity = Vector3.Zero;
        _presentation.SetDirection(arrival.FirstActionRotation);
        _villageHumanoid?.SetDirection(arrival.FirstActionRotation);
        GroundVillageHumanoid(CurrentTile);
        CompletedTileTransitions++;
        SetMeta("current_tile", CurrentTile);
        SetMeta("first_legal_destination_action", "adjacent-source-walkable-hex-step");
        SetMeta("first_legal_destination_action_applied", true);
    }

    internal void ConfirmVillageFirstActionFromLiveMovement(
        Fo2TrialVillageArrival arrival)
    {
        if (_profile is null || _presentation is null || _villageHumanoid is null ||
            CurrentMapIndex != arrival.MapIndex || CurrentMapSha256 != arrival.MapSha256 ||
            CurrentElevation != arrival.Elevation || CurrentTile != arrival.FirstActionToTile ||
            arrival.FirstActionToTile !=
                Fo1HexMath.TileInDirection(arrival.FirstActionFromTile,
                    arrival.FirstActionRotation) ||
            _presentation.Direction != arrival.FirstActionRotation ||
            !CanOccupy(CurrentTile) || !_controlsEnabled ||
            !GetMeta("destination_presentation_loaded").AsBool() ||
            _presentation.Visible || !_villageHumanoid.Visible ||
            !_villageHumanoid.UsesOwnedDonor ||
            _villageHumanoid.GetMeta("molded_floor_height_tile").AsInt32() != CurrentTile)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG live input did not reach the exact first-action state.");
        SetMeta("first_legal_destination_action",
            "godot-action-driven-adjacent-source-walkable-hex-step");
        SetMeta("first_legal_destination_action_applied", true);
        SetMeta("first_legal_destination_input_driven", true);
    }

    private void GroundVillageHumanoid(int tile)
    {
        if (_villageHumanoid is null)
            return;
        if (_profile is null)
            throw new InvalidOperationException(
                "Fallout 2 humanoid grounding requires an active player profile.");
        if (_villageFloorHeightByTile is null)
        {
            _villageHumanoid.Position = Vector3.Down * _profile.SpawnCenterHeightMeters;
            _villageHumanoid.SetMeta("molded_floor_height_meters", 0.0f);
            _villageHumanoid.SetMeta("molded_floor_height_tile", tile);
            return;
        }
        if (
            !_villageFloorHeightByTile.TryGetValue(tile, out var floorHeightMeters) ||
            !float.IsFinite(floorHeightMeters) || floorHeightMeters < 0.0f)
            throw new InvalidOperationException(
                $"Fallout 2 ARVILLAG molded floor height is unavailable: {tile}.");
        _villageHumanoid.Position = new Vector3(
            0.0f,
            floorHeightMeters - _profile.SpawnCenterHeightMeters,
            0.0f);
        _villageHumanoid.SetMeta("molded_floor_height_meters", floorHeightMeters);
        _villageHumanoid.SetMeta("molded_floor_height_tile", tile);
    }

    internal bool CanOccupy(int tile) =>
        tile >= 0 && _arrivalComponent?.Contains(tile) == true;

    internal static int DirectionForMovement(int tile, Vector3 desired)
    {
        if (desired.IsZeroApprox())
            throw new ArgumentException(
                "Fallout 2 player facing requires a non-zero movement vector.",
                nameof(desired));
        var origin = Fo1HexMath.Center(tile);
        var normalized = new Vector3(desired.X, 0.0f, desired.Z).Normalized();
        var bestDirection = -1;
        var bestDot = float.NegativeInfinity;
        for (var direction = 0; direction < Fo1HexMath.DirectionCount; direction++)
        {
            var neighbor = Fo1HexMath.TileInDirection(tile, direction);
            if (neighbor < 0)
                continue;
            var vector = (Fo1HexMath.Center(neighbor) - origin).Normalized();
            var dot = vector.Dot(normalized);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestDirection = direction;
            }
        }
        return bestDirection >= 0
            ? bestDirection
            : throw new InvalidOperationException(
                "Fallout 2 player tile has no source direction.");
    }
}

internal static class Fo2ArroyoCavesInput
{
    internal static void Configure(Fo2ArroyoPlayerProfile profile)
    {
        foreach (var binding in Bindings(profile))
        {
            if (InputMap.HasAction(binding.Action))
                InputMap.EraseAction(binding.Action);
            InputMap.AddAction(binding.Action);
            InputMap.ActionAddEvent(
                binding.Action,
                CreateEvent(binding.PhysicalKey, false));
        }
    }

    internal static InputEventKey CreateEvent(Key physicalKey, bool pressed) => new()
    {
        PhysicalKeycode = physicalKey,
        Pressed = pressed,
        Echo = false,
    };

    private static IEnumerable<Fo2ArroyoInputBinding> Bindings(
        Fo2ArroyoPlayerProfile profile)
    {
        yield return profile.MoveLeft;
        yield return profile.MoveRight;
        yield return profile.MoveForward;
        yield return profile.MoveBackward;
    }
}
