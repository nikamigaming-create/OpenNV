using System.Runtime.InteropServices;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutNativeSourceTickCounter : IFalloutSourceTickCounter
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    public string Owner => "actual-public-Win32-GetTickCount/current-thread/" + _thread;
    public uint Read() { Require(); return GetTickCount(); }
    public void Wait(uint milliseconds) { Require(); Sleep(milliseconds); }
    private void Require()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Selected source cached timer uses the public Windows tick counter.");
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Source counter sample/wait changed its actual caller thread.");
    }
    [LibraryImport("kernel32.dll")] private static partial uint GetTickCount();
    [LibraryImport("kernel32.dll")] private static partial void Sleep(uint milliseconds);
}
