using System.Collections;
using System.Reflection;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Tools;

public partial class NativeActiveRadioAudit
{
    private static object Voice(RuntimeNativeSpeech speech, FalloutFormKey station) =>
        ((IDictionary)typeof(RuntimeNativeSpeech).GetField("_channels", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(speech)!)[station] ?? throw new InvalidDataException("Original native station channel is absent.");
    private static AudioStreamPlayer Player(RuntimeNativeSpeech speech, FalloutFormKey station)
    {
        var voice = Voice(speech, station);
        return (AudioStreamPlayer)voice.GetType().GetField("Player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(voice)!;
    }
    private static NativeOwnedPcmStream Pcm(RuntimeNativeSpeech speech, FalloutFormKey station)
    {
        var voice = Voice(speech, station);
        return (NativeOwnedPcmStream)voice.GetType().GetField("Pcm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(voice)!;
    }

    private static void CheckOpaqueRefusals(RuntimeNativeSpeech speech, FalloutFormKey station)
    {
        var voice = Voice(speech, station);
        foreach (var name in new[] { "ResponseCompleted", "PackageCompleted" })
        {
            var field = voice.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(voice, (Action)(() => throw new InvalidDataException("Opaque callback was replayed.")));
            try
            {
                Require(!speech.CanCaptureState, "An opaque native callback was admitted as active radio.");
                Reject(() => speech.CaptureState(), "An opaque native callback was silently discarded.");
            }
            finally { field.SetValue(voice, null); }
        }
    }

    private void CheckNativeSuffix(RuntimeNativeSpeech warm, RuntimeNativeSpeech cold, FalloutFormKey station,
        bool enableOwners = false)
    {
        var original = TrackNativeRadioAudio(warm, station);
        var restored = TrackNativeRadioAudio(cold, station);
        Vector2[] expected, actual;
        FalloutPcmPlaybackSnapshot warmBefore, coldBefore, warmAfter, coldAfter;
        AudioServer.Lock();
        try
        {
            warmBefore = Pcm(warm, station).Capture(); coldBefore = Pcm(cold, station).Capture();
            if (enableOwners)
            {
                Require(!warm.GetTree().Paused && warm.ProcessMode == ProcessModeEnum.Disabled &&
                    cold.ProcessMode == ProcessModeEnum.Disabled, "Enabled-clock check requires actual initially disabled native owners.");
                warm.ProcessMode = ProcessModeEnum.Pausable; cold.ProcessMode = ProcessModeEnum.Pausable;
                Require(warm.CanProcess() && cold.CanProcess() && Pcm(warm, station).Capture() == warmBefore &&
                    Pcm(cold, station).Capture() == coldBefore, "Enabling the source owners changed their suspended sample clocks.");
            }
            else { Pcm(warm, station).SetSuspended(false); Pcm(cold, station).SetSuspended(false); }
            expected = original.MixAudio(1.137f, 257);
            actual = restored.MixAudio(1.137f, 257);
            warmAfter = Pcm(warm, station).Capture(); coldAfter = Pcm(cold, station).Capture();
        }
        finally
        {
            if (enableOwners) { warm.ProcessMode = ProcessModeEnum.Disabled; cold.ProcessMode = ProcessModeEnum.Disabled; }
            else { Pcm(warm, station).SetSuspended(true); Pcm(cold, station).SetSuspended(true); }
            AudioServer.Unlock();
        }
        var different = Enumerable.Range(0, Math.Min(expected.Length, actual.Length))
            .FirstOrDefault(index => expected[index] != actual[index], -1);
        GD.Print("OPENNV_NATIVE_RADIO_SUFFIX " + JsonSerializer.Serialize(new
        {
            warmBefore,
            coldBefore,
            warmAfter,
            coldAfter,
            expectedFrames = expected.Length,
            actualFrames = actual.Length,
            different,
            expected = different < 0 ? "equal" : expected[different].ToString(),
            actual = different < 0 ? "equal" : actual[different].ToString(),
            warmPlayback = original.GetInstanceId(),
            coldPlayback = restored.GetInstanceId(),
            warmStream = Pcm(warm, station).Stream.GetInstanceId(),
            coldStream = Pcm(cold, station).Stream.GetInstanceId(),
            warmPaused = Player(warm, station).StreamPaused,
            coldPaused = Player(cold, station).StreamPaused
        }));
        Require(warmBefore == coldBefore && expected.Length == 257 && expected.SequenceEqual(actual) && warmAfter == coldAfter &&
            warmAfter.Position > warmBefore.Position && warmAfter.Position != Math.Truncate(warmAfter.Position) &&
            warm.CaptureState().ActiveRadio!.Single().Samples == cold.CaptureState().ActiveRadio!.Single().Samples,
            "Native cold radio changed the interpolated sample suffix or fractional clock.");
    }

    private async Task CheckInitialDisabled(FalloutPluginStack records, FalloutFormKey station, FalloutFormKey topic)
    {
        var world = CreateWorld(records, _saved.References!, _saved.Scripts!.Values);
        var quests = new FalloutQuestState(records); quests.Restore(_saved.Quests!);
        var globals = FalloutGlobalState.Read(records);
        if (_saved.Globals is { } globalState) globals.Restore(globalState);
        var said = (_saved.Scripts.SaidInfos ?? []).ToHashSet(); world.SetBroadcastState(station, 0);
        RuntimeNativeSpeech? initial = null, restored = null;
        var priorPause = GetTree().Paused;
        try
        {
            GetTree().Paused = false;
            initial = CreateSpeech(records, world, quests, globals, said);
            initial.ProcessMode = ProcessModeEnum.Disabled;
            Require(initial.IsInsideTree() && !initial.CanProcess(), "Initial Disabled source owner is not attached and disabled.");
            initial.StartRadioConversation(station, topic);
            TrackNativeRadioAudio(initial, station);
            var snapshot = Copy(initial.CaptureState()); var saved = snapshot.ActiveRadio!.Single();
            Require(saved.Samples.Position == 0 && saved.Samples.Playing && !saved.Samples.StartPending,
                "Initially disabled native radio advanced or lost its real playback owner before capture.");
            await ToSignal(GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
            Require(initial.CaptureState().ActiveRadio!.Single().Samples == saved.Samples,
                "Initially disabled source radio advanced while its tree was unpaused.");
            var results = _results.Count; var notifications = _notifications.Count;
            var random = JsonSerializer.Serialize(world.ScriptValues.Capture());
            restored = CreateSpeech(records, world, quests, globals, said.ToHashSet());
            restored.ProcessMode = ProcessModeEnum.Disabled; restored.RestoreState(snapshot);
            TrackNativeRadioAudio(restored, station);
            await ToSignal(GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
            Require(restored.CaptureState().ActiveRadio!.Single() == saved && _results.Count == results &&
                _notifications.Count == notifications && JsonSerializer.Serialize(world.ScriptValues.Capture()) == random,
                "Disabled cold radio changed its exact source clock or replayed committed source work.");
            // Enable under the real AudioServer lock. Only the owner's supported
            // Unpaused notification may resume PCM before the strict native mix.
            CheckNativeSuffix(initial, restored, station, enableOwners: true);
            var paused = initial.CaptureState().ActiveRadio!.Single().Samples;
            await ToSignal(GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
            Require(!initial.CanProcess() && !restored.CanProcess() &&
                initial.CaptureState().ActiveRadio!.Single().Samples == paused &&
                restored.CaptureState().ActiveRadio!.Single().Samples == paused,
                "Disabling the reenabled native radio advanced its retained fractional clock.");
            GD.Print($"OPENNV_NATIVE_RADIO_DISABLED station={station} info={saved.Conversation.Info} initialClock=0 " +
                "coldClock=true nativeEnableSuffix=true disabledAgainClock=true prefixReplay=false");
        }
        finally
        {
            if (initial is not null) { initial.Free(); _speeches.Remove(initial); }
            if (restored is not null) { restored.Free(); _speeches.Remove(restored); }
            GetTree().Paused = priorPause;
        }
    }

    private void CheckRejectedRestoration(FalloutPluginStack records, FalloutReferenceWorld world, FalloutQuestState quests,
        FalloutGlobalState globals, HashSet<FalloutFormKey> said, FalloutNativeFinishedSpeechSnapshot snapshot)
    {
        var saved = snapshot.ActiveRadio!.Single();
        var results = _results.Count; var notifications = _notifications.Count;
        var random = JsonSerializer.Serialize(world.ScriptValues.Capture());
        foreach (var invalid in new[]
        {
            saved with { AudioSha256 = new('0', 64) }, saved with { DecodedSha256 = new('0', 64) },
            saved with { LipSha256 = new('0', 64) },
            saved with { Binding = saved.Binding with { AudioPath = saved.Binding.AudioPath + ".changed" } },
            saved with { Samples = saved.Samples with { Frames = saved.Samples.Frames + 1 } },
            saved with { Conversation = saved.Conversation with { InfoSha256 = new('0', 64) } }
        })
        {
            var rejected = CreateSpeech(records, world, quests, globals, said);
            try
            {
                Reject(() => rejected.RestoreState(snapshot with { ActiveRadio = [invalid] }), "Changed active source/media continuation was admitted.");
                Require(rejected.GetChildren().Count == 0 && !rejected.IsTalking(saved.Conversation.Identity.Station.Reference) &&
                    _results.Count == results && _notifications.Count == notifications &&
                    JsonSerializer.Serialize(world.ScriptValues.Capture()) == random,
                    "Rejected radio restoration retained a native child, published a channel or replayed source state.");
            }
            finally { rejected.Free(); _speeches.Remove(rejected); }
        }
    }

    private void CheckMixerFailure(FalloutPluginStack records, FalloutReferenceWorld world, FalloutQuestState quests,
        FalloutGlobalState globals, HashSet<FalloutFormKey> said, FalloutNativeFinishedSpeechSnapshot snapshot, FalloutFormKey station)
    {
        var failed = CreateSpeech(records, world, quests, globals, said);
        try
        {
            failed.RestoreState(snapshot);
            var results = _results.Count; var notifications = _notifications.Count;
            var playback = TrackNativeRadioAudio(failed, station);
            AudioServer.Lock();
            try { _ = playback.MixAudio(float.NaN, 16); }
            finally { AudioServer.Unlock(); }
            GetTree().Paused = false; failed._Process(0); GetTree().Paused = true;
            Require(failed.Error?.Contains("PCM mixer failed", StringComparison.Ordinal) == true && !failed.CanCaptureState &&
                _results.Count == results && _notifications.Count == notifications,
                "A failed native mixer became a successful source result/completion.");
        }
        finally { failed.Free(); _speeches.Remove(failed); }
    }

    private void DrainNativeResponse(RuntimeNativeSpeech speech, FalloutFormKey station)
    {
        var playback = TrackNativeRadioAudio(speech, station);
        var samples = speech.CaptureState().ActiveRadio!.Single().Samples;
        var remaining = Math.Ceiling((samples.Frames - samples.Position) * samples.OutputRate / samples.SourceRate) + 8192;
        AudioServer.Lock();
        try
        {
            Pcm(speech, station).SetSuspended(false);
            for (long mixed = 0; mixed <= remaining; mixed += 8192)
                if (playback.MixAudio(1, 8192).Length < 8192) return;
            throw new InvalidDataException("Finite native response exceeded its retained source extent.");
        }
        finally { Pcm(speech, station).SetSuspended(true); AudioServer.Unlock(); }
    }
}
