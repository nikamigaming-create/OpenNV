using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private const string MainFrameSchema = "opennv-source-main-frame/v1";
    private uint _mainWord;
    private FalloutMainFrameDeclaration _mainFrameDeclaration = null!;
    private readonly List<FalloutMainQueueFrameReceipt> _mainWindows = [];
    private FalloutActorProcessRuntimeHandoff? _mainFrameCold;
    private string? _mainFrameBoundary;
    private bool _mainFrameExecuting;
    private long _mainFrameReentry;
    // This replaces the former separate Boolean field. All existing forced
    // load/update writers and the queue guard consume the same actual word.
    private bool _forced
    {
        get => (_mainWord & FalloutMainFrameDeclaration.ForcedProcessing) != 0;
        set => _mainWord = value ? _mainWord | FalloutMainFrameDeclaration.ForcedProcessing :
            _mainWord & ~FalloutMainFrameDeclaration.ForcedProcessing;
    }
    private void ConstructSourceMainFrame()
    {
        _mainFrameDeclaration = FalloutMainFrameDeclaration.ForExecutable(_source.ExecutableSha256);
        _mainWord = _mainFrameDeclaration.ConstructorWord;
    }
    private void CompleteSourceMainWord(FalloutMainProcessOperation operation)
    {
        // The reached FO3 world-load return clears its independent bit0
        // together with bit1. The full-update return clears bit1 alone.
        if (_mainFrameDeclaration.ConstructorWord == 1 && operation == FalloutMainProcessOperation.SourceWorldLoad)
            _mainWord &= ~1u;
    }
    internal FalloutActorProcessFact<bool> MainPermitsForcedQueue => new(
        (_mainWord & FalloutMainFrameDeclaration.PermitsForcedQueue) != 0,
        "actual-selected-Main-independent-queue-window-bit/" + _sequence,
        _mainFrameBoundary ?? _mainWindows.FirstOrDefault(value => value.Failure is not null)?.Failure);
    private string? MainFrameSaveBlocker => _mainFrameExecuting ? "actual-source-Main-window-consumer-entered" : _mainFrameBoundary ??
        (_mainWindows.LastOrDefault() is { Next: not FalloutMainQueueFrameStep.Complete } window ?
            "actual-source-Main-window:" + window.Owner + ":" + (window.Failure ?? window.Next.ToString()) : null);
    internal void ExecuteMainQueueWindow(bool full, IFalloutMainQueueFrameConsumers consumers)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(consumers);
        RequireMainWriterOutsideWindow();
        ArgumentException.ThrowIfNullOrWhiteSpace(consumers.Owner);
        if (MainFrameSaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        var before = _mainWord;
        _mainWord |= FalloutMainFrameDeclaration.PermitsForcedQueue;
        var sequence = Next();
        var index = _mainWindows.Count;
        _mainWindows.Add(new(Guid.NewGuid(), consumers.Owner, full, before,
            full ? FalloutMainQueueFrameStep.WorkingContextFive : FalloutMainQueueFrameStep.FirstEntryWalk,
            false, sequence, sequence, null));
        _mainFrameExecuting = true;
        try
        {
            if (full) Enter(FalloutMainQueueFrameStep.WorkingContextFive, consumers.SetWorkingContextFive);
            Enter(FalloutMainQueueFrameStep.FirstEntryWalk, consumers.WalkFirstEntries);
            if (full)
            {
                Enter(FalloutMainQueueFrameStep.WorldPrelude, consumers.WorldPrelude);
                Enter(FalloutMainQueueFrameStep.TaskPrelude, consumers.TaskPrelude);
                Enter(FalloutMainQueueFrameStep.TaskContext, consumers.TaskContext);
                Enter(FalloutMainQueueFrameStep.PlayerCurrentCell, consumers.PlayerCurrentCell);
                Enter(FalloutMainQueueFrameStep.WorldFinal, consumers.WorldFinal);
            }
            Enter(FalloutMainQueueFrameStep.FirstChild, consumers.FirstChild);
            Enter(FalloutMainQueueFrameStep.SecondEntryWalk, consumers.WalkSecondEntries);
            Enter(FalloutMainQueueFrameStep.ArrayRelease, consumers.ReleaseArray);
            Enter(FalloutMainQueueFrameStep.FinalChild, consumers.FinalChild);
            _mainWord &= ~FalloutMainFrameDeclaration.PermitsForcedQueue;
            _mainWindows[index] = _mainWindows[index] with
            { Next = FalloutMainQueueFrameStep.Complete, ConsumerEntered = false, Changed = Next() };
        }
        catch (Exception error)
        {
            // The original does not execute its trailing clear on an entered
            // failing consumer. Neither disposal nor a later frame replays it.
            _mainWindows[index] = _mainWindows[index] with
            { Failure = _mainWindows[index].Failure ?? Message(error), Changed = Next() };
            throw;
        }
        finally { _mainFrameExecuting = false; }
        void Enter(FalloutMainQueueFrameStep step, Action call)
        {
            _mainWindows[index] = _mainWindows[index] with { Next = step, ConsumerEntered = true, Changed = Next() };
            var faults = _mainFrameReentry;
            call();
            if (faults != _mainFrameReentry) throw new InvalidOperationException("Main frame consumer caught a forbidden Main writer reentry.");
            _mainWindows[index] = _mainWindows[index] with { ConsumerEntered = false, Changed = Next() };
        }
    }
    internal void RetainUnownedMainFrame(string owner)
    {
        RequireNotBusy(); RequireMainWriterOutsideWindow(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        _mainFrameBoundary ??= owner;
    }
    private FalloutMainFrameSnapshot CaptureMainFrame()
    {
        if (_mainFrameExecuting) throw new NotSupportedException("Actual Main window consumer is still entered.");
        return new(MainFrameSchema, _mainFrameDeclaration.Contract, _stack, _process, _sequence,
            _mainWord, _mainWindows.ToArray(), _mainFrameBoundary, _mainFrameCold);
    }
    private void RequireMainWriterOutsideWindow()
    {
        if (!_mainFrameExecuting) return;
        _mainFrameReentry = checked(_mainFrameReentry + 1);
        throw new InvalidOperationException("Actual Main frame consumer cannot reenter a Main flag writer/window.");
    }
    private void RestoreMainFrame(FalloutMainFrameSnapshot saved)
    {
        ValidateMainFrame(saved, _source.ExecutableSha256);
        if (saved.Stack != _stack || saved.CapturedProcess == _process || saved.Sequence != _sequence)
            throw new InvalidDataException("Main frame continuation lost its actual runtime/new-process ownership.");
        _mainWord = saved.Word; _mainWindows.AddRange(saved.Windows); _mainFrameBoundary = saved.Boundary;
        _mainFrameCold = new(saved.CapturedProcess, _process, Next());
    }
    internal static void ValidateMainFrame(FalloutMainFrameSnapshot saved, string executable)
    {
        var declaration = FalloutMainFrameDeclaration.ForExecutable(executable);
        if (saved is null || saved.Schema != MainFrameSchema || saved.Contract != declaration.Contract ||
            string.IsNullOrWhiteSpace(saved.Stack) || saved.CapturedProcess == Guid.Empty || saved.Sequence < 0 ||
            saved.Windows is null || (saved.Word & ~11u) != 0 ||
            saved.Boundary is not null && string.IsNullOrWhiteSpace(saved.Boundary))
            throw new InvalidDataException("Current Main frame omitted its selected complete word/ordered windows.");
        long previous = 0; var active = false; var ids = new HashSet<Guid>();
        foreach (var window in saved.Windows)
        {
            if (window is null || window.Invocation == Guid.Empty || !ids.Add(window.Invocation) ||
                string.IsNullOrWhiteSpace(window.Owner) || !Enum.IsDefined(window.Next) || window.Entered <= previous ||
                window.Changed < window.Entered || window.Changed > saved.Sequence || (window.BeforeWord & ~3u) != 0 || active ||
                !window.Full && window.Next is (FalloutMainQueueFrameStep.WorkingContextFive or FalloutMainQueueFrameStep.WorldPrelude or
                    FalloutMainQueueFrameStep.TaskPrelude or FalloutMainQueueFrameStep.TaskContext or FalloutMainQueueFrameStep.PlayerCurrentCell or
                    FalloutMainQueueFrameStep.WorldFinal) ||
                window.Next == FalloutMainQueueFrameStep.Complete && (window.ConsumerEntered || window.Failure is not null) ||
                window.Next != FalloutMainQueueFrameStep.Complete && (!window.ConsumerEntered || string.IsNullOrWhiteSpace(window.Failure)))
                throw new InvalidDataException("Main frame lost its actual entered/returned/failing source prefix.");
            active = window.Next != FalloutMainQueueFrameStep.Complete;
            previous = window.Changed;
        }
        if (((saved.Word & FalloutMainFrameDeclaration.PermitsForcedQueue) != 0) != active)
            throw new InvalidDataException("Main queue-window word does not match its actual source clear/failure prefix.");
        if (saved.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != saved.CapturedProcess ||
            cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > saved.Sequence))
            throw new InvalidDataException("Main frame cold handoff has no genuine new process epoch.");
    }
    private static void ValidateRuntimeMainFrame(FalloutActorProcessRuntimeSnapshot saved)
    {
        var executable = FalloutMainFrameDeclaration.Executables.FirstOrDefault(image =>
            FalloutActorProcessRuntimeDeclaration.ForExecutable(image).Contract == saved.Contract) ??
            throw new InvalidDataException("Main word has no actual selected runtime source declaration.");
        ValidateMainFrame(saved.MainFrame, executable);
        var frame = saved.MainFrame;
        var initial = FalloutMainFrameDeclaration.ForExecutable(executable).ConstructorWord;
        var expectedFirstBit = saved.MainOperations.Any(value => value.Operation == FalloutMainProcessOperation.SourceWorldLoad &&
            value.Phase == FalloutMainProcessPhase.Complete) ? 0u : initial & 1u;
        if (frame.Stack != saved.Stack || frame.CapturedProcess != saved.CapturedProcess || frame.Sequence != saved.Sequence ||
            ((frame.Word & FalloutMainFrameDeclaration.ForcedProcessing) != 0) != saved.MainForcedProcessing ||
            (frame.Word & 1u) != expectedFirstBit || frame.Windows.Any(value => initial == 0 && (value.BeforeWord & 1u) != 0))
            throw new InvalidDataException("Main complete word differs from its actual constructor/runtime/source writer prefix.");
    }
}
