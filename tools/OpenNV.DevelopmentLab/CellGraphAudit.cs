using System.Diagnostics;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CellGraphAudit
{
    private static void RunCell(RuntimeLiveContentSource source, FalloutPluginStack records, FalloutFormKey key,
        string reportPath, Options options, float units, string configurationSha256, Stopwatch clock, CellAuditResources auditResources, SourceGraphInventory graph)
    {
        using var observation = new ReadObservation(source);
        var scene = FalloutCellSceneReader.Read(records, key);
        var faceControls = auditResources.Faces;
        using var virgin = new FalloutReferenceWorld(records, faceControls: faceControls);
        using var saved = new FalloutReferenceWorld(records, faceControls: faceControls);
        var world = virgin;
        var failures = new List<object>();
        if (faceControls is null && scene.BaseObjects.Values.Any(basis => basis.Signature == "NPC_"))
            failures.Add(new { lane = "face-controls-dependency", error = "Face matching has no owned facegen/si.ctl." });
        FalloutNativeCampaignState? checkpoint = null;
        string? checkpointSha256 = null;
        var savedWorldRestored = false;
        if (options.Checkpoint is { } checkpointPath)
        {
            try
            {
                var bytes = File.ReadAllBytes(checkpointPath);
                checkpointSha256 = Hash(bytes);
                var candidate = JsonSerializer.Deserialize<FalloutNativeCampaignState>(bytes) ?? throw new InvalidDataException("Checkpoint is empty.");
                if (candidate.SaveCompatibilityId != source.SaveCompatibilityId)
                    throw new InvalidDataException("Checkpoint source stack differs from the selected owned content.");
                if (records.GetEffective(candidate.ActiveCell).Signature != "CELL")
                    throw new InvalidDataException("Checkpoint active identity is not a winning CELL.");
                saved.RestoreEncounterZones(candidate.EncounterZones);
                saved.Restore(candidate.References ?? throw new InvalidDataException("Checkpoint has no reference state."));
                saved.RestoreActorOverrides(candidate.ActorOverrides);
                saved.RestoreFactionRelations(candidate.FactionRelations);
                checkpoint = candidate;
                world = saved; savedWorldRestored = true;
            }
            catch (Exception error) { failures.Add(new { lane = "saved-reference-world", error = error.Message }); }
        }
        object? nativeReview = null;
        string? snapshotSha256 = null;
        var nativeFailures = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        var nativeScope = new HashSet<string>(StringComparer.Ordinal);
        var nativeActorAi = new Dictionary<string, object>(StringComparer.Ordinal);
        if (options.Snapshot is { } snapshotPath)
        {
            try
            {
                var bytes = File.ReadAllBytes(snapshotPath); snapshotSha256 = Hash(bytes);
                using var snapshot = JsonDocument.Parse(bytes);
                var review = CellReview.Build(records, source.SaveCompatibilityId, snapshot.RootElement);
                if (review.Cell != key.ToString()) throw new InvalidDataException("Snapshot is a different CELL.");
                nativeReview = new { review.Cell, review.RuntimeBuild, review.CapturedUtc, sourceReferences = review.SourceReferences.Count,
                    failures = review.Failures, review.ExcludedNonresidentDiagnostics };
                foreach (var item in review.SourceReferences) nativeScope.Add(item.Identity);
                foreach (var failure in review.Failures)
                    foreach (var item in failure.References)
                    {
                        if (!nativeFailures.TryGetValue(item.Identity, out var list)) nativeFailures.Add(item.Identity, list = []);
                        list.Add(new { failure.Lane, failure.Reason });
                    }
                foreach (var actor in snapshot.RootElement.GetProperty("actors").EnumerateArray())
                {
                    if (!actor.GetProperty("animation").TryGetProperty("ai", out var ai)) continue;
                    JsonElement? Field(string name) => ai.TryGetProperty(name, out var field) ? field.Clone() : null;
                    nativeActorAi.Add(actor.GetProperty("reference").GetString()!, new
                    {
                        selectedPackage = Field("selectedPackage"), currentProcedure = Field("currentProcedure"),
                        error = Field("error"), idleCollection = Field("idleCollection"), furniture = Field("furniture"),
                        marker = Field("marker"), furniturePhase = Field("furniturePhase"), unbound = Field("unbound"),
                        snapshotOnly = true, savedStateAndSourceAuditAreSeparate = true
                    });
                }
            }
            catch (Exception error)
            {
                nativeReview = null; nativeScope.Clear(); nativeFailures.Clear(); nativeActorAi.Clear();
                failures.Add(new { lane = "native-snapshot-binding", error = error.Message });
            }
        }

        var resources = new Dictionary<string, ResourceRow>(StringComparer.OrdinalIgnoreCase);
        var models = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
        var allCollision = new List<Triangle>();
        var references = new List<ReferenceRow>();

        ResourceRow Resource(string path, string kind)
        {
            var row = auditResources.Read(path, kind);
            if (!resources.TryAdd(row.Path, row)) return row;
            foreach (var dependency in row.DependencyEdges) _ = Resource(dependency.Path, dependency.Kind);
            return row;
        }

        Model ReadModel(string path)
        {
            path = Canonical(path);
            if (models.TryGetValue(path, out var retained)) return retained;
            var model = auditResources.ReadModel(path);
            models.Add(path, model); _ = Resource(path, model.Report.Kind);
            return model;
        }

        void AddPlacedCollision(FalloutPlacedReference reference, ReferenceRow row, Model model)
        {
            if (!(row.SavedEnabled ?? row.SourceEnabled ?? false)) return;
            var pose = world.Placement(reference.FormKey);
            var transform = ReferenceTransform(pose.Position, pose.RotationRadians, reference.Scale, units);
            allCollision.AddRange(model.Collision.Select(triangle => triangle with { A = transform * triangle.A,
                B = transform * triangle.B, C = transform * triangle.C, Reference = row.Identity }));
        }

        foreach (var reference in scene.References)
        {
            var record = records.GetEffective(reference.FormKey);
            var basis = scene.BaseObjects[reference.Base];
            records.TryGetEffective(reference.Base, out var baseRecord);
            var row = new ReferenceRow { Identity = reference.FormKey.ToString(), Base = reference.Base.ToString(), Signature = basis.Signature,
                Winner = record.Plugin.Name, Sha256 = Hash(record.ReadData()), BaseWinner = baseRecord?.Plugin.Name,
                BaseSha256 = baseRecord is null ? null : Hash(baseRecord.ReadData()), Flags = reference.Flags,
                Position = reference.Position, Rotation = reference.RotationRadians, Scale = reference.Scale,
                InitiallyDisabled = FalloutCellSceneReader.IsInitiallyDisabled(reference), EnableParent = reference.EnableParent?.ToString(),
                Opposite = reference.EnableParentOpposite, Model = basis.ModelPath };
            references.Add(row);
            var sharedInstance = world.Get(reference.FormKey);
            row.Script = new { source = sharedInstance.Script?.Record.FormKey.ToString(), sha256 = sharedInstance.Script?.Sha256,
                savedExecutionError = sharedInstance.ScriptError, sharedInstance.PackageAssignment, sharedInstance.SelectionFailure,
                sharedInstance.PackageBindingFailure, sharedInstance.ProcedureCaptureBlocker };
            row.NativeActorAi = nativeActorAi.GetValueOrDefault(row.Identity);
            try { row.EnableDomain = EnableDomain(records, virgin, reference.FormKey); }
            catch (Exception error) { row.Failures.Add(new { lane = "source-enable-domain", error = error.Message }); }
            if (basis.Signature is "NPC_" or "CREA")
                try { row.Packages = PackageAlternatives(records, reference.Base); }
                catch (Exception error) { row.Failures.Add(new { lane = "actor-package-source-graph", error = error.Message }); }
            try { row.SourceEnabled = virgin.IsEnabled(reference.FormKey); }
            catch (Exception error) { row.Failures.Add(new { lane = "source-enable-chain", error = error.Message }); }
            if (savedWorldRestored)
                try { row.SavedEnabled = saved.IsEnabled(reference.FormKey); row.SavedPlacement = saved.Placement(reference.FormKey); }
                catch (Exception error) { row.Failures.Add(new { lane = "saved-enable-placement", error = error.Message }); }
            row.Native = nativeScope.Contains(row.Identity) ? new { inSelectedResidentScope = true,
                failures = nativeFailures.GetValueOrDefault(row.Identity) ?? [], observationDoesNotEstablishParity = true } :
                (object)new { inSelectedResidentScope = false, admission = "unverified" };
            if (FalloutNewVegasBuiltinForms.IsInternalStatic(basis.Signature, records.RuntimeFormId(basis.FormKey)))
                row.Disposition = "source-engine-static-marker-no-game-draw";
            else if (basis.Signature == "XPRM") row.Disposition = "source-primitive-trigger";
            else if (basis.Light is not null)
            {
                row.Disposition = "source-light";
                if (basis.ModelPath is { } lightModel)
                {
                    var model = ReadModel(lightModel);
                    foreach (var issue in model.Report.Failures)
                        row.Failures.Add(new { lane = "light-model-resource", model = model.Report.Path, issue });
                    AddPlacedCollision(reference, row, model);
                }
                try { _ = FalloutPlacedLightResolver.Resolve(reference, basis, records); }
                catch (Exception error) { row.Failures.Add(new { lane = "light-admission", error = error.Message }); }
            }
            else if (basis.Signature == "NPC_")
            {
                row.Disposition = "source-humanoid";
                try
                {
                    var instance = world.Get(reference.FormKey);
                    var selection = instance.Templates ?? new FalloutActorTemplateSelection(checkpoint?.Vitals?.Level ?? 1, 1);
                    selection.ResolveAll(records, reference.Base);
                    var armor = savedWorldRestored ? world.EquippedArmor(reference.FormKey, checkpoint?.Vitals?.Level ?? 1) : null;
                    var appearance = FalloutNpcAppearanceResolver.Resolve(records, reference.Base, reference.FormKey, armor,
                        savedWorldRestored ? world.ActorAppearanceOverride(reference.FormKey) : null, selection);
                    var skeleton = ReadModel(appearance.SkeletonPath);
                    foreach (var part in appearance.Models)
                    {
                        if (part.ModelPath is { } path) _ = ReadModel(path);
                        if (part.TexturePath is { } texture) _ = Resource(texture, "texture");
                        foreach (var alternate in part.AlternateTextures)
                            foreach (var alternateTexture in alternate.Textures.Values) _ = Resource(alternateTexture, "texture");
                    }
                    string? preparationFailure = null;
                    try { auditResources.ValidateAppearance(appearance); }
                    catch (Exception error) { preparationFailure = error.Message; row.Failures.Add(new { lane = "humanoid-prepared-geometry", error = error.Message }); }
                    foreach (var blocker in appearance.Blockers) row.Failures.Add(new { lane = "humanoid-appearance", error = blocker });
                    row.Actor = new { appearance.Npc, appearance.Race, appearance.Female, appearance.Height, appearance.SkeletonPath,
                        appearance.TraitsOwner, appearance.ModelOwner, appearance.InventoryOwner, appearance.EquippedArmor,
                        parts = appearance.Models.Select(part => new { part.Role, part.Source, part.ModelPath, part.TexturePath, part.BipedSlots }),
                        appearance.Blockers, preparationFailure, skeletonCollisionBodies = skeleton.Report.CollisionBodies,
                        physicalContactAdmission = "requires-native-contact-owner-audit", selectionIsDiagnosticWhenNotSaved = instance.Templates is null };
                }
                catch (Exception error) { row.Failures.Add(new { lane = "humanoid-graph", error = error.Message }); }
            }
            else if (basis.Signature == "CREA")
            {
                row.Disposition = "source-creature";
                try
                {
                    var appearance = FalloutCreatureAppearanceResolver.Resolve(records, reference.Base, reference.FormKey, world.Get(reference.FormKey).Templates);
                    _ = ReadModel(appearance.SkeletonPath);
                    foreach (var path in appearance.Models) _ = ReadModel(path);
                    foreach (var path in appearance.AnimationFiles) _ = ReadModel(path);
                    row.Actor = new { appearance.Creature, appearance.ModelOwner, appearance.StatsOwner, appearance.SkeletonPath,
                        appearance.BaseScale, appearance.Models, appearance.AnimationFiles, nativeAssemblyAdmission = "unverified" };
                }
                catch (Exception error) { row.Failures.Add(new { lane = "creature-graph", error = error.Message }); }
            }
            else if (basis.ModelPath is { } modelPath)
            {
                var model = ReadModel(modelPath);
                row.Disposition = model.File?.Blocks.Any(block => block.TypeName == "bhkSimpleShapePhantom") == true
                    ? "source-shaped-phantom-contact-unverified" : "source-model";
                foreach (var issue in model.Report.Failures) row.Failures.Add(new { lane = "model", model = model.Report.Path, issue });
                AddPlacedCollision(reference, row, model);
            }
            else row.Disposition = basis.Signature switch
            {
                "SOUN" => "source-placed-sound-native-owner-unverified",
                "ASPC" => "source-acoustic-volume-native-owner-unverified",
                _ => "source-no-model-runtime-owner-unverified"
            };
            try
            {
                if (FalloutReferencePrimitive.Read(record) is { } primitive)
                {
                    row.Primitive = new { primitive, scriptContactOwnerRequired = sharedInstance.Script is not null,
                        nativeContactExecution = "unverified" };
                    if (sharedInstance.Script is not null && (primitive.CollisionLayer is not (null or 12) || primitive.Type is not (1 or 2) ||
                        primitive.Type == 2 && (primitive.X != primitive.Y || primitive.X != primitive.Z)))
                        row.Failures.Add(new { lane = "primitive-contact-admission", primitive.Type, primitive.CollisionLayer,
                            error = "Primitive has no existing source contact/filter owner." });
                }
            }
            catch (Exception error) { row.Failures.Add(new { lane = "primitive-reader", error = error.Message }); }
            if (reference.Teleport is not null)
                try
                {
                    var door = FalloutDoorDestinationResolver.Resolve(records, reference);
                    var destinationNavigation = auditResources.Navigation(records, door.DestinationScene.Cell.FormKey);
                    if (destinationNavigation.Failures.Length != 0)
                        throw new InvalidDataException("Destination navigation declarations failed: " + JsonSerializer.Serialize(destinationNavigation.Failures, Json));
                    IReadOnlyList<FalloutNavigationMesh> destinationNav = destinationNavigation.Meshes;
                    var point = reference.Teleport.Position;
                    var floor = NavigationFloor(destinationNav, point[0], point[1]);
                    var destinationRecord = records.GetEffective(door.Destination.FormKey);
                    var lockSource = record.ReadSubrecords().Where(field => field.Signature == "XLOC").Select(field => field.Data.Length).ToArray();
                    var lockTarget = destinationRecord.ReadSubrecords().Where(field => field.Signature == "XLOC").Select(field => field.Data.Length).ToArray();
                    var destinationState = world.Get(door.Destination.FormKey);
                    row.Door = new { destination = door.Destination.FormKey.ToString(), cell = door.DestinationScene.Cell.FormKey.ToString(),
                        destinationEnabled = world.IsEnabled(door.Destination.FormKey), reference.Teleport.Position, reference.Teleport.RotationRadians,
                        directedLink = true, reciprocal = door.Destination.Teleport?.Door == reference.FormKey, reference.Teleport.Flags,
                        sourceXlocExtents = lockSource, targetXlocExtents = lockTarget,
                        sourceLockState = sharedInstance.LockState, sourceLegacyUnlocked = sharedInstance.Unlocked,
                        targetLockState = destinationState.LockState, targetLegacyUnlocked = destinationState.Unlocked,
                        sourceAccess = DoorAccess(virgin, reference.FormKey), targetSourceAccess = DoorAccess(virgin, door.Destination.FormKey),
                        savedAccess = savedWorldRestored ? DoorAccess(saved, reference.FormKey) : null,
                        targetSavedAccess = savedWorldRestored ? DoorAccess(saved, door.Destination.FormKey) : null,
                        destinationNavFloorHeights = floor, navMeshes = destinationNav.Count, physicalArrivalAdmission = "unverified" };
                }
                catch (Exception error) { row.Failures.Add(new { lane = "xtel-graph", error = error.Message }); }
        }
        var navigation = auditResources.Navigation(records, key);
        IReadOnlyList<FalloutNavigationMesh> navMeshes = navigation.Meshes;
        var navFailures = navigation.Failures;
        var navRows = navMeshes.Select(mesh => new { form = mesh.Form.ToString(), winner = records.GetEffective(mesh.Form).Plugin.Name,
            sha256 = Hash(records.GetEffective(mesh.Form).ReadData()), mesh.Version, vertices = mesh.Vertices.Length,
            triangles = mesh.Triangles.Length, externalEdges = mesh.Edges.Select(edge => new { edge.Type, mesh = edge.Mesh.ToString(), edge.Triangle,
                winningTarget = records.TryGetEffective(edge.Mesh, out var target) && target.Signature == "NAVM" }),
            doors = mesh.Doors.Select(door => new { door = door.Door.ToString(), door.Triangle,
                boundResidentReference = scene.References.Any(reference => reference.FormKey == door.Door) }) }).ToArray();
        var support = new List<object>();
        var floorIndex = new SourceFloorIndex(allCollision);
        foreach (var mesh in navMeshes)
            for (var index = 0; index < mesh.Triangles.Length; index++)
            {
                var triangle = mesh.Triangles[index];
                var center = (mesh.Vertices[triangle.Vertices[0]] + mesh.Vertices[triangle.Vertices[1]] + mesh.Vertices[triangle.Vertices[2]]) / 3;
                var native = GamebryoCoordinate.ConvertVector(new(center.X, center.Y, center.Z)) * units;
                var hits = Floors(floorIndex, native.X, native.Z);
                var nearest = hits.OrderBy(hit => MathF.Abs(hit.Height - native.Y)).FirstOrDefault();
                support.Add(new { mesh = mesh.Form.ToString(), triangle = index, triangle.Flags, sourceCentroid = new[] { center.X, center.Y, center.Z },
                    nativeHeight = native.Y, sampledCollisionHeight = nearest?.Height, heightDifference = nearest is null ? (float?)null : native.Y - nearest.Height,
                    nearest?.Reference, nearest?.Shape, nearest?.NormalY, sourceSupportSample = true, nativePhysicsUnverified = true });
            }
        var sample = options.SampleNative;
        var sampleHeights = sample is { } samplePoint ? Floors(floorIndex, samplePoint.X, samplePoint.Z) : [];
        var deleted = (graph.CellChildren.GetValueOrDefault(key) ?? []).Where(record => record.IsDeleted && PlacedSignatures.Contains(record.Signature))
            .Select(record => new { reference = record!.FormKey.ToString(), record.Signature, winner = record.Plugin.Name, record.Flags, sha256 = Hash(record.ReadData()) }).ToArray();
        var referenceIssues = references.Count(row => row.Failures.Count != 0);
        var report = new
        {
            schema = "opennv-development-cell-graph-audit/v1", cell = scene.Cell, source.SaveCompatibilityId,
            runtimeAuditBuild = typeof(FalloutNifFile).Module.ModuleVersionId,
            configurationSha256,
            plugins = records.Plugins.Select(plugin => new { plugin.Plugin.Name, plugin.Sha256, plugin.Bytes, plugin.MtimeUnixMilliseconds }),
            elapsedSeconds = clock.Elapsed.TotalSeconds,
            denominator = new { effectiveReferences = scene.References.Count, uniqueBases = scene.BaseObjects.Count, winningDeletedReferences = deleted.Length,
                enabledSource = references.Count(row => row.SourceEnabled == true), enabledSaved = references.Count(row => row.SavedEnabled == true),
                referencesWithFailures = referenceIssues, nativeScopeReferences = nativeScope.Count, nativeFailedIdentities = nativeFailures.Count,
                nifResources = models.Count, models = resources.Values.Count(resource => resource.Kind == "model"),
                animations = resources.Values.Count(resource => resource.Kind == "animation"), textures = resources.Values.Count(resource => resource.Kind == "texture"),
                failedResources = resources.Values.Count(resource => resource.Failures.Count != 0), collisionTrianglesEvaluated = allCollision.Count,
                navMeshes = navMeshes.Count, navVertices = navMeshes.Sum(mesh => mesh.Vertices.Length), navTriangles = navMeshes.Sum(mesh => mesh.Triangles.Length),
                navSupportWithoutPackedOrBoxHit = support.Count(item => JsonSerializer.SerializeToElement(item).GetProperty("sampledCollisionHeight").ValueKind == JsonValueKind.Null),
                referenceTypes = references.GroupBy(row => row.Signature).ToDictionary(group => group.Key, group => group.Count()),
                dispositions = references.GroupBy(row => row.Disposition).ToDictionary(group => group.Key, group => group.Count()) },
            invariants = new { uniqueReferenceIdentity = references.Select(row => row.Identity).Distinct().Count() == scene.References.Count,
                allEffectiveCellChildrenEnumerated = records.EffectiveCellChildren(key, new HashSet<string> { "REFR", "ACHR", "ACRE", "PGRE", "PMIS" }).Count == scene.References.Count,
                readerValidatedEveryPlacementAndRotation = true, everyBaseBoundOrDeclaredEnginePrimitive = scene.References.All(reference => scene.BaseObjects.ContainsKey(reference.Base)),
                savedWorldRestored, noSourceWrites = true, noGameplayDispatchOrNativePhysicsExecution = true,
                omittedEditorMarkersAreNotArchitecturalFloors = true, collisionMathScope = "Source current-state packed triangles and boxes with the placed-reference root replacement policy and shared transform/units. Other primitive hulls, source filters and native publication/contact remain explicit unverified lanes." },
            finiteDomain = new { enable = "Both independent root enable values through actual reference owner; all chain edges and current absent/taken cuts retained.",
                packages = "All authored package-owner template and leveled-list alternatives, priority candidates, schedules, conditions and event declarations.",
                appearances = "Selected saved or diagnostic template/outfit resource graph only. Arbitrary script-driven, leveled/template and outfit appearance/resource alternatives are not exhausted.",
                arbitraryScriptNumericStatesExhausted = false, nativeExecutionPermutationsExhausted = false },
            checkpoint = new { checkpointSha256, checkpoint?.Schema, checkpoint?.ActiveCell, checkpoint?.Stage,
                referenceStateOnly = true, wholeSaveColdLoadVerified = false },
            nativeReview, snapshotSha256, failures, references, deletedReferences = deleted,
            resources = resources.Values.OrderBy(resource => resource.Path), allActuallyReadSourceResources = observation.Resources.Values,
            projectionCandidates = new { floorIndex.TriangleCount, floorIndex.DegenerateProjections, floorIndex.UnboundedProjections,
                floorIndex.Nodes, floorIndex.Queries, floorIndex.VisitedNodes, floorIndex.CandidateEvaluations,
                exhaustiveCandidateEvaluations = floorIndex.Queries * floorIndex.TriangleCount,
                everyOriginalTriangleRetained = floorIndex.TriangleCount == allCollision.Count,
                projectionOwner = "Existing Height/Floors over original candidates; exhaustive equivalence and full-sweep performance have separate acceptance.",
                nativeContactTraversalAndParity = "unverified" },
            navMeshes = navRows, navFailures,
            navCentroidSupport = support, requestedSample = sample is { } sampled ? new { nativePoint = new[] { sampled.X, sampled.Y, sampled.Z },
                sourcePoint = new[] { sampled.X / units, -sampled.Z / units, sampled.Y / units },
                sourceNavHeights = NavigationFloor(navMeshes, sampled.X / units, -sampled.Z / units), collisionHeights = sampleHeights } : null,
            nativeOwner = new { rootTransform = "identity; runtime/main.tscn and native cell root", units,
                basis = "(x,y,z) -> (x,z,-y)", placement = "GamebryoCoordinate.ConvertReferenceEuler and ReferenceTransform",
                modelRoot = "Replaced by TES reference; descendants retain authored local transforms.",
                localCollision = "NativeNifCollisionBuilder BodyTransform/MatrixTransform; packed vertex Havok scale 7" },
            verdict = "Divergence inventory and source mathematics; no level-completion, gameplay, native contact, or parity claim."
        };
        WriteReport(reportPath, report);
        Console.WriteLine(JsonSerializer.Serialize(new { audit = "cell-graph", report = Path.GetFullPath(reportPath), cell = key.ToString(),
            references = references.Count, referenceIssues, resources = resources.Count, models = models.Count, navMeshes = navMeshes.Count,
            navTriangles = navMeshes.Sum(mesh => mesh.Triangles.Length), nativeFailedIdentities = nativeFailures.Count, savedWorldRestored,
            requestedSampleCollisionHeights = sampleHeights, elapsedSeconds = clock.Elapsed.TotalSeconds }));
    }

}
