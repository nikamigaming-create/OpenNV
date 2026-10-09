using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Lives on the actual Godot window/message thread. USER32 virtual-key bytes
// are never derived from DirectInput scan codes, Godot edges or guessed zeros.
internal sealed partial class NativePluginProductWindow
{
    private readonly int _managedThread = Environment.CurrentManagedThreadId;
    private readonly FalloutDirectInputState _input;
    internal string SourceIdentity => _input.Source;
    internal uint Window { get; }
    internal uint Thread { get; }
    internal uint Process { get; }
    private readonly HashSet<uint> _layouts = [];
    private readonly HashSet<uint> _enumerating = [];
    private readonly Dictionary<(uint, uint), bool> _attachments = [];
    private Rect? _previousClip, _currentClip;
    private int _readers;
    private bool _retired;
    private bool _enumerationEntered;
    private bool _keyboardConversionEntered;

    internal NativePluginProductWindow(FalloutDirectInputState input)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Original window imports require real Windows.");
        _input = input; input.RequireCurrent(); Window = HandleBits((nint)input.Window);
        Thread = GetWindowThreadProcessId(Pointer(Window), out var process); Process = process;
        if (Window == 0 || Thread == 0 || process != Environment.ProcessId || Thread != GetCurrentThreadId() || !IsWindow(Pointer(Window)))
            throw new InvalidDataException("Native window is not the actual living Godot product/message thread.");
    }
    internal IDisposable Retain()
    {
        Require(); var inputLease = _input.RetainNativeReader(); ++_readers;
        return new ReaderLease(this, inputLease);
    }
    private sealed class ReaderLease(NativePluginProductWindow owner, IDisposable input) : IDisposable
    {
        private bool _released;
        public void Dispose()
        {
            if (_released) return;
            owner.RequireThread(); if (owner._readers <= 0) throw new InvalidOperationException("Window readers are unbalanced.");
            input.Dispose(); --owner._readers; _released = true;
        }
    }
    internal void Require()
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
        _input.RequireCurrent();
        if (HandleBits((nint)_input.Window) != Window || !IsWindow(Pointer(Window)) ||
            GetWindowThreadProcessId(Pointer(Window), out var process) != Thread || process != Process)
            throw new InvalidDataException("Actual source/window/thread lifetime changed.");
    }
    private void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != _managedThread || GetCurrentThreadId() != Thread)
            throw new InvalidOperationException("Window SDK owner moved away from its genuine message thread.");
    }
    private nint Owned(uint window, bool optional = false)
    {
        if (window == 0 && optional) return 0;
        var pointer = Pointer(window);
        if (!IsWindow(pointer) || GetWindowThreadProcessId(pointer, out var process) != Thread || process != Process ||
            window != Window && !IsChild(Pointer(Window), pointer))
            throw new NotSupportedException("Original window operation targets an unowned foreign window/queue.");
        return pointer;
    }
    private void Foreground()
    {
        var window = GetForegroundWindow();
        if (window != Pointer(Window) && !IsChild(Pointer(Window), window))
            throw new InvalidOperationException("Original input mutation requires the actual foreground product window.");
    }
    private nint Layout(uint value)
    {
        if (value == 0) return 0;
        if (!_layouts.Contains(value)) throw new NotSupportedException("Keyboard layout has no actual source/thread SDK observation.");
        return Pointer(value);
    }
    internal NativePluginWindowResult Execute(NativePluginWindowOperation operation, uint target, uint incoming,
        uint childThread, ReadOnlySpan<byte> payload, Func<uint, (int Result, uint LastError)>? enumerate = null)
    {
        Require(); var bytes = payload.ToArray(); uint result; uint? observedError = null;
        uint Read(int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
        void Extent(int expected) { if (bytes.Length != expected) throw new InvalidDataException("Window call has an incomplete/extra public ABI extent."); }
        var handle = target == 0 ? 0 : Pointer(target);
        switch (operation)
        {
            case NativePluginWindowOperation.GetForeground:
                Extent(0); SetLastError(incoming); result = HandleBits(GetForegroundWindow()); break;
            case NativePluginWindowOperation.GetActive:
                Extent(0); SetLastError(incoming); result = HandleBits(GetActiveWindow()); break;
            case NativePluginWindowOperation.SetActive:
                handle = Owned(target, true); Extent(0); SetLastError(incoming); result = HandleBits(SetActiveWindow(handle)); break;
            case NativePluginWindowOperation.SetFocus:
                handle = Owned(target, true); Extent(0); SetLastError(incoming); result = HandleBits(SetFocus(handle)); break;
            case NativePluginWindowOperation.SetForeground:
                handle = Owned(target); Extent(0); SetLastError(incoming); result = unchecked((uint)SetForegroundWindow(handle)); break;
            case NativePluginWindowOperation.BringToTop:
                handle = Owned(target); Extent(0); SetLastError(incoming); result = unchecked((uint)BringWindowToTop(handle)); break;
            case NativePluginWindowOperation.Show:
                handle = Owned(target); Extent(4); SetLastError(incoming); result = unchecked((uint)ShowWindow(handle, unchecked((int)Read(0)))); bytes = []; break;
            case NativePluginWindowOperation.IsVisible:
                // Enumeration may observe opaque desktop windows; it does not
                // grant foreign title, input, mutation or process-memory access.
                if (target != Window && !_enumerating.Contains(target)) handle = Owned(target);
                Extent(0); SetLastError(incoming); result = unchecked((uint)IsWindowVisible(handle)); break;
            case NativePluginWindowOperation.GetThreadProcess:
                if (target != Window && !_enumerating.Contains(target)) handle = Owned(target);
                if (bytes.Length is not (0 or 4)) throw new InvalidDataException("Window process output lacks its complete optional DWORD extent.");
                using (var buffer = new Pinned(bytes)) { SetLastError(incoming); result = WindowThreadProcess(handle, buffer.Pointer); observedError = GetLastError(); }
                break;
            case NativePluginWindowOperation.GetText:
            case NativePluginWindowOperation.GetClass:
                if (operation == NativePluginWindowOperation.GetText) handle = Owned(target);
                else if (target != Window && !_enumerating.Contains(target)) handle = Owned(target);
                if (bytes.Length < 8 || Read(0) > int.MaxValue || Read(0) != bytes.Length - 8)
                    throw new InvalidDataException("ANSI window text buffer differs from its actual caller capacity.");
                if (Read(4) != GetACP()) throw new NotSupportedException("Original/product process ANSI code pages differ; a cross-process text-conversion owner is absent.");
                using (var buffer = new Pinned(bytes))
                {
                    SetLastError(incoming);
                    result = unchecked((uint)(operation == NativePluginWindowOperation.GetText ?
                        GetWindowTextA(handle, buffer.Pointer + 8, checked((int)Read(0))) :
                        GetClassNameA(handle, buffer.Pointer + 8, checked((int)Read(0)))));
                    observedError = GetLastError();
                }
                break;
            case NativePluginWindowOperation.GetClient:
            case NativePluginWindowOperation.GetRectangle:
                handle = Owned(target); Extent(16);
                using (var buffer = new Pinned(bytes))
                { SetLastError(incoming); result = unchecked((uint)(operation == NativePluginWindowOperation.GetClient ? GetClientRect(handle, buffer.Pointer) : GetWindowRect(handle, buffer.Pointer))); observedError = GetLastError(); }
                break;
            case NativePluginWindowOperation.GetCursor:
                Extent(8);
                using (var buffer = new Pinned(bytes)) { SetLastError(incoming); result = unchecked((uint)GetCursorPos(buffer.Pointer)); observedError = GetLastError(); }
                break;
            case NativePluginWindowOperation.ClientToScreen:
                handle = Owned(target); Extent(8);
                using (var buffer = new Pinned(bytes)) { SetLastError(incoming); result = unchecked((uint)ClientToScreen(handle, buffer.Pointer)); observedError = GetLastError(); }
                break;
            case NativePluginWindowOperation.SetCursor:
                Extent(8); Foreground();
                var x = unchecked((int)Read(0)); var y = unchecked((int)Read(4));
                if (!GetWindowRect(Pointer(Window), out var bounds) || x < bounds.Left || y < bounds.Top || x >= bounds.Right || y >= bounds.Bottom)
                    throw new NotSupportedException("Cursor mutation leaves the actual product window.");
                SetLastError(incoming); result = unchecked((uint)SetCursorPos(x, y)); bytes = []; break;
            case NativePluginWindowOperation.ClipCursor:
                Foreground(); if (bytes.Length is not (0 or 16)) throw new InvalidDataException("Clip rectangle is incomplete.");
                if (!GetClipCursor(out var previous)) throw new System.ComponentModel.Win32Exception(unchecked((int)GetLastError()));
                _previousClip ??= previous;
                if (bytes.Length == 16)
                {
                    var clip = Rect.Read(bytes);
                    if (!GetWindowRect(Pointer(Window), out var windowBounds) || !windowBounds.Contains(clip))
                        throw new NotSupportedException("Original cursor clipping leaves the actual product window.");
                }
                using (var buffer = new Pinned(bytes)) { SetLastError(incoming); result = unchecked((uint)ClipCursor(bytes.Length == 0 ? 0 : buffer.Pointer)); observedError = GetLastError(); }
                if (result != 0) { if (!GetClipCursor(out var current)) throw new System.ComponentModel.Win32Exception(unchecked((int)GetLastError())); _currentClip = current; }
                bytes = []; break;
            case NativePluginWindowOperation.GetKeyboardLayout:
                Extent(4); var queriedThread = Read(0);
                if (queriedThread != 0 && queriedThread != Thread && queriedThread != childThread)
                    throw new NotSupportedException("Keyboard layout targets an unowned thread.");
                SetLastError(incoming); result = HandleBits(GetKeyboardLayout(queriedThread == 0 ? Thread : queriedThread)); observedError = GetLastError();
                if (result != 0) _layouts.Add(result); bytes = []; break;
            case NativePluginWindowOperation.GetKeyboardState:
                Extent(256);
                using (var buffer = new Pinned(bytes)) { SetLastError(incoming); result = unchecked((uint)GetKeyboardState(buffer.Pointer)); observedError = GetLastError(); }
                break;
            case NativePluginWindowOperation.MapVirtualKey:
                Extent(12); var layout = Layout(Read(8)); SetLastError(incoming); result = MapVirtualKeyExA(Read(0), Read(4), layout); bytes = []; break;
            case NativePluginWindowOperation.ToUnicode:
                if (bytes.Length < 280 || Read(8) != 0 || Read(12) > int.MaxValue || bytes.Length != 280L + Read(12) * 2L)
                    throw new InvalidDataException("Unicode conversion lacks its exact virtual-key/input/output capacity.");
                var keyLayout = Layout(Read(20));
                using (var buffer = new Pinned(bytes))
                {
                    if ((Read(16) & 4U) == 0) _keyboardConversionEntered = true;
                    SetLastError(incoming); result = unchecked((uint)ToUnicodeEx(Read(0), Read(4), buffer.Pointer + 24,
                        buffer.Pointer + 280, checked((int)Read(12)), Read(16), keyLayout));
                    observedError = GetLastError();
                }
                break;
            case NativePluginWindowOperation.PostMessage:
                handle = Owned(target); Extent(12); Foreground(); var message = Read(0);
                if (!(message is 0 or 0x10 or 0x1f or 0x112 || message is >= 0x100 and <= 0x109 || message is >= 0x200 and <= 0x20e))
                    throw new NotSupportedException("Pointer/custom Windows message has no product/native source marshalling owner.");
                SetLastError(incoming); result = unchecked((uint)PostMessageA(handle, message, (nuint)Read(4), new nint(unchecked((int)Read(8))))); bytes = []; break;
            case NativePluginWindowOperation.AttachThread:
                Extent(12); var first = Read(0); var second = Read(4); var attach = Read(8) != 0;
                if ((first != Thread && first != childThread) || (second != Thread && second != childThread))
                    throw new NotSupportedException("Input attachment targets a foreign queue/thread.");
                SetLastError(incoming); result = unchecked((uint)AttachThreadInput(first, second, attach)); observedError = GetLastError();
                if (result != 0) { if (attach) _attachments[(first, second)] = true; else _attachments.Remove((first, second)); }
                bytes = []; break;
            case NativePluginWindowOperation.SendInput:
                Foreground(); var sent = SendOriginalInput(bytes, incoming); result = sent.Result; observedError = sent.Error; bytes = []; break;
            case NativePluginWindowOperation.Enumerate:
                Extent(0); if (enumerate is null || _enumerationEntered) throw new InvalidOperationException("Window enumeration has no independent actual callback owner.");
                ExceptionDispatchInfo? callbackFailure = null;
                int Callback(nint window, nint ignored)
                {
                    uint identity = 0;
                    try { identity = HandleBits(window); _enumerating.Add(identity); var outcome = enumerate(identity); SetLastError(outcome.LastError); return outcome.Result; }
                    catch (Exception error) { callbackFailure ??= ExceptionDispatchInfo.Capture(error); return 0; }
                    finally { _enumerating.Remove(identity); }
                }
                _enumerationEntered = true;
                try { EnumWindow callback = Callback; SetLastError(incoming); result = unchecked((uint)EnumWindows(callback, 0)); observedError = GetLastError(); GC.KeepAlive(callback); callbackFailure?.Throw(); }
                finally { _enumerationEntered = false; }
                break;
            default: throw new NotSupportedException("Window operation has no genuine public SDK owner.");
        }
        return new(result, observedError ?? GetLastError(), bytes);
    }
    // Win32 user handles may be carried as 32 bits between WOW64 processes.
    // Only these SDK user-object handles use this conversion. Windows defines
    // the lower 32 bits as significant across WOW64; this is not a pointer codec.
    internal static uint HandleBits(nint value) => unchecked((uint)value.ToInt64());
    private static nint Pointer(uint value) => new(unchecked((int)value));
    private sealed class Pinned : IDisposable
    {
        private GCHandle _handle;
        internal nint Pointer => _handle.IsAllocated ? _handle.AddrOfPinnedObject() : 0;
        internal Pinned(byte[] bytes) { if (bytes.Length != 0) _handle = GCHandle.Alloc(bytes, GCHandleType.Pinned); }
        public void Dispose() { if (_handle.IsAllocated) _handle.Free(); }
    }
}
