namespace OpenNV.Runtime.Content;

internal sealed record FalloutSandboxIdleSelectionSnapshot(int Cursor, FalloutFormKey? SelectedIdle)
{
    internal void Validate()
    {
        if (Cursor < -1 || (Cursor == -1) != (SelectedIdle is null) ||
            SelectedIdle is { } idle && (string.IsNullOrWhiteSpace(idle.OwnerPlugin) ||
                idle.ObjectId is 0 or > FalloutFormKey.ObjectIdMask))
            throw new InvalidDataException("Saved Sandbox idle selection has an invalid process cursor or IDLE identity.");
    }
}

// One actor process owns this selection across marker changes. Admission and
// fresh authored-order eligibility belong to the caller; this is not patrol
// collection playback and does not implement IDLT waits or DoOnce completion.
internal sealed class FalloutSandboxIdleSelection
{
    internal int Cursor { get; private set; } = -1;
    internal FalloutFormKey? SelectedIdle { get; private set; }

    internal FalloutSandboxIdleSelectionSnapshot Capture() => new(Cursor, SelectedIdle);

    internal void Restore(FalloutSandboxIdleSelectionSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (Cursor != -1 || SelectedIdle is not null)
            throw new InvalidOperationException("Sandbox idle selection restoration requires a fresh actor process.");
        saved.Validate();
        Cursor = saved.Cursor; SelectedIdle = saved.SelectedIdle;
    }

    internal bool TrySelect(byte idleFlags, IReadOnlyList<FalloutFormKey> eligible,
        Func<uint> random, Action<FalloutFormKey> playback)
    {
        ArgumentNullException.ThrowIfNull(eligible);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(playback);
        // No selection means no process-state or random-stream mutation. In
        // particular a temporarily empty list retains the prior selected IDLE.
        if (eligible.Count == 0) return false;
        var cursor = (idleFlags & 1) != 0
            ? Cursor < 0 || Cursor >= eligible.Count - 1 ? 0 : Cursor + 1
            : (int)(random() % (uint)eligible.Count);
        var selected = eligible[cursor];
        if (string.IsNullOrWhiteSpace(selected.OwnerPlugin) || selected.ObjectId is 0 or > FalloutFormKey.ObjectIdMask)
            throw new InvalidDataException("Sandbox selected idle has no source identity.");
        SelectedIdle = null;
        Cursor = cursor;
        SelectedIdle = selected;
        // Source selection commits before the playback request. A refusal must
        // not roll back the selected pointer/cursor or repeat the random draw.
        playback(selected);
        return true;
    }
}
