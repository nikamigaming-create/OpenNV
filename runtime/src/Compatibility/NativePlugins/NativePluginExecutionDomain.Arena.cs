namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginGuestAccess : uint { ReadOnly = 1, ReadWrite = 2 }
internal sealed record NativePluginGuestAllocation(ulong Generation, ulong Handle, uint Address, uint Length,
    uint CommittedBytes, uint ReservedBytes, NativePluginGuestAccess Access);
// This authored state-query view is not a TESForm or original plugin interface.
internal sealed record NativePluginGuestObject(NativePluginGuestAllocation Allocation, uint CdeclQuery,
    uint StdcallQuery, uint ThiscallQuery);
internal readonly record struct NativePluginGuestStateQuery(NativePluginGuestObject Object, uint Operation,
    uint Argument, ulong Callback, ulong ParentCall);
internal readonly record struct NativePluginGuestStatistics(uint Live, uint Retired, uint CommittedBytes,
    uint ReservedBytes, ulong Queries);

internal sealed partial class NativePluginExecutionDomain
{
    internal const uint GuestStateMagic = 0x53564e4f, GuestStateVersion = 1, GuestStateSize = 32;
    internal const uint MaximumGuestCommittedBytes = 8 * 1024 * 1024, MaximumGuestReservedBytes = 32 * 1024 * 1024;
    internal const uint MaximumLiveGuestAllocations = 64, MaximumGuestReservations = 256;
    internal const uint MaximumGuestTransfer = MaximumPayload - 64;
    private readonly Dictionary<ulong, NativePluginGuestAllocation> _guestAllocations = [];
    private readonly List<NativePluginGuestAllocation> _guestRetirements = [];
    private readonly Dictionary<ulong, (NativePluginGuestObject Object, Func<NativePluginGuestStateQuery, uint> Query)> _guestObjects = [];
    private bool _guestInitialized;
    private uint _guestPageSize, _guestAllocationGranularity, _guestCommitted, _guestReserved, _guestRetired;
    private ulong _guestQueries;

