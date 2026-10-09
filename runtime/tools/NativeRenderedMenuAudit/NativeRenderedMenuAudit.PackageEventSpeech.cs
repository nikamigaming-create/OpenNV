using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    // Owned native component proof. This reads a genuine checkpoint without
    // changing it. Direct package-event admission here is fixture setup, not
    // evidence that a native actor completed its package or the campaign.
    private async Task PackageEventSpeechCold(string game, string mod, string modRoot, string checkpoint,
        string actorIdentity, string packageIdentity, string eventKind, string infoIdentity, string[] dependencies)
    {
        RuntimeNativeNpc? body = null;
        RuntimeNativeSpeech? speech = null, coldSpeech = null;
        FalloutPluginStack? records = null;
        var worlds = new List<FalloutReferenceWorld>();
        var input = File.ReadAllBytes(checkpoint);
        try
        {
            var setup = new FalloutModStackSelection([new(mod, modRoot, dependencies)]).Resolve(game);
            RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
            var content = RuntimeLiveContentSource.Current!;
            records = FalloutPluginStack.Load(content.PluginSources);
            var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
            var opening = FalloutCellSceneReader.Read(records, new("FalloutNV.esm", 0x103df9));
            var saved = FalloutNativeCampaignSave.Read(checkpoint, content.SaveCompatibilityId, records).State;
            FalloutFormKey Key(string value)
            {
                var parts = value.Split(':');
                return parts.Length == 2 ? new(parts[0], Convert.ToUInt32(parts[1], 16)) :
                    throw new ArgumentException("Package speech fixture needs plugin:hex-object-id identities.");
            }
            var actor = Key(actorIdentity); var package = Key(packageIdentity); var expectedInfo = Key(infoIdentity);
            RequireFinished(records.GetEffective(actor).Signature == "ACHR", "Package speech fixture requires a real winning ACHR.");
            var source = FalloutScriptPackage.Read(records.GetEffective(package));
            if (!source.EventPrograms.TryGetValue(eventKind, out var eventProgram) || eventProgram.Topic is not { } topic)
                throw new InvalidDataException("Selected original package event has no source topic.");
            var expected = FalloutDialogueTopic.Decode(records.GetEffective(expectedInfo));
            RequireFinished(FalloutDialogueTopic.Read(records, topic).Infos.Any(info => info.Record.FormKey == expectedInfo) &&
                expected.Responses.Count != 0 && NativeAuthoredResultAudit.IsEmpty(records, expected, true) && NativeAuthoredResultAudit.IsEmpty(records, expected, false),
                "Selected package voice must have actual responses and independently empty original result scopes.");
            var configuration = RuntimeConfiguration.Load();
            var world = new FalloutReferenceWorld(records); worlds.Add(world);
            world.RestoreEncounterZones(saved.EncounterZones); world.Restore(saved.References!);
            world.RestoreActorOverrides(saved.ActorOverrides); world.RestoreFactionRelations(saved.FactionRelations);
            var quests = new FalloutQuestState(records); quests.Restore(saved.Quests!);
            var globals = FalloutGlobalState.Read(records); globals.Restore(saved.Globals!);
            if (saved.Scripts?.Values is { } values) world.ScriptValues.Restore(values);
            var said = (saved.Scripts?.SaidInfos ?? []).ToHashSet();
            RequireFinished(!said.Contains(expectedInfo) && world.Get(actor).Injury?.Dead != true && world.IsEnabled(actor),
                "Genuine checkpoint has already consumed the selected one-time voice or disabled/dead caller.");
            var cell = FalloutCellSceneReader.Read(records, world.Placement(actor).Cell); world.LoadCell(cell);
            body = FinishedSpeechBody(records, content, world, cell, actor, configuration.World.GameUnitsToMeters);
            AddChild(body); body.SetProcess(false); body.SetPhysicsProcess(false);
            var initialReferences = FinishedSpeechCopy(world.Capture().ToArray());
            var initialQuests = FinishedSpeechCopy(quests.Capture().ToArray());
            var initialValues = FinishedSpeechCopy(world.ScriptValues.Capture());
            var initialSaid = said.ToArray();
            var results = 0; var notifications = 0; var sourceCompletions = 0;
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new NotSupportedException("Package voice fixture reached an unowned original source effect."), Globals: globals));
            speech = PackageSpeechOwner(records, configuration, world, quests, said);
            speech.ExecuteOwnedResults = (info, caller, begin) =>
            {
                var result = scripts.ExecuteResultOwned(info, caller, begin);
                RequireFinished(result.CommittedSteps == 0, "Result-free package executed an authored instruction.");
                ++results;
                return result;
            };
            var expectedResults = NativeAuthoredResultAudit.Invocations(expected);
            speech.InfoCompleted += info =>
            {
                RequireFinished(info == expectedInfo, "Package voice completed an unexpected original INFO.");
                ++notifications;
            };
            speech.SayToCompleted += _ => ++sourceCompletions;
            AddChild(speech);
            var inputs = new[] { records.GetEffective(actor), records.GetEffective(world.Get(actor).Base),
                records.GetEffective(package), records.GetEffective(topic), expected.Record, records.GetEffective(cell.Cell.FormKey) };
            string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
            var hashes = inputs.Select(Hash).ToArray();
            speech.StartPackageEventTopic(actor, package, eventKind, topic);
            var voice = FinishedSpeechVoice(speech, actor);
            var selected = FinishedSpeechField<FalloutDialogueInfo>(voice, "Info");
            var player = FinishedSpeechField<AudioStreamPlayer>(voice, "Player");
            var started = JsonSerializer.SerializeToElement(speech.State);
            RequireFinished(selected.Record.FormKey == expectedInfo && player.Playing &&
                FinishedSpeechField<long>(voice, "Generation") == 1 &&
                FinishedSpeechVoiceField(voice, "PackageEvent").GetValue(voice) is not null &&
                started.GetProperty("audioSha256").GetString() is { Length: 64 },
                speech.Error ?? "Selected unchanged package event did not start its real native voice.");
            var audioEnds = 0; player.Finished += () => ++audioEnds;
            RejectFinished(() => speech.CaptureFinishedState(), "Active source package voice was saved as finished history.");
            var randomAfterSelection = JsonSerializer.Serialize(world.ScriptValues.Capture());
            await WaitFinishedSpeech(speech, allowFailure: false);
            RequireFinished(speech.Error is null && !speech.Active && !speech.IsTalking(actor) && !player.Playing &&
                audioEnds == selected.Responses.Count && notifications == 1 && sourceCompletions == 0 && results == expectedResults &&
                FinishedSpeechVoiceField(voice, "PackageCompleted").GetValue(voice) is null &&
                FinishedSpeechVoiceField(voice, "PackageEvent").GetValue(voice) is null,
                speech.Error ?? "Actual package voice did not retire its source event after audio and callback completion.");
            var finished = FinishedSpeechCopy(speech.CaptureFinishedState());
            RequireFinished(finished is
            {
                CompletedCommands: 0, CompletedPackages: 0,
                RequestedPackageEventTopics: 1, CompletedPackageEventTopics: 1, Failure: null
            } &&
                finished.Voices.Single() == new FalloutNativeVoiceHistory(actor, 1, 0) &&
                randomAfterSelection == JsonSerializer.Serialize(world.ScriptValues.Capture()),
                "Package event counter classification, generation or selection RNG changed at retirement.");
            var references = FinishedSpeechCopy(world.Capture().ToArray());
            var valuesAfter = FinishedSpeechCopy(world.ScriptValues.Capture()); var saidAfter = said.ToArray();
            speech._Process(0); speech._Process(0);
            RequireFinished(JsonSerializer.Serialize(finished) == JsonSerializer.Serialize(speech.CaptureFinishedState()) &&
                notifications == 1 && results == expectedResults && sourceCompletions == 0,
                "Repeated native processing replayed the ended package callback/results.");
            world.UnloadCell(cell.Cell.FormKey); scripts.UnloadCell(cell.Cell.FormKey);
            speech.Free(); speech = null; body.Free(); body = null;
            var cold = new FalloutReferenceWorld(records); worlds.Add(cold);
            cold.ScriptValues.Restore(valuesAfter); cold.Restore(references);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(quests.Capture());
            coldSpeech = PackageSpeechOwner(records, configuration, cold, coldQuests, saidAfter.ToHashSet());
            coldSpeech.ExecuteOwnedResults = (_, _, _) => throw new InvalidDataException("Cold package history replayed source results.");
            coldSpeech.InfoCompleted += _ => throw new InvalidDataException("Cold package history replayed INFO notification.");
            coldSpeech.SayToCompleted += _ => throw new InvalidDataException("Cold package history invented SayToDone.");
            AddChild(coldSpeech); coldSpeech.SetProcess(false); coldSpeech.RestoreFinishedState(finished);
            coldSpeech._Process(0); coldSpeech._Process(0);
            RequireFinished(!cold.IsResident(actor) && coldSpeech.GetChildren().Count == 0 && !coldSpeech.Active &&
                coldSpeech.Error is null && JsonSerializer.Serialize(finished) == JsonSerializer.Serialize(coldSpeech.CaptureFinishedState()) &&
                JsonSerializer.Serialize(valuesAfter) == JsonSerializer.Serialize(cold.ScriptValues.Capture()) &&
                saidAfter.ToHashSet().SetEquals(said),
                "Body/audio-free cold package history lost counters or replayed a consumed source prefix.");
            await PackageSpeechCallbackNegative(records, content, configuration, initialReferences, initialQuests,
                initialValues, initialSaid, cell, actor, package, eventKind, topic, expectedInfo, reentrant: true);
            await PackageSpeechCallbackNegative(records, content, configuration, initialReferences, initialQuests,
                initialValues, initialSaid, cell, actor, package, eventKind, topic, expectedInfo, reentrant: false);
            RequireFinished(input.AsSpan().SequenceEqual(File.ReadAllBytes(checkpoint)) &&
                !inputs.Where((record, index) => Hash(record) != hashes[index]).Any(),
                "Package speech fixture changed original source or genuine checkpoint bytes.");
            GD.Print("OPENNV_NATIVE_PACKAGE_EVENT_SPEECH_COLD_PASS " + JsonSerializer.Serialize(new
            {
                runtimeMvid = typeof(RuntimeNativeSpeech).Module.ModuleVersionId,
                content.SaveCompatibilityId,
                checkpointSha256 = Convert.ToHexString(SHA256.HashData(input)),
                actor = actor.ToString(),
                package = package.ToString(),
                eventKind,
                topic = topic.ToString(),
                info = expectedInfo.ToString(),
                sourceRecords = inputs.Select((record, index) => new
                {
                    form = record.FormKey.ToString(),
                    record.Signature,
                    winner = record.Plugin.Name,
                    sha256 = hashes[index]
                }),
                audioSha256 = started.GetProperty("audioSha256").GetString(),
                lipSha256 = started.GetProperty("lipSha256").GetString(),
                audioEnds,
                actualResultCalls = results,
                infoNotifications = notifications,
                scriptedCompletions = sourceCompletions,
                finished,
                coldWithoutBodyOrAudio = true,
                counterResultHistoryRandomOnce = true,
                reentrantAndFailedCallbacks = "separate explicitly injected first-party native components",
                sourceReadonly = true,
                saveReadonly = true,
                recording = false,
                campaignAndParity = "unverified"
            }));
        }
        finally
        {
            try { coldSpeech?.Free(); speech?.Free(); }
            finally
            {
                try { body?.Free(); }
                finally
                {
                    foreach (var world in worlds) world.Dispose();
                    records?.Dispose(); RuntimeLiveContentSource.Clear();
                }
            }
        }
    }

    private static RuntimeNativeSpeech PackageSpeechOwner(FalloutPluginStack records, RuntimeConfiguration configuration,
        FalloutReferenceWorld world, FalloutQuestState quests, HashSet<FalloutFormKey> said)
    {
        var speech = new RuntimeNativeSpeech();
        speech.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip, key => quests.Stage(key),
            saidInfos: said, templates: reference => world.Get(reference).Templates, quests: quests, playerFemale: () => false,
            actorValue: (reference, value) => world.ActorValue(reference, FalloutActorValue.UserSlot(value)),
            dialogueRandom: world.ScriptValues.RandomBounded, references: world);
        speech.PrepareSubtitle = _ => { };
        speech.ReportDivergence = value => GD.Print("OPENNV_NATIVE_PACKAGE_SPEECH_OBSERVED " + value);
        return speech;
    }
}
