using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private async Task MarkerTravelCold(string game, string mod, string root, string actorId, string questId,
        short stage, string[] dependencies, bool failedRoute = false, bool ownedRoom = false)
    {
        var fixture = new Node3D(); AddChild(fixture);
        FalloutReferenceWorld? world = null;
        RuntimeNativeNpc? actor = null;
        var roomPrototypes = new Dictionary<string, RuntimeNativeNifPrototype>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            world = new(records);
            var caller = FalloutDialogueTopic.Find(records, "ACHR", actorId).FormKey;
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
            var quests = new FalloutQuestState(records); quests.EnterStage(quest, stage);
            var globals = FalloutGlobalState.Read(records);
            var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records),
                FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe")));
            var cell = FalloutCellSceneReader.Read(records, world.Get(caller).Cell);
            world.LoadCell(cell);
            // Source actors can inherit their enable state. Select the root
            // of that authored chain for this isolated diagnostic fixture;
            // Enable on the child itself correctly leaves the chain unchanged.
            var enableRoot = caller; var rootEnabled = true;
            var enableParents = new HashSet<FalloutFormKey>();
            while (world.Get(enableRoot).EnableParent is { } parent)
            {
                if (!enableParents.Add(enableRoot)) throw new InvalidDataException("Fixture actor has an enable-parent cycle.");
                rootEnabled ^= parent.Opposite;
                enableRoot = parent.Reference;
                if (records.RuntimeFormId(enableRoot) == 0x14)
                    throw new NotSupportedException("Marker Travel fixture cannot change the engine player enable root.");
            }
            world.SetEnabled(enableRoot, rootEnabled); world.AdvanceEnableChanges(0, new(1, 1), _ => false);
            if (!world.IsEnabled(caller)) throw new InvalidDataException("Fixture actor did not inherit its enabled source chain.");
            var placed = cell.References.Single(value => value.FormKey == caller);
            var configuration = RuntimeConfiguration.Load();
            var units = configuration.World.GameUnitsToMeters;
            var navigation = CellNavigationGraph.LoadOwned(records, cell.Cell.FormKey);
            var packageEvents = new List<(string Event, FalloutFormKey Package)>();
            var effects = new List<FalloutReferenceScriptEffect>();
            Rid movementShape = default;

            Vector3 Source(Vector3 point) => new Vector3(point.X, -point.Z, point.Y) / units;
            Vector3 Native(Vector3 point) => GamebryoCoordinate.ConvertVector(point) * units;
            Transform3D Placement(FalloutReferenceWorld owner, FalloutPlacedReference reference)
            {
                var source = owner.Placement(reference.FormKey);
                return new(GamebryoCoordinate.ConvertReferenceEuler(new(source.RotationRadians[0],
                    source.RotationRadians[1], source.RotationRadians[2]), reference.Scale),
                    Native(new(source.Position[0], source.Position[1], source.Position[2])));
            }
            FalloutReferenceScripts Scripts(FalloutReferenceWorld owner) => new(records, owner, quests,
                new((_, _) => false, effect =>
                {
                    if (effect.Kind != FalloutReferenceEffectKind.SetStage || effect.Source != caller || effect.Target != quest)
                        throw new NotSupportedException("Marker Travel fixture reached a package result outside its selected quest stage.");
                    effects.Add(effect);
                    quests.EnterStage(quest, effect.Stage);
                }, Globals: globals));

            var scripts = Scripts(world);
            var context = new NativeActorCombatContext(() => null,
                () => throw new InvalidOperationException("Marker Travel fixture queried player vitals."),
                (_, _) => throw new InvalidOperationException("Marker Travel fixture attacked the player."),
                (from, to) => navigation.FindPath(Source(from), Source(to)).Select(Native).ToArray(),
                _ => true, () => 1, globals, .4f, 9.81f, PlayerCell: () => cell.Cell.FormKey);

            if (ownedRoom)
            {
                foreach (var reference in cell.References.Where(value => world.IsEnabled(value.FormKey)))
                {
                    var source = cell.BaseObjects[reference.Base];
                    if (source.Signature is not ("STAT" or "FURN") || source.ModelPath is null ||
                        FalloutNewVegasBuiltinForms.IsInternalStatic(source.Signature, records.RuntimeFormId(source.FormKey))) continue;
                    if (Placement(world, reference).Origin.DistanceTo(Placement(world, placed).Origin) > 20) continue;
                    if (!roomPrototypes.TryGetValue(source.ModelPath, out var prototype))
                    {
                        if (!content.TryRead(source.ModelPath, null, out var bytes, out _)) throw new FileNotFoundException(source.ModelPath);
                        var nif = FalloutNifFile.Read(bytes);
                        if (!nif.Blocks.Any(block => block.TypeName is "bhkCollisionObject" or "bhkBlendCollisionObject")) continue;
                        if (nif.Blocks.Any(block => block.TypeName == "NiControllerManager"))
                        {
                            GD.Print($"OPENNV_MARKER_ROOM_EXCLUDED reference={reference.FormKey} model={source.ModelPath} reason=static-collision-fixture-has-no-controller-owner");
                            continue;
                        }
                        GD.Print($"OPENNV_MARKER_ROOM_SOURCE reference={reference.FormKey} model={source.ModelPath}");
                        prototype = new(nif, units); roomPrototypes.Add(source.ModelPath, prototype);
                    }
                    var instance = prototype.InstantiatePlaced(Placement(world, reference));
                    instance.Name = $"Reference_{reference.FormKey}"; fixture.AddChild(instance);
                }
            }

            RuntimeNativeNpc CreateActor(FalloutReferenceWorld owner)
            {
                var selectedTemplates = owner.InitializeActorTemplates(caller, 1, globals);
                var created = RuntimeNativeNpc.Create(records, content, placed, units,
                    (_, _, _, _) => new StandardMaterial3D(), owner.EquippedArmor(caller, 1, globals), selectedTemplates);
                created.Transform = Placement(owner, placed);
                created.ConfigureContactShapes(configuration.Player.CollisionLayer);
                // Reference registration can precede the movement envelope.
                // Preparing that envelope must update the retained filters,
                // including when an enable/fade owner applies them later.
                GamebryoReferenceEnableRuntime.Apply(created, true);
                var movementMask = configuration.Player.CollisionMask | configuration.Player.CollisionLayer;
                created.Combat = RuntimeNativeActorCombat.Attach(created, created.Skeleton, created.Appearance.SkeletonPath,
                    owner, owner.Get(caller), records, content, configuration.Player.CollisionLayer,
                    movementMask, context);
                created.Combat.PreparePortalArrival();
                movementShape = created.GetChildren().OfType<CollisionShape3D>()
                    .Single(value => value.Name == "SourceActorMovementEnvelope").Shape.GetRid();
                GamebryoReferenceEnableRuntime.Apply(created, false);
                if (created.CollisionMask != 0) throw new InvalidDataException("Disabled movement envelope retained live collision.");
                created.Combat.RefreshAppearanceMovement();
                GamebryoReferenceEnableRuntime.Apply(created, true);
                if (created.CollisionMask != movementMask)
                    throw new InvalidDataException("Reference re-enable lost the late movement envelope's intended collision mask.");
                created.SetProcess(false); created.SetPhysicsProcess(false); created.Combat.SetPhysicsProcess(false);
                created.ExecutePackageEvent = scripts.ExecutePackageEvent;
                // Match the runtime bootstrap ordering: Combat owns the saved
                // package pose before source AI binds its continuation.
                created.ConfigureAi(records, quests, cell, reference => Placement(owner, reference),
                    () => owner.ActorFactions(caller), clock, globals, owner);
                fixture.AddChild(created);
                created.ConfigureHeadTracking(records, content,
                    reference => reference == caller ? created.HeadTargetPoint : null);
                // A retained Look reference is independent of package suppression.
                // Bind the actual actor's own loaded head for this component check.
                created.ApplyHeadTrackingCommand(caller);
                return created;
            }

            void DrainPackageEvents(FalloutReferenceWorld owner, FalloutReferenceScripts eventScripts)
            {
                for (var batchIndex = 0; batchIndex < 8 && owner.PackageEvents.HasPending(caller); batchIndex++)
                {
                    var batch = owner.PackageEvents.SnapshotPending(caller);
                    foreach (var item in batch.Events)
                        foreach (var package in item.Packages ?? throw new InvalidDataException("Package event has no typed source identity."))
                            packageEvents.Add((item.Name, package));
                    var results = eventScripts.DispatchFrame(caller, batch.Events, 0);
                    owner.PackageEvents.Consume(batch);
                    if (results.Any(result => result.Error is not null))
                        throw new NotSupportedException("Attached source actor package event failed: " +
                            string.Join("; ", results.Where(result => result.Error is not null).Select(result => result.Error)));
                }
                if (owner.PackageEvents.HasPending(caller))
                    throw new InvalidDataException("Marker Travel package events did not reach an admitted source frame.");
            }

            actor = CreateActor(world);
            var packageKey = actor.CurrentPackage ?? throw new InvalidDataException("The selected source stage has no resident actor package.");
            var package = records.GetEffective(packageKey);
            var travel = FalloutTravelPackage.Read(package, ownsIdleCollection: true);
            if (travel.LocationType != 0)
                throw new NotSupportedException($"Selected PACK {package.FormKey} is not reference-marker Travel.");
            var markerStart = travel.Start(records, world, caller);
            if (markerStart.Cell != cell.Cell.FormKey)
                throw new NotSupportedException("Marker Travel native audit requires its same-cell NAVM owner.");
            var targetSource = new Vector3(markerStart.Location[0], markerStart.Location[1], markerStart.Location[2]);
            var target = Native(navigation.FindNearestPoint(targetSource));
            var start = actor.GlobalPosition;
            if (start.DistanceTo(target) <= travel.Radius * units + .5f)
                throw new NotSupportedException("Selected actor is already inside its marker arrival radius; choose a source stage with a visible route.");
            IReadOnlyList<Vector3> corridor;
            if (failedRoute)
            {
                var sourceFailed = false;
                try { _ = navigation.FindPath(Source(start), targetSource); }
                catch (InvalidOperationException) { sourceFailed = true; }
                if (!sourceFailed) throw new InvalidOperationException("Failed-route fixture unexpectedly has a supported source corridor.");
                corridor = [Source(start)];
            }
            else corridor = navigation.FindPath(Source(start), targetSource);
            var corridorLength = corridor.Zip(corridor.Skip(1)).Sum(pair => pair.First.DistanceTo(pair.Second)) * units;
            if (!failedRoute && (corridorLength < 1 || corridorLength > 100))
                throw new NotSupportedException($"Selected source route length {corridorLength:R} is outside the isolated capsule-floor fixture.");
            var nativeCorridor = corridor.Select(Native).ToArray();
            if (!failedRoute && nativeCorridor.Any(point => Math.Abs(point.Y - target.Y) > .05f))
                throw new NotSupportedException("Isolated marker Travel floor requires a level source NAVM corridor.");
            if (!ownedRoom)
            {
                var floorPosition = start.Lerp(target, .5f);
                floorPosition.Y = (failedRoute ? start.Y : target.Y) - .05f;
                var floor = new StaticBody3D { Position = floorPosition };
                floor.AddChild(new CollisionShape3D
                { Shape = new BoxShape3D { Size = new(Math.Max(100, corridorLength + 20), .1f, Math.Max(100, corridorLength + 20)) } });
                fixture.AddChild(floor);
            }
            var packageHash = SHA256.HashData(package.ReadData());
            var navigationHash = navigation.SourceSha256;

            async Task Frame()
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                var current = actor ?? throw new InvalidOperationException("Marker Travel actor was released.");
                current._PhysicsProcess(1d / 60);
                current._Process(1d / 60);
                if (current.GetChildren().OfType<CollisionShape3D>()
                    .Single(value => value.Name == "SourceActorMovementEnvelope").Shape.GetRid() != movementShape)
                    throw new InvalidDataException("Source body turning rebuilt its unchanged movement envelope and invalidated its route: " +
                        JsonSerializer.Serialize(current.Combat!.Observation));
                DrainPackageEvents(world!, scripts);
                if (current.AiError is not null || current.AnimationError is not null)
                    throw new InvalidDataException("Native marker Travel diverged: " + JsonSerializer.Serialize(current.AiState) +
                        " " + JsonSerializer.Serialize(current.Combat!.Observation));
                if (current.CurrentPackage is { } activePackage)
                {
                    var head = JsonSerializer.SerializeToElement(current.HeadTrackingState);
                    var tracking = FalloutScriptPackage.Read(records.GetEffective(activePackage)).HeadTrackingEnabled;
                    var targetIsNull = head.GetProperty("pose").GetProperty("target").ValueKind == JsonValueKind.Null;
                    if (head.GetProperty("packageHeadTrackingEnabled").GetBoolean() != tracking ||
                        head.GetProperty("selected").GetString() != caller.ToString() ||
                        head.GetProperty("error").ValueKind != JsonValueKind.Null ||
                        targetIsNull == tracking)
                        throw new InvalidDataException("Package head suppression lost its source flag, retained Look target or physical publication.");
                }
            }

            for (var frame = 0; frame < 1800; frame++)
            {
                await Frame();
                var motion = world.Get(caller).PackageMotion;
                if (failedRoute && motion?.Travel is { Complete: false, RouteFailure: not null } &&
                    motion.Animation.Contains("mtidle", StringComparison.OrdinalIgnoreCase) && motion.Seconds > 0) break;
                if (motion?.Travel is { Complete: false, RouteWaypoints.Length: > 0 } && motion.Animation.Contains("mtforward", StringComparison.OrdinalIgnoreCase) &&
                    motion.Seconds > 0 && actor.GlobalPosition.DistanceTo(start) > .1f) break;
                if (frame == 1799) throw new InvalidDataException("Marker Travel did not produce an in-progress capsule route snapshot: " +
                    JsonSerializer.Serialize(actor.AiState) + " " + JsonSerializer.Serialize(actor.Combat!.Observation));
            }
            if (world.PendingPackageEventCount != 0) throw new InvalidDataException("Initial package event remained pending before the cold save.");
            var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            var savedActor = snapshots.Single(value => value.Reference == caller);
            var savedMotion = savedActor.PackageMotion ?? throw new InvalidDataException("Active marker Travel saved no package motion.");
            var savedProgress = savedMotion.Travel ?? throw new InvalidDataException("Active marker Travel saved no native route.");
            if (savedProgress.Complete || (failedRoute ? savedProgress.RouteFailure is null || savedProgress.RouteWaypoints is not { Length: 0 }
                    : savedProgress.RouteWaypoints is not { Length: > 0 } || savedProgress.RouteFailure is not null) ||
                !savedProgress.NavigationSha256!.Equals(navigationHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cold-save fixture did not capture its active route and winning NAVM identity.");
            var savedBaseSourceSeconds = actor.BaseSourceSeconds;
            var savedPosition = new Vector3(savedMotion.Position[0], savedMotion.Position[1], savedMotion.Position[2]);
            var savedRotation = new Quaternion(savedMotion.Rotation[0], savedMotion.Rotation[1], savedMotion.Rotation[2], savedMotion.Rotation[3]);
            var startMarks = packageEvents.Count(value => value.Event == "OnPackageStart" && value.Package == packageKey);
            if (startMarks != 1) throw new InvalidDataException("Marker Travel start event was not consumed exactly once before the cold save.");

            actor.Free(); actor = null;
            world.Dispose(); world = new(records); world.Restore(snapshots); world.LoadCell(cell);
            scripts = Scripts(world);
            var effectsBeforeCold = effects.Count;
            actor = CreateActor(world);
            var restoredMotion = world.Get(caller).PackageMotion ?? throw new InvalidDataException("Cold marker Travel lost its package motion.");
            var restoredProgress = restoredMotion.Travel ?? throw new InvalidDataException("Cold marker Travel lost its route cursor.");
            var nativeRoute = JsonSerializer.SerializeToElement(actor.Combat!.Observation)
                .GetProperty("engagement").GetProperty("motion");
            var restoredNativeWaypoints = nativeRoute.GetProperty("waypoints").GetInt32();
            var restoredNativeCursor = nativeRoute.GetProperty("cursor").GetInt32();
            var restoredRotation = actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
            if (actor.GlobalPosition.DistanceTo(savedPosition) > .0001f || restoredRotation.AngleTo(savedRotation) > .0001f ||
                restoredMotion.Seconds != savedMotion.Seconds || actor.BaseSourceSeconds != savedBaseSourceSeconds ||
                world.Get(caller).Animation.ElapsedSeconds != savedActor.Animation!.ElapsedSeconds ||
                restoredProgress.RouteCursor != savedProgress.RouteCursor || restoredNativeCursor != savedProgress.RouteCursor ||
                restoredNativeWaypoints != savedProgress.RouteWaypoints.Length ||
                !restoredProgress.NavigationSha256!.Equals(navigationHash, StringComparison.OrdinalIgnoreCase) ||
                restoredProgress.RouteWaypoints!.Length != savedProgress.RouteWaypoints.Length ||
                restoredProgress.RouteWaypoints.Zip(savedProgress.RouteWaypoints).Any(pair => !pair.First.SequenceEqual(pair.Second)) ||
                effects.Count != effectsBeforeCold || world.PendingPackageEventCount != 0 ||
                packageEvents.Count(value => value.Event == "OnPackageStart" && value.Package == packageKey) != 1)
                throw new InvalidDataException("Cold native marker Travel changed its root, package/base clocks, route cursor or event prefix.");

            if (failedRoute)
            {
                if (restoredProgress.RouteFailure != savedProgress.RouteFailure ||
                    nativeRoute.GetProperty("routeError").GetString() != savedProgress.RouteFailure!.Error)
                    throw new InvalidDataException("Cold failed Travel lost its visible source search failure or retry clock.");
                for (var frame = 0; frame < 120; frame++) await Frame();
                var failedMotion = world.Get(caller).PackageMotion!;
                if (failedMotion.Travel is not { Complete: false, RouteFailure: not null, RouteWaypoints.Length: 0 } ||
                    actor.GlobalPosition.DistanceTo(savedPosition) > .1f || effects.Count != effectsBeforeCold ||
                    packageEvents.Count(value => value.Event == "OnPackageStart" && value.Package == packageKey) != 1 ||
                    packageEvents.Any(value => value.Event == "OnPackageDone" && value.Package == packageKey) ||
                    !packageHash.SequenceEqual(SHA256.HashData(package.ReadData())))
                    throw new InvalidDataException("Cold failed Travel moved the actor, fabricated arrival, replayed source results or lost its failure.");
                _ = world.Capture();
                GD.Print($"OPENNV_NATIVE_MARKER_TRAVEL_FAILURE_COLD_PASS actor={caller} package={packageKey} quest={quest} initialStage={stage} " +
                    "sourceNavm=true sourceKf=true capsule=true coldRootClockFailure=true packageStartOnce=true " +
                    "packageDone=false retryPreserved=true sourceUnchanged=true fixture=isolated-floor portalTravelAndCampaign=unverified recording=false");
                return;
            }

            var resumedTravelSeconds = savedMotion.Seconds;
            for (var frame = 0; frame < 60 * 45 && world.Get(caller).PackageMotion?.Travel?.Complete != true; frame++)
            {
                await Frame();
                if (world.Get(caller).PackageMotion is { } advancing && advancing.Animation == savedMotion.Animation)
                    resumedTravelSeconds = Math.Max(resumedTravelSeconds, advancing.Seconds);
            }
            var completedMotion = world.Get(caller).PackageMotion ?? throw new InvalidDataException("Marker Travel lost its completion snapshot.");
            var completed = completedMotion.Travel ?? throw new InvalidDataException("Marker Travel lost its completion record.");
            DrainPackageEvents(world, scripts);
            var arrivalRadius = Math.Max(travel.Radius * units, .25f) + .1f;
            var doneMarks = packageEvents.Count(value => value.Event == "OnPackageDone" && value.Package == packageKey);
            if (!completed.Complete || !actor.IsOnFloor() || actor.GlobalPosition.DistanceTo(target) > arrivalRadius || doneMarks != 1 ||
                resumedTravelSeconds <= savedMotion.Seconds || !packageHash.SequenceEqual(SHA256.HashData(package.ReadData())) ||
                !navigationHash.Equals(CellNavigationGraph.LoadOwned(records, cell.Cell.FormKey).SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cold native marker Travel failed supported arrival, one-time POEA or source identity checks: " +
                    JsonSerializer.Serialize(actor.AiState) + " " + JsonSerializer.Serialize(actor.Combat!.Observation));
            var completionEffects = effects.Count;
            for (var frame = 0; frame < 12; frame++) await Frame();
            if (world.Get(caller).PackageMotion?.Travel?.Complete != true ||
                packageEvents.Count(value => value.Event == "OnPackageStart" && value.Package == packageKey) != 1 ||
                packageEvents.Count(value => value.Event == "OnPackageDone" && value.Package == packageKey) != 1 ||
                effects.Count != completionEffects)
                throw new InvalidDataException("Cold marker Travel repeated its consumed package start or arrival result.");
            _ = world.Capture();
            GD.Print($"OPENNV_NATIVE_MARKER_TRAVEL_COLD_PASS actor={caller} package={packageKey} quest={quest} initialStage={stage} " +
                $"arrivalRadius={travel.Radius} routeCursor={restoredProgress.RouteCursor} nativeWaypoints={restoredNativeWaypoints} " +
                "sourceNavm=true sourceKf=true capsule=true actualSourcePose=true coldRootClockRoute=true resumedArrival=true " +
                "packageStartOnce=true packageDoneOnce=true retainedCollisionFilter=true packageHeadTracking=true retainedLookTarget=true " +
                $"sourceUnchanged=true fixture={(ownedRoom ? "owned-room" : "isolated-floor")} campaignCollisionAndParity=unverified recording=false");
        }
        finally
        {
            actor?.Free(); world?.Dispose(); RuntimeLiveContentSource.Clear(); fixture.Free();
            foreach (var prototype in roomPrototypes.Values) prototype.Scene.Root.Free();
        }
    }
}
