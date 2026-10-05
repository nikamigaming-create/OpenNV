using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeLandscapeTransportAudit
{
    // A selected diagnostic component, not a campaign transition. It retains
    // the original XTEL point and asks the existing player mover to settle.
    private async Task RunPortalCapsuleAsync(string[] arguments)
    {
        if (arguments.Length < 7)
            throw new ArgumentException("Expected --portal-capsule game mod-id mod-root source-plugin source-hex selected-model-keys followed by dependency roots.");
        static FalloutFormKey Key(string value)
        {
            var fields = value.Split(':');
            if (fields.Length != 2 || fields[0].Length == 0)
                throw new ArgumentException("Selected model identities require plugin:hex-object-id.");
            return new(fields[0], Convert.ToUInt32(fields[1], 16));
        }
        var selected = arguments[6].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Key).ToArray();
        if (selected.Length == 0 || selected.Distinct().Count() != selected.Length)
            throw new ArgumentException("A nonempty distinct selection of actual source static references is required.");
        var installation = new FalloutModStackSelection([new(arguments[2], arguments[3], arguments[7..])]).Resolve(arguments[1]);
        RuntimeLiveContentSource.Configure(arguments[1], RuntimeLiveContentSource.FalloutNewVegasGame,
            installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        Node3D? fixture = null;
        var prototypes = new List<RuntimeNativeNifPrototype>();
        try
        {
            var content = RuntimeLiveContentSource.Current!;
            var resources = new ConcurrentDictionary<string, (string Path, string Sha256, int Bytes)>(StringComparer.OrdinalIgnoreCase);
            content.ResourceReadObserver = (path, identity, bytes) =>
            {
                var row = (Path: path, Sha256: Convert.ToHexString(SHA256.HashData(bytes.Span)), Bytes: bytes.Length);
                if (resources.TryGetValue(identity, out var previous) && (previous.Sha256 != row.Sha256 || previous.Bytes != row.Bytes))
                    throw new InvalidDataException("An owned resource changed during its portal diagnostic.");
                resources[identity] = row;
            };
            using var records = FalloutPluginStack.Load(content.PluginSources);
            string Hash(FalloutFormKey key) => Convert.ToHexString(SHA256.HashData(records.GetEffective(key).ReadData()));
            var hashes = new Dictionary<FalloutFormKey, string>();
            object Record(FalloutFormKey key)
            {
                var record = records.GetEffective(key);
                hashes.TryAdd(key, Hash(key));
                return new { form = key.ToString(), record.Signature, winner = record.Plugin.Name, sha256 = hashes[key] };
            }
            var sourceKey = new FalloutFormKey(arguments[4], Convert.ToUInt32(arguments[5], 16));
            var sourceRecord = records.GetEffective(sourceKey);
            if (sourceRecord.Signature != "REFR" || sourceRecord.IsDeleted)
                throw new InvalidDataException("The portal diagnostic requires an actual winning placed source door.");
            var sourceCell = FalloutCellSceneReader.ParentCell(sourceRecord) ?? throw new InvalidDataException("Source door has no parent CELL.");
            var sourceScene = FalloutCellSceneReader.Read(records, sourceCell);
            var sourceDoor = sourceScene.References.Single(value => value.FormKey == sourceKey);
            var destination = FalloutDoorDestinationResolver.Resolve(records, sourceDoor);
            var entry = sourceDoor.Teleport!;
            var worldspace = destination.DestinationScene.Cell.Worldspace ??
                throw new InvalidDataException("The selected source portal does not lead to an exterior worldspace.");
            var configuration = RuntimeConfiguration.Load();
            Engine.PhysicsTicksPerSecond = configuration.Simulation.PhysicsTicksPerSecond;
            var units = configuration.World.GameUnitsToMeters;
            var settings = FalloutInstallationSettings.Read(content);
            var diameter = Math.Max(checked((int)settings.Unsigned("General", "uGridsToLoad")), configuration.World.MinimumExteriorGridDiameter);
            var exterior = new FalloutExteriorGrid(records);
            var grid = exterior.Resolve(worldspace, exterior.PersistentCell(worldspace), entry.Position[0], entry.Position[1], diameter);
            if (grid.Scene.Cell.FormKey != exterior.SpatialCell(worldspace, entry.Position[0], entry.Position[1]) ||
                !grid.Scene.References.Any(value => value.FormKey == destination.Destination.FormKey))
                throw new InvalidDataException("The directed source portal lost its spatial cell or actual destination reference.");
            // This disposable world supplies existing source enable-parent
            // admission. No scripts, quest results, saves or native AI run.
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(grid.Scene);
            fixture = new Node3D { Name = "OwnedPortalCapsuleFixture" };
            AddChild(fixture);
            var textureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            var terrains = new List<RuntimeNativeLandscapeTransport>();
            var models = new List<(FalloutFormKey Key, Node3D Node, string Path, string Hash)>();
            foreach (var cell in grid.Cells)
            {
                var landSource = FalloutLandscapeTransportResolver.ResolveCell(records, cell, grid.PersistentCell);
                var terrain = RuntimeNativeLandscapeTransportBuilder.Build(landSource, units, textureCache);
                terrains.Add(terrain); fixture.AddChild(terrain);
                GD.Print(JsonSerializer.Serialize(new
                {
                    kind = "portal-capsule-land-source",
                    cell = Record(cell.FormKey),
                    land = Record(landSource.Landscape),
                    coordinates = new[] { landSource.ActiveCoordinates.X, landSource.ActiveCoordinates.Y },
                    textureSources = landSource.Textures.Select(pair => new { form = pair.Key.ToString(), pair.Value.DiffusePath, pair.Value.NormalPath })
                }));
            }
            foreach (var key in selected)
            {
                var placed = grid.Scene.References.SingleOrDefault(value => value.FormKey == key) ??
                    throw new InvalidDataException($"Selected model {key} is outside the actual source arrival grid.");
                var source = records.GetEffective(placed.Base);
                if (records.GetEffective(key).Signature != "REFR" || source.Signature != "STAT" || !world.IsEnabled(key))
                    throw new InvalidDataException("Selected portal geometry must be an actual source-enabled static reference.");
                var path = grid.Scene.BaseObjects[placed.Base].ModelPath ?? throw new InvalidDataException("Selected source static has no NIF.");
                if (!content.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException(path);
                var prototype = new RuntimeNativeNifPrototype(bytes, units); prototypes.Add(prototype);
                if (prototype.Scene.CollisionShapes == 0)
                    throw new InvalidDataException("Selected source NIF has no admitted native collision.");
                var placement = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                    new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale),
                    GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * units);
                var node = prototype.InstantiatePlaced(placement);
                fixture.AddChild(node);
                if (node.FindChildren("*", "", true, false).OfType<RigidBody3D>().Any(value => !value.Freeze))
                    throw new NotSupportedException("The selected portal collision component does not own dynamic-body initial state.");
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                models.Add((key, node, path, hash));
                GD.Print(JsonSerializer.Serialize(new
                {
                    kind = "portal-capsule-model-source",
                    reference = Record(key),
                    sourceBase = Record(placed.Base),
                    sourceCell = placed.Cell.ToString(),
                    placed.Position,
                    placed.RotationRadians,
                    placed.Scale,
                    path,
                    identity,
                    sha256 = hash,
                    prototype.Scene.CollisionBodies,
                    prototype.Scene.CollisionShapes,
                    prototype.Scene.CollisionTriangles
                }));
            }
            var arrival = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                new(entry.RotationRadians[0], entry.RotationRadians[1], entry.RotationRadians[2]), 1),
                GamebryoCoordinate.ConvertVector(new(entry.Position[0], entry.Position[1], entry.Position[2])) * units);
            var player = new RuntimeNativePlayer(); fixture.AddChild(player);
            player.Configure(configuration, arrival);
            player.SetModalInput(true);
            player.SetProcess(false); player.SetPhysicsProcess(false);
            player.SetProcessInput(false); player.SetProcessUnhandledInput(false); player.SetProcessUnhandledKeyInput(false);
            var resident = terrains.Select(value => value.Source.ActiveCoordinates).ToHashSet();
            bool Resident(Vector3 point)
            {
                var width = 4096 * units; var radius = configuration.Player.CapsuleRadiusMeters;
                for (var x = (int)MathF.Floor((point.X - radius) / width); x <= (int)MathF.Floor((point.X + radius) / width); x++)
                    for (var y = (int)MathF.Floor((-point.Z - radius) / width); y <= (int)MathF.Floor((-point.Z + radius) / width); y++)
                        if (!resident.Contains((x, y))) return false;
                return true;
            }
            player.CanOccupyPosition = Resident;
            object? Identity(GodotObject? collider)
            {
                if (collider is not Node node) return null;
                foreach (var terrain in terrains)
                    if (node == terrain || terrain.IsAncestorOf(node)) return new { kind = "LAND", form = terrain.Source.Landscape.ToString(), cell = terrain.Source.ActiveCell.ToString() };
                foreach (var model in models)
                    if (node == model.Node || model.Node.IsAncestorOf(node)) return new { kind = "REFR", form = model.Key.ToString(), model.Path };
                return null;
            }
            static float[] Point(Vector3 value) => [value.X, value.Y, value.Z];
            object? Floor(Vector3 point)
            {
                using var request = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * configuration.Player.StepHeightMeters,
                    point - Vector3.Up * configuration.Player.StepHeightMeters, player.CollisionMask, [player.GetRid()]);
                using var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(request);
                return hit.Count == 0 ? null : new
                {
                    point = Point(hit["position"].AsVector3()),
                    normal = Point(hit["normal"].AsVector3()),
                    shape = hit["shape"].AsInt32(),
                    identity = Identity(GodotObject.InstanceFromId(checked((ulong)hit["collider_id"].AsInt64()))),
                    from = Point(request.From),
                    to = Point(request.To)
                };
            }
            object? Support(out bool supported)
            {
                supported = NativeCharacterStep.TrySupport(player, player.GlobalPosition, configuration.Player.StepHeightMeters, out var hit);
                return hit is null ? null : new
                {
                    point = Point(hit.Point),
                    normal = Point(hit.Normal),
                    hit.Collider,
                    hit.Shape,
                    identity = Identity(GodotObject.InstanceFromId(hit.Collider)),
                    from = Point(hit.From),
                    to = Point(hit.Desired)
                };
            }
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var geometry = fixture.FindChildren("*", "", true, false).OfType<CollisionObject3D>()
                .Where(value => value != player).Select(value => (Node: value, Transform: value.GlobalTransform)).ToArray();
            GD.Print(JsonSerializer.Serialize(new
            {
                kind = "portal-capsule-source",
                runtimeBuild = typeof(RuntimeNativePlayer).Module.ModuleVersionId,
                content.SaveCompatibilityId,
                source = Record(sourceKey),
                sourceBase = Record(sourceDoor.Base),
                sourceCell = Record(sourceCell),
                destination = Record(destination.Destination.FormKey),
                destinationBase = Record(destination.Destination.Base),
                persistentCell = Record(grid.PersistentCell),
                spatialCell = Record(grid.Scene.Cell.FormKey),
                worldspace = Record(worldspace),
                sourcePosition = entry.Position,
                sourceRotation = entry.RotationRadians,
                exactArrival = Point(arrival.Origin),
                diameter,
                landCount = terrains.Count,
                selectedModels = selected.Select(value => value.ToString()),
                configuration.Player.CapsuleHeightMeters,
                configuration.Player.CapsuleRadiusMeters,
                configuration.Player.SpawnCenterHeightMeters,
                configuration.Player.StepHeightMeters,
                configuration.Player.MaximumWalkableSlopeDegrees,
                player.SafeMargin,
                player.FloorSnapLength,
                units,
                actualPhysicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
                configuredPhysicsTicksPerSecond = configuration.Simulation.PhysicsTicksPerSecond,
                recording = false,
                campaign = false,
                parity = false,
                boundary = "all source LAND in current arrival grid; explicitly selected original static NIFs; other reference collision/scripts/AI/sky/audio/whole-cell presentation unexecuted"
            }));
            var contactOverflow = false; var unownedContact = false;
            foreach (var motion in new[] { Vector3.Zero, Vector3.Down * player.SafeMargin * 8, Vector3.Up * player.SafeMargin * 8 })
            {
                var before = player.GlobalTransform;
                using var request = new PhysicsTestMotionParameters3D
                { From = before, Motion = motion, Margin = player.SafeMargin, MaxCollisions = 8, RecoveryAsCollision = true };
                using var result = new PhysicsTestMotionResult3D();
                var collided = PhysicsServer3D.BodyTestMotion(player.GetRid(), request, result);
                contactOverflow |= result.GetCollisionCount() == request.MaxCollisions;
                unownedContact |= Enumerable.Range(0, result.GetCollisionCount()).Any(index => Identity(result.GetCollider(index)) is null);
                GD.Print(JsonSerializer.Serialize(new
                {
                    kind = "portal-capsule-exact-arrival-query",
                    from = Point(before.Origin),
                    motion = Point(motion),
                    collided,
                    travel = Point(result.GetTravel()),
                    floor = Floor(before.Origin),
                    contactOverflow,
                    unownedContact,
                    contacts = Enumerable.Range(0, result.GetCollisionCount()).Select(index => new
                    {
                        point = Point(result.GetCollisionPoint(index)),
                        normal = Point(result.GetCollisionNormal(index)),
                        depth = result.GetCollisionDepth(index),
                        collider = result.GetColliderId(index),
                        shape = result.GetColliderShape(index),
                        identity = Identity(result.GetCollider(index))
                    })
                }));
                if (player.GlobalTransform != before) throw new InvalidDataException("A source arrival query moved its player.");
            }
            void Observe(int frame)
            {
                var support = Support(out var supported);
                unownedContact |= Enumerable.Range(0, player.GetSlideCollisionCount()).Any(index => Identity(player.GetSlideCollision(index).GetCollider()) is null);
                GD.Print(JsonSerializer.Serialize(new
                {
                    kind = "portal-capsule-mover",
                    frame,
                    position = Point(player.GlobalPosition),
                    velocity = Point(player.Velocity),
                    onFloor = player.IsOnFloor(),
                    supported,
                    support,
                    nearbyFloor = Floor(player.GlobalPosition),
                    resident = Resident(player.GlobalPosition),
                    contacts = Enumerable.Range(0, player.GetSlideCollisionCount()).Select(index =>
                    {
                        var hit = player.GetSlideCollision(index);
                        return new { point = Point(hit.GetPosition()), normal = Point(hit.GetNormal()), depth = hit.GetDepth(), identity = Identity(hit.GetCollider()) };
                    })
                }));
            }
            Observe(0);
            var stableFrames = 0; var completedFrames = 0;
            for (var frame = 1; frame <= configuration.Simulation.PhysicsTicksPerSecond * 4; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                var before = player.GlobalPosition;
                player._PhysicsProcess(GetPhysicsProcessDeltaTime());
                Support(out var supported);
                stableFrames = player.IsOnFloor() && supported && Resident(player.GlobalPosition) &&
                    before.DistanceTo(player.GlobalPosition) <= player.SafeMargin * 2 ? stableFrames + 1 : 0;
                completedFrames = frame;
                if (frame is 1 or 10 or 30 or 60 or 90 or 180 || stableFrames == 12) Observe(frame);
                if (stableFrames == 12) break;
            }
            Observe(completedFrames);
            if (geometry.Any(value => value.Node.GlobalTransform != value.Transform))
                throw new InvalidDataException("The arrival diagnostic changed source collision geometry.");
            foreach (var (key, hash) in hashes)
                if (Hash(key) != hash) throw new InvalidDataException("A source record changed during the arrival diagnostic.");
            foreach (var model in models)
                if (!content.TryRead(model.Path, null, out var after, out _) || Convert.ToHexString(SHA256.HashData(after)) != model.Hash)
                    throw new InvalidDataException("An original selected model changed during the arrival diagnostic.");
            GD.Print(JsonSerializer.Serialize(new
            {
                kind = "portal-capsule-resource-sources",
                resources = resources.OrderBy(pair => pair.Key).Select(pair => new { identity = pair.Key, pair.Value.Path, pair.Value.Sha256, pair.Value.Bytes }),
                sourceUnchanged = true,
                recording = false
            }));
            using var standQuery = new NativeCapsulePlacementQuery(player);
            var clearSupportedPlacement = standQuery.CanStand(player.GlobalPosition);
            var horizontalRecovery = new Vector2(player.GlobalPosition.X - arrival.Origin.X, player.GlobalPosition.Z - arrival.Origin.Z).Length();
            if (contactOverflow || unownedContact || stableFrames < 12 || !clearSupportedPlacement || horizontalRecovery > configuration.Player.CapsuleRadiusMeters)
                throw new InvalidDataException("The exact authored portal arrival did not settle with bounded recovery, actual source root support and complete capsule clearance.");
            GD.Print($"OPENNV_PORTAL_CAPSULE_PASS source={sourceKey} spatialCell={grid.Scene.Cell.FormKey} " +
                $"frames={completedFrames} horizontalRecovery={horizontalRecovery:R} authoredHeightUnchanged=true sourceGeometryUnchanged=true " +
                "exactArrivalQueriesReadOnly=true normalPlayerMover=true recording=false ordinaryPortal=unverified campaign=false parity=false");
        }
        finally
        {
            fixture?.Free();
            foreach (var prototype in prototypes) prototype.Scene.Root.Free();
            if (RuntimeLiveContentSource.Current is { } content) content.ResourceReadObserver = null;
            RuntimeLiveContentSource.Clear();
        }
    }
}
