using System.ComponentModel;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginProductWindow
{
    internal void RequireColdCapture()
    {
        Require();
        if (_enumerationEntered || _attachments.Count != 0 || _currentClip is not null || _keyboardConversionEntered)
            throw new NotSupportedException("Current native input queues/cursor ownership cannot be restored from a cold pointer snapshot.");
    }
    internal void RetireAfterChildExit()
    {
        RequireThread();
        if (_retired) return;
        if (_enumerationEntered) throw new InvalidOperationException("An actual original window callback still owns the enumeration stack.");
        // Child closure itself removes its thread's attachment. Do not call
        // AttachThreadInput using a now-reusable numeric thread identifier.
        if (_attachments.Count != 0)
            throw new InvalidOperationException("Native input attachments lacked actual live-thread retirement before child closure.");
        RestoreClip();
        if (_readers != 0) throw new InvalidOperationException("Native window readers remain after verified child closure.");
        _retired = true; _layouts.Clear();
    }
    internal void RetireBeforeChildExit(uint childThread)
    {
        Require(); if (_enumerationEntered) throw new InvalidOperationException("An actual enumeration callback prevents queue retirement.");
        List<Exception> failures = [];
        foreach (var row in _attachments.Keys.ToArray())
        {
            if ((row.Item1 != Thread && row.Item1 != childThread) || (row.Item2 != Thread && row.Item2 != childThread))
            { failures.Add(new InvalidDataException("Input retirement lost its exact living thread identities.")); continue; }
            if (AttachThreadInput(row.Item1, row.Item2, false) == 0) { failures.Add(new Win32Exception(unchecked((int)GetLastError()), "Actual native queue detachment failed.")); continue; }
            _attachments.Remove(row);
        }
        try { RestoreClip(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Independent source/window retirement failed.", failures);
    }
    private void RestoreClip()
    {
        if (_currentClip is not { } owned) return;
        if (!GetClipCursor(out var current)) throw new Win32Exception(unchecked((int)GetLastError()));
        if (current != owned) throw new InvalidOperationException("Cursor clip changed under another actual writer; retirement cannot overwrite it.");
        var previous = _previousClip ?? throw new InvalidDataException("Source cursor clip has no retained prior SDK observation.");
        if (!ClipCursor(ref previous)) throw new Win32Exception(unchecked((int)GetLastError()), "Actual prior cursor clip did not restore.");
        _currentClip = _previousClip = null;
    }
}
