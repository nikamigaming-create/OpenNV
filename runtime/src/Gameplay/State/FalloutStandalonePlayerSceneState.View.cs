using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// The loader and both frame toggles write the existing Scene _view field.
// Each retains one bounded latest span; cold publication lineage is independent.
internal sealed partial class FalloutStandalonePlayerSceneState
{
    private readonly List<FalloutPlayerViewPublication> _viewPublications = [];
    private FalloutPlayerViewSpan? _animationLoadView, _tailView;
    private FalloutPlayerViewStore? _lastViewWrite;
    private FalloutPlayerPostAnimationSource? _viewSource;
    private bool _viewWriting;
    private string? ViewSaveBlocker => _viewWriting ? "source-Player-view-write-in-flight" : _animationLoadView is { Second: null }
        ? "source-Player-animation-load-temporary-view-unrestored" : _tailView is { Second: null }
        ? "source-Player-tail-second-view-toggle-unreached" : null;

    private void ConstructViewOwner(FalloutStandalonePlayerSceneSnapshot? saved)
    {
        if (saved?.ViewFields is { } fields)
        {
            _viewPublications.AddRange(fields.Publications);
            _animationLoadView = fields.Load; _tailView = fields.Tail; _lastViewWrite = fields.LastWrite;
        }
        _viewPublications.Add(new(_process, _constructor, saved is null ? 0 : _sequence, _view,
            saved?.CapturedProcess, saved?.Constructor));
    }

    internal FalloutPlayerViewRead ReadAnimationLoadView(FalloutPlayerPostAnimationSource source,
        FalloutReferenceModelDataInvocation invocation, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        CheckLoad(); var read = new FalloutPlayerViewRead(Source.Identity, _stack, _process, _constructor,
            _sequence, _view, Load: invocation); CheckLoad(); return read;
        void CheckLoad() => RequireLoadView(source, invocation, requireActual);
    }

    internal FalloutPlayerViewStore EnterAnimationLoadView(FalloutPlayerPostAnimationSource source,
        FalloutReferenceModelDataInvocation invocation, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireLoadView(source, invocation, requireActual);
        if (_animationLoadView is { Second: null } || _animationLoadView?.Initial.Load?.Call == invocation.Call)
            throw new InvalidOperationException("Player load replayed or crossed its unreturned temporary-view span.");
        var before = ReadAnimationLoadView(source, invocation, requireActual);
        return Write(() => RequireLoadView(source, invocation, requireActual), () =>
        {
            var store = Commit(invocation.Call, FalloutPlayerViewWriter.LoadTemporary, before, 1);
            _animationLoadView = new(before, store); return store;
        });
    }

    internal FalloutPlayerViewStore RestoreAnimationLoadView(FalloutPlayerPostAnimationSource source,
        FalloutReferenceModelDataInvocation invocation, FalloutPlayerViewStore actualEntry,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireLoadView(source, invocation, requireActual);
        if (_animationLoadView is not { Second: null } span || span.First != actualEntry ||
            span.Initial.Load != invocation || actualEntry.Writer != FalloutPlayerViewWriter.LoadTemporary)
            throw new InvalidOperationException("Player load replaced its actual saved view or replayed restoration.");
        var current = ReadAnimationLoadView(source, invocation, requireActual);
        return Write(() => RequireLoadView(source, invocation, requireActual), () =>
        {
            var store = Commit(invocation.Call, FalloutPlayerViewWriter.LoadRestore, current, span.Initial.Value);
            _animationLoadView = span with { Second = store }; return store;
        });
    }

    internal FalloutPlayerViewRead ReadTailView(FalloutPlayerPostAnimationSource source,
        FalloutPlayerLateFrameInvocation frame, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        CheckFrame(); var read = new FalloutPlayerViewRead(Source.Identity, _stack, _process, _constructor,
            _sequence, _view, Frame: frame); CheckFrame(); return read;
        void CheckFrame() => RequireTailView(source, frame, requireActual);
    }

    internal FalloutPlayerViewStore StoreFirstTailViewToggle(FalloutPlayerPostAnimationSource source,
        FalloutPlayerLateFrameInvocation frame, Guid consumer, FalloutPlayerViewRead initial,
        FalloutPlayerViewRead current, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequireTailView(source, frame, requireActual); RequireCurrentRead(current, frame);
        if (consumer == Guid.Empty || initial.Frame != frame || initial.Load is not null || initial.Source != Source.Identity ||
            initial.Process != _process || initial.Constructor != _constructor || initial.Stack != _stack ||
            initial.Sequence > current.Sequence || _tailView is { Second: null } ||
            _tailView?.Initial.Frame is { } previous && (previous.PlayerProcess == frame.PlayerProcess &&
                previous.Caller.Ordinal >= frame.Caller.Ordinal || previous.Caller.Main == frame.Caller.Main))
            throw new InvalidOperationException("Player tail replaced or replayed its actual first-view read/span.");
        return Write(() => RequireTailView(source, frame, requireActual), () =>
        {
            var store = Commit(consumer, FalloutPlayerViewWriter.FirstTailToggle, current, Toggle(current.Value));
            _tailView = new(initial, store); return store;
        });
    }

