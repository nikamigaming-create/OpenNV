namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Exact public Windows entry families. Each original call still requires its
// genuine product-window or x86-process owner; a declaration is not readiness.
internal static class NativePluginWindowProcessImports
{
    internal static readonly IReadOnlySet<string> Window = new HashSet<string>(StringComparer.Ordinal)
    {
        "SetActiveWindow", "GetForegroundWindow", "SetForegroundWindow", "GetWindowTextA", "GetClientRect", "GetWindowRect",
        "SendInput", "GetCursorPos", "ClientToScreen", "ClipCursor", "EnumWindows", "GetClassNameA", "GetWindowThreadProcessId",
        "GetActiveWindow", "SetFocus", "SetCursorPos", "BringWindowToTop", "ShowWindow", "PostMessageA", "AttachThreadInput",
        "IsWindowVisible", "GetKeyboardLayout", "GetKeyboardState", "MapVirtualKeyExA", "ToUnicodeEx",
    };
    internal static readonly IReadOnlySet<string> Process = new HashSet<string>(StringComparer.Ordinal)
    { "OpenThread", "GetProcessTimes", "ReadProcessMemory", "CreateToolhelp32Snapshot", "Module32FirstW", "Module32NextW" };
    internal static string? Owner(string library, string name)
        => library.Equals("user32.dll", StringComparison.OrdinalIgnoreCase) && Window.Contains(name)
            ? "actual-product-thread-Windows-window-input:" + name
            : (library is "kernel32.dll" or "kernelbase.dll" || library.StartsWith("api-ms-win-core-", StringComparison.OrdinalIgnoreCase)) && Process.Contains(name)
                ? "actual-restricted-x86-process-Windows-operation:" + name : null;
}

internal enum NativePluginWindowOperation : uint
{
    SetActive = 1, GetForeground = 2, SetForeground = 3, GetText = 4, GetClient = 5, GetRectangle = 6,
    SendInput = 7, GetCursor = 8, ClientToScreen = 9, ClipCursor = 10, Enumerate = 11, GetClass = 12,
    GetThreadProcess = 13, GetActive = 14, SetFocus = 15, SetCursor = 16, BringToTop = 17, Show = 18,
    PostMessage = 19, AttachThread = 20, IsVisible = 21, GetKeyboardLayout = 22, GetKeyboardState = 23,
    MapVirtualKey = 24, ToUnicode = 25,
}
internal sealed record NativePluginWindowResult(uint Result, uint LastError, byte[] Bytes);
internal sealed record NativePluginWindowReceipt(ulong Generation, ulong Module, uint NativeThread, ulong Parent,
    NativePluginWindowOperation Operation, uint Target, uint Result, uint LastError, int Bytes, string SourceIdentity);
internal sealed record NativePluginProcessReceipt(ulong Generation, ulong Module, uint NativeThread, ulong Parent,
    uint Operation, uint Handle, long Result, uint LastError, uint Bytes, uint Process, uint Thread, bool OutputCountObserved);
