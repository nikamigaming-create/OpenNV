using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginMutexFallbackSid(string Sid, uint Attributes);
internal sealed record NativePluginMutexFallbackToken(string Role, ulong TokenId, ulong AuthenticationId,
    ulong ModifiedId, uint Type, uint? ImpersonationLevel, uint Session, bool Restricted,
    string User, ImmutableArray<NativePluginMutexFallbackSid> Groups,
    ImmutableArray<NativePluginMutexFallbackSid> Restrictions, string DefaultDaclSha256);
internal sealed record NativePluginMutexFallbackDescriptor(string Role, string RequestedName,
    uint? OpenStatus, ulong Handle, string? CanonicalName, string? Sha256, string? Sddl,
    string? Failure);
internal sealed record NativePluginMutexFallbackOpen(int Ordinal, string Stage, string RootName,
    string Name, uint Access, uint Status, uint? Win32Translation, ulong Handle, string? CanonicalName,
    string? MetadataFailure);
internal sealed record NativePluginMutexFallbackClose(string Role, ulong Handle,
    bool Entered, bool? Result, uint? LastError);
internal sealed record NativePluginMutexFallbackObservation(ulong Generation, ulong Module, ulong Call,
    uint NativeThread, uint ProcessId, string RootName, string RestrictingSid,
    uint? ThreadTokenOpenError, bool? ThreadTokenPresent,
    ImmutableArray<NativePluginMutexFallbackToken> Tokens,
    ImmutableArray<NativePluginMutexFallbackDescriptor> Descriptors,
    ImmutableArray<NativePluginMutexFallbackOpen> Opens,
    ImmutableArray<NativePluginMutexFallbackClose> Closes,
    bool ImpersonationEntered, bool RevertEntered, bool? RevertResult, uint? RevertError,
    bool WorkerStarted, bool WorkerExited, bool? SourceFallbackEligible, bool? Restricted15Opened,
    bool? Restricted6Opened, bool? DescriptorsUnchanged, string? Failure)
{
    internal bool ResourcesClosed => (!WorkerStarted || WorkerExited) && Closes.All(value => value.Result == true);
}

// These are access observations only. No directory or mutex capability is
// published, and no denied return is converted to a successful namespace.
internal sealed partial class NativePluginMutexNamespaces
{
    private readonly List<NativePluginMutexFallbackObservation> _fallbackObservations = [];
    internal IReadOnlyList<NativePluginMutexFallbackObservation> FallbackObservations => _fallbackObservations.AsReadOnly();
    private void ObserveFallback(ulong call, string rootName, NativePluginObjectDirectoryHandle root)
    {
        var observation = process.ProbeMutexDirectoryFallback(generation, module, call,
            thread, restrictingSid, rootName, root);
        _fallbackObservations.Add(observation);
        if (!observation.ResourcesClosed)
            throw new InvalidDataException("Actual mutex directory access observation retains an independently failed reference lifetime.");
        // A failed observation is retained as failed evidence. It does not
        // manufacture a Windows return or erase the subsequent actual SDK call.
    }
}
