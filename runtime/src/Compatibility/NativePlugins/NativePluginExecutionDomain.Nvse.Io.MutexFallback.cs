namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private readonly List<NativePluginMutexFallbackObservation> _retiredMutexFallbackObservations = [];
    internal IReadOnlyList<NativePluginMutexFallbackObservation> NvseMutexFallbackObservations =>
        _mutexNamespaces?.FallbackObservations ?? _retiredMutexFallbackObservations.AsReadOnly();
}
