namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutMainPlayerPendingState
{
    private FalloutCharacterControllerState? _characterController;
    private FalloutCharacterControllerSnapshot? _characterControllerRestore;
    private string? CharacterControllerSaveBlocker => _characterControllerRestore is not null ?
        "source-character-controller-cold-actual-factory-body-not-rebound" : _characterController?.SaveBlocker;

    internal FalloutCharacterControllerState BindCharacterController(IFalloutCharacterControllerBody body)
    {
        Require(); ArgumentNullException.ThrowIfNull(body);
        if (body.Factory.Process != _process || body.Factory.Source.Pending != _source || body.Factory.Stack != _stack ||
            !body.Factory.Actor.EnginePlayer)
            throw new InvalidDataException("Pending Player scalar child has a foreign source controller factory.");
        if (_characterController is not null)
        {
            _characterController.Rebind(body); return _characterController;
        }
        var candidate = new FalloutCharacterControllerState(body, _characterControllerRestore);
        _characterController = candidate; _characterControllerRestore = null;
        return candidate;
    }

    internal IFalloutPlayerTransferController RequireCharacterController(FalloutMainPlayerCellInvocation invocation)
    {
        invocation.Require(FalloutMainPlayerCellStep.PendingSceneScalar);
        var current = _characterController ?? throw new NotSupportedException("Actual source High controller factory/body is absent.");
        if (current.SourceProcess != _process || current.Factory.Source.Pending != _source || current.SaveBlocker is { })
            throw new NotSupportedException("Actual source High controller is retired, faulted or awaiting its genuine body rebind.");
        return current;
    }

    private FalloutCharacterControllerSnapshot? CaptureCharacterController()
    {
        if (_characterControllerRestore is not null)
            throw new NotSupportedException("Cold scalar fields require actual new-process character controller construction before capture.");
        return _characterController?.Capture();
    }

    private static void ValidateCharacterController(FalloutMainPlayerPendingSnapshot saved)
    {
        if (saved.CharacterController is { } controller)
        {
            FalloutCharacterControllerState.Validate(controller);
            if (controller.Factory.Process != saved.CapturedProcess || controller.Factory.Stack != saved.Stack ||
                controller.Factory.Source.Pending != saved.Source || !controller.Factory.Actor.EnginePlayer)
                throw new InvalidDataException("Pending Player and character controller differ in actual source/process lifetime.");
        }
        if (saved.Scalar is not { } scalar) return;
        var current = saved.CharacterController ?? throw new InvalidDataException("Pending scalar omitted its genuine character-controller field owner.");
        if (scalar.Bits != current.ScalarBits || scalar.ProcessEpoch != current.Factory.ProcessEpoch ||
            current.LastStore is not { } store || scalar.Main != store.Main || scalar.Request != store.Request || scalar.Bits != store.Bits ||
            scalar.ControllerOwner != store.Controller || scalar.SourceProcess != store.Process)
            throw new InvalidDataException("Pending scalar receipt and its actual current/cold controller field disagree.");
    }
}
