using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Physics;

// Real retained storage, separate live count and source capacity flags. Count
// clear deliberately leaves entries/storage in place; source removal callbacks
// belong to their independently reached array consumers.
internal sealed class FalloutControllerDynamicArray<T>(string role) where T : class
{
    private T?[]? _storage;
    internal int Count { get; private set; }
    internal uint CapacityFlags { get; private set; } = FalloutEarlyControllerSource.ArrayEmptyFlag;
    internal int Capacity => (int)(CapacityFlags & FalloutEarlyControllerSource.ArrayCapacityMask);
    internal void Append(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Count == Capacity)
        {
            var capacity = Count == 0 ? 1 : checked(Count * 2);
            var next = new T?[capacity];
            if (_storage is not null) Array.Copy(_storage, next, Count);
            _storage = next; CapacityFlags = (CapacityFlags & FalloutEarlyControllerSource.ArrayRetainedFlag) | (uint)capacity;
        }
        _storage![Count++] = value;
    }
    internal T Read(int index) => index >= 0 && index < Count ? _storage![index] ??
        throw new InvalidDataException("Actual controller array contains an unowned null entry.") : throw new ArgumentOutOfRangeException(nameof(index));
    internal T? ReadRetainedStorage(int index) => _storage is not null && index >= 0 && index < _storage.Length ? _storage[index] :
        throw new ArgumentOutOfRangeException(nameof(index));
    internal void ClearCount() => Count = 0;
    internal FalloutControllerArraySnapshot Capture() => new(role, CapacityFlags, Count, _storage?.Length ?? 0);
    internal void Retire() { _storage = null; Count = 0; }
}
