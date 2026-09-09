namespace OpenNV.Runtime.Content;

internal sealed record FalloutMessageRequest(FalloutFormKey Form, FalloutFormKey Caller, ulong Sequence);
internal sealed record FalloutMessageResultsSnapshot(ulong Sequence, FalloutFormKey? Caller, int Button);

// ShowMessage and GetButtonPressed share one consumptive result slot. Object
// calls are owned by the reference; a quest/player call uses the executing SCPT.
// A later message replaces the slot. Another caller cannot consume its result.
internal sealed class FalloutMessageResults
{
    private ulong _sequence;
    private FalloutFormKey? _caller;
    private int _button = -1;

    internal FalloutMessageRequest Begin(FalloutFormKey form, FalloutFormKey caller)
    {
        _sequence = checked(_sequence + 1);
        _caller = caller;
        _button = -1;
        return new(form, caller, _sequence);
    }

    internal bool Select(FalloutMessageRequest request, int button)
    {
        if (button is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(button));
        if (request.Sequence != _sequence || request.Caller != _caller) return false;
        _button = button;
        return true;
    }

    internal int Take(FalloutFormKey caller)
    {
        if (caller != _caller || _button < 0) return -1;
        var result = _button;
        _caller = null;
        _button = -1;
        return result;
    }

    internal FalloutMessageResultsSnapshot Capture() => new(_sequence, _caller, _button);
    internal void Restore(FalloutMessageResultsSnapshot snapshot)
    {
        if (_sequence != 0) throw new InvalidOperationException("Message restoration needs a fresh owner.");
        if (snapshot.Button is < -1 or > 9 || snapshot.Button >= 0 && snapshot.Caller is null ||
            snapshot.Sequence == 0 && (snapshot.Caller is not null || snapshot.Button != -1))
            throw new InvalidDataException("Saved message result is invalid.");
        _sequence = snapshot.Sequence; _caller = snapshot.Caller; _button = snapshot.Button;
    }
}