    internal FalloutPlayerViewStore StoreSecondTailViewToggle(FalloutPlayerPostAnimationSource source,
        FalloutPlayerLateFrameInvocation frame, Guid consumer, FalloutPlayerViewRead current,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequireTailView(source, frame, requireActual); RequireCurrentRead(current, frame);
        if (_tailView is not { Second: null } span || span.Initial.Frame != frame ||
            span.First.Consumer != consumer || current.Sequence < span.First.Changed)
            throw new InvalidOperationException("Player tail replaced its actual first toggle or replayed the second.");
        return Write(() => RequireTailView(source, frame, requireActual), () =>
        {
            var store = Commit(consumer, FalloutPlayerViewWriter.SecondTailToggle, current, Toggle(current.Value));
            _tailView = span with { Second = store }; return store;
        });
    }

    private FalloutPlayerViewStore Write(Action check, Func<FalloutPlayerViewStore> commit)
    {
        if (_viewWriting)
        { _reentry = checked(_reentry + 1); throw new InvalidOperationException("Actual Player view writer reentered."); }
        var fault = _reentry; _viewWriting = true;
        try
        {
            check(); var result = commit(); check();
            if (_reentry != fault) throw new InvalidOperationException("Player view writer swallowed actual owner reentry.");
            return result;
        }
        finally { _viewWriting = false; }
    }
    private FalloutPlayerViewStore Commit(Guid consumer, FalloutPlayerViewWriter writer, FalloutPlayerViewRead read, byte value)
    {
        _view = value;
        return _lastViewWrite = new(Source.Identity, consumer, writer, read, Next(), value);
    }
    private void RequireCurrentRead(FalloutPlayerViewRead read, FalloutPlayerLateFrameInvocation frame)
    {
        if (read.Source != Source.Identity || read.Stack != _stack || read.Process != _process || read.Constructor != _constructor ||
            read.Frame != frame || read.Load is not null || read.Sequence > _sequence || read.Value != _view)
            throw new InvalidOperationException("Player toggle changed its exact fresh current Scene view read.");
    }
    private void RequireLoadView(FalloutPlayerPostAnimationSource source, FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(requireActual); RequireViewSource(source); requireActual(invocation); RequireThread();
        if (source.Tail.Scene != Source || _retired || invocation.Call == Guid.Empty ||
            invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || invocation.ReferenceProcess != _process ||
            invocation.ReferenceConstructor != _constructor || invocation.CommonProcess == Guid.Empty || !invocation.Actor.EnginePlayer ||
            invocation.Epoch < 1 || invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Temporary view writer lost its actual outer Player load/source/thread scope.");
    }
    private void RequireTailView(FalloutPlayerPostAnimationSource source, FalloutPlayerLateFrameInvocation frame,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(requireActual); RequireViewSource(source); requireActual(frame); RequireThread();
        if (source.Tail.Scene != Source || _retired || frame.PlayerProcess != _process || frame.PlayerConstructor != _constructor ||
            frame.ThreadId != Environment.CurrentManagedThreadId || frame.Prelude == Guid.Empty ||
            _clock is not { Step: FalloutStandaloneSceneClockStep.VirtualEntered } clock ||
            clock.Main != frame.Caller.Main || clock.MainOrdinal != frame.Caller.Ordinal)
            throw new InvalidOperationException("Tail view writer lost its actual entered Scene/Main/Player scope.");
    }
    private void RequireViewSource(FalloutPlayerPostAnimationSource source)
    {
        if (_viewSource is null)
        {
            source.Validate();
            if (source.Tail.Scene != Source) throw new InvalidDataException("View writer changed its selected Scene source.");
            _viewSource = source;
        }
        else if (_viewSource != source) throw new InvalidDataException("View writer replaced its actual selected source.");
    }
    private static byte Toggle(byte value) => value == 0 ? (byte)1 : (byte)0;
    internal FalloutPlayerViewSnapshot CaptureViewFields()
    {
        RequireThread();
        if (_viewWriting)
        { _reentry = checked(_reentry + 1); throw new InvalidOperationException("View capture reentered its actual writer."); }
        return new(Source.Identity, _stack, _process, _constructor, _sequence,
            _view, _viewPublications.ToArray(), _animationLoadView, _tailView, _lastViewWrite);
    }
}
