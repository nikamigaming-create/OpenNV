using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginProductWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Rect(int Left, int Top, int Right, int Bottom)
    {
        internal bool Contains(Rect other) => other.Left >= Left && other.Top >= Top &&
            other.Right <= Right && other.Bottom <= Bottom && other.Right >= other.Left && other.Bottom >= other.Top;
        internal static Rect Read(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt32LittleEndian(bytes),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]));
    }
    // The public INPUT union is 28 bytes in the original x86 caller, and 40
    // bytes in the 64-bit product. No original pointer is used in the parent.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(8)] internal int MouseX;
        [FieldOffset(12)] internal int MouseY;
        [FieldOffset(16)] internal uint MouseData;
        [FieldOffset(20)] internal uint MouseFlags;
        [FieldOffset(24)] internal uint MouseTime;
        [FieldOffset(32)] internal ulong MouseExtra;
        [FieldOffset(8)] internal ushort VirtualKey;
        [FieldOffset(10)] internal ushort Scan;
        [FieldOffset(12)] internal uint KeyFlags;
        [FieldOffset(16)] internal uint KeyTime;
        [FieldOffset(24)] internal ulong KeyExtra;
        [FieldOffset(8)] internal uint HardwareMessage;
        [FieldOffset(12)] internal ushort HardwareLow;
        [FieldOffset(14)] internal ushort HardwareHigh;
    }
    private static (uint Result, uint Error) SendOriginalInput(byte[] bytes, uint incoming)
    {
        if (nint.Size != 8 || bytes.Length < 8) throw new NotSupportedException("Original INPUT requires its x86 declaration and actual x64 Windows consumer.");
        uint Read(int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
        var count = Read(0); var size = Read(4);
        if (size != 28 || count > (NativePluginExecutionDomain.MaximumPayload - 8) / 28 || bytes.Length != 8L + count * 28L)
            throw new InvalidDataException("Original INPUT has an unknown union size or incomplete caller extent.");
        var values = new Input[checked((int)count)];
        for (var index = 0; index < values.Length; ++index)
        {
            var at = 8 + index * 28; var type = Read(at); var value = new Input { Type = type };
            switch (type)
            {
                case 0:
                    value.MouseX = unchecked((int)Read(at + 4)); value.MouseY = unchecked((int)Read(at + 8));
                    value.MouseData = Read(at + 12); value.MouseFlags = Read(at + 16); value.MouseTime = Read(at + 20); value.MouseExtra = Read(at + 24); break;
                case 1:
                    value.VirtualKey = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 4));
                    value.Scan = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 6));
                    value.KeyFlags = Read(at + 8); value.KeyTime = Read(at + 12); value.KeyExtra = Read(at + 16); break;
                case 2:
                    value.HardwareMessage = Read(at + 4);
                    value.HardwareLow = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 8));
                    value.HardwareHigh = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 10)); break;
                default: throw new NotSupportedException("Original INPUT union discriminator has no public typed owner.");
            }
            values[index] = value;
        }
        SetLastError(incoming); var result = SendInput(count, values, Marshal.SizeOf<Input>()); var error = GetLastError();
        GC.KeepAlive(values); return (result, error);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int EnumWindow(nint window, nint parameter);
    // SetLastError=false is deliberate: .NET must not clear the actual native
    // thread's incoming last-error slot before an API that leaves it unchanged.
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern uint GetACP();
    [DllImport("kernel32.dll")] private static extern uint GetLastError();
    [DllImport("kernel32.dll")] private static extern void SetLastError(uint value);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsChild(nint parent, nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] private static extern uint WindowThreadProcess(nint window, nint process);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetActiveWindow();
    [DllImport("user32.dll")] private static extern nint SetActiveWindow(nint window);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern int SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern int BringWindowToTop(nint window);
    [DllImport("user32.dll")] private static extern int ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern int IsWindowVisible(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int GetWindowTextA(nint window, nint bytes, int count);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int GetClassNameA(nint window, nint bytes, int count);
    [DllImport("user32.dll")] private static extern int GetClientRect(nint window, nint rectangle);
    [DllImport("user32.dll")] private static extern int GetWindowRect(nint window, nint rectangle);
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern int GetCursorPos(nint point);
    [DllImport("user32.dll")] private static extern int ClientToScreen(nint window, nint point);
    [DllImport("user32.dll")] private static extern int SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern int ClipCursor(nint rectangle);
    [DllImport("user32.dll", EntryPoint = "ClipCursor")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClipCursor(ref Rect rectangle);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClipCursor(out Rect rectangle);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern int GetKeyboardState(nint bytes);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint MapVirtualKeyExA(uint code, uint type, nint layout);
    [DllImport("user32.dll")] private static extern int ToUnicodeEx(uint key, uint scan, nint state, nint characters, int count, uint flags, nint layout);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int PostMessageA(nint window, uint message, nuint first, nint second);
    [DllImport("user32.dll")] private static extern int AttachThreadInput(uint first, uint second, [MarshalAs(UnmanagedType.Bool)] bool attach);
    [DllImport("user32.dll")] private static extern int EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, [In] Input[] values, int size);
}
