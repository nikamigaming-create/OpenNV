namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginSteamSourceFileKind : uint { Provider = 1, Callable = 2 }
internal sealed record NativePluginSteamSourceHashDelivery(ulong Generation, ulong Session, ulong Ordinal,
    ulong WaitingRequest, ulong Callback, NativePluginSteamOperation Operation, NativePluginSteamSourceFileKind Kind,
    string Path, NativePluginSteamFileIdentity Native, NativePluginSteamFileIdentity? Managed = null,
    string? Sha256 = null, bool Returned = false, string? FailureType = null, string? Error = null);
internal sealed record NativePluginSteamSourceHashPrefix(ulong Ordinal, ulong Request, ulong Callback,
    NativePluginSteamOperation Operation, NativePluginSteamSourceFileKind Kind, uint Phase,
    string? Path, bool BeforeCopied, NativePluginSteamFileIdentity? Before, bool AfterCopied,
    NativePluginSteamFileIdentity? After, string Sha256, uint Error);
