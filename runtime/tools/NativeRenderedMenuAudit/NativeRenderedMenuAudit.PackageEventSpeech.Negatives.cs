using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private async Task PackageSpeechCallbackNegative(FalloutPluginStack records, RuntimeLiveContentSource content,
        RuntimeConfiguration configuration, FalloutReferenceSnapshot[] references, FalloutQuestSnapshot[] questState,
        FalloutScriptValueStoreSnapshot values, FalloutFormKey[] initialSaid, FalloutCellScene cell, FalloutFormKey actor,
        FalloutFormKey package, string eventKind, FalloutFormKey topic, FalloutFormKey expectedInfo, bool reentrant)
    {
        RuntimeNativeNpc? body = null; RuntimeNativeSpeech? speech = null;
        using var world = new FalloutReferenceWorld(records);
        try
        {
            world.ScriptValues.Restore(FinishedSpeechCopy(values)); world.Restore(FinishedSpeechCopy(references)); world.LoadCell(cell);
            var quests = new FalloutQuestState(records); quests.Restore(FinishedSpeechCopy(questState));
            body = FinishedSpeechBody(records, content, world, cell, actor, configuration.World.GameUnitsToMeters);
            AddChild(body); body.SetProcess(false); body.SetPhysicsProcess(false);
            speech = PackageSpeechOwner(records, configuration, world, quests, initialSaid.ToHashSet());
            var results = 0; var infos = 0; var sourceCompletions = 0; var callbackPrefix = 0; var nextEnded = 0;
            speech.ExecuteResults = (_, _, _) => { ++results; throw new InvalidDataException("Original result-free INFO invented results."); };
            speech.InfoCompleted += info => { RequireFinished(info == expectedInfo, "Callback fixture changed original INFO."); ++infos; };
            speech.SayToCompleted += _ => ++sourceCompletions;
            AddChild(speech); speech.StartPackageEventTopic(actor, package, eventKind, topic);
            var native = speech;
            var voice = FinishedSpeechVoice(native, actor);
            var original = FinishedSpeechField<FalloutDialogueInfo>(voice, "Info");
            RequireFinished(original.Record.FormKey == expectedInfo, "Callback fixture selected a different original package voice.");
            var player = FinishedSpeechField<AudioStreamPlayer>(voice, "Player");
            var ends = 0; player.Finished += () => ++ends;
            var originalCallback = FinishedSpeechField<Action>(voice, "PackageCompleted");
            const string callbackError = "Isolated package callback failed after its committed prefix.";
            FinishedSpeechVoiceField(voice, "PackageCompleted").SetValue(voice, (Action)(() =>
            {
                originalCallback(); ++callbackPrefix;
                if (!reentrant) throw new NotSupportedException(callbackError);
                // This is explicitly a component response, not another authored
                // package selection or a bypass of INFO's consumed SayOnce bit.
                // Its real owned audio keeps the same actor's new generation busy.
                native.StartResponse(actor, original, 0, () => ++nextEnded);
            }));
            var random = JsonSerializer.Serialize(world.ScriptValues.Capture());
            var deadline = Time.GetTicksMsec() + 25000;
            while (callbackPrefix == 0 && native.Error is null && Time.GetTicksMsec() < deadline)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            RequireFinished(callbackPrefix == 1 && ends == original.Responses.Count && results == 0 && sourceCompletions == 0 &&
                random == JsonSerializer.Serialize(world.ScriptValues.Capture()),
                native.Error ?? "Package completion callback did not consume exactly one original audio prefix.");
            var state = JsonSerializer.SerializeToElement(native.State);
            RequireFinished(state.GetProperty("requestedPackageEventTopics").GetInt64() == 1 &&
                state.GetProperty("completedPackageEventTopics").GetInt64() == 1 &&
                state.GetProperty("completedPackages").GetInt64() == 0,
                "Completed old source event was classified from a reentrant/new voice marker.");
            if (reentrant)
            {
                RequireFinished(native.Error is null && FinishedSpeechField<long>(voice, "Generation") == 2 &&
                    native.IsTalking(actor) && player.Playing && nextEnded == 0 && infos == 1 &&
                    FinishedSpeechVoiceField(voice, "ResponseCompleted").GetValue(voice) is not null,
                    native.Error ?? "Old package retirement cleared its reentrant native response generation.");
                RejectFinished(() => native.CaptureFinishedState(), "Reentrant response audio/opaque callback was saved as ended speech.");
                RequireFinished(!native.CanCaptureFinishedState, "Active native response was admitted before save capture.");
                await WaitFinishedSpeech(native, allowFailure: false);
                var settled = native.CaptureFinishedState();
                RequireFinished(nextEnded == 1 && callbackPrefix == 1 && infos == 1 && results == 0 && sourceCompletions == 0 &&
                    settled.Voices.Single() == new FalloutNativeVoiceHistory(actor, 2, 0) &&
                    settled is { CompletedPackages: 0, RequestedPackageEventTopics: 1, CompletedPackageEventTopics: 1 },
                    "Reentrant native generation lost its independent completion or replayed original package history.");
                GD.Print("OPENNV_NATIVE_PACKAGE_SPEECH_REENTRANT_PASS originalOwnedPackageVoice=true " +
                    "syntheticCompletionCallback=true realNewResponseAudio=true oldGeneration=1 newGeneration=2 " +
                    "sourceSayOnceNotReset=true oldCountersOnce=true activeCaptureRefused=true recording=false");
            }
            else
            {
                RequireFinished(native.Error == callbackError && !native.IsTalking(actor) && !player.Playing &&
                    FinishedSpeechVoiceField(voice, "PackageEvent").GetValue(voice) is not null && !native.CanCaptureFinishedFailure,
                    "Failed package callback was erased or promoted into a fully owned stopped source receipt.");
                RejectFinished(() => native.CaptureFinishedState(), "Incomplete package callback was saved without continuation state.");
                RequireFinished(!native.CanCaptureFinishedState, "Ended but opaque package callback was admitted before save capture.");
                native._Process(0); native._Process(0);
                RequireFinished(callbackPrefix == 1 && native.Error == callbackError && results == 0 && sourceCompletions == 0 &&
                    random == JsonSerializer.Serialize(world.ScriptValues.Capture()),
                    "Failed native package callback replayed committed results/counters/RNG.");
                GD.Print("OPENNV_NATIVE_PACKAGE_SPEECH_CALLBACK_FAILURE_PASS actualOwnedAudioEnd=true " +
                    "syntheticCallbackPrefix=1 originalPackageCounterConsumed=true retainedFailure=true " +
                    "unfinishedCallbackCaptureRefused=true noReplay=true recording=false");
            }
        }
        finally { speech?.Free(); body?.Free(); }
    }
}
