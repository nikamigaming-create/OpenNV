using System.Diagnostics;
using System.Text.Json;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness
{
    private RecordedInputDeliveryJournal? _replayDeliveryJournal;

    private bool DispatchSequentialRecordedInput(JsonElement command, ulong request)
    {
        switch (command.GetProperty("op").GetString())
        {
            case "input.binding":
                if (_inputPlayback?.Active == true || _inputRecording is not null || _checkpointTransitioning?.Invoke() == true || !_replayCheckpointPrepared)
                    throw new InvalidOperationException("Export an input binding only after an actual completed checkpoint load without intervening input.");
                var restored = _restoredCheckpoint?.Invoke() ?? throw new InvalidOperationException("No actual restored OpenNV checkpoint exists.");
                AtomicWrite(System.IO.Path.Combine(_directory, $"{request:D10}.input-binding.json"), JsonSerializer.Serialize(new
                {
                    schema = "opennv-current-input-binding/v1",
                    request,
                    process = System.Environment.ProcessId,
                    checkpoint = restored,
                    binding = InputBinding(restored.Path),
                    prepared = true,
                    sampledMonotonicTicks = Stopwatch.GetTimestamp(),
                    frequency = Stopwatch.Frequency,
                    ordinaryInput = false,
                    retailStateAlignment = "unverified"
                }, Json));
                return true;
            case "input.replay.diagnostic.start":
                if (_inputPlayback?.Active == true || _inputRecording is not null || _checkpointTransitioning?.Invoke() == true)
                    throw new InvalidOperationException("Input replay cannot replace an active recording, replay or checkpoint transition.");
                var path = command.GetProperty("path").GetString()!; RequireTapePath(path);
                var tape = RecordedInputTape.Read(path);
                var limit = command.TryGetProperty("maximumLatenessMicroseconds", out var value) ? value.GetInt64() : 250_000;
                var playback = RecordedInputPlayback.UnjoinedDiagnostic(tape, limit);
                _campaignBot.Stop(); _bot?.Stop(); ReleaseAll();
                BeginReplayDeliveryJournal(command, tape, null, request);
                _inputPlayback = playback; _inputTapePath = path; _inputStarted = Stopwatch.GetTimestamp();
                _inputReplayRequest = request; _replayCheckpointPrepared = false;
                PublishState(); return true;
            default: return false;
        }
    }

    private void BeginReplayDeliveryJournal(JsonElement command, RecordedInputTape tape, RecordedInputBinding? actualBinding, ulong request)
    {
        if (!command.TryGetProperty("receiptJournal", out var journalPath)) return;
        if (_replayDeliveryJournal is not null) throw new InvalidOperationException("A prior replay receipt owner remains active.");
        var path = journalPath.GetString() ?? throw new ArgumentException("A replay receipt journal path is absent.");
        _replayDeliveryJournal = new(path, new
        {
            engine = "opennv",
            process = System.Environment.ProcessId,
            request,
            tape = tape.Header,
            tapeDigest = tape.Footer.Sha256,
            actualBinding,
            alignment = tape.Unjoined ? "unjoined-diagnostic" : "checkpoint-bound; retail-state-alignment-unverified",
            clock = "replay-owner-monotonic; native delivery inside callback",
            retailStateAuthority = false
        });
    }

    private void RecordReplayDelivery(JsonElement input)
    {
        _replayDeliveryJournal?.Append("Godot-input-dispatch-returned", InputMicroseconds, new
        {
            inputOrdinal = _inputPlayback!.Cursor + 1,
            input,
            stateKey = _captureIdentity().StateKey,
            observedMonotonicTicks = Stopwatch.GetTimestamp(),
            frequency = Stopwatch.Frequency,
            physicsCount = Godot.Engine.GetPhysicsFrames(),
            drawCount = Godot.Engine.GetFramesDrawn(),
            gameplayEffect = "unverified"
        });
    }

    private void FinishReplayDeliveryJournal(string? ownerFailure = null)
    {
        var journal = _replayDeliveryJournal; _replayDeliveryJournal = null;
        if (journal is null) return;
        try
        {
            journal.Finish(InputMicroseconds, ownerFailure ?? _inputPlayback?.Error ??
            (_inputPlayback?.Complete == true ? null : "Replay retired without completion."), _inputPlayback?.State);
        }
        catch (Exception error)
        {
            _inputPlayback?.RetainEvidenceFailure(error.GetType().Name + ": " + error.Message);
            Godot.GD.PushError("OPENNV_INPUT_REPLAY_RECEIPT_RETIREMENT_FAILED " + error);
        }
        finally { journal.Dispose(); }
    }
}
