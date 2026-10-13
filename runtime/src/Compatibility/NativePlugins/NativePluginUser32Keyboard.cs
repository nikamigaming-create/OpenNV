using System.Buffers.Binary;
using System.Runtime.InteropServices;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Public thread/layout/buffer APIs are independent of HWND ownership. Both
// adapters execute this owner on the real product thread. A headless command
// sample supplies scan codes, not the Win32 queue's virtual-key state.
internal sealed class NativePluginUser32Keyboard(FalloutDirectInputState input, uint thread)
{
    private const int VirtualKeyBytes = 256;
    private const int UnicodeHeaderBytes = 24;
    private const int UnicodeOutputOffset = UnicodeHeaderBytes + VirtualKeyBytes;
    private const uint PreserveKeyboardBuffer = 4;
    private readonly int _managedThread = Environment.CurrentManagedThreadId;
    private readonly HashSet<uint> _layouts = [];
    private bool _conversionChanged, _retired;

    internal static bool Handles(NativePluginWindowOperation operation) => operation is
        NativePluginWindowOperation.GetKeyboardLayout or NativePluginWindowOperation.GetKeyboardState or
        NativePluginWindowOperation.MapVirtualKey or NativePluginWindowOperation.ToUnicode;

    internal NativePluginWindowResult Execute(NativePluginWindowOperation operation, uint incoming,
        uint childThread, ReadOnlySpan<byte> payload)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this); input.RequireCurrent();
        var bytes = payload.ToArray(); uint result; uint? observedError = null;
        uint Read(int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
        void Extent(int expected)
        { if (bytes.Length != expected) throw new InvalidDataException("Keyboard API has an incomplete/extra public ABI extent."); }
        nint Layout(uint value)
        {
            if (value == 0) return 0;
            if (!_layouts.Contains(value)) throw new NotSupportedException("Keyboard layout has no actual source/thread SDK observation.");
            return new(unchecked((int)value));
        }
        switch (operation)
        {
            case NativePluginWindowOperation.GetKeyboardLayout:
                Extent(sizeof(uint)); var queriedThread = Read(0);
                if (queriedThread != 0 && queriedThread != thread && queriedThread != childThread)
                    throw new NotSupportedException("Keyboard layout targets an unowned thread.");
                SetLastError(incoming); result = NativePluginProductWindow.HandleBits(GetKeyboardLayout(queriedThread == 0 ? thread : queriedThread));
                observedError = GetLastError(); if (result != 0) _layouts.Add(result); bytes = []; break;
            case NativePluginWindowOperation.GetKeyboardState:
                Extent(VirtualKeyBytes);
                if (input.Provider != FalloutInputProvider.WindowsDirectInput)
                    throw new NotSupportedException("Headless command input has no Win32 message-queue virtual-key state producer.");
                using (var buffer = new Pinned(bytes))
                { SetLastError(incoming); result = unchecked((uint)GetKeyboardState(buffer.Pointer)); observedError = GetLastError(); }
                break;
            case NativePluginWindowOperation.MapVirtualKey:
                Extent(3 * sizeof(uint)); var layout = Layout(Read(2 * sizeof(uint)));
                SetLastError(incoming); result = MapVirtualKeyExA(Read(0), Read(sizeof(uint)), layout); bytes = []; break;
            case NativePluginWindowOperation.ToUnicode:
                if (bytes.Length < UnicodeOutputOffset || Read(8) != 0 || Read(12) > int.MaxValue ||
                    bytes.Length != UnicodeOutputOffset + Read(12) * (long)sizeof(char))
                    throw new InvalidDataException("Unicode conversion lacks its exact virtual-key/input/output capacity.");
                var keyLayout = Layout(Read(20));
                using (var buffer = new Pinned(bytes))
                {
                    if ((Read(16) & PreserveKeyboardBuffer) == 0) _conversionChanged = true;
                    SetLastError(incoming); result = unchecked((uint)ToUnicodeEx(Read(0), Read(4), buffer.Pointer + UnicodeHeaderBytes,
                        buffer.Pointer + UnicodeOutputOffset, checked((int)Read(12)), Read(16), keyLayout));
                    observedError = GetLastError();
                }
                break;
            default: throw new NotSupportedException("Operation has no public keyboard API owner.");
        }
        return new(result, observedError ?? GetLastError(), bytes);
    }
    internal void RequireColdCapture()
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
        if (_conversionChanged) throw new NotSupportedException("Current native keyboard conversion buffer has no cold-state producer.");
    }
    internal void Retire() { RequireThread(); _retired = true; _layouts.Clear(); }
    private void RequireThread()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Original keyboard APIs require real Windows.");
        if (Environment.CurrentManagedThreadId != _managedThread || GetCurrentThreadId() != thread)
            throw new InvalidOperationException("Keyboard API owner moved away from its actual product thread.");
    }
    private sealed class Pinned : IDisposable
    {
        private GCHandle _handle;
        internal nint Pointer => _handle.IsAllocated ? _handle.AddrOfPinnedObject() : 0;
        internal Pinned(byte[] bytes) { if (bytes.Length != 0) _handle = GCHandle.Alloc(bytes, GCHandleType.Pinned); }
        public void Dispose() { if (_handle.IsAllocated) _handle.Free(); }
    }
    // Preserve the caller's actual last-error cell, including SDK APIs that
    // leave it untouched. No P/Invoke prolog clears it on the product thread.
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern uint GetLastError();
    [DllImport("kernel32.dll")] private static extern void SetLastError(uint value);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] private static extern int GetKeyboardState(nint bytes);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint MapVirtualKeyExA(uint code, uint kind, nint layout);
    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(uint key, uint scan, nint state, nint output,
        int capacity, uint flags, nint layout);
}
