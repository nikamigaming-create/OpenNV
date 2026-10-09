using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenNV.Runtime.InputSystem;

// Public DirectInput8 COM ABI. This owns real device interfaces in the Godot
// process; it does not inject inputs into a retail process or a native child.
internal sealed class WindowsDirectInputDevice : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private nint _input, _keyboard, _mouse;
    internal nint Window { get; }
    private readonly byte[] _keyboardBytes = new byte[256], _mouseBytes = new byte[20];
    private readonly List<nint> _formatMemory = [];
    internal WindowsDirectInputDevice(nint window)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Complete native DirectInput requires Windows.");
        if (window == 0 || !IsWindow(window) || GetWindowThreadProcessId(window, out var process) == 0 || process != Environment.ProcessId)
            throw new InvalidDataException("DirectInput window is not a living product-owned HWND.");
        Window = window;
        try
        {
            var iid = new Guid("bf798031-483a-4da2-aa99-5d64ed369700");
            Check(DirectInput8Create(GetModuleHandleW(null), 0x800, ref iid, out _input, 0), "DirectInput8Create");
            _keyboard = Create(new("6f1d2b61-d5a0-11cf-bfc7-444553540000"), false);
            _mouse = Create(new("6f1d2b60-d5a0-11cf-bfc7-444553540000"), true);
        }
        catch { Dispose(); throw; }
    }
    internal (byte[] Keyboard, byte[] MouseButtons, int X, int Y, int Wheel) Read()
    {
        RequireOwner();
        if (GetForegroundWindow() != Window) throw new InvalidOperationException("Product window is not the foreground DirectInput owner.");
        Read(_keyboard, _keyboardBytes); Read(_mouse, _mouseBytes);
        return (_keyboardBytes.ToArray(), _mouseBytes.AsSpan(12, 8).ToArray(),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(_mouseBytes),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(_mouseBytes.AsSpan(4)),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(_mouseBytes.AsSpan(8)));
    }
    private nint Create(Guid kind, bool mouse)
    {
        Check(Function<CreateDevice>(_input, 3)(_input, ref kind, out var device, 0), "CreateDevice");
        try
        {
            var objects = new List<ObjectFormat>();
            nint GuidMemory(Guid value) { var at = Allocate(Marshal.SizeOf<Guid>()); Marshal.StructureToPtr(value, at, false); return at; }
            if (!mouse)
            {
                var key = GuidMemory(new("55728220-d33c-11cf-bfc7-444553540000"));
                for (uint index = 0; index < 256; ++index) objects.Add(new() { Guid = key, Offset = index, Type = 0x0c | (index << 8) | 0x80000000U });
            }
            else
            {
                var axes = new[] { "a36d02e0-c9f3-11cf-bfc7-444553540000", "a36d02e1-c9f3-11cf-bfc7-444553540000", "a36d02e2-c9f3-11cf-bfc7-444553540000" };
                for (uint index = 0; index < 3; ++index) objects.Add(new() { Guid = GuidMemory(new(axes[index])), Offset = index * 4, Type = 1 | (index << 8) | 0x80000000U });
                var button = GuidMemory(new("a36d02f0-c9f3-11cf-bfc7-444553540000"));
                for (uint index = 0; index < 8; ++index) objects.Add(new() { Guid = button, Offset = 12 + index, Type = 0x0c | (index << 8) | 0x80000000U });
            }
            var extent = Marshal.SizeOf<ObjectFormat>(); var array = Allocate(checked(objects.Count * extent));
            for (var index = 0; index < objects.Count; ++index) Marshal.StructureToPtr(objects[index], array + index * extent, false);
            var format = new DataFormat { Size = checked((uint)Marshal.SizeOf<DataFormat>()), ObjectSize = checked((uint)extent),
                Flags = 2, DataSize = mouse ? 20U : 256U, ObjectCount = checked((uint)objects.Count), Objects = array };
            Check(Function<SetFormat>(device, 11)(device, ref format), "SetDataFormat");
            Check(Function<Cooperative>(device, 13)(device, Window, 6), "SetCooperativeLevel foreground/nonexclusive");
            return device;
        }
        catch { Function<Release>(device, 2)(device); throw; }
    }
    private void Read(nint device, byte[] data)
    {
        Check(Function<Acquire>(device, 7)(device), "Acquire");
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { Check(Function<GetState>(device, 9)(device, checked((uint)data.Length), pinned.AddrOfPinnedObject()), "GetDeviceState complete extent"); }
        finally { pinned.Free(); }
    }
    private nint Allocate(int size) { var memory = Marshal.AllocHGlobal(size); _formatMemory.Add(memory); return memory; }
    private void RequireOwner()
    {
        ObjectDisposedException.ThrowIf(_input == 0, this);
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("DirectInput COM owner thread changed.");
    }
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("DirectInput retirement requires its creator thread.");
        foreach (var device in new[] { _mouse, _keyboard })
            if (device != 0) { _ = Function<Acquire>(device, 8)(device); _ = Function<Release>(device, 2)(device); }
        _mouse = _keyboard = 0;
        if (_input != 0) { _ = Function<Release>(_input, 2)(_input); _input = 0; }
        foreach (var memory in _formatMemory) Marshal.FreeHGlobal(memory);
        _formatMemory.Clear();
    }
    private static T Function<T>(nint pointer, int slot) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), checked(slot * IntPtr.Size)));
    private static void Check(int status, string operation)
    { if (status < 0) throw new Win32Exception(status, operation + " failed: HRESULT 0x" + unchecked((uint)status).ToString("X8")); }
    [StructLayout(LayoutKind.Sequential)] private struct ObjectFormat { internal nint Guid; internal uint Offset, Type, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct DataFormat { internal uint Size, ObjectSize, Flags, DataSize, ObjectCount; internal nint Objects; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateDevice(nint self, ref Guid kind, out nint device, nint outer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetFormat(nint self, ref DataFormat format);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Cooperative(nint self, nint window, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Acquire(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetState(nint self, uint bytes, nint output);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint Release(nint self);
    [DllImport("dinput8.dll", ExactSpelling = true)] private static extern int DirectInput8Create(nint module, uint version, ref Guid iid, out nint result, nint outer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint GetForegroundWindow();
}
