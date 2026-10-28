using System.Runtime.CompilerServices;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

// Decoded owned NIF sources are immutable and scoped to their live content
// owner. A changed winning payload replaces its entry; eviction never retires
// a model already held by an actor or worker. No transformed file is saved.
internal static class NativeOwnedNifCache
{
    private const int MaximumEntries = 256;
    private sealed record Entry(byte[] Payload, Lazy<FalloutNifFile> Model, LinkedListNode<string> Recency);
    private sealed class Cache
    {
        internal readonly object Gate = new();
        internal readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
        internal readonly LinkedList<string> Recency = [];
    }
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, Cache> Caches = new();

    internal static FalloutNifFile Read(RuntimeLiveContentSource content, string path, out string identity)
    {
        if (!content.TryRead(path, null, out var bytes, out identity)) throw new FileNotFoundException(path);
        var cache = Caches.GetValue(content, _ => new()); Entry entry;
        lock (cache.Gate)
        {
            if (!cache.Entries.TryGetValue(identity, out entry!) || !ReferenceEquals(entry.Payload, bytes))
            {
                if (entry is not null) cache.Recency.Remove(entry.Recency);
                entry = new(bytes, new Lazy<FalloutNifFile>(() => FalloutNifFile.Read(bytes),
                    LazyThreadSafetyMode.ExecutionAndPublication), cache.Recency.AddLast(identity));
                cache.Entries[identity] = entry;
                while (cache.Entries.Count > MaximumEntries)
                {
                    var oldest = cache.Recency.First!;
                    cache.Recency.RemoveFirst();
                    cache.Entries.Remove(oldest.Value);
                }
            }
            else { cache.Recency.Remove(entry.Recency); cache.Recency.AddLast(entry.Recency); }
        }
        return entry.Model.Value;
    }
}
