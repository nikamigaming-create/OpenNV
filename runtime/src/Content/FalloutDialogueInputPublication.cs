namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutDialogueInputToken(ulong View, ulong Rows, ulong Response, string Phase);

// A native signal belongs to the source view/rows that actually created it.
// Publication, focus and input delivery do not complete a response or result.
internal sealed class FalloutDialogueInputPublication
{
    internal FalloutDialogueInputToken Current { get; private set; }
    internal bool Submitted { get; private set; } = true;
    private bool _retired;

    internal void Publish(string phase, ulong response)
    {
        if (_retired) throw new InvalidOperationException("A retired dialogue view cannot accept another source publication.");
        if (phase is not ("speaking" or "choices" or "continuing") || phase == "speaking" && response == 0)
            throw new InvalidDataException("Dialogue input has no current source response/choice publication.");
        Current = new(checked(Current.View + 1), 0, response, phase);
        Submitted = phase == "continuing";
    }
    internal FalloutDialogueInputToken RebuildRows()
    {
        if (!_retired && Current.View != 0)
            Current = Current with { Rows = checked(Current.Rows + 1) };
        return Current;
    }
    internal bool IsCurrent(FalloutDialogueInputToken token) => !_retired && !Submitted && Current.View != 0 && token == Current;
    internal bool TrySubmit(FalloutDialogueInputToken token, bool speaking)
    {
        if (!IsCurrent(token) || Current.Phase != (speaking ? "speaking" : "choices")) return false;
        // Commit delivery before the callback. A throwing effect cannot retry
        // this native view or dispatch the same input into a later source view.
        Submitted = true;
        return true;
    }
    internal void Retire() { _retired = true; Submitted = true; }
}
