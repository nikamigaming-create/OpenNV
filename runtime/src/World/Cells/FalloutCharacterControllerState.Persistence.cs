using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCharacterControllerState
{
    internal FalloutCharacterControllerSnapshot Capture()
    {
        Require(); if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        var faults = _reentry; _busy = true;
        try
        {
            RequireBody().VerifyCurrent();
            if (_reentry != faults) throw new InvalidOperationException("Controller capture swallowed an actual owner reentry refusal.");
            var result = new FalloutCharacterControllerSnapshot(Schema, _factory, Identity, _scalarBits, _sequence,
                _lastStore, _failureType, _error, _handoffs.ToArray());
            Validate(result); return result;
        }
        finally { _busy = false; }
    }

    private void Restore(FalloutCharacterControllerSnapshot saved)
    {
        Validate(saved);
        if (saved.Factory.Source != _factory.Source || saved.Factory.Stack != _factory.Stack ||
            saved.Factory.Process == _factory.Process || saved.Identity == Identity || saved.Factory.ProcessEpoch != ProcessEpoch ||
            saved.Factory.Actor != _factory.Actor || saved.Factory.Level != _factory.Level ||
            !FalloutActorProcessCommonState.BodyEquivalent(saved.Factory.Body, _factory.Body))
            throw new InvalidDataException("Cold character controller changed source winner/body or reused a native process epoch.");
        _scalarBits = saved.ScalarBits; _sequence = saved.Sequence; _lastStore = saved.LastStore;
        _failureType = saved.FailureType; _error = saved.Error;
        _handoffs.AddRange(saved.Handoffs);
        _handoffs.Add(new(saved.Factory.Process, SourceProcess, saved.Identity, Identity, Next()));
    }

    internal static void RequireFactory(FalloutCharacterControllerFactory factory)
    {
        if (factory is null || factory.Source is null || string.IsNullOrWhiteSpace(factory.Stack) || factory.Process == Guid.Empty ||
            factory.ProcessEpoch < 1 || factory.Level is not (FalloutDetectionProcessLevel.MiddleHigh or FalloutDetectionProcessLevel.High) ||
            factory.Actor is null || factory.Body is null || string.IsNullOrWhiteSpace(factory.Owner))
            throw new InvalidDataException("Character controller has no actual source MiddleHigh/High factory/body epoch.");
        factory.Source.Validate(); factory.Actor.Validate();
        FalloutActorProcessCommonState.RequireBody(factory.Body, factory.Actor.Reference);
        if (factory.Actor.EnginePlayer && factory.Actor.ReferenceSha256 != factory.Source.Pending.Player.Main.EngineSha256)
            throw new InvalidDataException("Character controller factory changed its selected Player source.");
    }

    internal static void Validate(FalloutCharacterControllerSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Identity == Guid.Empty || saved.Sequence < 0 ||
            (saved.Error is null) != (saved.FailureType is null) || saved.Error is { Length: 0 } || saved.FailureType is { Length: 0 } ||
            saved.Handoffs is null)
            throw new InvalidDataException("Character controller snapshot omitted its actual constructor/field lifetime.");
        RequireFactory(saved.Factory);
        if (saved.LastStore is { } store && (store.Main == Guid.Empty || store.Request == Guid.Empty ||
            store.Changed < 1 || store.Changed > saved.Sequence || store.Bits != saved.ScalarBits || string.IsNullOrWhiteSpace(store.Owner) ||
            store.Process == Guid.Empty || store.Controller == Guid.Empty || store.ProcessEpoch != saved.Factory.ProcessEpoch) ||
            saved.LastStore is null && saved.ScalarBits != 0)
            throw new InvalidDataException("Character controller field lacks its actual original zero constructor or consumed source store.");
        var seenProcesses = new HashSet<Guid>();
        FalloutCharacterControllerHandoff? previous = null;
        foreach (var handoff in saved.Handoffs)
        {
            if (handoff is null || handoff.PreviousProcess == Guid.Empty || handoff.CurrentProcess == Guid.Empty ||
                handoff.PreviousProcess == handoff.CurrentProcess || handoff.PreviousController == Guid.Empty || handoff.CurrentController == Guid.Empty ||
                handoff.PreviousController == handoff.CurrentController || handoff.Changed < 1 || handoff.Changed > saved.Sequence ||
                !seenProcesses.Add(handoff.PreviousProcess) || previous is not null &&
                (previous.CurrentProcess != handoff.PreviousProcess || previous.CurrentController != handoff.PreviousController || previous.Changed >= handoff.Changed))
                throw new InvalidDataException("Character controller cold continuation lost a genuine source/process field handoff.");
            previous = handoff;
        }
        if (previous is not null && (previous.CurrentProcess != saved.Factory.Process || previous.CurrentController != saved.Identity ||
            seenProcesses.Contains(previous.CurrentProcess)))
            throw new InvalidDataException("Character controller cold chain reused or lost its actual current factory.");
        if (saved.LastStore is { } last && (last.Process != saved.Factory.Process || last.Controller != saved.Identity) &&
            !saved.Handoffs.Any(handoff => handoff.PreviousProcess == last.Process && handoff.PreviousController == last.Controller && handoff.Changed > last.Changed))
            throw new InvalidDataException("Retained controller scalar has no actual source-store-to-current-process handoff.");
    }
}
