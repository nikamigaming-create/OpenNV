namespace OpenNV.Runtime.Compatibility.NativePlugins;

// One product-thread capability owns a native module's reached USER32 calls.
// A windowless adapter publishes no HWND and refuses window/input operations
// whose authoritative producer does not exist in that mode.
internal interface INativePluginProductUser32
{
    string SourceIdentity { get; }
    uint Window { get; }
    uint Thread { get; }
    uint Process { get; }
    IDisposable Retain();
    void Require();
    NativePluginWindowResult Execute(NativePluginWindowOperation operation, uint target, uint incoming,
        uint childThread, ReadOnlySpan<byte> payload, Func<uint, (int Result, uint LastError)>? enumerate = null);
    void RequireColdCapture();
    void RetireBeforeChildExit(uint childThread);
    void RetireAfterChildExit();
}
