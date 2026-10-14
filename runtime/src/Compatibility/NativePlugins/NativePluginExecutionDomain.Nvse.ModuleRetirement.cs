namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    // The native command/PluginInfo publications retain these same consumers
    // until their original DLLs unload. Self-publications impose no ordering.
    private readonly Dictionary<ulong, HashSet<ulong>> _nvsePublicationProviders = [];
    private readonly Dictionary<ulong, HashSet<ulong>> _nvsePublicationConsumers = [];

    private void RetainNvsePublicationProvider(ulong provider, NativeNvsePlugin consumer)
    {
        VerifyNvse(consumer);
        if (!_nvseModules.TryGetValue(provider, out var original) || original.Generation != Generation ||
            original.Phase is NativeNvsePhase.Retired or NativeNvsePhase.Faulted)
            throw new InvalidDataException("Native publication has no living original provider in this process generation.");
        if (provider == consumer.Module) return;
        if (!_nvsePublicationProviders.TryGetValue(consumer.Module, out var providers))
            _nvsePublicationProviders.Add(consumer.Module, providers = []);
        if (!providers.Add(provider)) return;
        if (!_nvsePublicationConsumers.TryGetValue(provider, out var consumers))
            _nvsePublicationConsumers.Add(provider, consumers = []);
        consumers.Add(consumer.Module);
    }

    private NativeNvsePlugin[] NvseAutomaticRetirementOrder()
    {
        var modules = _nvseModules.Values.Where(plugin => plugin.Phase != NativeNvsePhase.Retired).ToArray();
        var consumers = modules.ToDictionary(plugin => plugin.Module,
            plugin => _nvsePublicationConsumers.GetValueOrDefault(plugin.Module)?.Count ?? 0);
        // Initially eligible modules keep discovery order. Indexed edges and
        // this queue require O(V+E) work over the actual retirement graph.
        var ready = new Queue<NativeNvsePlugin>();
        foreach (var plugin in modules)
            if (consumers[plugin.Module] == 0) ready.Enqueue(plugin);
        var order = new List<NativeNvsePlugin>(modules.Length);
        while (ready.TryDequeue(out var consumer))
        {
            order.Add(consumer);
            if (!_nvsePublicationProviders.TryGetValue(consumer.Module, out var providers)) continue;
            foreach (var provider in providers)
                if (consumers.TryGetValue(provider, out var remaining) && remaining != 0)
                {
                    consumers[provider] = --remaining;
                    if (remaining == 0) ready.Enqueue(_nvseModules[provider]);
                }
        }
        if (order.Count != modules.Length)
            throw new NotSupportedException("Original modules retain cyclic native publication borrows without their genuine release/retirement owner.");
        return order.ToArray();
    }

    private void ForgetNvsePublicationProvider(ulong module)
    {
        if (_nvsePublicationProviders.Remove(module, out var providers))
            foreach (var provider in providers)
                if (_nvsePublicationConsumers.TryGetValue(provider, out var consumers))
                {
                    consumers.Remove(module);
                    if (consumers.Count == 0) _nvsePublicationConsumers.Remove(provider);
                }
        if (_nvsePublicationConsumers.Remove(module, out var remainingConsumers))
            foreach (var consumer in remainingConsumers)
                if (_nvsePublicationProviders.TryGetValue(consumer, out var remainingProviders))
                {
                    remainingProviders.Remove(module);
                    if (remainingProviders.Count == 0) _nvsePublicationProviders.Remove(consumer);
                }
    }
}
