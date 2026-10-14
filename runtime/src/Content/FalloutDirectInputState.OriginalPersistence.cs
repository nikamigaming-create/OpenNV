namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutDirectInputState
{
    private FalloutOriginalInputSnapshot? _historicalInput;
    internal FalloutOriginalInputSnapshot? HistoricalSourceInput { get { RequireOwner(); return _historicalInput; } }
    internal FalloutOriginalInputSnapshot CaptureSourceInput()
    {
        RequireOwner(); RequireIdleOriginal();
        if (_originalSource is null || _originalConstruction is null || _originalHooks is null)
            throw new NotSupportedException("source-original-input-construction-absent");
        return new(_originalSource.Identity, Source, Factory, Provider, Window, _originalHooks.Lane,
            _bufferRevision, _pollSequence, _querySequence, Freeze(_engineKeyboard), Freeze(_previousKeyboard),
            Freeze(_engineMouse), Freeze(_previousMouse), Freeze(_modeFourMouse), Freeze(_controller), Freeze(_previousController),
            _swap, _doubleClickMilliseconds, _controllerSuppression, _menuWord,
            new(_keyboardKnown, _previousKeyboardKnown, _mouseKnown, _previousMouseKnown, _modeFourKnown,
                _swapKnown, _suppressionKnown, _menuKnown), _originalConstruction, _originalSelection, _originalPoll, _originalQuery, _originalWriter,
            _originalMenuWordWrites.ToArray(), _originalMenuClose, _originalMenuMouseFlushes.ToArray(), _originalMenuCloses.ToArray());
    }
    internal void RetainHistoricalSourceInput(FalloutOriginalInputSnapshot historical, Action<FalloutInputInvocation> requireHistorical)
    {
        RequireOriginal(); ArgumentNullException.ThrowIfNull(requireHistorical);
        FalloutOriginalInputValidation.ValidateSnapshot(_originalSource!, Source, historical);
        if (_historicalInput is not null || historical.Factory == Factory)
            throw new InvalidDataException("Cold input history must belong to a distinct prior manager, once.");
        _originalBusy = true; _originalFault = false;
        try
        {
            if (historical.Query is { } query) requireHistorical(query.Invocation);
            if (historical.UnownedWriter is { } writer) requireHistorical(writer.Invocation);
            CheckOriginalFault();
            // Keep history independently. Hardware/command state, factories,
            // taps and sequence numbers are always from the fresh provider.
            _historicalInput = historical with
            {
                Keyboard = Freeze(historical.Keyboard),
                PreviousKeyboard = Freeze(historical.PreviousKeyboard),
                Mouse = Freeze(historical.Mouse),
                PreviousMouse = Freeze(historical.PreviousMouse),
                ModeFourMouse = Freeze(historical.ModeFourMouse),
                Controller = Freeze(historical.Controller),
                PreviousController = Freeze(historical.PreviousController),
                Query = historical.Query is { } old ? old with { Observations = Array.AsReadOnly(old.Observations.ToArray()) } : null,
                MenuWordWrites = Array.AsReadOnly((historical.MenuWordWrites ?? []).ToArray()),
                MenuClose = historical.MenuClose is { } close ? close with { Observations = Array.AsReadOnly(close.Observations.ToArray()) } : null,
                MenuMouseFlushes = Array.AsReadOnly((historical.MenuMouseFlushes ?? []).ToArray())
            };
        }
        finally { _originalBusy = false; }
    }
    private static IReadOnlyList<byte> Freeze(IEnumerable<byte> bytes) => Array.AsReadOnly(bytes.ToArray());
}
