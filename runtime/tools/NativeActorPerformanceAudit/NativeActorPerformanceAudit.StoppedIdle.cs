using System.Diagnostics;
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
    private async Task OwnedStoppedIdle(string game, string mod, string root, string checkpoint,
        string actorId, string questId, short stage, string packageId, string idleId, string[] soundIds, string[] dependencies)
    {
        var original = File.ReadAllBytes(checkpoint);
        var manualFixtureDirectory = Path.Combine(Path.GetTempPath(), "opennv-native-manual-wait-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(manualFixtureDirectory);
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(original) ?? throw new InvalidDataException("Stopped idle checkpoint is absent.");
            if (saved.SaveCompatibilityId != content.SaveCompatibilityId || saved.References is null ||
                saved.Schema is not (FalloutNativeCampaignSave.ExpectedSchema or FalloutNativeCampaignSave.ActivationRelaySchema or FalloutNativeCampaignSave.NativeSoundHistorySchema or FalloutNativeCampaignSave.TerminalResultsSchema))
                throw new InvalidDataException("Stopped idle fixture requires its genuine matching complete checkpoint.");
            static FalloutFormKey Key(string text)
            {
                var parts = text.Split(':');
                return parts.Length == 2 ? new(parts[0], Convert.ToUInt32(parts[1], 16)) : throw new ArgumentException("Expected plugin:hex-object-id.");
            }
            var caller = Key(actorId); var expectedPackage = Key(packageId); var idle = Key(idleId);
            var expectedSounds = soundIds.Select(Key).ToArray();
            if (expectedSounds.Length == 0 || expectedSounds.Distinct().Count() != expectedSounds.Length ||
                expectedSounds.Any(sound => records.GetEffective(sound).Signature != "SOUN"))
                throw new InvalidDataException("Stopped idle sound expectations lack unique winning source identities.");
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
            var quests = new FalloutQuestState(records); quests.Restore(saved.Quests!);
            if (stage <= quests.Stage(quest)) throw new InvalidDataException("Disposable stage fixture must be later than its genuine checkpoint.");
            // Explicit diagnostic inputs. EnterStage executes no source results;
            // neither the prior campaign nor its completed speech is claimed.
            quests.EnterStage(quest, stage);
            var questReceipt = JsonSerializer.Serialize(quests.Capture());
            var globals = FalloutGlobalState.Read(records); globals.Restore(saved.Globals!);
            var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records), FalloutCalendar.Read(Path.Combine(game, "FalloutNV.exe")));
            clock.Restore(saved.GameTime!);
            using var world = new FalloutReferenceWorld(records);
            world.RestoreEncounterZones(saved.EncounterZones); world.Restore(saved.References);
            world.RestoreActorOverrides(saved.ActorOverrides); world.RestoreFactionRelations(saved.FactionRelations);
            var state = world.Get(caller);
            if (state.Injury?.Dead == true || state.Engagement is not null || state.PackageBindingFailure is not null)
                throw new InvalidDataException("Disposable fixture may not replace an existing physical or failed-package continuation.");
            world.SetEnabled(caller, true); world.AdvanceEnableChanges(0, new(1, 1), _ => false);
            state.TalkedToPlayer = true;
            var cell = FalloutCellSceneReader.Read(records, world.Placement(caller).Cell); world.LoadCell(cell);
            var placed = cell.References.Single(value => value.FormKey == caller);
            if (!world.IsEnabled(caller) || records.GetEffective(placed.Base).Signature != "NPC_")
                throw new InvalidDataException("Stopped idle fixture has no enabled original NPC.");
            var scriptBefore = ScriptReceipt(state);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            var sourceIdle = FalloutActorIdleSource.Resolve(records, records.GetEffective(idle));
            if (sourceIdle.Objects.Count != 0 || !content.TryRead(sourceIdle.AnimationPath, null, out var idleBytes, out _))
                throw new InvalidDataException("Stopped idle fixture lacks its unchanged source KF without animated objects.");
            var idleFile = FalloutNifFile.Read(idleBytes);
            var timing = FalloutIdleAnimationData.Read(records.GetEffective(idle));
            if (timing.LoopMinimum != byte.MaxValue || timing.LoopMaximum != byte.MaxValue)
                throw new InvalidDataException("Selected source idle is not the actual endless-repeat fixture.");
            var effects = 0;
            Transform3D Placement(FalloutReferenceWorld owner, FalloutPlacedReference reference)
            {
                var location = owner.Placement(reference.FormKey);
                return new(GamebryoCoordinate.ConvertReferenceEuler(new(location.RotationRadians[0], location.RotationRadians[1], location.RotationRadians[2]), reference.Scale),
                    GamebryoCoordinate.ConvertVector(new(location.Position[0], location.Position[1], location.Position[2])) * units);
            }
            RuntimeNativeNpc Assemble(FalloutReferenceWorld owner)
            {
                var templates = owner.InitializeActorTemplates(caller, saved.Vitals?.Level ?? 1, globals);
                var actor = RuntimeNativeNpc.Create(records, content, placed, units, (_, _, _, _) => new StandardMaterial3D(),
                    owner.EquippedArmor(caller, saved.Vitals?.Level ?? 1, globals), templates, owner.ActorAppearanceOverride(caller));
                try
                {
                    actor.Transform = Placement(owner, placed);
                    actor.ExecutePackageEvent = (_, _) => { effects++; throw new InvalidDataException("Stopped fixture executed a package result."); };
                    actor.BeginPackageDialogue = (_, _) => { effects++; throw new InvalidDataException("Stopped fixture began package speech."); };
                    actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath, owner, owner.Get(caller), records, content, 1, 1);
                    actor.ConfigureAi(records, quests, cell, reference => Placement(owner, reference), clock: clock, globals: globals, world: owner);
                    fixture.AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false); actor.Combat!.SetPhysicsProcess(false);
                    if (actor.CurrentPackage != expectedPackage || actor.AiError is null)
                        throw new InvalidDataException("Actual source conditions did not select the expected failed package.");
                    return actor;
                }
                catch { actor.Free(); throw; }
            }
            var finiteWaitProven = false;
            var manualFixtureWrites = 0;
            var nativeManualWait = new RuntimeManualSaveRequests();
            var nativeManualSession = Guid.NewGuid();
            async Task SettleSounds(FalloutReferenceInstance owner)
            {
                var observeWait = !finiteWaitProven && ReferenceEquals(owner, state) && !owner.AnimationSoundEvents.CanCapture;
                if (observeWait) nativeManualWait.Request(nativeManualSession, content.SaveCompatibilityId, Engine.GetProcessFrames());
                var timer = Stopwatch.StartNew();
                while (!owner.AnimationSoundEvents.CanCapture && timer.Elapsed.TotalSeconds < 10)
                {
                    if (owner.AnimationSoundEvents.Events.Any(entry => entry.End is FalloutAnimationSoundEnd.Cancelled or FalloutAnimationSoundEnd.Faulted)) break;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (observeWait && !finiteWaitProven && world.PendingProcedureFiniteVoiceWait() is { Count: > 0 } live)
                    {
                        var phase = Engine.GetProcessFrames();
                        if (phase <= nativeManualWait.Receipt!.RequestedPhase) continue;
                        if (state.PackageBindingFailureCaptureReady || live.Any(voice => voice.NativeOwner == 0 ||
                            !expectedSounds.Contains(voice.Sound))) throw new InvalidDataException("Pending native wait lost its actual source owner.");
                        nativeManualWait.Drain(nativeManualSession, content.SaveCompatibilityId, phase,
                            () => new(RuntimeManualSaveAdmissionKind.FiniteSourceAudio, Voices: live),
                            _ => throw new InvalidDataException("Active finite source voice reached the save writer."));
                        finiteWaitProven = nativeManualWait.Pending && nativeManualWait.Receipt!.DeferredVoices is { Count: > 0 };
                        if (!finiteWaitProven || manualFixtureWrites != 0) throw new InvalidDataException("Native finite voice was dropped, failed or falsely saved.");
                    }
                }
                if (!owner.AnimationSoundEvents.CanCapture) throw new InvalidDataException("Source idle sound did not actually finish; silence is insufficient.");
                if (observeWait && !finiteWaitProven) nativeManualWait.Cancel("Finite voice finished before the component sampled a live pending phase.");
                if (observeWait && finiteWaitProven && nativeManualWait.Pending)
                {
                    if (!state.PackageBindingFailureCaptureReady || world.PendingProcedureCaptureCount != 0)
                        throw new InvalidDataException("Finished voice left an independent capture refusal.");
                    var committed = nativeManualWait.Drain(nativeManualSession, content.SaveCompatibilityId, Engine.GetProcessFrames(),
                        () => new(RuntimeManualSaveAdmissionKind.Ready), id =>
                        {
                            manualFixtureWrites++;
                            var output = Path.Combine(manualFixtureDirectory, id.ToString("N") + ".json");
                            File.WriteAllText(output, JsonSerializer.Serialize(world.Capture()));
                            return new(id.ToString("N"), output, "disposable-reference-world-only", null, null, null, DateTime.UtcNow);
                        });
                    if (!committed || manualFixtureWrites != 1 || nativeManualWait.Pending ||
                        nativeManualWait.Receipt!.AwaitedVoices is not { Count: > 0 })
                        throw new InvalidDataException("Actual Finished did not permit exactly one later complete reference capture.");
                    nativeManualWait.Drain(nativeManualSession, content.SaveCompatibilityId, Engine.GetProcessFrames() + 1,
                        () => throw new InvalidDataException("Completed native save request replayed admission."),
                        _ => throw new InvalidDataException("Completed native save request replayed writer."));
                }
            }
            var warm = Assemble(world);
            using var warmLifetime = new PackageFixtureLifetime(warm);
            var camera = new Camera3D { Current = true, Position = warm.Position + Vector3.Up }; fixture.AddChild(camera);
            warm.BeginResponseAnimation(records, idle);
            var activeRefused = false;
            try { state.Capture(); } catch (NotSupportedException) { activeRefused = true; }
            if (!activeRefused || world.PendingProcedureFiniteVoiceWait() is not null)
                throw new InvalidDataException("Active response pose entered a complete stopped checkpoint or finite wait.");
            warm.EndResponseAnimation(); // Explicit ended-response marker; no voice/event is fabricated.
            for (var frame = 0; frame < 1600; frame++)
            {
                warm._Process(.125); await SettleSounds(state);
                if (warm.AnimationError is not null) throw new InvalidDataException("Source idle publication failed: " + warm.AnimationError);
                var phase = state.Capture().PackageBindingFailure?.IndependentIdle?.Animation.Clock;
                if (phase?.CompletedRepeats >= 2 && expectedSounds.All(sound => state.AnimationSoundEvents.Events.Any(entry =>
                    entry.Sound == sound && entry.End == FalloutAnimationSoundEnd.NativeFinished))) break;
            }
            if (!finiteWaitProven || manualFixtureWrites != 1)
                throw new InvalidDataException("Actual source idle did not prove a live finite wait followed by one complete reference capture.");
            var before = world.Capture(); var selected = before.Single(value => value.Reference == caller);
            var failure = selected.PackageBindingFailure ?? throw new InvalidDataException("Stopped package has no native capture owner.");
            var overlay = failure.IndependentIdle ?? throw new InvalidDataException("Stopped source idle has no independent clock.");
            if (overlay.Owner != "dialogue-response" || overlay.Animation.Idle != idle || overlay.Animation.Clock.CompletedRepeats < 2 ||
                overlay.Animation.Clock.SelectedAdditionalLoops != byte.MaxValue || overlay.Animation.Clock.Complete ||
                JsonSerializer.SerializeToElement(warm.AnimationState).GetProperty("responseIdleActive").GetBoolean() ||
                expectedSounds.Any(sound => !selected.AnimationSoundEvents!.Events.Any(entry => entry.Sound == sound &&
                    entry.End == FalloutAnimationSoundEnd.NativeFinished && entry.PartialLanes.Count > 0)))
                throw new InvalidDataException("Stopped idle fixture lost its real repeat or finished partial sound history.");
            using var cold = new FalloutReferenceWorld(records);
            cold.RestoreEncounterZones(saved.EncounterZones); cold.Restore(Copy(before));
            cold.RestoreActorOverrides(saved.ActorOverrides); cold.RestoreFactionRelations(saved.FactionRelations); cold.LoadCell(cell);
            var resumed = Assemble(cold);
            using var resumedLifetime = new PackageFixtureLifetime(resumed);
            RequireSame(warm, resumed, world, cold, caller, "cold assembly");
            var repeats = overlay.Animation.Clock.CompletedRepeats;
            var suffixKeys = 0;
            for (var frame = 0; frame < 160; frame++)
            {
                warm._Process(.125); resumed._Process(.125);
                await SettleSounds(state); await SettleSounds(cold.Get(caller));
                RequireSame(warm, resumed, world, cold, caller, "source suffix");
                var keys = JsonSerializer.SerializeToElement(warm.AnimationState).GetProperty("textKeyCrossings").GetRawText();
                if (keys != JsonSerializer.SerializeToElement(resumed.AnimationState).GetProperty("textKeyCrossings").GetRawText())
                    throw new InvalidDataException("Cold source idle repeated or lost an authored suffix key.");
                suffixKeys += JsonSerializer.SerializeToElement(warm.AnimationState).GetProperty("textKeyCrossings").GetArrayLength();
                if (state.Capture().PackageBindingFailure!.IndependentIdle!.Animation.Clock.CompletedRepeats > repeats) break;
            }
            var advanced = world.Capture(); var advancedActor = advanced.Single(value => value.Reference == caller);
            if (suffixKeys == 0 || advancedActor.PackageBindingFailure!.IndependentIdle!.Animation.Clock.CompletedRepeats <= repeats ||
                ScriptReceipt(state) != scriptBefore || JsonSerializer.Serialize(quests.Capture()) != questReceipt || effects != 0)
                throw new InvalidDataException("Cold suffix did not cross a source repeat or replayed source prefix effects.");
            fixture.RemoveChild(warm); world.UnloadCell(cell.Cell.FormKey);
            if (state.CanCapturePackageBindingFailure is not null || state.CapturePackageBindingFailure is not null || world.IsResident(caller) ||
                JsonSerializer.Serialize(world.Capture()) != JsonSerializer.Serialize(advanced))
                throw new InvalidDataException("Child-first retirement changed its exact idle, random, source fault or finished sounds.");
            using var retired = new FalloutReferenceWorld(records);
            retired.RestoreEncounterZones(saved.EncounterZones); retired.Restore(Copy(advanced));
            retired.RestoreActorOverrides(saved.ActorOverrides); retired.RestoreFactionRelations(saved.FactionRelations); retired.LoadCell(cell);
            var rebound = Assemble(retired);
            using var reboundLifetime = new PackageFixtureLifetime(rebound);
            RequireSame(resumed, rebound, cold, retired, caller, "retired cold assembly");
            if (rebound.FindChildren("*", "", true, false).Any(node => node is AudioStreamPlayer or AudioStreamPlayer3D) || effects != 0 ||
                ScriptReceipt(retired.Get(caller)) != scriptBefore)
                throw new InvalidDataException("Retired cold binding replayed voice, result, script prefix or finished sound.");
            foreach (var invalid in new[]
            {
                overlay with { Owner = "dialogue-active" },
                overlay with { Animation = overlay.Animation with { IdleSha256 = new string('0', 64) } },
                overlay with { Animation = overlay.Animation with { Clock = overlay.Animation.Clock with { Complete = true } } },
            })
            {
                using var rejected = new FalloutReferenceWorld(records);
                var refused = false;
                try
                {
                    rejected.Restore(before.Select(value => value.Reference == caller ? value with
                    { PackageBindingFailure = failure with { IndependentIdle = invalid } } : value).ToArray());
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException) { refused = true; }
                if (!refused || rejected.InstanceCount != 0) throw new InvalidDataException("Invalid independent idle was not refused atomically.");
            }
            GD.Print("OPENNV_OWNED_STOPPED_IDLE_PASS " + JsonSerializer.Serialize(new
            {
                runtimeBuild = typeof(RuntimeNativeNpc).Assembly.ManifestModule.ModuleVersionId,
                reference = caller.ToString(),
                package = expectedPackage.ToString(),
                idle = idle.ToString(),
                actorSha256 = Hash(records.GetEffective(caller)),
                baseSha256 = Hash(records.GetEffective(placed.Base)),
                packageSha256 = Hash(records.GetEffective(expectedPackage)),
                idleSha256 = Hash(records.GetEffective(idle)),
                kfSha256 = idleFile.Sha256,
                checkpointSha256 = Convert.ToHexString(SHA256.HashData(original)),
                manualFiniteWait = nativeManualWait.Receipt,
                manualFixtureWrites,
                quest = quest.ToString(),
                fixtureStage = stage,
                failure = advancedActor.PackageBindingFailure,
                soundReceipts = advancedActor.AnimationSoundEvents,
                suffixKeys,
                allBoneCount = resumed.Skeleton.Node.GetBoneCount(),
                exactCold = true,
                childFirstRetirement = true,
                noReplay = true,
                activeResponseRefused = activeRefused,
                boundary = "explicit-disposable-stage-enabled-talked-to-player-and-ended-response-marker;ordinary-campaign-voice-audio-output-and-parity-unverified"
            }));
        }
        finally
        {
            fixture.Free();
            Directory.Delete(manualFixtureDirectory, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!original.AsSpan().SequenceEqual(File.ReadAllBytes(checkpoint))) throw new InvalidDataException("Stopped idle fixture changed its genuine checkpoint input.");
        }
        static IReadOnlyList<FalloutReferenceSnapshot> Copy(IReadOnlyList<FalloutReferenceSnapshot> value) =>
            JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(value))!;
        static string ScriptReceipt(FalloutReferenceInstance state) => JsonSerializer.Serialize(new { state.ScriptError, state.Variables });
        static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
        static void RequireSame(RuntimeNativeNpc warm, RuntimeNativeNpc cold, FalloutReferenceWorld a, FalloutReferenceWorld b,
            FalloutFormKey actor, string phase)
        {
            if (warm.Transform != cold.Transform || warm.AiError != cold.AiError || warm.CurrentPackage != cold.CurrentPackage ||
                JsonSerializer.Serialize(a.Capture()) != JsonSerializer.Serialize(b.Capture()) ||
                warm.Skeleton.Node.GetBoneCount() != cold.Skeleton.Node.GetBoneCount())
                throw new InvalidDataException("Stopped source idle changed clock, random, history, fault or pose during " + phase + ".");
            for (var index = 0; index < warm.Skeleton.Node.GetBoneCount(); index++)
                if (warm.Skeleton.Node.GetBoneName(index) != cold.Skeleton.Node.GetBoneName(index) ||
                    warm.Skeleton.Node.GetBonePose(index) != cold.Skeleton.Node.GetBonePose(index))
                    throw new InvalidDataException("Stopped source idle changed actual bone " + index + " during " + phase + ".");
        }
    }
}
