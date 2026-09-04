using System.IO.MemoryMappedFiles;
using System.Text;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed class ParitySharedMemoryRing : IDisposable
{
    private static readonly byte[] Magic = "ONVPRNG1"u8.ToArray();
    private const int Version = 1;
    private const int HeaderBytes = 64;
    private const int SlotHeaderBytes = 16;
    private const long VersionOffset = 8;
    private const long CapacityOffset = 12;
    private const long SlotBytesOffset = 16;
    private const long WriteSequenceOffset = 24;
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
        var totalBytes = checked(HeaderBytes + (long)capacity * (SlotHeaderBytes + slotBytes));
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
        if (packet.Length == 0 || packet.Length > _slotBytes)
            throw new InvalidDataException("Parity telemetry packet does not fit its shared-memory slot.");
        Enter();
        try
        {
            var sequence = checked(_view.ReadInt64(WriteSequenceOffset) + 1);
            var slot = (int)((sequence - 1) % _capacity);
            var offset = SlotOffset(slot);
            _view.Write(offset + 8, 0);
            _view.WriteArray(offset + SlotHeaderBytes, packet.ToArray(), 0, packet.Length);
            _view.Write(offset, sequence);
            _view.Write(offset + 8, packet.Length);
            _view.Write(WriteSequenceOffset, sequence);
            _view.Flush();
            return sequence;
        }
        finally
        {
            _mutex.ReleaseMutex();
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
            var earliest = Math.Max(1, latest - _capacity + 1);
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
        HeaderBytes + (long)slot * (SlotHeaderBytes + _slotBytes);

    private byte[] ReadLocked(long sequence)
    {
        var slot = (int)((sequence - 1) % _capacity);
        var offset = SlotOffset(slot);
        var committedSequence = _view.ReadInt64(offset);
        var length = _view.ReadInt32(offset + 8);
        if (committedSequence != sequence || length <= 0 || length > _slotBytes)
            throw new InvalidDataException("Parity shared-memory slot is incomplete.");
        var packet = new byte[length];
        _view.ReadArray(offset + SlotHeaderBytes, packet, 0, length);
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
