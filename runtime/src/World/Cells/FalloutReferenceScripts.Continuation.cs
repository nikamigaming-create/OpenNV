using System.Globalization;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    private bool FixedContinuationOperand(FalloutScriptBindings bindings, string token) =>
        double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ||
        FalloutScriptBindings.IsPlayer(token) && bindings.HasPlayerReference || bindings.TryForm(token) is not null;

    internal bool CanResumeSpeechCompletion(FalloutSpeechCompletionReceipt receipt)
    {
        var instance = world.Retained(receipt.Speaker);
        if (instance.ScriptError is null) return false;
        var frame = instance.ScriptStoppedFrame;
        if (frame?.PreparedDetection is null || instance.Script is null || !world.Detection.CanCreate(frame.PreparedDetection)) return false;
        try
        {
            var source = Program(instance);
            frame.Validate(instance.ScriptError, instance.Script.Sha256, source.Events);
            if (frame.Event.Equals("SayToDone", StringComparison.OrdinalIgnoreCase)) RequireSameSpeech(frame.Speech!, receipt);
            _ = frame.DetectionContinuation(source.Events, token => FixedContinuationOperand(source.Bindings, token));
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException)
        { return false; }
    }

    private static void RequireSameSpeech(FalloutFinishedSpeechReceipt retained, FalloutSpeechCompletionReceipt actual)
    {
        if (retained.Speaker != actual.Speaker || retained.Info != actual.Info || retained.Generation != actual.Generation ||
            !retained.Topics.ToHashSet().SetEquals(actual.Topics))
            throw new InvalidDataException("Stopped SayToDone belongs to a different original voice receipt.");
    }

    internal FalloutReferenceScriptEventResult ResumeSpeechCompletion(FalloutSpeechCompletionReceipt receipt)
    {
        var instance = world.Retained(receipt.Speaker);
        _ = FalloutFinishedSpeechSourceBinding.Capture(records, instance, receipt);
        if (instance.ScriptStoppedFrame is not { } frame || instance.Script is null)
            throw new NotSupportedException("Historical speech/script failure has no retained original invocation; it cannot be reconstructed.");
        if (!CanResumeSpeechCompletion(receipt)) throw new NotSupportedException("Stopped speech source command has no validated suffix owner.");
        if (frame.Speech is { } speech) FalloutFinishedSpeechSourceBinding.Require(records, instance, speech);
        var source = Program(instance);
        var suffix = frame.DetectionContinuation(source.Events, token => FixedContinuationOperand(source.Bindings, token));
        var blockIndex = frame.Block;
        var active = source.Events[blockIndex];
        var activeProgram = suffix;
        var previousSpeech = _speechInvocation;
        _speechInvocation = frame.Speech;
        _restoringDetection = frame.PreparedDetection;
        _preparedDetection = null;
        var blocks = 0;
        try
        {
            Execute(instance.Reference, source.Bindings, suffix, frame.ActionReference, frame.ElapsedSeconds);
            ++blocks;
            for (++blockIndex; blockIndex < source.Events.Count; ++blockIndex)
            {
                active = source.Events[blockIndex]; activeProgram = active.Program;
                if (!active.Event.Equals(frame.Event, StringComparison.OrdinalIgnoreCase)) continue;
                if (frame.Event.Equals("SayToDone", StringComparison.OrdinalIgnoreCase) && active.Filter is not null)
                {
                    var topic = source.Bindings.Form(active.Filter);
                    if (topic.Signature != "DIAL") throw new InvalidDataException("SayToDone continuation filter is not DIAL.");
                    if (!receipt.Topics.Contains(topic.FormKey)) continue;
                }
                else if (active.Filter is not null) throw new NotSupportedException("Stopped event filter has no complete retained admission.");
                Execute(instance.Reference, source.Bindings, active.Program, frame.ActionReference, frame.ElapsedSeconds);
                ++blocks;
            }
            // A completed source suffix retains its historical fault receipt;
            // the now-settled invocation can never be entered twice.
            instance.CompletedScriptContinuation = frame.Copy();
            instance.ScriptStoppedFrame = null; instance.ScriptError = null;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException or OverflowException)
        {
            var failure = active.Event + ": " + error.Message;
            instance.ScriptError = failure;
            instance.ScriptStoppedFrame = new FalloutReferenceScriptStoppedFrame(failure, instance.Script.Sha256, blockIndex,
                activeProgram.LastStatement, active.Event, active.Filter, frame.ElapsedSeconds, frame.ActionReference,
                _speechInvocation, _preparedDetection, frame.OriginalError ?? frame.Error).Copy();
            return new(receipt.Speaker, "SayToDone", blocks, failure);
        }
        finally { _preparedDetection = null; _restoringDetection = null; _speechInvocation = previousSpeech; }
        // GameMode had prevented entry into this completion. Its suffix is now
        // committed, so deliver SayToDone for the first time with the same receipt.
        return frame.Event.Equals("GameMode", StringComparison.OrdinalIgnoreCase) ? DispatchSpeechCompletion(receipt) :
            new(receipt.Speaker, "SayToDone", blocks, null, frame.Error);
    }
}
