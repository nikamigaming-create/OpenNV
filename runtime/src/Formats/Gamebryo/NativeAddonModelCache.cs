using System.Runtime.CompilerServices;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

// Decoded owned NIF sources are immutable and scoped to their live content
// owner. A changed winning payload replaces its entry; eviction never retires
// a model already held by an actor or worker. No transformed file is saved.
internal static class NativeOwnedNifCache
{
    private const int MaximumEntries = 256;
    private const long MaximumSourceBytes = 128L * 1024 * 1024;
    private sealed record Entry(byte[] Payload, Lazy<FalloutNifFile> Model, LinkedListNode<string> Recency);
    private sealed class Cache
    {
        internal readonly object Gate = new();
        internal readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
        internal readonly LinkedList<string> Recency = [];
        internal long Bytes;
        internal bool Retired;
        internal void Retire()
        {
            lock (Gate) { Retired = true; Entries.Clear(); Recency.Clear(); Bytes = 0; }
        }
    }
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, Cache> Caches = new();

    internal static FalloutNifFile Read(RuntimeLiveContentSource content, string path, out string identity)
    {
        if (!content.TryRead(path, null, out var bytes, out identity)) throw new FileNotFoundException(path);
        var cache = Caches.GetValue(content, owner =>
        {
            var created = new Cache();
            owner.RegisterRetirement(created.Retire);
            return created;
        }); Entry entry;
        lock (cache.Gate)
        {
            ObjectDisposedException.ThrowIf(cache.Retired, cache);
            // Oversized resources remain usable by their requesting resident
            // owner; they are not retained by the bounded decoded-source cache.
            if (bytes.LongLength > MaximumSourceBytes) return FalloutNifFile.Read(bytes);
            if (!cache.Entries.TryGetValue(identity, out entry!) || !ReferenceEquals(entry.Payload, bytes))
            {
                if (entry is not null) { cache.Recency.Remove(entry.Recency); cache.Bytes -= entry.Payload.LongLength; }
                entry = new(bytes, new Lazy<FalloutNifFile>(() => FalloutNifFile.Read(bytes),
                    LazyThreadSafetyMode.ExecutionAndPublication), cache.Recency.AddLast(identity));
                cache.Entries[identity] = entry;
                cache.Bytes += bytes.LongLength;
                while (cache.Entries.Count > MaximumEntries || cache.Bytes > MaximumSourceBytes)
                {
                    var oldest = cache.Recency.First!;
                    cache.Recency.RemoveFirst();
                    cache.Bytes -= cache.Entries[oldest.Value].Payload.LongLength;
                    cache.Entries.Remove(oldest.Value);
                }
            }
            else { cache.Recency.Remove(entry.Recency); cache.Recency.AddLast(entry.Recency); }
        }
        return entry.Model.Value;
    }
}
