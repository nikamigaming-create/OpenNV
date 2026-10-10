namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativeEngineGlobalKind : uint { Word = 1, Object = 2, StaticTlsIndex = 3 }
internal enum NativeEngineGlobalPhase { Declared, Published, Retired, Closed }
internal abstract class NativeNvseEngineWordAuthority
{
    internal abstract string SourceOwner { get; }
    internal abstract string RuntimeSha256 { get; }
    internal abstract uint SourceAddress { get; }
    internal abstract void RequireCurrent();
    internal abstract uint Read();
    internal abstract IDisposable Retain();
}
internal abstract class NativeNvseEngineObjectAuthority
{
    internal abstract string SourceOwner { get; }
    internal abstract string RuntimeSha256 { get; }
    internal abstract uint SourceAddress { get; }
    internal abstract void RequireBinding(NativeNvseSourceObject value);
    internal abstract IDisposable Retain();
}
internal sealed class NativeEngineGlobal(ulong id, ulong generation, ulong module, uint thread,
    uint address, NativeEngineGlobalKind kind, uint value, string owner)
{
    internal ulong Id { get; } = id;
    internal ulong Generation { get; } = generation;
    internal ulong Module { get; } = module;
    internal uint Thread { get; } = thread;
    internal uint Address { get; } = address;
    internal NativeEngineGlobalKind Kind { get; } = kind;
    internal uint Value { get; set; } = value;
    internal string SourceOwner { get; } = owner;
    internal ulong Lease { get; set; }
    internal NativeEngineGlobalPhase Phase { get; set; }
    internal NativeNvseEngineWordAuthority? Word { get; set; }
    internal NativeNvseEngineObjectAuthority? ObjectAuthority { get; set; }
    internal NativeNvseSourceObject? Object { get; set; }
    internal IDisposable? SourceLease { get; set; }
    internal uint Calls { get; set; }
    internal string? Failure { get; set; }
}
internal sealed class NativeEngineStaticTls(ulong generation, ulong module, uint thread,
    NativeEngineStaticTlsSource source, string carrier, string carrierSha256)
{
    internal ulong Generation { get; } = generation;
    internal ulong Module { get; } = module;
    internal uint Thread { get; } = thread;
    internal NativeEngineStaticTlsSource Source { get; } = source;
    internal string Carrier { get; } = carrier;
    internal string CarrierSha256 { get; } = carrierSha256;
    internal uint Image { get; set; }
    internal uint Index { get; set; }
    internal uint Pointer { get; set; }
    internal uint Extent { get; set; }
    internal uint CarrierIndexRva { get; set; }
    internal bool Published { get; set; }
    internal bool Retired { get; set; }
    internal bool SourceConstructed { get; set; }
    internal byte[] PublishedImage { get; set; } = [];
    internal FileStream? CarrierLease { get; set; }
    internal IDisposable? CurrentLease { get; set; }
    internal NativeNvseEngineTlsAuthority? CurrentAuthority { get; set; }
    internal byte[]? PendingImage { get; set; }
    internal string? Failure { get; set; }
}
internal sealed record NativeEngineGlobalEvent(ulong Generation, ulong Module, uint Thread, ulong Call,
    ulong Callback, ulong Global, uint Address, NativeEngineGlobalKind Kind, uint Value,
    NativeEngineGlobalPhase Phase, uint? SdkResult, uint? LastError, string? Failure);
internal sealed record NativeEngineTlsEvent(ulong Generation, ulong Module, uint Thread, ulong Callback,
    ulong Parent, uint Image, uint Index, uint Pointer, uint Extent, bool Retired,
    bool SourceConstructed, uint? SdkResult, uint? LastError, string? Failure);

// Every byte and callback/runtime field needs the real current thread/context
// producer. Loader-copied bytes are not an evaluated current script/TLS frame.
internal abstract class NativeNvseEngineTlsAuthority
{
    internal abstract string RuntimeSha256 { get; }
    internal abstract string SourceOwner { get; }
    internal abstract void RequireCurrent(uint nativeThread);
    internal abstract byte[] ReadCompleteImage(NativeEngineStaticTlsSource source);
    internal abstract void RequireSourceCallbacks(NativeEngineStaticTlsSource source);
    internal abstract IDisposable Retain();
}
