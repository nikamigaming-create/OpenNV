using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutMainPlayerPendingState
{
    internal FalloutMainPlayerPendingSnapshot Capture()
    {
        if (_busy) { _callbackFault = checked(_callbackFault + 1); throw new NotSupportedException("Entered pending source child has no cold return receipt."); }
        if (_ownedChild is not null)
            throw new NotSupportedException("Source Player refcounted child requires its real typed native cold reconstruction owner.");
        var result = new FalloutMainPlayerPendingSnapshot(Schema, _source, _stack, _process, _sequence, null,
            _flags, _childMutation, _scalar, _callbacks, _lastCallback, _lastFurniture, _flagStores,
            _failureType, _error, _cold, _exteriorLoaders?.Capture(), CaptureCharacterController());
        Validate(result); return result;
    }
    private void Restore(FalloutMainPlayerPendingSnapshot saved)
    {
        Validate(saved);
        if (saved.Source != _source || saved.Stack != _stack || saved.CapturedProcess == _process)
            throw new InvalidDataException("Player pending cold owner differs from its selected source/current Main.");
        _characterControllerRestore = saved.CharacterController;
        _sequence = saved.Sequence; _flags = saved.Flags; _childMutation = saved.ChildMutation; _scalar = saved.Scalar;
        _callbacks = saved.Callbacks; _lastCallback = saved.LastCallbackRequest; _lastFurniture = saved.LastFurnitureRequest;
        _flagStores = saved.FlagStores; _failureType = saved.FailureType; _error = saved.Error;
        _cold = new(saved.CapturedProcess, _process, Next());
    }
    internal static void Validate(FalloutMainPlayerPendingSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Source is null || string.IsNullOrWhiteSpace(saved.Stack) ||
            saved.CapturedProcess == Guid.Empty || saved.Sequence < 0 || saved.OwnedChild is not null ||
            saved.Callbacks < 0 || saved.FlagStores < 0 || (saved.Flags & ~1) != 0 ||
            (saved.FlagStores == 0) != ((saved.Flags & 1) == 0) ||
            (saved.Error is null) != (saved.FailureType is null) || saved.Error is { Length: 0 } ||
            saved.LastCallbackRequest == Guid.Empty || saved.LastFurnitureRequest == Guid.Empty ||
            saved.Callbacks > 0 && saved.LastCallbackRequest is null)
            throw new InvalidDataException("Player pending snapshot omitted mandatory source field/lifetime state.");
        saved.Source.Validate();
        if (saved.Source.Player.Main.HasNewVegasChildren)
        {
            var loaders = saved.ExteriorLoaders ?? throw new InvalidDataException("Source NV pending reset omitted its actual loader map.");
            FalloutExteriorCellLoaderState.Validate(loaders, saved.Source);
            if (loaders.Stack != saved.Stack || loaders.Process != saved.CapturedProcess)
                throw new InvalidDataException("Pending reset and Player fields belong to different actual source processes.");
        }
        else if (saved.ExteriorLoaders is not null || saved.Scalar is not null || saved.CharacterController is not null)
            throw new InvalidDataException("FO3 pending continuation imported a different source loader map or outer scalar arm.");
        if (saved.ChildMutation is { } mutation && (mutation.Changed < 1 || mutation.Changed > saved.Sequence ||
            mutation.Before == Guid.Empty || mutation.After == Guid.Empty || string.IsNullOrWhiteSpace(mutation.Owner) ||
            mutation.Returned == (mutation.Error is not null) || (mutation.Error is null) != (mutation.FailureType is null) ||
            mutation.After is not null && mutation.Returned))
            throw new InvalidDataException("Pending child snapshot fabricated a refcount/null-store completion.");
        if (saved.Scalar is { } scalar && (scalar.Main == Guid.Empty || scalar.Request == Guid.Empty ||
            scalar.ControllerOwner == Guid.Empty || scalar.SourceProcess == Guid.Empty || scalar.ProcessEpoch < 1 ||
            scalar.Changed < 1 || scalar.Changed > saved.Sequence || string.IsNullOrWhiteSpace(scalar.Owner)))
            throw new InvalidDataException("Scalar receipt omitted its actual source High-process character-controller field owner.");
        ValidateCharacterController(saved);
        if (saved.Handoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != saved.CapturedProcess ||
            cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > saved.Sequence))
            throw new InvalidDataException("Player pending snapshot lost its actual new-process handoff.");
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_busy) { _callbackFault = checked(_callbackFault + 1); throw new InvalidOperationException("Live pending child must return before its source provider retires."); }
        var failures = new List<Exception>();
        if (_ownedChild is not null)
        {
            try
            {
                if (_childMutation is not { Returned: true })
                    throw new InvalidOperationException("Failed source refcount operation needs its actual remaining-ownership receipt before cleanup can retry.");
                _busy = true;
                try { AssignOwnedChild(null, "actual-selected-Player-destructor-owned-child-release"); }
                finally { _busy = false; }
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        try { _exteriorLoaders?.Retire(); }
        catch (Exception failure) { failures.Add(failure); }
        try { _characterController?.Retire(); }
        catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0)
            throw new AggregateException("Player pending retirement retains every independent still-owned child.", failures);
        _retired = true;
        // Source failure and consumed fields remain diagnostic state. Retiring
        // a null child is not a fabricated successful pending payload store.
    }
}
