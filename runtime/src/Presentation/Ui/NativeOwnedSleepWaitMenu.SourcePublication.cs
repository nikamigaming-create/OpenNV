namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeSleepWaitMenuSource
{
    internal NativeSleepWaitMenuSource CreateCurrentPublication()
    {
        // Reuse the selected byte owner/cache, not mutable tile bindings from a
        // retired menu. Read validates the current winning bytes and expansion.
        var current = new NativeSleepWaitMenuSource(_records, Source, _time, _calendarCaption, _scriptUi);
        if (current.Identity != Identity || current.Source != Source || ReferenceEquals(current.Tiles, Tiles))
            throw new InvalidDataException("Rest menu publication changed its actual winning source or reused retired tile state.");
        return current;
    }
}
