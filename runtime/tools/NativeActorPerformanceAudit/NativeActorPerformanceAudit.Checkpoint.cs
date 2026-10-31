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
    private async Task NpcCheckpoint(string game, string? mod, string? root, string actorId, string questId,
        short stage, string expected, string[] dependencies, float? scheduleHour = null)
    {
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            if (mod is null)
                RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                    activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
            else
            {
                var installation = new FalloutModStackSelection([new(mod, root!, dependencies)]).Resolve(game);
                RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                    installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            }
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var identity = actorId.Split(':');
            var caller = identity is { Length: 2 } ? new FalloutFormKey(identity[0], Convert.ToUInt32(identity[1], 16)) :
                FalloutDialogueTopic.Find(records, "ACHR", actorId).FormKey;
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
            var quests = new FalloutQuestState(records); quests.EnterStage(quest, stage);
            var globals = FalloutGlobalState.Read(records);
            var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records),
                FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe")));
            if (scheduleHour is { } hour) clock.SetHour(hour);
            if (expected == "selection") world.UnloadedPackages = new(records, world, quests, clock, globals,
                (_, _) => throw new InvalidDataException("Failed selection unload ran package results."), () => 1);
            var cell = FalloutCellSceneReader.Read(records, world.Get(caller).Cell); world.LoadCell(cell);
            var rootReference = caller; var enabled = true; var parents = new HashSet<FalloutFormKey>();
            while (world.Get(rootReference).EnableParent is { } parent)
            {
                if (!parents.Add(rootReference)) throw new InvalidDataException("Fixture enable chain is cyclic.");
                enabled ^= parent.Opposite; rootReference = parent.Reference;
            }
            world.SetEnabled(rootReference, enabled); world.AdvanceEnableChanges(0, new(1, 1), _ => false);
            // Explicit source state for this isolated continuation fixture.
            // No prior campaign, dialogue or quest result is claimed.
            world.Get(caller).TalkedToPlayer = true;
            var placed = cell.References.Single(value => value.FormKey == caller);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            Transform3D Placement(FalloutReferenceWorld owner, FalloutPlacedReference reference)
            {
                var location = owner.Placement(reference.FormKey);
                return new(GamebryoCoordinate.ConvertReferenceEuler(new(location.RotationRadians[0],
                    location.RotationRadians[1], location.RotationRadians[2]), reference.Scale),
                    GamebryoCoordinate.ConvertVector(new(location.Position[0], location.Position[1], location.Position[2])) * units);
            }
            RuntimeNativeNpc Assemble(FalloutReferenceWorld owner)
            {
                var templates = owner.InitializeActorTemplates(caller, 1, globals);
                var actor = RuntimeNativeNpc.Create(records, content, placed, units,
                    (_, _, _, _) => new StandardMaterial3D(), owner.EquippedArmor(caller, 1, globals), templates);
                actor.Transform = Placement(owner, placed); fixture.AddChild(actor);
                actor.SetProcess(false); actor.SetPhysicsProcess(false);
                try
                {
                    actor.ConfigureAi(records, quests, cell, reference => Placement(owner, reference),
                        clock: clock, globals: globals, world: owner);
                }
                catch { actor.Free(); throw; }
                return actor;
            }
            var warm = Assemble(world);
            using var warmLifetime = new PackageFixtureLifetime(warm);
            if (expected == "quest-running")
            {
                NativePackageQuestRunning(warm, records, world, quests, caller, content, cell, units, fixture);
                return;
            }
            warm._Process(.125);
            if (expected == "furniture-idle")
            {
                for (var frame = 0; frame < 7200; ++frame)
                {
                    if (world.Get(caller).FurnitureCaptureReady &&
                        world.Get(caller).Capture().FurnitureContinuation?.IdleState?.ActiveAnimation?.Clock.CompletedRepeats >= 2) break;
                    warm._Process(1d / 60);
                }
            }
            if (expected == "dialogue")
            {
                for (var frame = 0; frame < 7200 && !world.Get(caller).DialogueCaptureReady; ++frame)
                    warm._Process(1d / 60);
            }
            var saved = world.Capture(); var actorState = saved.Single(value => value.Reference == caller);
            bool HasOwner(FalloutReferenceSnapshot state) => expected switch
            {
                "furniture" => state.FurnitureContinuation is not null,
                "furniture-idle" => state.FurnitureContinuation?.IdleState?.ActiveAnimation is not null,
                "selection" => state.SelectionFailure is not null,
                "dialogue" => state.DialogueContinuation is not null,
                "binding" => state.PackageBindingFailure is not null,
                _ => throw new ArgumentException("Unknown checkpoint owner fixture."),
            };
            if (!HasOwner(actorState) || world.PendingProcedureCaptureCount != 0)
                throw new InvalidDataException("Source fixture did not reach the required owned checkpoint continuation.");
            if (expected == "furniture")
            {
                using (var newProcedureWorld = new FalloutReferenceWorld(records))
                {
                    newProcedureWorld.LoadCell(cell);
                    newProcedureWorld.SetEnabled(rootReference, enabled);
                    newProcedureWorld.AdvanceEnableChanges(0, new(1, 1), _ => false);
                    newProcedureWorld.Get(caller).TalkedToPlayer = true;
                    var directory = warm.Appearance.SkeletonPath.Replace('\\', '/');
                    directory = directory[..directory.LastIndexOf('/')];
                    var previousPath = directory + "/locomotion/mtidle.kf";
                    var previousSource = NativeAnimationResources.For(content).Read(previousPath);
                    newProcedureWorld.Get(caller).Animation.Change(previousPath, previousSource.Sha256);
                    newProcedureWorld.Get(caller).Animation.Advance(2.5);
                    var selectedProcedure = Assemble(newProcedureWorld);
                    using var selectedLifetime = new PackageFixtureLifetime(selectedProcedure);
                    selectedProcedure._Process(.125);
                    var selectedState = newProcedureWorld.Get(caller).Capture();
                    var selectedFurniture = selectedState.FurnitureContinuation ??
                        throw new InvalidDataException("Fresh source furniture did not retain its selected procedure.");
                    selectedFurniture.ValidateClock(selectedState.Animation);
                    if (selectedState.Animation?.Resource.Equals(previousPath, StringComparison.OrdinalIgnoreCase) != false)
                        throw new InvalidDataException("Retained locomotion replaced newly selected furniture base ownership.");
                    using var restoredProcedureWorld = new FalloutReferenceWorld(records);
                    restoredProcedureWorld.Restore(newProcedureWorld.Capture());
                    restoredProcedureWorld.LoadCell(cell);
                    var restoredProcedure = Assemble(restoredProcedureWorld);
                    using var restoredLifetime = new PackageFixtureLifetime(restoredProcedure);
                    if (restoredProcedureWorld.Get(caller).Animation.Capture() != selectedState.Animation ||
                        restoredProcedure.SittingState != selectedProcedure.SittingState)
                        throw new InvalidDataException("Selected furniture source/phase changed on cold restore.");
                    GD.Print("OPENNV_FURNITURE_BASE_HANDOFF_PASS actualProcedure=true priorWalkingClock=true cold=true");
                }
                using var handoffWorld = new FalloutReferenceWorld(records);
                handoffWorld.Restore(saved); handoffWorld.LoadCell(cell);
                RuntimeNativeNpc AssembleHandoff()
                {
                    var result = Assemble(handoffWorld);
                    result.ConfigureHeadTracking(records, content, target => target == caller ? result.HeadTargetPoint : null);
                    return result;
                }
                var initial = AssembleHandoff();
                var handoff = new RuntimeNativeReferencePresentation(handoffWorld, [placed], _ => AssembleHandoff());
                fixture.AddChild(handoff);
                using var handoffLifetime = new PackageFixtureLifetime(handoff);
                handoff.Register(caller, initial);
                var beforeHandoff = JsonSerializer.Serialize(handoffWorld.Get(caller).Capture());
                await handoff.PrepareActorAudioTransferAsync([]);
                handoff.RetireOverlappingActors([placed], out var previous);
                if (GodotObject.IsInstanceValid(initial) || previous.Count != 1 || handoff.HasPrepared(caller) ||
                    handoffWorld.Get(caller).CaptureHeadTracking is not null || handoffWorld.Get(caller).HeadTracking is null)
                    throw new InvalidDataException("Actor overlap handoff left an old native head owner alive.");
                handoff.RestoreRetiredActors(previous);
                if (handoffWorld.Get(caller).CaptureHeadTracking is null ||
                    JsonSerializer.Serialize(handoffWorld.Get(caller).Capture()) != beforeHandoff)
                {
                    using var before = JsonDocument.Parse(beforeHandoff);
                    var after = JsonSerializer.SerializeToElement(handoffWorld.Get(caller).Capture());
                    void Difference(JsonElement left, JsonElement right, string path)
                    {
                        if (left.GetRawText() == right.GetRawText()) return;
                        if (left.ValueKind == JsonValueKind.Object && right.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var field in left.EnumerateObject())
                                if (right.TryGetProperty(field.Name, out var value)) Difference(field.Value, value, path + "." + field.Name);
                            return;
                        }
                        static string Bounded(JsonElement value) { var text = value.GetRawText(); return text[..Math.Min(512, text.Length)]; }
                        GD.Print($"OPENNV_FURNITURE_HANDOFF_DIFFERENCE field={path} before={Bounded(left)} after={Bounded(right)}");
                    }
                    Difference(before.RootElement, after, "actor");
                    throw new InvalidDataException("Actor overlap rollback changed its retained source continuation.");
                }
                handoff.SetResidency([], _ => throw new InvalidDataException("Warm retirement must not rematerialize."), _ => true);
                await handoff.PrepareActorAudioTransferAsync([]);
                handoff.RetireOverlappingActors([placed], out var warmPrevious);
                if (warmPrevious.Count != 0 || handoff.HasPrepared(caller) ||
                    handoffWorld.Get(caller).CaptureHeadTracking is not null)
                    throw new InvalidDataException("Warm overlap retained its native binding or requested resident rollback.");
                GD.Print("OPENNV_NATIVE_ACTOR_OVERLAP_PASS activeAndWarm=true headOwnerRetired=true rollbackExactContinuation=true");
            }
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(saved))!);
            cold.LoadCell(cell);
            var resumed = Assemble(cold);
            using var resumedLifetime = new PackageFixtureLifetime(resumed);
            if (resumed.Transform != warm.Transform || resumed.AiError != warm.AiError ||
                resumed.SittingState != warm.SittingState || resumed.CurrentPackage != warm.CurrentPackage ||
                JsonSerializer.Serialize(cold.Get(caller).Capture()) != JsonSerializer.Serialize(world.Get(caller).Capture()))
                throw new InvalidDataException("Native cold NPC assembly changed its pose, source fault, random queue or procedure state.");
            if (actorState.FurnitureContinuation is { Phase: 3 })
                NativePackageSitting(warm, resumed, records, world, cold, caller, actorState.FurnitureContinuation);
            if (expected == "furniture-idle")
                FurnitureIdleSuffix(warm, resumed, world, cold, caller, actorState);
            foreach (var delta in new[] { 0d, .01, .125, .25 })
            {
                warm._Process(delta); resumed._Process(delta);
                if (resumed.Transform != warm.Transform || resumed.AiError != warm.AiError ||
                    JsonSerializer.Serialize(cold.Get(caller).Capture()) != JsonSerializer.Serialize(world.Get(caller).Capture()))
                    throw new InvalidDataException("Native cold NPC continuation diverged after identical clock advancement.");
            }
            if (expected == "selection")
            {
                var beforeUnload = world.Capture();
                var failureBeforeUnload = beforeUnload.Single(value => value.Reference == caller).SelectionFailure!;
                fixture.RemoveChild(warm); world.UnloadCell(cell.Cell.FormKey);
                var retained = world.Get(caller);
                if (retained.CanCaptureSelectionFailure is not null || retained.CaptureSelectionFailure is not null ||
                    retained.QueryCurrentPackage is not null || retained.CapturePackageAssignment is not null ||
                    retained.SelectionFailure is null || retained.ProcedureCaptureBlocker != failureBeforeUnload.Error ||
                    world.IsResident(caller) || world.PendingProcedureCaptureCount != 0 ||
                    world.CurrentPackage(caller) is not null)
                    throw new InvalidDataException("Native selection unload lost its retained fault or left a live capture delegate.");
                var afterUnload = world.Capture();
                if (JsonSerializer.Serialize(afterUnload) != JsonSerializer.Serialize(beforeUnload))
                    throw new InvalidDataException("Native selection unload changed source, pose, clock, random, blink or consumed retirement.");
                foreach (var invalid in new[] { failureBeforeUnload with { Sha256 = new string('0', 64) },
                    failureBeforeUnload with { Condition = FalloutCondition.Read(records.GetEffective(failureBeforeUnload.Candidate)).Count } })
                {
                    using var rejected = new FalloutReferenceWorld(records);
                    var corrupted = afterUnload.Select(value => value.Reference == caller ? value with { SelectionFailure = invalid } : value).ToArray();
                    var refused = false;
                    try { rejected.Restore(corrupted); }
                    catch (InvalidDataException) { refused = true; }
                    if (!refused || rejected.InstanceCount != 0)
                        throw new InvalidDataException("Native unloaded failed-selection source drift was not rejected atomically.");
                }
                using var unloadedCold = new FalloutReferenceWorld(records);
                unloadedCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(afterUnload))!);
                unloadedCold.LoadCell(cell);
                var afterUnloadActor = Assemble(unloadedCold);
                using var afterUnloadLifetime = new PackageFixtureLifetime(afterUnloadActor);
                if (afterUnloadActor.Transform != warm.Transform || afterUnloadActor.AiError != warm.AiError ||
                    afterUnloadActor.CurrentPackage is not null ||
                    JsonSerializer.Serialize(unloadedCold.Capture()) != JsonSerializer.Serialize(afterUnload))
                    throw new InvalidDataException("Cold native attachment changed the unloaded failed selection.");
                GD.Print($"OPENNV_NATIVE_SELECTION_UNLOAD_CHECKPOINT_PASS actor={caller} delegateRetired=true " +
                    "unloadedCapture=true exactSourcePoseClockFaultRandom=true nativeCold=true sourceDriftAtomic=true packageResultsNotReplayed=true");
            }
            if (expected == "furniture-idle")
            {
                var beforeUnload = world.Capture();
                var occupied = beforeUnload.Single(value => value.Reference == caller).FurnitureContinuation!;
                if (occupied.IdleState?.ActiveAnimation is null)
                    throw new InvalidDataException("Native suffix fixture finished its selected overlay before unload.");
                fixture.RemoveChild(warm); world.UnloadCell(cell.Cell.FormKey);
                var retained = world.Get(caller);
                var afterUnload = world.PendingProcedureCaptureCount == 0 ? world.Capture() : null;
                if (retained.CaptureFurniture is not null || retained.CanCaptureFurniture is not null ||
                    !retained.FurnitureCaptureReady || world.PendingProcedureCaptureCount != 0 || world.IsResident(caller) ||
                    JsonSerializer.Serialize(afterUnload) != JsonSerializer.Serialize(beforeUnload))
                {
                    GD.Print("OPENNV_NATIVE_OCCUPIED_IDLE_RETIREMENT_DIFF " + JsonSerializer.Serialize(new
                    {
                        captureDelegate = retained.CaptureFurniture is not null,
                        readinessDelegate = retained.CanCaptureFurniture is not null,
                        ready = retained.FurnitureCaptureReady,
                        pending = world.PendingProcedureCaptureCount,
                        resident = world.IsResident(caller),
                        assignment = retained.PackageAssignment,
                        blocker = retained.ProcedureCaptureBlocker,
                        fields = afterUnload is null ? null : CorpseFieldDiff(JsonSerializer.SerializeToElement(beforeUnload.Single(value => value.Reference == caller)),
                            JsonSerializer.SerializeToElement(afterUnload.Single(value => value.Reference == caller))),
                    }));
                    throw new InvalidDataException("Occupied collection idle lost its selected clock/seat after child-first retirement.");
                }
                using var unloadedCold = new FalloutReferenceWorld(records);
                unloadedCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(beforeUnload))!);
                unloadedCold.LoadCell(cell);
                var afterUnloadActor = Assemble(unloadedCold);
                using var afterUnloadLifetime = new PackageFixtureLifetime(afterUnloadActor);
                if (!unloadedCold.OwnsFurnitureSeat(occupied.Furniture!.Value, occupied.Seat!.Index, caller) ||
                    JsonSerializer.Serialize(unloadedCold.Capture()) != JsonSerializer.Serialize(beforeUnload))
                    throw new InvalidDataException("Cold native idle assembly changed its occupied seat, phase or consumed selection.");
                FurnitureIdleSuffix(resumed, afterUnloadActor, cold, unloadedCold, caller,
                    beforeUnload.Single(value => value.Reference == caller));
                FurnitureResidualBindingRefusals(records, beforeUnload, caller, Assemble);
                foreach (var invalid in new[]
                {
                    occupied.IdleState with { ActiveAnimation = occupied.IdleState.ActiveAnimation with { IdleSha256 = new string('0', 64) } },
                    occupied.IdleState with { ActiveAnimation = occupied.IdleState.ActiveAnimation with { Resource = "meshes/unbound-source.kf" } },
                    occupied.IdleState with { Collection = new(0, 0, 0, false) }
                })
                {
                    using var rejected = new FalloutReferenceWorld(records);
                    var refused = false;
                    try
                    {
                        rejected.Restore(beforeUnload.Select(value => value.Reference == caller ? value with
                        { FurnitureContinuation = occupied with { IdleState = invalid } } : value).ToArray());
                    }
                    catch (InvalidDataException) { refused = true; }
                    if (!refused || rejected.InstanceCount != 0)
                        throw new InvalidDataException("Occupied source idle/selection drift was not refused atomically.");
                }
                GD.Print($"OPENNV_NATIVE_OCCUPIED_IDLE_CHECKPOINT_PASS actor={caller} idle={occupied.IdleState.ActiveAnimation.Idle} " +
                    $"runtimeMvid={typeof(RuntimeConfiguration).Assembly.ManifestModule.ModuleVersionId} " +
                    "selectedLoops=true separateBaseClock=true textKeySuffix=true occupiedSeat=true nativeCold=true " +
                    "residualComponents=true nativeSkeletonAndCoverageDrift=true childFirstRetirement=true " +
                    "sourceDriftAtomic=true sourceEffectsNotReplayed=true recording=false");
            }
            if (cold.Get(caller).Capture().FurnitureContinuation is { Furniture: { } furniture, Seat: { } seat })
                RestoreFurnitureReservation(resumed, cold, caller, records.RuntimeFormKey(0x14), furniture, seat.Index);
            PreserveReplacementCaptureOwner(resumed, cold.Get(caller));
            GD.Print($"OPENNV_NATIVE_NPC_CHECKPOINT_PASS actor={caller} owner={expected} phase={warm.SittingState} " +
                "nativeCold=true exactPose=true clock=true randomAndBlink=true sourceEffectsNotReplayed=true " +
                "replacementCaptureOwnerPreserved=true fixture=isolated-owned-records campaignAndParity=unverified recording=false");
        }
        finally { if (GodotObject.IsInstanceValid(fixture)) fixture.Free(); }
    }

    private static void RestoreFurnitureReservation(RuntimeNativeNpc actor, FalloutReferenceWorld world,
        FalloutFormKey caller, FalloutFormKey otherActor, FalloutFormKey furniture, int seat)
    {
        var before = JsonSerializer.Serialize(world.Get(caller).Capture());
        var pose = actor.GlobalTransform;
        world.ReleaseFurnitureSeat(furniture, seat, caller);
        actor.RestoreSourceBindings();
        if (!world.OwnsFurnitureSeat(furniture, seat, caller))
            throw new InvalidDataException("Failed replacement retirement lost the live actor's source furniture seat.");
        world.ReleaseFurnitureSeat(furniture, seat, caller);
        if (!world.ReserveFurnitureSeat(furniture, seat, otherActor))
            throw new InvalidDataException("Furniture handoff fixture could not reserve its actual source seat.");
        try
        {
            var refused = false;
            try { actor.RestoreSourceBindings(); }
            catch (NotSupportedException) { refused = true; }
            if (!refused || !world.OwnsFurnitureSeat(furniture, seat, otherActor))
                throw new InvalidDataException("Furniture owner restoration stole an occupied source seat.");
        }
        finally
        {
            world.ReleaseFurnitureSeat(furniture, seat, otherActor);
            actor.RestoreSourceBindings();
        }
        if (actor.GlobalTransform != pose || JsonSerializer.Serialize(world.Get(caller).Capture()) != before)
            throw new InvalidDataException("Furniture owner restoration changed the live pose or continuation.");
        GD.Print($"OPENNV_NATIVE_FURNITURE_RESERVATION_HANDOFF_PASS actor={caller} furniture={furniture} seat={seat} " +
            "sameSourceSeat=true conflictRefused=true exactPoseAndContinuation=true");
    }

    private static void PreserveReplacementCaptureOwner(RuntimeNativeNpc actor, FalloutReferenceInstance state)
    {
        var pending = state.PendingPackageSelection;
        var dialogue = state.DialogueContinuation;
        var furniture = state.FurnitureContinuation;
        var assignment = state.PackageAssignment;
        var blocker = state.ProcedureCaptureBlocker;
        Func<bool> ready = () => throw new InvalidDataException("Retiring actor queried its replacement capture owner.");
        Func<FalloutActorPendingPackageSelection> pendingCapture = () => throw new InvalidDataException("Replacement pending capture was invoked.");
        Func<FalloutActorDialogueContinuation?> dialogueCapture = () => throw new InvalidDataException("Replacement dialogue capture was invoked.");
        Func<FalloutActorFurnitureContinuation?> furnitureCapture = () => throw new InvalidDataException("Replacement furniture capture was invoked.");
        Func<FalloutFormKey?> package = () => null;
        state.CanCapturePendingPackageSelection = state.CanCaptureDialogue = state.CanCaptureFurniture = ready;
        state.CapturePendingPackageSelection = pendingCapture;
        state.CaptureDialogue = dialogueCapture;
        state.CaptureFurniture = furnitureCapture;
        state.QueryCurrentPackage = package;
        actor.Free();
        if (!ReferenceEquals(state.PendingPackageSelection, pending) || !ReferenceEquals(state.DialogueContinuation, dialogue) ||
            !ReferenceEquals(state.FurnitureContinuation, furniture) || !ReferenceEquals(state.PackageAssignment, assignment) ||
            state.ProcedureCaptureBlocker != blocker || state.CapturePendingPackageSelection != pendingCapture ||
            state.CaptureDialogue != dialogueCapture || state.CaptureFurniture != furnitureCapture ||
            state.CanCapturePendingPackageSelection != ready || state.CanCaptureDialogue != ready || state.CanCaptureFurniture != ready ||
            state.QueryCurrentPackage != package)
            throw new InvalidDataException("Retiring actor overwrote another native owner's continuation or binding.");
        state.CanCapturePendingPackageSelection = state.CanCaptureDialogue = state.CanCaptureFurniture = null;
        state.CapturePendingPackageSelection = null; state.CaptureDialogue = null; state.CaptureFurniture = null;
        state.QueryCurrentPackage = null; state.CapturePackageAssignment = null;
    }

    private static void NativePackageQuestRunning(RuntimeNativeNpc actor, FalloutPluginStack records,
        FalloutReferenceWorld world, FalloutQuestState quests, FalloutFormKey caller,
        RuntimeLiveContentSource content, FalloutCellScene cell, float units, Node3D fixture)
    {
        var sourceActor = world.Get(caller);
        var packageOwner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(sourceActor.Base), 32, sourceActor.Templates);
        var conditions = packageOwner.ReadSubrecords().Where(field => field.Signature == "PKID")
            .Select(field => records.GetEffective(packageOwner.Plugin.AdjustFormId(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span))))
            .SelectMany(FalloutCondition.Read).Where(condition => condition.Function == 56 && condition.RunOn == 0).ToArray();
        if (conditions.Length == 0) throw new InvalidDataException("Selected source actor has no admitted quest-running package condition.");
        var creatureReference = cell.References.FirstOrDefault(reference => records.GetEffective(reference.Base).Signature == "CREA") ??
            throw new InvalidDataException("Selected source cell has no creature for the native condition-owner check.");
        var creatureState = world.Get(creatureReference.FormKey);
        world.InitializeActorTemplates(creatureReference.FormKey, 1, FalloutGlobalState.Read(records));
        var creature = RuntimeNativeCreature.Create(records, content, creatureReference, creatureState, units);
        try
        {
            creature.SetProcess(false); creature.SetPhysicsProcess(false); fixture.AddChild(creature);
            creature.ConfigureAi(records, quests, world);
            NativeCreaturePackageSitting(actor, creature, records, world, caller, conditions[0].Owner);
            foreach (var condition in conditions)
            {
                var original = quests.Capture().SingleOrDefault(value => value.Quest == condition.FormArgument1)?.Running ??
                    (quests.Evaluate(condition) == 1);
                foreach (var running in new[] { false, true, false })
                {
                    quests.SetRunning(condition.FormArgument1, running);
                    if (actor.EvaluateAiCondition(condition) != (running ? 1 : 0) ||
                        creature.PackageCondition(condition) != (running ? 1 : 0))
                        throw new InvalidDataException("Native package condition ignored its actual quest-running owner.");
                }
                foreach (var invalid in new[] { condition with { Function = 45 }, condition with { RunOn = 1, Reference = 0 },
                    condition with { Argument1 = condition.Owner.RawFormId } })
                {
                    static void Refuse(Func<float> query)
                    {
                        try { _ = query(); }
                        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
                        throw new InvalidDataException("Native package quest query admitted an unrelated function, scope or record type.");
                    }
                    Refuse(() => actor.EvaluateAiCondition(invalid)); Refuse(() => creature.PackageCondition(invalid));
                }
                quests.SetRunning(condition.FormArgument1, original);
                GD.Print($"OPENNV_NATIVE_PACKAGE_QUEST_RUNNING_PASS actor={caller} source={condition.Owner.FormKey} " +
                    $"quest={condition.FormArgument1} npcAndCreature=true liveStartStop=true wrongTypeScopeAndUnrelatedFunctionRefused=true gameplay=false recording=false");
            }
        }
        finally { creature.Free(); }
    }
}
