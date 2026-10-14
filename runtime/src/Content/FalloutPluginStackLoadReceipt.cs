using System.Text.Json;

namespace OpenNV.Runtime.Content;

// This receipt belongs to the complete C# reader, independently of any retail
// loader or later World/Common invocation. The stack publishes it only at its
// actual successful return; consumers also require that same live stack.
internal sealed record FalloutPluginStackLoadReceipt(Guid Factory, string SourceHash,
    string StackIdentity, IReadOnlyList<string> InputOrder, int ThreadId, bool Returned)
{
    internal static FalloutPluginStackLoadReceipt CreateReturned(Guid factory,
        IReadOnlyList<FalloutPluginContext> contexts, RuntimeLiveContentSource? ownedSource)
    {
        if (factory == Guid.Empty || contexts.Count == 0)
            throw new InvalidDataException("Completed reader lost its actual input/factory lifetime.");
        var sourceHash = FalloutAdvancementRuntimeReceipt.Hash(JsonSerializer.Serialize(new
        {
            reader = "opennv-complete-plugin-reader/v1;readonly-headers;ordered-canonical-masters;winning-overrides",
            files = contexts.Select(context => new
            {
                context.LoadOrderIndex,
                context.Plugin.Name,
                context.Bytes,
                context.MtimeUnixMilliseconds,
                context.Sha256,
                context.Plugin.Masters,
            }),
        }));
        return new(factory, sourceHash, ownedSource?.StackId ?? sourceHash,
            Array.AsReadOnly(contexts.Select(context => context.Plugin.Name).ToArray()),
            Environment.CurrentManagedThreadId, true);
    }
}
