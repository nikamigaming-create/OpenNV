using Godot;

namespace OpenNV.Runtime.Presentation.Rendering;

/// <summary>Retains immutable image resources, not per-surface material or animation state.</summary>
internal sealed class NativeDdsTextureCache(long budgetBytes, int maximumEntries = 4096)
{
    internal readonly record struct Key(string Source, bool Normal, string? LogicalPath);
    private sealed record Entry(Key Key, WeakReference<byte[]> Payload, Texture2D Texture, long Bytes);
    private readonly object _gate = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _lru = [];
    private long _bytes, _hits, _misses;
    private bool _retired;

    internal object State { get { lock (_gate) return new { bytes = _bytes, budgetBytes, entries = _entries.Count, hits = _hits, misses = _misses, retired = _retired }; } }

    internal Texture2D Get(Key key, byte[] payload, Func<(Texture2D Texture, long Bytes)> create)
    {
        if (budgetBytes < 1 || maximumEntries < 1) throw new ArgumentOutOfRangeException(nameof(budgetBytes));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retired, this);
            if (_entries.TryGetValue(key, out var found))
            {
                if (found.Value.Payload.TryGetTarget(out var prior) && ReferenceEquals(prior, payload) &&
                    GodotObject.IsInstanceValid(found.Value.Texture))
                {
                    ++_hits; _lru.Remove(found); _lru.AddLast(found); return found.Value.Texture;
                }
                Remove(found);
            }
            ++_misses;
            var (texture, bytes) = create();
            if (bytes <= 0) throw new InvalidDataException("Texture cache requires its complete uploaded data extent.");
            if (bytes > budgetBytes) return texture;
            while ((_bytes + bytes > budgetBytes || _entries.Count >= maximumEntries) && _lru.First is { } oldest) Remove(oldest);
            var entry = new Entry(key, new(payload), texture, bytes);
            _entries.Add(key, _lru.AddLast(entry)); _bytes += bytes;
            return texture;
        }
    }

    private void Remove(LinkedListNode<Entry> entry)
    {
        _entries.Remove(entry.Value.Key); _lru.Remove(entry); _bytes -= entry.Value.Bytes;
        // A material can still own the native resource. Eviction must not
        // Dispose it (especially a Texture2Drd with an authored-mip RID).
    }

    internal void Retire()
    {
        lock (_gate) { _retired = true; _entries.Clear(); _lru.Clear(); _bytes = 0; }
    }
}
