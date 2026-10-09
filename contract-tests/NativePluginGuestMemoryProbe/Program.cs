using OpenNV.Runtime.Compatibility.NativePlugins;

if (args.Length == 5 && args[0] == "--test-native-mutex")
{
    NativeMutexContracts.Run(args[1], args[2], args[3], args[4]);
    return;
}

if (args is ["--test-native-engine-command-leaves"])
{
    NativeEngineCommandLeafContracts.Run();
    return;
}

if (args.Length == 5 && args[0] == "--test-native-cng-shared")
{
    NativeCngSharedContracts.Run(args[1], args[2], args[3], args[4]);
    return;
}

if (args.Length == 5 && args[0] == "--test-native-crt")
{
    NativeCrtContracts.Run(args[1], args[2], args[3], args[4]);
    return;
}

if (args.Length == 3 && args[0] == "--test-native-guest-arena")
{
    NativeGuestArenaContracts.Run(args[1], args[2]);
    return;
}

if (args.Length == 3 && args[0] == "--test-native-domain")
{
    NativeExecutionContracts.Run(args[1], args[2]);
    return;
}

if (args.Length != 0)
{
    if (args.Length != 2 || args[0] != "--audit-owned-memory")
        throw new ArgumentException("Use --audit-owned-memory <unchanged-owned-x86-plugin.dll>.");
    OwnedPluginMemoryAudit.Run(args[1]);
    return;
}

NativeEngineCommandLeafContracts.Run();
OwnershipAndEndian();
AdjacentExtents();
AtomicRefusal();
MappingRefusal();
AddressSpaceBoundary();
ThreadOwnership();
Retirement();
Console.WriteLine("OPENNV_X86_GUEST_MEMORY_CONTRACT_PASS execution=absent objectLayouts=absent hooks=absent");

static void OwnershipAndEndian()
{
    using var memory = new X86GuestMemory(8);
    byte[] source = [0x12, 0x34, 0x56, 0x78];
    memory.Map(0x1000, source, X86GuestMemoryAccess.ReadWrite);
    source[0] = 0;
    Equal(0x78563412, memory.ReadUInt32(0x1000), "Mapping retained a caller-owned array.");
    var returned = Bytes(memory, 0x1000, 4);
    returned[0] = 0;
    Equal(0x78563412, memory.ReadUInt32(0x1000), "Read leaked its backing array.");
    memory.WriteUInt32(0x1000, 0x12345678);
    Same([0x78, 0x56, 0x34, 0x12], Bytes(memory, 0x1000, 4), "DWORD writes must be little-endian.");
    Same([0, 0x34, 0x56, 0x78], source, "Guest write changed its source file buffer.");
    memory.Map(0x2000, [1, 2, 3, 4], X86GuestMemoryAccess.ReadOnly);
    Reject<NotSupportedException>(() => memory.WriteUInt32(0x2000, 0));
    Same([1, 2, 3, 4], Bytes(memory, 0x2000, 4), "Read-only memory changed.");
}

static void AdjacentExtents()
{
    using var memory = new X86GuestMemory(8);
    memory.Map(0x1004, [5, 6, 7, 8], X86GuestMemoryAccess.ReadWrite);
    memory.Map(0x1000, [1, 2, 3, 4], X86GuestMemoryAccess.ReadWrite);
    Equal(0x06050403, memory.ReadUInt32(0x1002), "Unaligned DWORD read crossed adjacent owners incorrectly.");
    memory.WriteUInt32(0x1002, 0x12345678);
    Same([1, 2, 0x78, 0x56, 0x34, 0x12, 7, 8], Bytes(memory, 0x1000, 8),
        "Unaligned DWORD write crossed adjacent owners incorrectly.");
}

static void AtomicRefusal()
{
    using var memory = new X86GuestMemory(12);
    memory.Map(0x1000, [1, 2, 3, 4], X86GuestMemoryAccess.ReadWrite);
    memory.Map(0x1004, [5, 6, 7, 8], X86GuestMemoryAccess.ReadOnly);
    Reject<NotSupportedException>(() => memory.WriteUInt32(0x1002, 0));
    Same([1, 2, 3, 4, 5, 6, 7, 8], Bytes(memory, 0x1000, 8), "Protection refusal wrote a consumed prefix.");

    memory.Map(0x2000, [9, 10, 11, 12], X86GuestMemoryAccess.ReadWrite);
    byte[] destination = [0xaa, 0xbb, 0xcc, 0xdd];
    Reject<InvalidDataException>(() => memory.Read(0x2002, destination));
    Same([0xaa, 0xbb, 0xcc, 0xdd], destination, "Failed read changed its destination prefix.");
    Reject<InvalidDataException>(() => memory.WriteUInt32(0x2002, 0));
    Same([9, 10, 11, 12], Bytes(memory, 0x2000, 4), "Unmapped-tail refusal changed memory.");
    Reject<InvalidDataException>(() => memory.ReadUInt32(0x1800));
    Reject<InvalidDataException>(() => memory.WriteUInt32(0x1800, 1));
    Reject<InvalidDataException>(() => memory.Read(0x1000, new byte[0x1004]));
    Same([1, 2, 3, 4], Bytes(memory, 0x1000, 4), "Unmapped memory access changed a valid owner.");
}

