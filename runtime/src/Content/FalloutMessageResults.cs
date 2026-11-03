namespace OpenNV.Runtime.Content;

internal sealed record FalloutMessageRequest(FalloutFormKey Form, FalloutFormKey Caller, ulong Sequence);
internal sealed record FalloutMessageResultsSnapshot(ulong Sequence, FalloutFormKey? Caller, int Button, ulong? OwnerSequence = null);

// ShowMessage and GetButtonPressed share one consumptive result slot. Object
// calls are owned by the reference; a quest/player call uses the executing SCPT.
// A later message replaces the result owner, independently of modal FIFO.
// Native UI callbacks publish a button globally; another caller cannot take it.
internal sealed class FalloutMessageResults
{
    private ulong _sequence, _ownerSequence;
    private FalloutFormKey? _caller;
    private int _button = -1;

    internal FalloutMessageRequest Begin(FalloutFormKey form, FalloutFormKey caller)
    {
        _sequence = checked(_sequence + 1);
        _caller = caller;
        _ownerSequence = _sequence;
        _button = -1;
        return new(form, caller, _sequence);
    }

    internal bool Select(FalloutMessageRequest request, int button)
    {
        if (button is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(button));
        if (!IsPending(request)) return false;
        _button = button;
        return true;
    }

    internal bool IsPending(FalloutMessageRequest request) =>
        request.Sequence == _ownerSequence && request.Caller == _caller && _button == -1;

    internal int Take(FalloutFormKey caller)
    {
        if (caller != _caller || _button < 0) return -1;
        var result = _button;
        _caller = null;
        _ownerSequence = 0;
        _button = -1;
        return result;
    }

    // The actual MessageMenu callback has no script/request argument. It writes
    // the shared button even if an earlier queue entry was the visible menu.
    internal void PublishButton(int button)
    {
        if (button is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(button));
        if (_sequence == 0) throw new InvalidOperationException("Message input has no admitted source request.");
        _button = button;
    }

    internal void CommitPrepared(FalloutMessageResultsSnapshot before, IReadOnlyList<FalloutMessageRequest> requests)
    {
        if (Capture() != before) throw new InvalidOperationException("Prepared message result owner changed before publication.");
        var expected = before.Sequence;
        foreach (var request in requests)
            if (request.Sequence != checked(++expected))
                throw new InvalidDataException("Prepared messages lost source sequence order.");
        if (requests.Count == 0) return;
        var last = requests[^1];
        _sequence = _ownerSequence = last.Sequence; _caller = last.Caller; _button = -1;
    }

    internal void RollbackPrepared(FalloutMessageResultsSnapshot before, ulong lastOwned)
    {
        // Preserve a later source request/result. Never reuse an exposed ID.
        if (_ownerSequence != lastOwned) return;
        _caller = before.Caller; _button = before.Button;
        _ownerSequence = before.OwnerSequence ?? (before.Caller is null ? 0 : before.Sequence);
    }

    internal FalloutMessageResultsSnapshot Capture() => new(_sequence, _caller, _button, _ownerSequence);
    internal void Restore(FalloutMessageResultsSnapshot snapshot)
    {
        if (_sequence != 0) throw new InvalidOperationException("Message restoration needs a fresh owner.");
        var owner = snapshot.OwnerSequence ?? (snapshot.Caller is null ? 0 : snapshot.Sequence);
        if (snapshot.Button is < -1 or > 9 || owner > snapshot.Sequence ||
            (snapshot.Caller is null) != (owner == 0) ||
            snapshot.Sequence == 0 && (snapshot.Caller is not null || snapshot.Button != -1))
            throw new InvalidDataException("Saved message result is invalid.");
        _sequence = snapshot.Sequence; _ownerSequence = owner; _caller = snapshot.Caller; _button = snapshot.Button;
    }
}
