using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private static void FinishedSpeechOpaqueNegative(RuntimeNativeSpeech speech, FalloutFormKey actor,
        FalloutNativeFinishedSpeechSnapshot expected)
    {
        // This is an explicitly injected first-party native callback boundary,
        // separate from the unchanged owned voice and source invocation.
        var voice = FinishedSpeechVoice(speech, actor);
        var field = FinishedSpeechVoiceField(voice, "PackageCompleted");
        RequireFinished(field.GetValue(voice) is null, "Owned finished voice unexpectedly has a package callback.");
        var callbackCalls = 0;
        field.SetValue(voice, (Action)(() => ++callbackCalls));
        try
        {
            RequireFinished(!speech.CanCaptureFinishedFailure, "Opaque native callback was ignored by readiness.");
            RejectFinished(() => speech.CaptureFinishedState(), "Opaque native callback was captured without continuation.");
            RequireFinished(callbackCalls == 0, "Read-only capture executed an opaque callback.");
        }
        finally { field.SetValue(voice, null); }
        RequireFinished(JsonSerializer.Serialize(expected) == JsonSerializer.Serialize(speech.CaptureFinishedState()),
            "Opaque-callback refusal changed the retained ended receipt.");
        GD.Print("OPENNV_NATIVE_FINISHED_SPEECH_OPAQUE_NEGATIVE_PASS syntheticCallbackInjection=true actualNativeCapture=true noCallbackExecution=true");
    }

    private void FinishedSpeechRestoreNegatives(FalloutPluginStack records, RuntimeConfiguration configuration,
        FalloutReferenceWorld world, FalloutQuestState quests, FalloutNativeFinishedSpeechSnapshot source)
    {
        var failure = source.Failure ?? throw new InvalidDataException("Native restore negative needs the actual ended source failure.");
        foreach (var invalid in new[]
        {
            source with { Voices = [source.Voices[0], source.Voices[0]] },
            source with { Voices = [source.Voices[0] with { Generation = source.Voices[0].Generation + 1 }] },
            source with { Failure = failure with { Receipt = failure.Receipt with { Generation = 0 } } },
            source with { Failure = failure with { Receipt = failure.Receipt with {
                Source = failure.Receipt.Source with { InfoSha256 = new('0', 64) } } } },
            source with { Failure = failure with { Receipt = failure.Receipt with { Topics = [] } } },
            source with { Failure = failure with { CompletedVoices = [new(failure.Receipt.Speaker, failure.Receipt.Generation)] } }
        })
        {
            var rejected = new RuntimeNativeSpeech();
            try
            {
                rejected.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip, quest => quests.Stage(quest), references: world);
                AddChild(rejected); rejected.SetProcess(false);
                var empty = JsonSerializer.Serialize(rejected.CaptureFinishedState());
                RejectFinished(() => rejected.RestoreFinishedState(FinishedSpeechCopy(invalid)),
                    "Malformed native source/clock snapshot was restored.");
                RequireFinished(!rejected.Active && rejected.Error is null && rejected.GetChildren().Count == 0 &&
                    empty == JsonSerializer.Serialize(rejected.CaptureFinishedState()),
                    "Malformed native snapshot partially restored audio/history/source error.");
            }
            finally { rejected.Free(); }
        }
        GD.Print("OPENNV_NATIVE_FINISHED_SPEECH_RESTORE_NEGATIVES_PASS nativeRestore=true sourceDrift=true generationMismatch=true duplicate=true atomic=true");
    }

    private async Task FinishedSpeechResultNegative(FalloutPluginStack records, RuntimeConfiguration configuration,
        FalloutReferenceWorld world, FalloutQuestState quests, RuntimeNativeNpc body, FalloutFormKey actor, FalloutFormKey topic)
    {
        // The original selected INFO and its canonical end scope execute through
        // the shared owner. This negative then injects a first-party owner fault,
        // not an unknown SCDA instruction or an original-plugin failure. The
        // genuine receipt is withheld; it never stamps native completion.
        var resultSpeech = new RuntimeNativeSpeech();
        try
        {
            resultSpeech.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip, quest => quests.Stage(quest),
                templates: reference => world.Get(reference).Templates, quests: quests, playerFemale: () => false,
                actorValue: (reference, value) => world.ActorValue(reference, FalloutActorValue.UserSlot(value)),
                dialogueRandom: world.ScriptValues.RandomBounded, references: world);
            resultSpeech.PrepareSubtitle = _ => { };
            resultSpeech.ReportDivergence = value => GD.Print("OPENNV_NATIVE_FINISHED_SPEECH_EXPECTED_RESULT_FAULT " + value);
            var prefixes = 0; var sourceCompletions = 0; var notifications = 0; var ended = 0; var looks = 0; var invocations = 0;
            FalloutScriptResultReceipt? completedEnd = null;
            const string resultError = "Isolated native result owner failed after its original compiled prefix.";
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                RequireFinished(effect.Kind == FalloutReferenceEffectKind.HeadTracking,
                    "Result-negative fixture reached an unrelated original result effect.");
                ApplyFinishedSpeechLook(body, records, effect); ++looks;
            }));
            resultSpeech.ExecuteOwnedResults = (info, speaker, begin) =>
            {
                var scope = FalloutScriptScope.Dialogue(info.Record, begin);
                RequireFinished(scope.Compiled, "Result-negative fixture needs an original authored SCDA scope.");
                var receipt = scripts.ExecuteResultOwned(info, speaker, begin); ++invocations;
                receipt.Require(scope, speaker);
                if (begin) return receipt;
                completedEnd = receipt; ++prefixes;
                throw new NotSupportedException(resultError);
            };
            resultSpeech.SayToCompleted += _ => ++sourceCompletions;
            resultSpeech.InfoCompleted += _ => ++notifications;
            AddChild(resultSpeech);
            resultSpeech.SayTo(actor, records.RuntimeFormKey(0x14), topic, true);
            var voice = FinishedSpeechVoice(resultSpeech, actor);
            var actual = FinishedSpeechField<FalloutDialogueInfo>(voice, "Info");
            var end = FalloutScriptScope.Dialogue(actual.Record, false);
            RequireFinished(actual.Responses.Count > 0 && end.Compiled && (actual.Flags & 8) == 0 && resultSpeech.Error is null,
                "Result-negative fixture requires real audio and an original compiled end result scheduled at audio end.");
            var hash = Convert.ToHexString(SHA256.HashData(actual.Record.ReadData()));
            var player = FinishedSpeechField<AudioStreamPlayer>(voice, "Player");
            player.Finished += () => ++ended;
            RequireFinished(player.Playing, "Result-negative fixture did not play the actual source audio.");
            await WaitFinishedSpeech(resultSpeech, allowFailure: true);
            RequireFinished(resultSpeech.Error == resultError && ended == actual.Responses.Count && !player.Playing &&
                !resultSpeech.IsTalking(actor) && !resultSpeech.CanCaptureFinishedFailure && !resultSpeech.CanCaptureState &&
                prefixes == 1 && looks == 1 && invocations == NativeAuthoredResultAudit.Invocations(actual) &&
                completedEnd is { Authority: FalloutScriptResultAuthority.CompiledVanilla, Completed: true, CommittedSteps: > 0 } &&
                FinishedSpeechVoiceField(voice, "EndResults").GetValue(voice) is null && sourceCompletions == 0 && notifications == 0,
                resultSpeech.Error ?? "Actual native end owner failure became successful source completion.");
            completedEnd!.Require(end, actor);
            RejectFinished(() => resultSpeech.CaptureFinishedState(), "Unfinished result owner became a durable native receipt.");
            RejectFinished(() => resultSpeech.CaptureState(), "Unfinished result owner became a capturable radio prefix.");
            var random = JsonSerializer.Serialize(world.ScriptValues.Capture());
            resultSpeech._Process(0); resultSpeech._Process(0);
            RejectFinished(() => resultSpeech.SayTo(actor, records.RuntimeFormKey(0x14), topic, true),
                "Failed result owner admitted a second original prefix.");
            RequireFinished(prefixes == 1 && looks == 1 && invocations == NativeAuthoredResultAudit.Invocations(actual) &&
                sourceCompletions == 0 && notifications == 0 && resultSpeech.Error == resultError &&
                random == JsonSerializer.Serialize(world.ScriptValues.Capture()) &&
                hash == Convert.ToHexString(SHA256.HashData(actual.Record.ReadData())),
                "Result owner fault replayed its original effects/RNG or modified the original INFO.");
            GD.Print("OPENNV_NATIVE_FINISHED_SPEECH_RESULTS_NEGATIVE_PASS " + JsonSerializer.Serialize(new
            {
                runtimeMvid = typeof(RuntimeNativeSpeech).Module.ModuleVersionId,
                info = actual.Record.FormKey.ToString(),
                winner = actual.Record.Plugin.Name,
                infoSha256 = hash,
                end.ScopeSha256,
                receipt = completedEnd,
                actualOwnedAudioEnd = true,
                originalCompiledResultExecuted = true,
                originalLookEffects = looks,
                syntheticOwnerCallbackFault = true,
                sourceCompletionNotEntered = true,
                receiptNotInstalled = true,
                nativeCaptureRefused = true,
                noReplay = true,
                campaign = "unverified",
                recording = false
            }));
        }
        finally { resultSpeech.Free(); }
    }

    private void FinishedSpeechUnretainedNegative(FalloutPluginStack records, RuntimeConfiguration configuration,
        FalloutQuestSnapshot[] quests, FalloutReferenceSnapshot[] references, FalloutScriptValueStoreSnapshot values,
        FalloutDetectionEventsSnapshot detection, FalloutNativeFinishedSpeechSnapshot finished)
    {
        // Explicit omission fixture, not a reconstruction of any old campaign
        // diagnostic. Its genuine native-ended receipt remains known, while
        // the source invocation needed for recovery is deliberately absent.
        using var legacy = new FalloutReferenceWorld(records);
        legacy.ScriptValues.Restore(values);
        legacy.Restore(references.Select(value => value with { ScriptStoppedFrame = null }).ToArray());
        legacy.RestoreDetection(detection);
        var states = new FalloutQuestState(records); states.Restore(quests);
        legacy.Detection.Bind(_ => throw new InvalidDataException("Missing original frame fabricated a location."),
            owner => owner == records.RuntimeFormKey(0x14) ? FalloutDetectionProcessLevel.High : null);
        var effects = 0;
        var scripts = new FalloutReferenceScripts(records, legacy, states, new((_, _) => false, _ => ++effects));
        var native = new RuntimeNativeSpeech();
        try
        {
            native.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip, quest => states.Stage(quest), references: legacy);
            native.CanResumeSourceCompletion = scripts.CanResumeSpeechCompletion;
            native.ResumeSourceCompletion = receipt =>
            {
                var result = scripts.ResumeSpeechCompletion(receipt);
                if (result.Error is not null) throw new NotSupportedException(result.Error);
            };
            AddChild(native); native.SetProcess(false); native.RestoreFinishedState(finished);
            native._Process(0);
            var receipt = finished.Failure!.Receipt.Completion();
            RequireFinished(!scripts.CanResumeSpeechCompletion(receipt) && native.Error == finished.Failure.Error && effects == 0 &&
                JsonSerializer.Serialize(finished) == JsonSerializer.Serialize(native.CaptureFinishedState()) &&
                native.GetChildren().Count == 0,
                "Missing source invocation was reconstructed or replayed by native restoration.");
            RejectFinished(() => scripts.ResumeSpeechCompletion(receipt), "Unretained original invocation admitted a fabricated source suffix.");
            RequireFinished(effects == 0 && JsonSerializer.Serialize(values) == JsonSerializer.Serialize(legacy.ScriptValues.Capture()),
                "Unretained source refusal replayed prefix effects or RNG.");
            GD.Print("OPENNV_NATIVE_FINISHED_SPEECH_UNRETAINED_NEGATIVE_PASS syntheticInvocationOmission=true nativeReceiptPreserved=true " +
                "originalPrefixNotReconstructed=true suffixRefused=true actualLegacyCampaign=unverified");
        }
        finally { native.Free(); }
    }
}