static void MappingRefusal()
{
    Reject<ArgumentOutOfRangeException>(() => new X86GuestMemory(0));
    Reject<ArgumentOutOfRangeException>(() => new X86GuestMemory(-1));
    using var memory = new X86GuestMemory(8);
    memory.Map(0x1000, [1, 2, 3, 4], X86GuestMemoryAccess.ReadWrite);
    Reject<InvalidDataException>(() => memory.Map(0x0fff, [0, 0], X86GuestMemoryAccess.ReadOnly));
    Reject<InvalidDataException>(() => memory.Map(0x1003, [0, 0], X86GuestMemoryAccess.ReadOnly));
    Reject<InvalidDataException>(() => memory.Map(0x1000, [0], X86GuestMemoryAccess.ReadOnly));
    Reject<InvalidOperationException>(() => memory.Map(0x2000, new byte[5], X86GuestMemoryAccess.ReadWrite));
    Reject<ArgumentOutOfRangeException>(() => memory.Map(0x2000, [0], (X86GuestMemoryAccess)99));
    Reject<ArgumentOutOfRangeException>(() => memory.Map(0x2000, [], X86GuestMemoryAccess.ReadOnly));
    Reject<InvalidDataException>(() => memory.Map(0, [0], X86GuestMemoryAccess.ReadOnly));
    memory.Map(0x2000, [5, 6, 7, 8], X86GuestMemoryAccess.ReadWrite);
    Same([5, 6, 7, 8], Bytes(memory, 0x2000, 4), "Failed mapping consumed address space or budget.");
    Same([1, 2, 3, 4], Bytes(memory, 0x1000, 4), "Failed mapping changed a previous owner.");
}

static void AddressSpaceBoundary()
{
    using var memory = new X86GuestMemory(8);
    memory.Map(0xfffffffc, [1, 2, 3, 4], X86GuestMemoryAccess.ReadWrite);
    Equal(0x04030201, memory.ReadUInt32(0xfffffffc), "The last x86 DWORD must not overflow the host.");
    memory.Write(0xffffffff, [9]);
    Same([1, 2, 3, 9], Bytes(memory, 0xfffffffc, 4), "The last x86 byte is valid.");
    Reject<InvalidDataException>(() => memory.Map(0xffffffff, [1, 2], X86GuestMemoryAccess.ReadWrite));
    Reject<InvalidDataException>(() => memory.ReadUInt32(0xfffffffd));
    Reject<InvalidDataException>(() => memory.WriteUInt32(0xfffffffd, 0));
    Reject<InvalidDataException>(() => memory.ReadUInt32(0));
    Reject<InvalidDataException>(() => memory.WriteUInt32(0, 0));
    Reject<ArgumentOutOfRangeException>(() => memory.Read(0x1000, []));
    Reject<ArgumentOutOfRangeException>(() => memory.Write(0x1000, []));
    Same([1, 2, 3, 9], Bytes(memory, 0xfffffffc, 4), "Wrapped access changed guest memory.");
}

static void Retirement()
{
    var memory = new X86GuestMemory(4);
    memory.Map(0x1000, [1, 2, 3, 4], X86GuestMemoryAccess.ReadWrite);
    memory.Dispose();
    memory.Dispose();
    Reject<ObjectDisposedException>(() => memory.ReadUInt32(0x1000));
    Reject<ObjectDisposedException>(() => memory.WriteUInt32(0x1000, 0));
    Reject<ObjectDisposedException>(() => memory.Map(0x1000, [1], X86GuestMemoryAccess.ReadOnly));
}

static void ThreadOwnership()
{
    using var memory = new X86GuestMemory(8);
    memory.Map(0x1000, [1, 2, 3, 4], X86GuestMemoryAccess.ReadWrite);
    Exception? failure = null;
    var other = new Thread(() =>
    {
        try
        {
            Reject<InvalidOperationException>(() => memory.Map(0x2000, [5, 6, 7, 8], X86GuestMemoryAccess.ReadWrite));
            Reject<InvalidOperationException>(() => memory.ReadUInt32(0x1000));
            Reject<InvalidOperationException>(() => memory.WriteUInt32(0x1000, 0));
            Reject<InvalidOperationException>(() => memory.Dispose());
        }
        catch (Exception error) { failure = error; }
    });
    other.Start();
    other.Join();
    if (failure is not null) throw new InvalidOperationException("Foreign-thread access did not fail closed.", failure);
    Equal(0x04030201, memory.ReadUInt32(0x1000), "Foreign-thread access changed or retired the owner.");
    memory.Map(0x2000, [5, 6, 7, 8], X86GuestMemoryAccess.ReadWrite);
}

static byte[] Bytes(X86GuestMemory memory, uint address, int count)
{
    var bytes = new byte[count];
    memory.Read(address, bytes);
    return bytes;
}

static void Same(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, string failure)
{
    if (!expected.SequenceEqual(actual)) throw new InvalidOperationException(failure);
}

static void Equal(uint expected, uint actual, string failure)
{
    if (expected != actual) throw new InvalidOperationException(failure);
}

static void Reject<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
