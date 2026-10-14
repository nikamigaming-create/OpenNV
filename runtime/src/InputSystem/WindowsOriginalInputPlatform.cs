using System.Runtime.InteropServices;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.InputSystem;

// CPU platform producers remain independent from HWND, Godot and rendering.
// This selected source imports XINPUT1_3 ordinal 2; another XInput version is
// not silently substituted for an unavailable original producer.
internal sealed class WindowsOriginalInputPlatform : IFalloutOriginalInputPlatform, IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly nint _module;
    private readonly ReadController _read;
    private bool _retired;
    public Guid Factory { get; } = Guid.NewGuid();
    public string Owner => "Windows USER32 and XINPUT1_3 ordinal2 current product platform";
    internal WindowsOriginalInputPlatform()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Original source input platform requires its actual Windows CPU APIs.");
        _module = NativeLibrary.Load("xinput1_3.dll", typeof(WindowsOriginalInputPlatform).Assembly, DllImportSearchPath.System32);
        try
        {
            var address = GetProcAddress(_module, (nint)2);
            if (address == 0) throw new NotSupportedException("Original input XINPUT1_3 ordinal2 is unavailable.");
            _read = Marshal.GetDelegateForFunctionPointer<ReadController>(address);
        }
        catch { NativeLibrary.Free(_module); throw; }
    }
    public void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Original input platform changed its actual product thread.");
    }
    public void ConstructMouse(Action<int> storeSwap, Action<uint> storeDoubleClickMilliseconds)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(storeSwap); ArgumentNullException.ThrowIfNull(storeDoubleClickMilliseconds);
        var previous = SwapMouseButton(0);
        try { storeSwap(previous); }
        finally { _ = SwapMouseButton(previous); }
        storeDoubleClickMilliseconds(GetDoubleClickTime());
        RequireCurrent();
    }
    public unsafe int ReadControllerState(Span<byte> destination)
    {
        RequireCurrent();
        if (destination.Length != 16) throw new InvalidDataException("Original controller poll requires the complete retained state extent.");
        fixed (byte* bytes = destination) return unchecked((int)_read(0, (nint)bytes));
    }
    public void Dispose()
    {
        if (_retired) return;
        RequireCurrent(); NativeLibrary.Free(_module); _retired = true;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReadController(uint user, nint output);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int SwapMouseButton(int swap);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetDoubleClickTime();
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern nint GetProcAddress(nint module, nint ordinal);
}
