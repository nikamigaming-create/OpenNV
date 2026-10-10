using System.Runtime.ExceptionServices;

namespace OpenNV.Runtime.Gameplay.State;

// This is the current native presentation's process-local update lifetime.
// Ending it is not an XP phase/sound/frame completion receipt.
internal sealed class FalloutExperiencePresentationLifetime
{
    private ExceptionDispatchInfo? _failure;
    private ExceptionDispatchInfo? _retirementFailure;
    internal string? Cancellation { get; private set; }
    internal bool RetirementEntered { get; private set; }
    internal bool RetirementReturned { get; private set; }
    internal string? Failure => _failure?.SourceException.ToString();
    internal string? RetirementFailure => _retirementFailure?.SourceException.ToString();

    internal bool BeginUpdate(Func<string?> currentCancellation, Action retireNative)
    {
        ArgumentNullException.ThrowIfNull(currentCancellation);
        ArgumentNullException.ThrowIfNull(retireNative);
        _failure?.Throw();
        if (RetirementEntered) return false;
        string? cancellation;
        try { cancellation = currentCancellation(); }
        catch (Exception error) { _failure ??= ExceptionDispatchInfo.Capture(error); throw; }
        if (cancellation is null) return true;
        Retire(cancellation, retireNative);
        return false;
    }

    internal void Retire(string cancellation, Action retireNative)
    {
        ArgumentNullException.ThrowIfNull(retireNative);
        if (RetirementEntered) { _retirementFailure?.Throw(); return; }
        if (string.IsNullOrWhiteSpace(cancellation))
            throw new InvalidDataException("XP presentation retirement has no actual caller cause.");
        Cancellation = cancellation;
        RetirementEntered = true;
        try { retireNative(); RetirementReturned = true; }
        catch (Exception error)
        {
            _retirementFailure = ExceptionDispatchInfo.Capture(error);
            _failure ??= _retirementFailure;
            throw;
        }
    }
}