    internal NativePluginGuestAllocation AllocateGuest(uint length, NativePluginGuestAccess access,
        ReadOnlySpan<byte> initial = default)
    {
        EnsureGuestArena(); RequireGuestLifetimeChange();
        if (length == 0 || length > MaximumGuestCommittedBytes || initial.Length > length || initial.Length > MaximumGuestTransfer ||
            access is not (NativePluginGuestAccess.ReadOnly or NativePluginGuestAccess.ReadWrite) ||
            access == NativePluginGuestAccess.ReadOnly && initial.Length != length)
            throw new ArgumentException("Guest allocation requires an owned extent, access and bounded initialization.");
        var bytes = initial.ToArray();
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.GuestAllocate, Payload(writer =>
            { writer.Write(length); writer.Write((uint)access); writer.Write(checked((uint)bytes.Length)); writer.Write(bytes); }));
            var allocation = ReadGuestAllocation(reader, length, access, out var stats); Finish(reader);
            AdmitGuest(allocation, stats); return allocation;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    internal NativePluginGuestObject BindGuestState(Func<NativePluginGuestStateQuery, uint> query)
    {
        ArgumentNullException.ThrowIfNull(query); EnsureGuestArena(); RequireGuestLifetimeChange();
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.GuestBindState, []);
            var allocation = ReadGuestAllocation(reader, GuestStateSize, NativePluginGuestAccess.ReadOnly, out var stats);
            var binding = new NativePluginGuestObject(allocation, reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()); Finish(reader);
            if (binding.CdeclQuery == 0 || binding.StdcallQuery == 0 || binding.ThiscallQuery == 0)
                throw new InvalidDataException("Native state binding lacks compiled callable thunks.");
            AdmitGuest(allocation, stats); _guestObjects.Add(allocation.Handle, (binding, query)); return binding;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    internal byte[] ReadGuest(NativePluginGuestAllocation allocation, uint offset, int count)
    {
        VerifyGuest(allocation); GuestExtent(allocation, offset, count);
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.GuestRead, Payload(writer =>
            { writer.Write(allocation.Handle); writer.Write(offset); writer.Write(checked((uint)count)); }));
            if (reader.ReadUInt32() != count) throw new InvalidDataException("Native guest read length drifted.");
            var bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException("Native guest read is truncated.");
            var stats = ReadGuestStatistics(reader); Finish(reader); CheckGuestStatistics(stats); return bytes;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    internal void WriteGuest(NativePluginGuestAllocation allocation, uint offset, ReadOnlySpan<byte> source)
    {
        VerifyGuest(allocation); GuestExtent(allocation, offset, source.Length);
        if (allocation.Access != NativePluginGuestAccess.ReadWrite)
            throw new InvalidOperationException("Guest writes cannot change a read-only state/table owner.");
        var bytes = source.ToArray();
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.GuestWrite, Payload(writer =>
            { writer.Write(allocation.Handle); writer.Write(offset); writer.Write(checked((uint)bytes.Length)); writer.Write(bytes); }));
            var stats = ReadGuestStatistics(reader); Finish(reader); CheckGuestStatistics(stats);
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    internal void ReleaseGuest(NativePluginGuestAllocation allocation)
    {
        VerifyGuest(allocation); RequireGuestLifetimeChange();
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.GuestRelease, Payload(writer => writer.Write(allocation.Handle)));
            if (reader.ReadUInt32() != 1) throw new InvalidDataException("Native guest release lacks its decommit/quarantine receipt.");
            var stats = ReadGuestStatistics(reader); Finish(reader);
            CheckGuestStatistics(stats, liveDelta: -1, retiredDelta: 1, committedDelta: -(long)allocation.CommittedBytes);
            _guestAllocations.Remove(allocation.Handle); _guestObjects.Remove(allocation.Handle);
            _guestRetirements.Add(allocation);
            PublishGuestStatistics(stats);
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    internal NativePluginGuestStatistics GuestStatistics()
    {
        EnsureGuestArena();
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.GuestStatistics, []);
            var stats = ReadGuestStatistics(reader); Finish(reader); CheckGuestStatistics(stats); return stats;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    private void EnsureGuestArena()
    {
        VerifyOwner();
        if (_guestInitialized) return;
        RequireGuestLifetimeChange();
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.GuestCapabilities, []);
            if (reader.ReadUInt32() != GuestStateVersion || reader.ReadUInt32() != MaximumGuestCommittedBytes ||
                reader.ReadUInt32() != MaximumGuestReservedBytes || reader.ReadUInt32() != MaximumLiveGuestAllocations ||
                reader.ReadUInt32() != MaximumGuestReservations || reader.ReadUInt32() != MaximumGuestTransfer)
                throw new InvalidDataException("Native guest budgets or state bridge ABI drifted.");
            var page = reader.ReadUInt32(); var granularity = reader.ReadUInt32(); Finish(reader);
            if (page == 0 || page > 65536 || (page & (page - 1)) != 0 || granularity < page ||
                granularity > MaximumGuestCommittedBytes || (granularity & (granularity - 1)) != 0)
                throw new InvalidDataException("Native guest page size/allocation granularity is invalid.");
            _guestPageSize = page; _guestAllocationGranularity = granularity; _guestInitialized = true;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    private NativePluginGuestAllocation ReadGuestAllocation(BinaryReader reader, uint length,
        NativePluginGuestAccess access, out NativePluginGuestStatistics stats)
    {
        var allocation = new NativePluginGuestAllocation(Generation, reader.ReadUInt64(), reader.ReadUInt32(),
            reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), (NativePluginGuestAccess)reader.ReadUInt32());
        stats = ReadGuestStatistics(reader);
        var pages = checked(((length - 1) / _guestPageSize + 1) * _guestPageSize);
        var reservation = checked(((pages - 1) / _guestAllocationGranularity + 1) * _guestAllocationGranularity);
        if (allocation.Handle == 0 || _guestAllocations.ContainsKey(allocation.Handle) || allocation.Address == 0 ||
            allocation.Address % _guestAllocationGranularity != 0 || allocation.Length != length || allocation.Access != access ||
            allocation.CommittedBytes != pages || allocation.ReservedBytes != reservation ||
            (ulong)allocation.Address + reservation > (1UL << 32))
            throw new InvalidDataException("Native guest allocation identity, extent or protection drifted.");
        if (_guestAllocations.Values.Concat(_guestRetirements).Any(previous => previous.Handle == allocation.Handle ||
            (ulong)previous.Address < (ulong)allocation.Address + reservation &&
            (ulong)allocation.Address < (ulong)previous.Address + previous.ReservedBytes))
            throw new InvalidDataException("Native guest allocation aliases an existing or quarantined lifetime.");
        CheckGuestStatistics(stats, liveDelta: 1, committedDelta: pages, reservedDelta: reservation);
        return allocation;
    }
    private void AdmitGuest(NativePluginGuestAllocation allocation, NativePluginGuestStatistics stats)
    {
        VerifyOwner(); _guestAllocations.Add(allocation.Handle, allocation); PublishGuestStatistics(stats);
    }
    private void VerifyGuest(NativePluginGuestAllocation allocation)
    {
        VerifyOwner();
        if (allocation.Generation != Generation || !_guestAllocations.TryGetValue(allocation.Handle, out var owner) ||
            !ReferenceEquals(owner, allocation)) throw new InvalidOperationException("Guest allocation belongs to a foreign, forged or retired lifetime.");
    }
    private void RequireGuestLifetimeChange()
    {
        if (_callDepth != 0) throw new InvalidOperationException("An active native call/callback owns its guest allocation and state lifetimes.");
    }
    private static void GuestExtent(NativePluginGuestAllocation allocation, uint offset, int count)
    {
        if (count <= 0 || count > MaximumGuestTransfer || offset > allocation.Length || count > allocation.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(count), "Guest access crosses an owned extent or transfer budget.");
    }
    private static NativePluginGuestStatistics ReadGuestStatistics(BinaryReader reader) => new(
        reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt64());
    private void CheckGuestStatistics(NativePluginGuestStatistics stats, int liveDelta = 0, int retiredDelta = 0,
        long committedDelta = 0, long reservedDelta = 0)
    {
        if (stats.Live != _guestAllocations.Count + liveDelta || stats.Retired != _guestRetired + retiredDelta ||
            stats.CommittedBytes != _guestCommitted + committedDelta || stats.ReservedBytes != _guestReserved + reservedDelta ||
            stats.Queries != _guestQueries || stats.Live > MaximumLiveGuestAllocations ||
            stats.Live + stats.Retired > MaximumGuestReservations || stats.CommittedBytes > MaximumGuestCommittedBytes ||
            stats.ReservedBytes > MaximumGuestReservedBytes)
            throw new InvalidDataException("Native guest allocation/query ledger drifted.");
    }
    private void PublishGuestStatistics(NativePluginGuestStatistics stats)
    { _guestCommitted = stats.CommittedBytes; _guestReserved = stats.ReservedBytes; _guestRetired = stats.Retired; }
    private uint DispatchGuestStateQuery(Frame frame, ulong waitingCall)
    {
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var capability = reader.ReadUInt64();
        var address = reader.ReadUInt32(); var argument = reader.ReadUInt32(); Finish(reader);
        if (thread != NativeThread || !_guestObjects.TryGetValue(capability, out var binding) ||
            binding.Object.Allocation.Address != address)
            throw new InvalidDataException("Native state query lacks its actual thread/object lifetime.");
        VerifyGuest(binding.Object.Allocation);
        _guestQueries = checked(_guestQueries + 1);
        return binding.Query(new(binding.Object, frame.Operation, argument, frame.Id, waitingCall));
    }
    private void ReleaseGuestResources()
    { foreach (var allocation in _guestAllocations.Values.ToArray()) ReleaseGuest(allocation); }
    private void ClearGuestCapabilities()
    { _guestObjects.Clear(); _guestAllocations.Clear(); _guestRetirements.Clear(); }
}
