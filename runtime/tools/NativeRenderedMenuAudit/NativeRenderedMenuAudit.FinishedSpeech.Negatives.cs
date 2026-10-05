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
        FalloutReferenceWorld world, FalloutQuestState quests, FalloutFormKey actor, FalloutFormKey topic)
    {
        // Real native audio completes. Only this negative substitutes an
        // independent first-party result body/callback after selection; its
        // behavior is not attributed to the original INFO result script.
        var resultSpeech = new RuntimeNativeSpeech();
        try
        {
            resultSpeech.Configure(records, configuration.ActorCompiler.FaceGenAnimation.Lip, quest => quests.Stage(quest),
                templates: reference => world.Get(reference).Templates, quests: quests, playerFemale: () => false,
                actorValue: (reference, value) => world.ActorValue(reference, FalloutActorValue.UserSlot(value)),
                dialogueRandom: world.ScriptValues.RandomBounded, references: world);
            resultSpeech.PrepareSubtitle = _ => { };
            resultSpeech.ReportDivergence = value => GD.Print("OPENNV_NATIVE_FINISHED_SPEECH_EXPECTED_RESULT_FAULT " + value);
            var prefixes = 0; var sourceCompletions = 0; var notifications = 0; var ended = 0;
            const string resultError = "Isolated native INFO end-result prefix remains incomplete.";
            // Original begin results still use their real C# source owner.
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new InvalidDataException("Result-negative fixture reached an unrelated original effect.")));
            resultSpeech.ExecuteResults = (info, speaker, begin) =>
            {
                if (begin) { scripts.ExecuteResult(info, speaker, true); return; }
                ++prefixes;
                throw new NotSupportedException(resultError);
            };
            resultSpeech.SayToCompleted += _ => ++sourceCompletions;
            resultSpeech.InfoCompleted += _ => ++notifications;
            AddChild(resultSpeech);
            resultSpeech.SayTo(actor, records.RuntimeFormKey(0x14), topic, true);
            var voice = FinishedSpeechVoice(resultSpeech, actor);
            var infoField = FinishedSpeechVoiceField(voice, "Info");
            var actual = FinishedSpeechField<FalloutDialogueInfo>(voice, "Info");
            RequireFinished(actual.Responses.Count > 0 && resultSpeech.Error is null,
                "Result-negative fixture selected no original voice.");
            infoField.SetValue(voice, actual with { EndScript = "Set syntheticResultPrefix to syntheticResultPrefix + 1", Flags = (byte)(actual.Flags & ~8) });
            var player = FinishedSpeechField<AudioStreamPlayer>(voice, "Player");
            player.Finished += () => ++ended;
            RequireFinished(player.Playing, "Result-negative fixture did not play the actual source audio.");
            await WaitFinishedSpeech(resultSpeech, allowFailure: true);
            RequireFinished(resultSpeech.Error == resultError && ended == actual.Responses.Count && !player.Playing &&
                !resultSpeech.IsTalking(actor) && !resultSpeech.CanCaptureFinishedFailure &&
                prefixes == 1 && sourceCompletions == 0 && notifications == 0,
                resultSpeech.Error ?? "Unfinished actual native result phase was promoted to ended source completion.");
            RejectFinished(() => resultSpeech.CaptureFinishedState(), "Incomplete native INFO end results became a durable finished receipt.");
            GD.Print("OPENNV_NATIVE_FINISHED_SPEECH_RESULTS_NEGATIVE_PASS actualOwnedAudioEnd=true syntheticResultPrefix=1 " +
                "sourceCompletionNotEntered=true nativeCaptureRefused=true campaign=unverified recording=false");
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
