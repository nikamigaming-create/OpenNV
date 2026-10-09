using System.IO.MemoryMappedFiles;
using System.Text;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed class ParitySharedMemoryRing : IDisposable
{
    private static readonly byte[] Magic = "ONVPRNG2"u8.ToArray();
    private const int Version = 2;
    private const int HeaderBytes = 64;
    private const int SlotHeaderBytes = 16;
    private const int DirectoryEntryBytes = 16;
    private const long VersionOffset = 8;
    private const long CapacityOffset = 12;
    private const long SlotBytesOffset = 16;
    private const long WriteSequenceOffset = 24;
    private const long NextSlotOffset = 32;
    private const long EarliestSequenceOffset = 40;
    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;
    private readonly Mutex _mutex;
    private readonly int _capacity;
    private readonly int _slotBytes;

    private ParitySharedMemoryRing(
        MemoryMappedFile map,
        MemoryMappedViewAccessor view,
        Mutex mutex,
        int capacity,
        int slotBytes)
    {
        _map = map;
        _view = view;
        _mutex = mutex;
        _capacity = capacity;
        _slotBytes = slotBytes;
    }

    internal static ParitySharedMemoryRing CreateOrOpen(
        string channel,
        int capacity = 128,
        int slotBytes = 1024 * 1024)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Named parity memory is currently Windows-only.");
        if (string.IsNullOrWhiteSpace(channel) ||
            channel.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_') ||
            capacity is < 2 or > 4096 || slotBytes is < 4096 or > 64 * 1024 * 1024)
            throw new ArgumentException("Parity shared-memory configuration is invalid.");
        var mapName = $"Local\\OpenNV.Parity.{channel}";
        var mutexName = $"Local\\OpenNV.Parity.{channel}.Mutex";
        var totalBytes = checked(HeaderBytes + (long)capacity * (DirectoryEntryBytes + SlotHeaderBytes + slotBytes));
        var map = MemoryMappedFile.CreateOrOpen(
            mapName,
            totalBytes,
            MemoryMappedFileAccess.ReadWrite);
        var view = map.CreateViewAccessor(0, totalBytes, MemoryMappedFileAccess.ReadWrite);
        var mutex = new Mutex(false, mutexName);
        var ring = new ParitySharedMemoryRing(map, view, mutex, capacity, slotBytes);
        ring.InitializeOrValidate();
        return ring;
    }

    internal long Publish(ReadOnlySpan<byte> packet)
    {
        var fragments = checked((int)(((long)packet.Length + _slotBytes - 1) / _slotBytes));
        if (packet.Length == 0 || fragments > _capacity)
            throw new InvalidDataException($"Parity telemetry packet ({packet.Length} bytes) exceeds the ring's {_capacity * (long)_slotBytes}-byte capacity.");
        Enter();
        try
        {
            var sequence = checked(_view.ReadInt64(WriteSequenceOffset) + 1);
            var first = _view.ReadInt32(NextSlotOffset);
            var earliest = Math.Max(_view.ReadInt64(EarliestSequenceOffset), sequence - _capacity + 1);
            var bytes = packet.ToArray();
            // One complete frame may span several slots. Never truncate its
            // fields to fit; publish the directory entry only after every byte.
            for (var fragment = 0; fragment < fragments; fragment++)
            {
                var offset = SlotOffset((first + fragment) % _capacity);
                earliest = Math.Max(earliest, _view.ReadInt64(offset) + 1);
                var sourceOffset = fragment * _slotBytes;
                var count = Math.Min(_slotBytes, bytes.Length - sourceOffset);
                _view.WriteArray(offset + SlotHeaderBytes, bytes, sourceOffset, count);
                _view.Write(offset, sequence);
                _view.Write(offset + 8, count);
                _view.Write(offset + 12, fragment);
            }
            var entry = DirectoryOffset(sequence);
            _view.Write(entry, sequence);
            _view.Write(entry + 8, first);
            _view.Write(entry + 12, packet.Length);
            _view.Write(NextSlotOffset, (first + fragments) % _capacity);
            _view.Write(EarliestSequenceOffset, Math.Max(1, earliest));
            _view.Write(WriteSequenceOffset, sequence);
            _view.Flush();
            return sequence;
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    internal long EarliestAvailableSequence
    {
        get
        {
            Enter();
            try { return _view.ReadInt64(EarliestSequenceOffset); }
            finally { _mutex.ReleaseMutex(); }
        }
    }

    internal bool TryReadLatest(out long ringSequence, out byte[] packet)
    {
        Enter();
        try
        {
            ringSequence = _view.ReadInt64(WriteSequenceOffset);
            if (ringSequence <= 0)
            {
                packet = [];
                return false;
            }
            packet = ReadLocked(ringSequence);
            return true;
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    internal bool TryRead(long requestedSequence, out byte[] packet)
    {
        if (requestedSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedSequence));
        Enter();
        try
        {
            var latest = _view.ReadInt64(WriteSequenceOffset);
            if (requestedSequence > latest)
            {
                packet = [];
                return false;
            }
            var earliest = _view.ReadInt64(EarliestSequenceOffset);
            if (requestedSequence < earliest)
                throw new InvalidDataException(
                    $"Parity telemetry overrun: requested {requestedSequence}, earliest retained {earliest}, latest {latest}.");
            packet = ReadLocked(requestedSequence);
            return true;
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    private void InitializeOrValidate()
    {
        Enter();
        try
        {
            var bytes = new byte[Magic.Length];
            _view.ReadArray(0, bytes, 0, bytes.Length);
            if (bytes.All(value => value == 0))
            {
                _view.WriteArray(0, Magic, 0, Magic.Length);
                _view.Write(VersionOffset, Version);
                _view.Write(CapacityOffset, _capacity);
                _view.Write(SlotBytesOffset, _slotBytes);
                _view.Write(WriteSequenceOffset, 0L);
                _view.Write(NextSlotOffset, 0);
                _view.Write(EarliestSequenceOffset, 1L);
                _view.Flush();
                return;
            }
            if (!bytes.AsSpan().SequenceEqual(Magic) ||
                _view.ReadInt32(VersionOffset) != Version ||
                _view.ReadInt32(CapacityOffset) != _capacity ||
                _view.ReadInt32(SlotBytesOffset) != _slotBytes)
                throw new InvalidDataException("Parity shared-memory contract differs from the requested layout.");
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    private long SlotOffset(int slot) =>
        HeaderBytes + (long)_capacity * DirectoryEntryBytes + (long)slot * (SlotHeaderBytes + _slotBytes);

    private long DirectoryOffset(long sequence) =>
        HeaderBytes + (sequence - 1) % _capacity * DirectoryEntryBytes;

    private byte[] ReadLocked(long sequence)
    {
        var entry = DirectoryOffset(sequence);
        var committedSequence = _view.ReadInt64(entry);
        var first = _view.ReadInt32(entry + 8);
        var length = _view.ReadInt32(entry + 12);
        if (committedSequence != sequence || first < 0 || first >= _capacity ||
            length <= 0 || length > (long)_slotBytes * _capacity)
            throw new InvalidDataException("Parity shared-memory frame directory is incomplete.");
        var packet = new byte[length];
        var destination = 0;
        for (var fragment = 0; destination < length; fragment++)
        {
            var offset = SlotOffset((first + fragment) % _capacity);
            var count = Math.Min(_slotBytes, length - destination);
            if (_view.ReadInt64(offset) != sequence || _view.ReadInt32(offset + 8) != count ||
                _view.ReadInt32(offset + 12) != fragment)
                throw new InvalidDataException("Parity shared-memory frame is incomplete or overwritten.");
            _view.ReadArray(offset + SlotHeaderBytes, packet, destination, count);
            destination += count;
        }
        return packet;
    }

    private void Enter()
    {
        try
        {
            if (!_mutex.WaitOne(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Timed out waiting for the parity shared-memory writer.");
        }
        catch (AbandonedMutexException)
        {
            // The mutex is acquired when its prior owner exited unexpectedly.
        }
    }

    public void Dispose()
    {
        _view.Dispose();
        _map.Dispose();
        _mutex.Dispose();
    }
}
