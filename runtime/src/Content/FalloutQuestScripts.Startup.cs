namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutQuestScripts
{
    // A native first placement cannot replace a suspended original invocation
    // with the world host or consume its movement while the suffix is held.
    internal bool HasPendingStartupExecution => _compiledExecuting ||
        _compiledInstances.Any(instance => instance.Pending is not null) ||
        _instances.Any(instance => instance.PendingCommand is not null);

    internal bool HasQueuedMessage => _messages.Any(message =>
        message.Request is { } request && MessageResults.IsPending(request));
}
