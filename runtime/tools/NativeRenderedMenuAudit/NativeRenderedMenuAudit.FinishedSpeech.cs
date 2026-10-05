using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    // Disposable native component fixture. The selected original program and
    // voice remain unchanged. Stage/local/enable initialization is explicit
    // fixture setup, never a campaign checkpoint or an ordinary traversal.
    private async Task FinishedSpeechCold(string game, string mod, string modRoot, string actorId,
        string topicId, string questId, short stage, string initialLocal, double initialValue,
        string completedLocal, double completedValue, string[] dependencies)
    {
        RuntimeNativeNpc? body = null;
        RuntimeNativeSpeech? speech = null, restoredSpeech = null, settledSpeech = null;
        FalloutPluginStack? recordOwner = null;
        var worldOwners = new List<FalloutReferenceWorld>();
        try
        {
            var setup = new FalloutModStackSelection([new(mod, modRoot, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
            var content = RuntimeLiveContentSource.Current!;
            var records = recordOwner = FalloutPluginStack.Load(content.PluginSources);
            var world = new FalloutReferenceWorld(records); worldOwners.Add(world);
            var actor = FalloutDialogueTopic.Find(records, "ACHR", actorId);
            var topic = FalloutDialogueTopic.Read(records, topicId);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId).FormKey;
            var quests = new FalloutQuestState(records);
            quests.SetRunning(quest, true); quests.EnterStage(quest, stage);
            var cell = FalloutCellSceneReader.Read(records, world.Get(actor.FormKey).Cell);
            world.LoadCell(cell);
            FinishedSpeechEnableActor(records, world, actor.FormKey);
            var configuration = RuntimeConfiguration.Load();
            var units = configuration.World.GameUnitsToMeters;
            body = FinishedSpeechBody(records, content, world, cell, actor.FormKey, units);
            AddChild(body); body.SetProcess(false); body.SetPhysicsProcess(false);
            ConfigureFinishedSpeechLook(body, records, content, world);
            var instance = world.Get(actor.FormKey);
            var definition = instance.Script ?? throw new InvalidDataException("Owned native completion caller has no source script.");
            var initialIndex = definition.Locals.TryGetValue(initialLocal, out var first) ? first :
                throw new InvalidDataException("Native speech fixture initial local is not source-declared.");
            var completedIndex = definition.Locals.TryGetValue(completedLocal, out var last) ? last :
                throw new InvalidDataException("Native speech fixture completed local is not source-declared.");
            RequireFinished(initialIndex != completedIndex && double.IsFinite(initialValue) && double.IsFinite(completedValue),
                "Native speech fixture local initialization is invalid.");
            instance.Write(initialIndex, initialValue);
            var bodyAtRequest = body;
            var originalPlacement = world.Placement(actor.FormKey);
            instance.QuerySpatialPlacement = () =>
            {
                var point = bodyAtRequest.GlobalPosition / units;
                var angle = GamebryoCoordinate.ReferenceEuler(bodyAtRequest.GlobalBasis);
                return new(originalPlacement.Cell, [point.X, -point.Z, point.Y], [angle.X, angle.Y, angle.Z]);
            };
            // Retain an actual prepared request before the unbound process tier.
            // Presence of this NPC body is deliberately not treated as High.
            world.AdvanceDetection(7.25f);
            world.Detection.Bind(reference => world.DetectionPlacement(reference, originalPlacement, units), null);
            var effects = new List<FalloutReferenceScriptEffect>();
            var lookEffects = new List<FalloutReferenceScriptEffect>();
            var results = new List<(FalloutFormKey Info, FalloutFormKey Speaker, bool Begin)>();
            var completedInfos = new List<FalloutFormKey>();
            var receipts = new List<FalloutSpeechCompletionReceipt>();
            var said = new HashSet<FalloutFormKey>();
            speech = new RuntimeNativeSpeech();
            var startedSpeech = speech;
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                if (effect.Kind == FalloutReferenceEffectKind.HeadTracking)
                {
                    ApplyFinishedSpeechLook(bodyAtRequest, records, effect);
                    lookEffects.Add(effect);
                    return;
                }
                RequireFinished(effect.Kind == FalloutReferenceEffectKind.SayTo && effect.Source == actor.FormKey &&
                    effect.Target == actor.FormKey && effect.Argument is not null && effect.Topic == topic.Topic.FormKey,
                    "Native completion fixture reached an unrelated source effect.");
                effects.Add(effect);
                startedSpeech.SayTo(effect.Target!.Value, effect.Argument!.Value, effect.Topic!.Value, effect.ForceSubtitles);
            }, IsTalking: startedSpeech.IsTalking));
            ConfigureFinishedSpeech(speech, records, configuration, world, quests, said, scripts, results, receipts, completedInfos);
            AddChild(speech);
            var fault = scripts.Dispatch(actor.FormKey, "GameMode", elapsedSeconds: .25);
            var frame = instance.ScriptStoppedFrame;
            RequireFinished(fault.Error is not null && frame is { PreparedDetection: not null, Event: "gamemode" or "GameMode" } &&
                frame.ElapsedSeconds == .25 && effects.Count == 1 && speech.IsTalking(actor.FormKey) && speech.Error is null,
                fault.Error ?? "Original source did not start its voice before retaining the detection fault.");
            var retainedFrame = frame ?? throw new InvalidDataException("Original stopped invocation is absent.");
            var active = JsonSerializer.SerializeToElement(speech.State);
            var selected = topic.Infos.Single(info => info.Record.FormKey.ToString() == active.GetProperty("info").GetString());
            var channel = FinishedSpeechVoice(speech, actor.FormKey);
            var player = FinishedSpeechField<AudioStreamPlayer>(channel, "Player");
            var audioEnds = 0;
            player.Finished += () => ++audioEnds;
            RequireFinished(player.Playing && active.GetProperty("audioSha256").GetString() is { Length: 64 } &&
                FinishedSpeechField<long>(channel, "Generation") == 1 && instance.Read(completedIndex) != completedValue,
                "Original source selected no actual native audio/generation or failed to commit its source prefix.");
            RejectFinished(() => speech.CaptureFinishedState(), "Active voice was admitted as finished history.");
            var inputs = new[] { actor, records.GetEffective(instance.Base), definition.Record, topic.Topic, selected.Record,
                records.GetEffective(cell.Cell.FormKey) }.ToList();
            const string lifetimeName = "fDetectionEventExpireTime";
            var lifetime = records.NumericSettings.Float(lifetimeName);
            var lifetimeRecords = records.EffectiveRecords("GMST").Where(record => record.ReadSubrecords().Any(field =>
                field.Signature == "EDID" && FalloutDialogueTopic.Text(field.Data.Span).Equals(lifetimeName, StringComparison.OrdinalIgnoreCase))).ToArray();
            RequireFinished(lifetimeRecords.Length <= 1, "Detection lifetime has ambiguous winning GMST owners.");
            inputs.AddRange(lifetimeRecords);
            var numericState = JsonSerializer.SerializeToElement(records.NumericSettings.State);
            RequireFinished(numericState.GetProperty("defaultsSource").GetString() == content.StackId,
                "Native speech fixture lost its original owned numeric-default source graph.");
            var defaultExecutable = lifetimeRecords.Length == 0
                ? Path.Combine(Path.GetDirectoryName(content.ContentRoot)!, "FalloutNV.exe") : null;
            string? ExecutableHash()
            {
                if (defaultExecutable is null) return null;
                using var stream = File.OpenRead(defaultExecutable);
                return Convert.ToHexString(SHA256.HashData(stream));
            }
            var executableHash = ExecutableHash();
            string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
            var hashes = inputs.Select(Hash).ToArray();
            var randomAfterPrefix = JsonSerializer.Serialize(world.ScriptValues.Capture());
            var localsAfterPrefix = new Dictionary<uint, double>(instance.Variables);
            await WaitFinishedSpeech(speech, allowFailure: true);
            RequireFinished(lookEffects.Count == 1 && lookEffects[0].Source == actor.FormKey &&
                lookEffects[0].Argument == records.RuntimeFormKey(0x14),
                "Original INFO Look result did not reach its real native actor owner exactly once.");
            RequireFinished(audioEnds == selected.Responses.Count && !player.Playing && !speech.IsTalking(actor.FormKey) &&
                speech.Error == fault.Error && speech.CanCaptureFinishedFailure && completedInfos.Count == 0 && receipts.Count == 1 &&
                randomAfterPrefix == JsonSerializer.Serialize(world.ScriptValues.Capture()),
                speech.Error ?? "Native ended voice lost its committed audio or retained source failure.");
            speech.SetProcess(false);
            var finished = FinishedSpeechCopy(speech.CaptureFinishedState());
            RequireFinished(finished.Failure is { } failure && failure.Receipt.Info == selected.Record.FormKey &&
                failure.Receipt.Speaker == actor.FormKey && failure.Receipt.Generation == 1 &&
                failure.Receipt.Topics.SequenceEqual([topic.Topic.FormKey]) && failure.Error == fault.Error &&
                finished.Voices.Single().CompletedCommands == 0 && finished.CompletedCommands == 0,
                "Native finished snapshot differs from its original INFO/DIAL/generation/source fault.");
            var actualResultCount = results.Count;
            FinishedSpeechOpaqueNegative(speech, actor.FormKey, finished);
            var references = FinishedSpeechCopy(world.Capture().ToArray());
            var originalHead = references.Single(value => value.Reference == actor.FormKey).HeadTracking ??
                throw new InvalidDataException("Original INFO Look was consumed without a head receipt.");
            RequireFinished(originalHead.Targets.Slots[2] is { Enabled: true } lookSlot && lookSlot.Target == records.RuntimeFormKey(0x14) &&
                instance.HeadTrackingRequired && instance.CaptureHeadTracking is not null,
                "Original native Look did not mark its mandatory shared reference capture owner.");
            var headBodySource = records.GetEffective(originalHead.Binding.BodyPartSource);
            var headBodyHash = Hash(headBodySource);
            var headRigPath = originalHead.Binding.SkeletonResource;
            RequireFinished(content.TryRead(headRigPath, null, out var headRigBytes, out _), "Owned head rig is missing.");
            var headRigHash = Convert.ToHexString(SHA256.HashData(headRigBytes));
            var scriptValues = FinishedSpeechCopy(world.ScriptValues.Capture());
            var detection = FinishedSpeechCopy(world.CaptureDetection()!);
            var questState = FinishedSpeechCopy(quests.Capture().ToArray());
            var saidState = said.ToArray();
            world.UnloadCell(cell.Cell.FormKey); scripts.UnloadCell(cell.Cell.FormKey);
            RequireFinished(!world.IsResident(actor.FormKey) &&
                JsonSerializer.Serialize(references) == JsonSerializer.Serialize(world.Capture()),
                "Actual unload changed a retained source invocation.");
            speech.Free(); speech = null; body.Free(); body = null;
            RequireFinished(instance.CaptureHeadTracking is null && instance.HeadTrackingRequired &&
                JsonSerializer.Serialize(originalHead) == JsonSerializer.Serialize(instance.HeadTracking),
                "Child-first native body retirement lost its exact Look targets or raw head pose.");
            var retainedHead = instance.HeadTracking; instance.HeadTracking = null;
            try { RejectFinished(() => instance.Capture(), "Consumed native Look lost its mandatory receipt without refusing capture."); }
            finally { instance.HeadTracking = retainedHead; }

            var cold = new FalloutReferenceWorld(records); worldOwners.Add(cold);
            cold.ScriptValues.Restore(scriptValues); cold.Restore(references); cold.RestoreDetection(detection);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(questState);
            var coldScripts = new FalloutReferenceScripts(records, cold, coldQuests, new((_, _) => false,
                _ => throw new InvalidDataException("Native cold source suffix replayed a previous effect.")));
            var playerKey = records.RuntimeFormKey(0x14);
            cold.Detection.Bind(_ => throw new InvalidDataException("Cold source suffix re-observed its original native position."),
                owner => owner == playerKey ? FalloutDetectionProcessLevel.High : null);
            restoredSpeech = new RuntimeNativeSpeech();
            var recovering = restoredSpeech;
            ConfigureFinishedSpeech(recovering, records, configuration, cold, coldQuests, saidState.ToHashSet(),
                coldScripts, results, receipts, completedInfos);
            var allowResume = false;
            recovering.CanResumeSourceCompletion = receipt => allowResume && coldScripts.CanResumeSpeechCompletion(receipt);
            recovering.ResumeSourceCompletion = receipt =>
            {
                var result = coldScripts.ResumeSpeechCompletion(receipt);
                if (result.Error is not null) throw new NotSupportedException(result.Error);
            };
            AddChild(recovering); recovering.SetProcess(false);
            recovering.RestoreFinishedState(finished);
            RequireFinished(recovering.CanCaptureFinishedFailure && recovering.Error == fault.Error &&
                !cold.IsResident(actor.FormKey) && recovering.GetChildren().OfType<AudioStreamPlayer>().Count() == 0 &&
                JsonSerializer.Serialize(finished) == JsonSerializer.Serialize(recovering.CaptureFinishedState()),
                "Cold ended failure restarted audio, required the unloaded body, or changed its exact history.");
            FinishedSpeechRestoreNegatives(records, configuration, cold, coldQuests, finished);
            FinishedSpeechUnretainedNegative(records, configuration, questState, references, scriptValues, detection, finished);
            recovering._Process(0);
            allowResume = true;
            var previousPause = GetTree().Paused;
            GetTree().Paused = true;
            try
            {
                recovering._Process(.25); RequireFinished(recovering.Error == fault.Error && completedInfos.Count == 0,
                "Paused native restoration resumed the source failure.");
            }
            finally { GetTree().Paused = previousPause; }
            recovering._Process(0);
            var resumed = cold.Retained(actor.FormKey);
            var eventState = cold.CaptureDetection()!.Events.Single();
            var settled = FinishedSpeechCopy(recovering.CaptureFinishedState());
            RequireFinished(!recovering.Active && recovering.Error is null && resumed.ScriptError is null &&
                resumed.ScriptStoppedFrame is null && resumed.CompletedScriptContinuation?.Error == retainedFrame.Error &&
                resumed.Read(completedIndex) == completedValue && localsAfterPrefix.Where(item => item.Key != completedIndex)
                    .All(item => resumed.Read(item.Key) == item.Value) && effects.Count == 1 && audioEnds == selected.Responses.Count &&
                results.Count == actualResultCount && completedInfos.SequenceEqual([selected.Record.FormKey]) && receipts.Count == 1 &&
                randomAfterPrefix == JsonSerializer.Serialize(cold.ScriptValues.Capture()) &&
                eventState.CreatedSeconds == retainedFrame.PreparedDetection!.ObservedSeconds &&
                JsonSerializer.Serialize(eventState.Request) == JsonSerializer.Serialize(retainedFrame.PreparedDetection) &&
                settled.CompletedCommands == 1 && settled.Voices.Single().Generation == 1 &&
                settled.Voices.Single().CompletedCommands == 1 && settled.LastCompletedFailure?.Error == fault.Error,
                "Native cold completion replayed audio/results/RNG, lost prefix locals or changed original position/clock/generation.");
            RequireFinished(recovering.GetChildren().OfType<AudioStreamPlayer>().All(audio => !audio.Playing && audio.Stream is null),
                "Cold source resumption bound or played previously committed audio.");
            RejectFinished(() => cold.Detection.RequireConsumer(playerKey), "Creation silently claimed receiver/hearing ownership.");
            recovering._Process(1);
            RequireFinished(JsonSerializer.Serialize(settled) == JsonSerializer.Serialize(recovering.CaptureFinishedState()) &&
                completedInfos.Count == 1 && results.Count == actualResultCount,
                "Native repeated process replayed an ended receipt.");
            RejectFinished(() => recovering.RestoreFinishedState(finished), "Native live owner restored a duplicate failure.");
            var ledger = typeof(RuntimeNativeSpeech).GetField("_emptyCompletions", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(recovering) as FalloutSpeechCompletionEvents ?? throw new InvalidDataException("Native completion owner changed.");
            RejectFinished(() => ledger.Complete(finished.Failure!.Receipt.Completion(), _ =>
                throw new InvalidDataException("Duplicate completion invoked source again.")),
                "Native committed generation accepted the original receipt again.");
            RequireFinished(JsonSerializer.Serialize(settled) == JsonSerializer.Serialize(recovering.CaptureFinishedState()),
                "Duplicate completion refusal modified native settled state.");

            var settledWorld = new FalloutReferenceWorld(records); worldOwners.Add(settledWorld);
            settledWorld.ScriptValues.Restore(FinishedSpeechCopy(cold.ScriptValues.Capture()));
            settledWorld.Restore(FinishedSpeechCopy(cold.Capture().ToArray()));
            settledWorld.RestoreDetection(FinishedSpeechCopy(cold.CaptureDetection()!));
            var settledQuests = new FalloutQuestState(records); settledQuests.Restore(FinishedSpeechCopy(coldQuests.Capture().ToArray()));
            var settledScripts = new FalloutReferenceScripts(records, settledWorld, settledQuests,
                new((_, _) => false, _ => throw new InvalidDataException("Settled native cold history invented source effects.")));
            settledSpeech = new RuntimeNativeSpeech();
            ConfigureFinishedSpeech(settledSpeech, records, configuration, settledWorld, settledQuests, saidState.ToHashSet(),
                settledScripts, results, receipts, completedInfos);
            AddChild(settledSpeech); settledSpeech.SetProcess(false); settledSpeech.RestoreFinishedState(settled);
            settledSpeech._Process(0);
            RequireFinished(JsonSerializer.Serialize(settled) == JsonSerializer.Serialize(settledSpeech.CaptureFinishedState()) &&
                completedInfos.Count == 1 && results.Count == actualResultCount && !settledSpeech.Active,
                "Second cold restoration lost the retired failure or redelivered its source notification.");

            // A genuinely new native command keeps the speaker's restored high
            // water mark; it is not resumption of the old GameMode prefix.
            settledWorld.LoadCell(cell);
            body = FinishedSpeechBody(records, content, settledWorld, cell, actor.FormKey, units);
            AddChild(body); body.SetProcess(false); body.SetPhysicsProcess(false);
            ConfigureFinishedSpeechLook(body, records, content, settledWorld);
            RequireFinished(JsonSerializer.Serialize(originalHead) == JsonSerializer.Serialize(body.CaptureHeadTracking()),
                "Cold source actor did not restore its original INFO Look cache/timer/controller or raw native pose.");
            foreach (var invalidHead in new[]
            {
                originalHead with { Binding = originalHead.Binding with { SkeletonSha256 = new('0', 64) } },
                originalHead with { Binding = originalHead.Binding with { BodyPartSha256 = new('0', 64) } },
                originalHead with { Binding = originalHead.Binding with { Bone = originalHead.Binding.Bone + 1 } },
                originalHead with { Targets = originalHead.Targets with { SourceHoldSeconds = originalHead.Targets.SourceHoldSeconds + 1 } },
            })
                RejectFinished(() => FalloutActorHeadTrackingSource.Validate(records, actor.FormKey, invalidHead),
                    "Saved head source drift was admitted.");
            RequireFinished(JsonSerializer.Serialize(originalHead) == JsonSerializer.Serialize(body.CaptureHeadTracking()) &&
                Hash(headBodySource) == headBodyHash && content.TryRead(headRigPath, null, out var unchangedRig, out _) &&
                Convert.ToHexString(SHA256.HashData(unchangedRig)) == headRigHash,
                "Rejected head source drift mutated its actual cold owner or original source bytes.");
            var nextBody = body;
            var nextLookEffects = new List<FalloutReferenceScriptEffect>();
            var nextResults = new FalloutReferenceScripts(records, settledWorld, settledQuests,
                new((_, _) => false, effect =>
                {
                    RequireFinished(effect.Kind == FalloutReferenceEffectKind.HeadTracking,
                        "New generation reached an unrelated original INFO result effect.");
                    ApplyFinishedSpeechLook(nextBody, records, effect);
                    nextLookEffects.Add(effect);
                }));
            settledSpeech.ExecuteResults = (info, speaker, begin) =>
            {
                nextResults.ExecuteResult(info, speaker, begin);
                results.Add((info.Record.FormKey, speaker, begin));
            };
            settledSpeech.SayTo(actor.FormKey, playerKey, topic.Topic.FormKey, true);
            var nextVoice = FinishedSpeechVoice(settledSpeech, actor.FormKey);
            RequireFinished(settledSpeech.IsTalking(actor.FormKey) &&
                FinishedSpeechField<long>(nextVoice, "Generation") == 2 && FinishedSpeechField<AudioStreamPlayer>(nextVoice, "Player").Playing,
                "A genuinely new native voice reset its restored per-speaker generation.");
            RejectFinished(() => settledSpeech.CaptureFinishedState(), "New generation's active audio was admitted as retired history.");
            RejectFinished(() => settledSpeech.RestoreFinishedState(settled), "Restoration replaced an active native voice.");
            settledSpeech.SetProcess(true);
            await WaitFinishedSpeech(settledSpeech, allowFailure: false);
            RequireFinished(lookEffects.Count == 1 && nextLookEffects.Count == 1 && effects.Count == 1 &&
                nextLookEffects[0].Argument == playerKey,
                "New native INFO result replayed an old command or lost its fresh Look owner.");
            RequireFinished(settledSpeech.CaptureFinishedState().Voices.Single().Generation == 2 &&
                settledSpeech.CaptureFinishedState().Voices.Single().CompletedCommands == 2 && completedInfos.Count == 2,
                "New native generation failed to commit independently of the previous receipt.");
            await FinishedSpeechResultNegative(records, configuration, settledWorld, settledQuests, actor.FormKey, topic.Topic.FormKey);
            RequireFinished(inputs.Where((record, index) => Hash(record) != hashes[index]).Any() == false &&
                executableHash == ExecutableHash(),
                "Owned native speech audit modified source bytes.");
            GD.Print("OPENNV_NATIVE_FINISHED_SPEECH_COLD_PASS " + JsonSerializer.Serialize(new
            {
                runtimeMvid = typeof(RuntimeNativeSpeech).Module.ModuleVersionId,
                content.SaveCompatibilityId,
                actor = actor.FormKey.ToString(),
                topic = topic.Topic.FormKey.ToString(),
                info = selected.Record.FormKey.ToString(),
                sourceRecords = inputs.Select((record, index) => new
                {
                    form = record.FormKey.ToString(),
                    record.Signature,
                    winner = record.Plugin.Name,
                    sha256 = hashes[index]
                }),
                detectionLifetime = new
                {
                    name = lifetimeName,
                    value = lifetime,
                    numericState,
                    source = lifetimeRecords.Length == 0 ? "owned executable numeric default" : "winning GMST",
                    ownedExecutableSha256 = executableHash
                },
                nativeVoice = new
                {
                    audioSha256 = active.GetProperty("audioSha256").GetString(),
                    lipSha256 = active.GetProperty("lipSha256").GetString(),
                    selected.Responses.Count,
                    audioEnds,
                    sourceBeginResultPresent = FalloutDialogueTopic.CodeLines(selected.BeginScript).Any(),
                    sourceEndResultPresent = FalloutDialogueTopic.CodeLines(selected.EndScript).Any(),
                    actualResultCalls = actualResultCount
                },
                fixture = new
                {
                    stage,
                    initialLocal,
                    initialValue,
                    completedLocal,
                    completedValue,
                    nativeSourcePlacement = true,
                    processOwner = "explicit proven reserved-player High class; all NPC process tiers remain unknown",
                    lookTargetPoint = "explicit disposable player-target component; original source Look applies through real native NPC",
                    originalLookCalls = lookEffects.Count,
                    nextGenerationLookCalls = nextLookEffects.Count,
                    headTargetColdPersistence = "original INFO Look exact targets/raw pose/controller, child-first retirement and cold source validation"
                },
                originalFrame = frame,
                nativeFailure = finished.Failure,
                settledGeneration = 1,
                nextNativeGeneration = 2,
                audioResultsPrefixRandomOnce = true,
                nativeUnloadCold = true,
                sourceSuffixOnce = true,
                duplicateActiveOpaqueAndDriftRefused = true,
                consumerOwned = false,
                sourceReadonly = true,
                recording = false,
                campaignCheckpoint = false,
                parity = "unverified"
            }));
        }
        finally
        {
            // Native body/head retirement reads the actual winning records.
            // It must precede disposal of both the shared worlds and their
            // source streams, including when a proof fails halfway through.
            try { settledSpeech?.Free(); restoredSpeech?.Free(); speech?.Free(); }
            finally
            {
                try { body?.Free(); }
                finally
                {
                    foreach (var owner in worldOwners.AsEnumerable().Reverse()) owner.Dispose();
                    recordOwner?.Dispose();
                    RuntimeLiveContentSource.Clear();
                }
            }
        }
    }

    private static void ConfigureFinishedSpeechLook(RuntimeNativeNpc body, FalloutPluginStack records,
        RuntimeLiveContentSource content, FalloutReferenceWorld world)
    {
        // An explicitly declared component point, as in the owned Look audit.
        // It is not an ordinary player's observed position or campaign state.
        var player = records.RuntimeFormKey(0x14);
        var target = Vector3.Zero;
        body.ConfigureHeadTracking(records, content, reference => reference == player ? target : null, world.Get(body.Appearance.Reference!.Value));
        target = (body.HeadTargetPoint ?? throw new InvalidDataException("Selected source body has no head target node.")) +
            new Vector3(2, 0, -2);
    }

    private static void ApplyFinishedSpeechLook(RuntimeNativeNpc body, FalloutPluginStack records,
        FalloutReferenceScriptEffect effect)
    {
        RequireFinished(effect.Target == body.Appearance.Reference && effect.Argument == records.RuntimeFormKey(0x14) &&
            effect.Source == body.Appearance.Reference,
            $"Native finished speech reached a different source head-tracking result: source={effect.Source} target={effect.Target} argument={effect.Argument} actor={body.Appearance.Reference}.");
        body.ApplyBoundHeadTrackingCommand(new(0, effect.Target!.Value, effect.Argument));
        body._Process(1.0 / 60);
        RequireFinished(body.AnimationError is null && JsonSerializer.SerializeToElement(body.HeadTrackingState)
            .GetProperty("selected").GetString() == effect.Argument!.Value.ToString(),
            body.AnimationError ?? "Source Look did not bind its original native target slot.");
    }

    private static RuntimeNativeNpc FinishedSpeechBody(FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutReferenceWorld world, FalloutCellScene cell, FalloutFormKey actor, float units)
    {
        var placed = cell.References.Single(reference => reference.FormKey == actor);
        var templates = world.InitializeActorTemplates(actor, 1);
        var body = RuntimeNativeNpc.Create(records, content, placed, units,
            (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, new Color(.4f, .4f, .4f)),
            world.EquippedArmor(actor, 1), templates);
        body.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(placed.RotationRadians[0], placed.RotationRadians[1],
            placed.RotationRadians[2]), placed.Scale), GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * units);
        if (body.AnimationError is not null || body.AppearanceError is not null)
        { var error = body.AnimationError ?? body.AppearanceError; body.Free(); throw new InvalidDataException(error); }
        return body;
    }

    private static void FinishedSpeechEnableActor(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor)
    {
        var root = actor; var enabled = true; var seen = new HashSet<FalloutFormKey>();
        while (world.Get(root).EnableParent is { } parent)
        {
            if (!seen.Add(root)) throw new InvalidDataException("Native speech fixture enable-parent cycle.");
            enabled ^= parent.Opposite; root = parent.Reference;
            if (root == records.RuntimeFormKey(0x14)) throw new NotSupportedException("Native speech fixture cannot enable the player.");
        }
        world.SetEnabled(root, enabled); world.AdvanceEnableChanges(0, new(1, 1), _ => false);
        RequireFinished(world.IsEnabled(actor), "Native speech fixture actor did not inherit its source enable state.");
    }

    private static void ConfigureFinishedSpeech(RuntimeNativeSpeech speech, FalloutPluginStack records,
        RuntimeConfiguration configuration, FalloutReferenceWorld world, FalloutQuestState quests, HashSet<FalloutFormKey> said,
        FalloutReferenceScripts scripts, List<(FalloutFormKey Info, FalloutFormKey Speaker, bool Begin)> results,
        List<FalloutSpeechCompletionReceipt> receipts, List<FalloutFormKey> completed)
    {
        speech.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip, quest => quests.Stage(quest),
            saidInfos: said, templates: reference => world.Get(reference).Templates, quests: quests, playerFemale: () => false,
            actorValue: (reference, value) => world.ActorValue(reference, FalloutActorValue.UserSlot(value)),
            dialogueRandom: world.ScriptValues.RandomBounded, references: world);
        speech.PrepareSubtitle = _ => { };
        // Intentional negative phases remain visible without generating Godot
        // ERROR output that would falsely look like a failing native fixture.
        speech.ReportDivergence = value => GD.Print("OPENNV_NATIVE_FINISHED_SPEECH_OBSERVED " + value);
        speech.ExecuteResults = (info, speaker, begin) =>
        { scripts.ExecuteResult(info, speaker, begin); results.Add((info.Record.FormKey, speaker, begin)); };
        speech.SayToCompleted += receipt =>
        {
            receipts.Add(receipt);
            var result = scripts.DispatchSpeechCompletion(receipt);
            if (result.Error is not null) throw new NotSupportedException(result.Error);
        };
        speech.InfoCompleted += info =>
        {
            RequireFinished(speech.Error is null && !speech.CanCaptureFinishedFailure,
                "Native INFO notification preceded source commitment.");
            completed.Add(info);
        };
    }

    private async Task WaitFinishedSpeech(RuntimeNativeSpeech speech, bool allowFailure)
    {
        var deadline = Time.GetTicksMsec() + 25000;
        while (speech.Active && speech.Error is null && Time.GetTicksMsec() < deadline)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        RequireFinished(speech.Error is not null ? allowFailure : !speech.Active,
            speech.Error ?? "Native owned voice exceeded the fixture's 25-second completion deadline.");
    }

    private static T FinishedSpeechCopy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void RequireFinished(bool value, string error)
    { if (!value) throw new InvalidDataException(error); }
    private static void RejectFinished(Action action, string error)
    {
        try { action(); }
        catch (Exception failure) when (failure is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException)
        { return; }
        throw new InvalidDataException(error);
    }
    private static object FinishedSpeechVoice(RuntimeNativeSpeech speech, FalloutFormKey actor)
    {
        var channels = typeof(RuntimeNativeSpeech).GetField("_channels", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(speech)
            as System.Collections.IDictionary ?? throw new InvalidDataException("Native speech channel ownership changed.");
        return channels[actor] ?? throw new InvalidDataException("Selected source has no actual native voice.");
    }
    private static FieldInfo FinishedSpeechVoiceField(object voice, string name) =>
        voice.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
            throw new InvalidDataException("Native speech fixture field is absent: " + name);
    private static T FinishedSpeechField<T>(object voice, string name) => (T)FinishedSpeechVoiceField(voice, name).GetValue(voice)!;
}
