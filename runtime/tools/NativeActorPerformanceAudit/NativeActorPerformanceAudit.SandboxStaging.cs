using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private async Task OwnedSandboxStaging(string game, string checkpoint, string[] actorIds)
    {
        var original = File.ReadAllBytes(checkpoint);
        var checkpointHash = Convert.ToHexString(SHA256.HashData(original));
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(original) ?? throw new InvalidDataException("Reached stopped checkpoint is absent.");
        if (saved.Schema != FalloutNativeCampaignSave.ExpectedSchema || saved.SaveCompatibilityId != content.SaveCompatibilityId ||
            saved.References is null || saved.Quests is null || saved.Globals is null || saved.GameTime is null || saved.SkyLighting is null)
            throw new InvalidDataException("Native Sandbox audit requires a genuine current checkpoint from this exact owned vanilla stack.");
        var sourceHashes = content.PluginSources.ToDictionary(source => source.AbsolutePath, source => SourceHash(source.AbsolutePath));
        var configuration = RuntimeConfiguration.Load();
        var units = configuration.World.GameUnitsToMeters;
        var globals = FalloutGlobalState.Read(records); globals.Restore(saved.Globals);
        var quests = new FalloutQuestState(records); quests.Restore(saved.Quests);
        var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records), FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe")));
        clock.Restore(saved.GameTime);
        var sky = new FalloutSkyLightingState(records, FalloutGameSettingFloats.ReadRetained(records, "fDaytimeColorExtension", nameof(FalloutSkyLightingState)));
        sky.Restore(saved.SkyLighting);
        var unexpectedResults = 0;
        var fixture = new Node3D
        {
            Name = "OwnedSandboxStagingAudit",
            ProcessMode = ProcessModeEnum.Disabled,
            Transform = new(new Basis(Vector3.Up, .37f).Scaled(Vector3.One * 1.5f), new(3, -2, 4))
        };
        AddChild(fixture);
        try
        {
            foreach (var text in actorIds)
            {
                var split = text.Split(':');
                if (split.Length != 2) throw new InvalidDataException("Supply an exact owned actor FormKey.");
                var key = new FalloutFormKey(split[0], Convert.ToUInt32(split[1], 16));
                var input = saved.References.Single(value => value.Reference == key);
                if (records.GetEffective(input.Base).Signature != "NPC_" || input.SandboxAdmissionFailure is not { Blink: not null, LastEvent: "POBA", EventRevision: 1 } stopped ||
                    input.Animation is null || input.SoundRandomState is null || input.HeadTracking is null)
                    throw new InvalidDataException("Selected owned NPC has no eligible genuine stopped POBA/blink/clock/head prefix.");
                foreach (var failure in new[] { "before-events", "after-configure", "stale-publication" })
                {
                    // Admit this exact genuine NPC continuation only. Other saved
                    // actors may require live audio/runtime owners outside this
                    // fixture. They remain untouched in the original checkpoint;
                    // this is not whole-campaign cold-save admission. LoadCell
                    // still creates ordinary source state for its other references.
                    using var world = RestoreWorld([input], saved.ActorOverrides);
                    var cell = FalloutCellSceneReader.Read(records, world.Placement(key).Cell);
                    world.LoadCell(cell);
                    var placed = cell.References.Single(value => value.FormKey == key);
                    var templates = world.InitializeActorTemplates(key, saved.Vitals?.Level ?? 1, globals);
                    var before = Snapshot(world);
                    var packageHistory = PackageHistory(world, key);
                    if (JsonSerializer.Serialize(world.Get(key).PackageStarts.ToArray()) != JsonSerializer.Serialize((input.PackageStarts ?? []).ToArray()) ||
                        JsonSerializer.Serialize(world.PackageEvents.Capture(key)) != JsonSerializer.Serialize(input.PackageEvents ?? []))
                        throw new InvalidDataException("Restored native fixture changed genuine package timing or pending event marks.");
                    var beforeQuest = JsonSerializer.Serialize(quests.Capture());
                    var beforeGlobals = JsonSerializer.Serialize(globals.Capture());
                    var beforeTime = JsonSerializer.Serialize(clock.Capture());
                    string expected = before;
                    if (failure == "stale-publication")
                    {
                        using var expectedWorld = RestoreWorld(world.Capture(), world.CaptureActorOverrides());
                        expectedWorld.SetActorAlerted(key, !expectedWorld.ActorAlerted(key));
                        expected = Snapshot(expectedWorld);
                    }
                    RuntimeNativeNpc? candidate = null;
                    var refused = false;
                    try
                    {
                        candidate = Create(world, cell, placed, templates);
                        if (failure == "before-events")
                        {
                            var blink = typeof(RuntimeNativeNpc).GetField("_blink", BindingFlags.Instance | BindingFlags.NonPublic) ??
                                throw new MissingFieldException("Owned NPC blink fault seam is absent.");
                            if (blink.FieldType != typeof(FalloutFaceBlink) || blink.GetValue(candidate) is null)
                                throw new InvalidDataException("The actual source actor has no blink owner to fault.");
                            // Audit-only loss of a real rig owner, never a production
                            // fault option, fabricated actor, receipt or retail edit.
                            blink.SetValue(candidate, null);
                        }
                        candidate.ConfigureAi(records, quests, cell, reference => Placement(world, reference),
                            () => world.ActorFactions(key), clock, globals, world);
                        if (failure == "before-events") throw new InvalidDataException("Expected pre-event restore failure did not occur.");
                        RequireSandboxLifecycle(candidate, stopped);
                        RequirePackageHistory(world, key, packageHistory);
                        if (failure == "after-configure") throw new IOException("Injected post-Configure detached failure.");
                        world.SetActorAlerted(key, !world.ActorAlerted(key));
                        fixture.AddChild(candidate);
                        if (world.Get(key).CanCaptureSandboxAdmissionFailure?.Invoke() != false)
                            throw new InvalidDataException("Uncommitted tree attachment admitted stopped capture.");
                        RuntimeCoordinator.RequireNativeActorPublication(candidate);
                        throw new InvalidDataException("Stale native publication unexpectedly committed.");
                    }
                    catch (InvalidDataException error) when (failure == "before-events" && error.Message == "Saved Sandbox has no source blink owner.")
                    {
                        var events = Events(candidate!);
                        if (events.Active is not null || events.Revision != 0) throw new InvalidDataException("Blink failure occurred after restoring events.");
                        refused = true;
                    }
                    catch (IOException error) when (failure == "after-configure" && error.Message == "Injected post-Configure detached failure.") { refused = true; }
                    catch (InvalidDataException error) when (failure == "stale-publication" && error.Message.Contains("authority changed during detached staging", StringComparison.Ordinal)) { refused = true; }
                    finally
                    {
                        if (candidate is not null)
                        {
                            RuntimeCoordinator.DiscardNativeActor(candidate, null);
                            if (GodotObject.IsInstanceValid(candidate)) throw new InvalidDataException("Product discard left the failed owned NPC alive.");
                        }
                    }
                    var state = world.Get(key);
                    RequirePackageHistory(world, key, packageHistory);
                    if (!refused || Snapshot(world) != expected || state.QueryCurrentPackage is not null ||
                        state.CaptureSandboxAdmissionFailure is not null || state.CanCaptureSandboxAdmissionFailure is not null ||
                        state.CapturePackageAssignment is not null || state.QuerySpatialPlacement is not null || state.CaptureHeadTracking is not null ||
                        beforeQuest != JsonSerializer.Serialize(quests.Capture()) || beforeGlobals != JsonSerializer.Serialize(globals.Capture()) ||
                        beforeTime != JsonSerializer.Serialize(clock.Capture()) || unexpectedResults != 0)
                        throw new InvalidDataException("Actual native failure/discard changed retained state, leaked bindings or replayed results: " + failure);
                    var retryBaseline = world.Capture().ToArray();
                    var retryOverrides = JsonSerializer.Serialize(world.CaptureActorOverrides());
                    var retry = Create(world, cell, placed, templates);
                    try
                    {
                        var receipt = state.SandboxAdmissionFailure!;
                        retry.ConfigureAi(records, quests, cell, reference => Placement(world, reference),
                            () => world.ActorFactions(key), clock, globals, world);
                        fixture.AddChild(retry);
                        var intended = receipt.ResolveTransform(fixture.GlobalTransform, units, placed.Scale);
                        RuntimeCoordinator.RequireNativeActorPublication(retry);
                        RequireSandboxLifecycle(retry, stopped);
                        RequirePackageHistory(world, key, packageHistory);
                        if (!retry.GlobalTransform.IsEqualApprox(intended) || state.CanCaptureSandboxAdmissionFailure?.Invoke() != true ||
                            state.SandboxAdmissionFailure?.SourcePlacement is not null || unexpectedResults != 0)
                            throw new InvalidDataException("Fresh native retry failed pose publication, capture admission or event preservation.");
                        var retained = state.Capture().SandboxAdmissionFailure!;
                        if (retained.EventRevision != 1 || retained.LastEvent != "POBA" || retained.AiRandomState != receipt.AiRandomState ||
                            state.Animation.ElapsedSeconds != input.Animation.ElapsedSeconds || state.SoundRandom.State != input.SoundRandomState)
                            throw new InvalidDataException("Fresh retry changed event history, retained AI/shared RNG or the base clock before ticks.");
                        RequireSnapshotExceptPublishedPose(retryBaseline, world.Capture(), key);
                        if (JsonSerializer.Serialize(world.CaptureActorOverrides()) != retryOverrides)
                            throw new InvalidDataException("Fresh native retry replaced shared actor overrides.");
                        if (failure == "stale-publication") await ExerciseConversationFacing(world, cell, placed, retry, key);
                        RequirePackageHistory(world, key, packageHistory);
                    }
                    finally
                    {
                        if (GodotObject.IsInstanceValid(retry))
                        {
                            RuntimeCoordinator.RetireNativeActorBindings(retry);
                            RuntimeCoordinator.DiscardNativeActor(retry, null);
                        }
                    }
                    GD.Print($"OPENNV_NATIVE_SANDBOX_STAGING_CASE_PASS actor={key} fault={failure} actualConfigure=true actualDiscard=true publicationCaller=true retry=true retainedEventRevision=1 pendingMarksUnchanged=true packageStartsUnchanged=true additionalResults=0 compiledResultProof=false scopedNativeFixture=true fullCampaignSaveAdmission=unverified ordinaryInput=false recording=false");
                }
            }
        }
        finally
        {
            if (GodotObject.IsInstanceValid(fixture)) fixture.Free();
            if (!File.ReadAllBytes(checkpoint).AsSpan().SequenceEqual(original)) throw new InvalidDataException("Reached checkpoint changed during native audit.");
            foreach (var (source, hash) in sourceHashes)
                if (SourceHash(source) != hash) throw new InvalidDataException("Owned plugin changed during native audit.");
        }
        GD.Print($"OPENNV_NATIVE_SANDBOX_STAGING_PASS actors={actorIds.Length} checkpointSha256={checkpointHash} actualGodot=true scopedNativeFixture=true fullCampaignSaveAdmission=unverified ordinaryInput=false campaignAndPixels=unverified sourceReadonly=true recording=false");

        FalloutReferenceWorld RestoreWorld(IReadOnlyList<FalloutReferenceSnapshot> references, IReadOnlyList<FalloutActorOverrides>? overrides)
        {
            var owner = new FalloutReferenceWorld(records);
            try
            {
                owner.RestoreEncounterZones(saved.EncounterZones); owner.Restore(references); owner.RestoreActorOverrides(overrides);
                owner.RestoreFactionRelations(saved.FactionRelations); owner.RestoreDetection(saved.DetectionEvents);
                owner.UnloadedPackages = new(records, owner, quests, clock, globals, (_, _) => UnexpectedResult(), () => saved.Vitals?.Level ?? 1);
                return owner;
            }
            catch { owner.Dispose(); throw; }
        }
        void UnexpectedResult() { unexpectedResults++; throw new InvalidDataException("Cold native binding dispatched an unexpected package result."); }
        static string SourceHash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        string Snapshot(FalloutReferenceWorld owner) => JsonSerializer.Serialize(new { References = owner.Capture(), ActorOverrides = owner.CaptureActorOverrides() });
        string PackageHistory(FalloutReferenceWorld owner, FalloutFormKey actor) =>
            JsonSerializer.Serialize(new { Starts = owner.Get(actor).PackageStarts.ToArray(), Marks = owner.PackageEvents.Capture(actor) });
        void RequirePackageHistory(FalloutReferenceWorld owner, FalloutFormKey actor, string expected)
        {
            if (PackageHistory(owner, actor) != expected)
                throw new InvalidDataException("Actual native binding replayed or changed genuine package starts or pending event marks/revisions.");
        }
        Transform3D Placement(FalloutReferenceWorld owner, FalloutPlacedReference reference)
        {
            var placement = owner.Placement(reference.FormKey);
            return new(GamebryoCoordinate.ConvertReferenceEuler(new(placement.RotationRadians[0], placement.RotationRadians[1], placement.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(placement.Position[0], placement.Position[1], placement.Position[2])) * units);
        }
        RuntimeNativeNpc Create(FalloutReferenceWorld owner, FalloutCellScene cell, FalloutPlacedReference placed, FalloutActorTemplateSelection templates)
        {
            Color ambient;
            if (cell.Cell.Lighting is { } lighting) ambient = new(lighting.AmbientRgb[0] / 255f, lighting.AmbientRgb[1] / 255f, lighting.AmbientRgb[2] / 255f);
            else
            {
                var weather = sky.ActiveWeather;
                var weights = FalloutWeatherTimeWeights.Sample(sky.Climate, clock.Hour, sky.DaytimeExtension, weather.TimeSamples);
                var rgb = weather.Sample(weights, 3); ambient = new(rgb[0], rgb[1], rgb[2]);
            }
            var npc = RuntimeNativeNpc.Create(records, content, placed, units,
                (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, ambient),
                owner.EquippedArmor(placed.FormKey, saved.Vitals?.Level ?? 1, globals), templates, owner.ActorAppearanceOverride(placed.FormKey));
            try
            {
                npc.Transform = Placement(owner, placed);
                npc.ConfigureContactShapes(configuration.Player.CollisionLayer);
                npc.ConfigureHeadTracking(records, content, _ => null);
                npc.ExecutePackageEvent = (_, _) => UnexpectedResult();
                npc.Combat = RuntimeNativeActorCombat.Attach(npc, npc.Skeleton, npc.Appearance.SkeletonPath, owner, owner.Get(placed.FormKey),
                    records, content, configuration.Player.CollisionLayer, configuration.Player.CollisionMask | configuration.Player.CollisionLayer);
                npc.SetProcess(false); npc.SetPhysicsProcess(false); npc.Combat.SetPhysicsProcess(false);
                return npc;
            }
            catch { RuntimeCoordinator.DiscardNativeActor(npc, null); throw; }
        }
        static FalloutPackageEvents Events(RuntimeNativeNpc npc) =>
            (typeof(RuntimeNativeNpc).GetField("_packageEvents", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(npc) as FalloutPackageEvents) ??
            throw new InvalidDataException("Native package event owner is absent.");
        static void RequireSandboxLifecycle(RuntimeNativeNpc npc, FalloutActorSandboxAdmissionFailure stopped)
        {
            var events = Events(npc);
            if (events.Active?.Form != stopped.Package || events.Done || events.Error is not null || events.Revision != stopped.EventRevision ||
                events.LastEvent != stopped.LastEvent || events.LastPackage != stopped.LastPackage)
                throw new InvalidDataException("Actual ConfigureAi failed to restore the consumed POBA lifecycle.");
        }
        static void RequireSnapshotExceptPublishedPose(IReadOnlyList<FalloutReferenceSnapshot> expected,
            IReadOnlyList<FalloutReferenceSnapshot> actual, FalloutFormKey key)
        {
            var before = expected.Single(value => value.Reference == key).SandboxAdmissionFailure ??
                throw new InvalidDataException("Native checkpoint comparison lost its original stopped receipt.");
            // Actual global readback is checked separately with Godot's transform
            // tolerance. Only its float encoding and consumed pending-placement
            // marker may differ; every other retained field must remain exact.
            var normalized = actual.Select(value => value.Reference != key ? value : value with
            {
                SandboxAdmissionFailure = (value.SandboxAdmissionFailure ?? throw new InvalidDataException("Native capture lost stopped receipt.")) with
                { Position = before.Position, Basis = before.Basis, SourcePlacement = before.SourcePlacement }
            }).ToArray();
            if (JsonSerializer.Serialize(normalized) != JsonSerializer.Serialize(expected))
                throw new InvalidDataException("Native attachment changed retained state beyond validated pose publication.");
        }
        async Task ExerciseConversationFacing(FalloutReferenceWorld owner, FalloutCellScene cell, FalloutPlacedReference placed,
            RuntimeNativeNpc npc, FalloutFormKey key)
        {
            var state = owner.Get(key);
            var packageHistory = PackageHistory(owner, key);
            var sourcePose = owner.SpawnPlacement(key, null!, units);
            owner.SetPlacement(key, sourcePose);
            var pending = state.SandboxAdmissionFailure!;
            npc.Transform = Placement(owner, placed);
            npc.AcknowledgeSandboxPlacement(pending);
            var before = npc.GlobalBasis;
            var player = FalloutNativeCampaignSave.RestorePlayerPosition(saved, configuration.Player.SpawnCenterHeightMeters);
            var target = new Vector3(player[0], player[1], player[2]);
            var direction = target - npc.GlobalPosition; direction.Y = 0;
            if (direction.LengthSquared() < .0001f || before.Orthonormalized().GetRotationQuaternion().AngleTo(
                Basis.LookingAt(direction.Normalized(), Vector3.Up).GetRotationQuaternion()) < .05f)
                throw new InvalidDataException("Genuine player position does not qualify a measurable conversation turn for this actor.");
            var ticks = 0;
            void Tick() => ticks++;
            npc.PosePublished += Tick;
            npc.BeginConversationFacing(() => target, FalloutGameSettingFloats.Read(records, "fCharacterDefaultTurningSpeed"));
            npc.ProcessMode = ProcessModeEnum.Always; npc.SetProcess(true);
            try
            {
                for (var frame = 0; frame < 300; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (ticks > 0 && before.Orthonormalized().GetRotationQuaternion().AngleTo(npc.GlobalBasis.Orthonormalized().GetRotationQuaternion()) > .02f) break;
                }
            }
            finally { npc.SetProcess(false); npc.EndConversationFacing(); npc.PosePublished -= Tick; }
            var turned = npc.GlobalTransform;
            RequireSandboxLifecycle(npc, state.SandboxAdmissionFailure!);
            RequirePackageHistory(owner, key, packageHistory);
            if (ticks == 0 || before.Orthonormalized().GetRotationQuaternion().AngleTo(turned.Basis.Orthonormalized().GetRotationQuaternion()) <= .02f)
                throw new InvalidDataException("Actual Godot NPC process did not retain its conversation-facing turn.");
            var playerPlacement = new FalloutReferencePlacement(saved.ActiveCell,
                [target.X / units, -target.Z / units, target.Y / units], [0, 0, 0]);
            var turnedHeading = owner.HeadingAngle(key, records.RuntimeFormKey(0x14), playerPlacement, units);
            var captured = owner.Capture();
            var capturedOverrides = owner.CaptureActorOverrides();
            var retained = captured.Single(value => value.Reference == key).SandboxAdmissionFailure!;
            if (retained.SourcePlacement is not null || !retained.ResolveTransform(fixture.GlobalTransform, units, placed.Scale).IsEqualApprox(turned))
                throw new InvalidDataException("Shared capture replaced the later dialogue basis with the earlier source placement.");
            var afterTurn = Snapshot(owner);
            RuntimeCoordinator.RetireNativeActorBindings(npc); RuntimeCoordinator.DiscardNativeActor(npc, null);
            RequirePackageHistory(owner, key, packageHistory);
            if (Snapshot(owner) != afterTurn ||
                Math.Abs(owner.HeadingAngle(key, records.RuntimeFormKey(0x14), playerPlacement, units) - turnedHeading) > .001f)
                throw new InvalidDataException("Actual retirement changed the frozen post-dialogue retained state or heading.");
            using var cold = RestoreWorld(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(captured))!,
                JsonSerializer.Deserialize<FalloutActorOverrides[]>(JsonSerializer.Serialize(capturedOverrides))!);
            cold.LoadCell(cell);
            var coldTemplates = cold.InitializeActorTemplates(key, saved.Vitals?.Level ?? 1, globals);
            var coldNpc = Create(cold, cell, placed, coldTemplates);
            try
            {
                coldNpc.ConfigureAi(records, quests, cell, reference => Placement(cold, reference), () => cold.ActorFactions(key), clock, globals, cold);
                fixture.AddChild(coldNpc); RuntimeCoordinator.RequireNativeActorPublication(coldNpc);
                RequireSandboxLifecycle(coldNpc, retained);
                RequirePackageHistory(cold, key, packageHistory);
                var coldReceipt = cold.Get(key).Capture().SandboxAdmissionFailure!;
                if (!coldNpc.GlobalTransform.IsEqualApprox(turned) || coldReceipt.AiRandomState != retained.AiRandomState ||
                    coldReceipt.EventRevision != 1 || cold.Get(key).Animation.ElapsedSeconds != state.Animation.ElapsedSeconds ||
                    cold.Get(key).SoundRandom.State != state.SoundRandom.State ||
                    Math.Abs(cold.HeadingAngle(key, records.RuntimeFormKey(0x14), playerPlacement, units) - turnedHeading) > .001f || unexpectedResults != 0)
                    throw new InvalidDataException("Cold actual NPC lost post-dialogue facing, clocks, RNG or consumed event history.");
                RequireSnapshotExceptPublishedPose(captured, cold.Capture(), key);
                if (JsonSerializer.Serialize(cold.CaptureActorOverrides()) != JsonSerializer.Serialize(capturedOverrides))
                    throw new InvalidDataException("Cold native reconstruction replaced post-dialogue shared actor overrides.");
            }
            finally
            {
                if (GodotObject.IsInstanceValid(coldNpc))
                { RuntimeCoordinator.RetireNativeActorBindings(coldNpc); RuntimeCoordinator.DiscardNativeActor(coldNpc, null); }
            }
            RequirePackageHistory(cold, key, packageHistory);
            GD.Print($"OPENNV_NATIVE_SANDBOX_DIALOGUE_FACING_PASS actor={key} processTicks={ticks} sharedPlacementAcknowledged=true actualConversationFacing=true actualRetirement=true coldBasis=true coldHeading=true eventRevision=1 pendingMarksUnchanged=true packageStartsUnchanged=true additionalResults=0 compiledResultProof=false elapsedRandomPreservedAtCold=true scopedNativeFixture=true fullCampaignSaveAdmission=unverified ordinaryInput=false campaignAndPixels=unverified");
        }
    }
}
