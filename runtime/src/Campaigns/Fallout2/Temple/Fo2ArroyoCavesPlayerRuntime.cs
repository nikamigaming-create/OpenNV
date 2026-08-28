using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArroyoCavesPlayerRuntimeCoverage(
    Fo2ArroyoPlayerProfile Profile,
    string FloorCollisionPath,
    int FloorSupportPatches,
    int FloorCollisionTriangles,
    int ArrivalComponentHexes,
    Fo2ArroyoCavesPlayerBody Player);

internal static class Fo2ArroyoCavesPlayerRuntime
{
    internal static Fo2ArroyoCavesPlayerRuntimeCoverage Build(
        Fo2ArroyoCavesPresentationCatalog catalog,
        Fo2ArroyoCavesSceneCoverage scene)
    {
        var profile = Fo2ArroyoPlayerProfile.Load(catalog);
        var component = EntryComponent(catalog.ArrivalTile, catalog.Walkable);
        if (component.Count != catalog.ArrivalComponentHexes ||
            Fo2TempleMovementConsumer.MaskSha256(catalog.Walkable) != catalog.WalkMaskSha256)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo player source-walk component drifted.");

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
        player.Configure(catalog, profile, component);
        return new Fo2ArroyoCavesPlayerRuntimeCoverage(
            profile,
            floorBody.GetPath().ToString(),
            floorPatches.Length,
            floorPatches.Length * 2,
            component.Count,
            player);
    }

    private static ArrayMesh BuildFloorCollisionMesh(IReadOnlyCollection<int> floorPatches)
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

    private static HashSet<int> EntryComponent(
        int arrivalTile,
        IReadOnlyList<bool> walkable)
    {
        var visited = new HashSet<int> { arrivalTile };
        var queue = new Queue<int>();
        queue.Enqueue(arrivalTile);
        while (queue.Count > 0)
            foreach (var neighbor in Fo1HexMath.Neighbors(queue.Dequeue()))
                if (walkable[neighbor] && visited.Add(neighbor))
                    queue.Enqueue(neighbor);
        return visited;
    }
}

internal sealed partial class Fo2ArroyoCavesPlayerBody : CharacterBody3D
{
    private Fo2ArroyoPlayerProfile? _profile;
    private HashSet<int>? _arrivalComponent;
    private Vector3 _spawnWorldMeters;

    internal int ArrivalTile { get; private set; }
    internal int CurrentTile { get; private set; }
    internal int CompletedTileTransitions { get; private set; }
    internal int RejectedMovementFrames { get; private set; }
    internal int LastRejectedCandidateTile { get; private set; } = -1;
    internal Vector3 SpawnWorldMeters => _spawnWorldMeters;
    internal float HorizontalDistanceFromSpawn => new Vector2(
        Position.X - _spawnWorldMeters.X,
        Position.Z - _spawnWorldMeters.Z).Length();

    internal void Configure(
        Fo2ArroyoCavesPresentationCatalog catalog,
        Fo2ArroyoPlayerProfile profile,
        HashSet<int> arrivalComponent)
    {
        if (_profile is not null ||
            arrivalComponent.Count != catalog.ArrivalComponentHexes ||
            !arrivalComponent.Contains(catalog.ArrivalTile))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo player was configured more than once or without its arrival component.");
        _profile = profile;
        _arrivalComponent = arrivalComponent.ToHashSet();
        ArrivalTile = catalog.ArrivalTile;
        CurrentTile = ArrivalTile;
        _spawnWorldMeters = Fo1HexMath.Center(ArrivalTile) +
            Vector3.Up * profile.SpawnCenterHeightMeters;
        Position = _spawnWorldMeters;
        Name = "MAP_3_ARRIVAL_PLAYER_BODY_NO_CHARACTER_ART";
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
        SetMeta("blocked_movement_mode", profile.BlockedMovementMode);
        SetMeta("arrival_tile", ArrivalTile);
        SetMeta("arrival_rotation", catalog.ArrivalRotation);
        SetMeta("character_art_loaded", false);
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
        var camera = new Camera3D
        {
            Name = "ARROYO_PLAYER_FOLLOW_CAMERA",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = profile.CameraSizeMeters,
            Near = profile.CameraNearMeters,
            Far = profile.CameraFarMeters,
            Position = profile.CameraOffsetMeters,
            Current = true,
        };
        AddChild(camera);
        camera.LookAt(
            GlobalPosition + Vector3.Up * profile.CameraLookHeightMeters,
            Vector3.Up);
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
        var desired = new Vector3(input.X, 0.0f, input.Y);
        if (desired.LengthSquared() > 1.0f)
            desired = desired.Normalized();
        var before = Position;
        var horizontal = desired * _profile.SpeedMetersPerSecond;
        var candidatePosition = before + horizontal * (float)delta;
        var candidateTile = Fo1HexMath.NearestTile(new Vector3(
            candidatePosition.X,
            0.0f,
            candidatePosition.Z));
        if (!desired.IsZeroApprox() && !CanOccupy(candidateTile))
        {
            horizontal = Vector3.Zero;
            RejectedMovementFrames++;
            LastRejectedCandidateTile = candidateTile;
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
        if (movedTile != CurrentTile)
        {
            CurrentTile = movedTile;
            CompletedTileTransitions++;
            SetMeta("current_tile", CurrentTile);
        }
    }

    internal bool CanOccupy(int tile) =>
        tile >= 0 && _arrivalComponent?.Contains(tile) == true;
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
